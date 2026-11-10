using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  FRAME JENDELA: sudut siku (tidak membulat) & tanpa garis tepi, untuk semua jendela
    // ============================================================
    internal static class FrameStyler
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);

        private static readonly HashSet<IntPtr> _done = new HashSet<IntPtr>();
        private static readonly List<IntPtr> _hooks = new List<IntPtr>();
        private static WinEventProc _proc;
        private static Timer _sweep;

        public static bool Enabled { get { return Settings.Get("FlatFrames", false); } }

        public static void Start()
        {
            if (_proc != null) return;
            _proc = OnEvent;
            _hooks.Add(SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, _proc, 0, 0, 0));   // OBJECT_SHOW
            _hooks.Add(SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _proc, 0, 0, 0));   // SYSTEM_FOREGROUND
            _sweep = new Timer(); _sweep.Interval = 3000; _sweep.Tick += delegate { Sweep(); }; _sweep.Start();
            Sweep();
        }

        public static void Stop()
        {
            foreach (IntPtr h in _hooks) if (h != IntPtr.Zero) UnhookWinEvent(h);
            _hooks.Clear(); _proc = null;
            if (_sweep != null) { _sweep.Stop(); _sweep.Dispose(); _sweep = null; }
            foreach (IntPtr w in _done) if (Native.IsWindow(w)) Set(w, false);   // kembalikan seperti bawaan Windows
            _done.Clear();
        }

        private static void OnEvent(IntPtr hook, uint ev, IntPtr h, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || h == IntPtr.Zero) return;
            Apply(h);
        }

        private static void Sweep()
        {
            Native.EnumWindows(delegate (IntPtr h, IntPtr l) { Apply(h); return true; }, IntPtr.Zero);
            List<IntPtr> dead = new List<IntPtr>();
            foreach (IntPtr w in _done) if (!Native.IsWindow(w)) dead.Add(w);
            foreach (IntPtr w in dead) _done.Remove(w);
        }

        private static void Apply(IntPtr h)
        {
            if (_done.Contains(h) || !Native.IsWindowVisible(h)) return;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero && (Native.GetWindowLong(h, -16) & 0x00C00000) != 0x00C00000) return;
            int st = Native.GetWindowLong(h, -16), ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return;
            if ((st & 0x00C00000) != 0x00C00000 && (st & 0x00040000) == 0) return;      // hanya jendela berbingkai / bisa di-resize
            if ((ex & Native.WS_EX_LAYERED) != 0 && Control.FromHandle(h) is OverlayForm) return;
            Set(h, true);
            _done.Add(h);
        }

        private static void Set(IntPtr h, bool flat)
        {
            int corner = flat ? 1 /* DONOTROUND */ : 0 /* DEFAULT */;
            int border = flat ? unchecked((int)0xFFFFFFFE) /* NONE */ : unchecked((int)0xFFFFFFFF) /* DEFAULT */;
            try { DwmSetWindowAttribute(h, 33, ref corner, 4); DwmSetWindowAttribute(h, 34, ref border, 4); } catch { }
        }
    }
}

namespace LiquidMac
{
    // ============================================================
    //  TASKBAR HANYA DARI SUDUT KANAN BAWAH (saat taskbar sembunyi otomatis)
    //  Taskbar disembunyikan penuh; muncul hanya saat kursor menyentuh pojok kanan bawah.
    // ============================================================
    internal static class TaskbarGate
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string c, string t);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string c, string t);

        private static System.Windows.Forms.Timer _t;
        private static bool _allowed;          // taskbar sedang boleh tampil (dibuka dari pojok / sedang dipakai)
        private static DateTime _leftAt = DateTime.MinValue, _cornerAt = DateTime.MinValue;
        private static readonly List<IntPtr> _mine = new List<IntPtr>();   // taskbar yang disembunyikan LiquidMac

        public static bool Enabled { get { return Settings.Get("TaskbarCornerOnly", false); } }

        // Jeda (detik) kursor harus diam di pojok kanan bawah sebelum taskbar muncul.
        public static int DelaySeconds
        {
            get { int v; if (!int.TryParse(Settings.GetS("TaskbarRevealDelay", "5"), out v)) v = 5; return Math.Max(0, Math.Min(30, v)); }
        }

        public static void Start()
        {
            if (_t != null) return;
            _allowed = false; _leftAt = _cornerAt = DateTime.MinValue;
            _t = new System.Windows.Forms.Timer(); _t.Interval = 40; _t.Tick += delegate { Tick(); }; _t.Start();
        }

        public static void Stop()
        {
            if (_t != null) { _t.Stop(); _t.Dispose(); _t = null; }
            ShowAll();
        }

        private static List<IntPtr> Trays()
        {
            List<IntPtr> l = new List<IntPtr>();
            foreach (string cls in new string[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                for (IntPtr w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, cls, null); w != IntPtr.Zero && l.Count < 16; w = FindWindowEx(IntPtr.Zero, w, cls, null)) l.Add(w);
            return l;
        }

        private static void ShowAll()
        {
            foreach (IntPtr w in Trays()) if (!Native.IsWindowVisible(w)) ShowWindow(w, 8 /* SW_SHOWNA */);
            _mine.Clear();
        }

        private static void HideAll(List<IntPtr> trays)
        {
            foreach (IntPtr w in trays) if (Native.IsWindowVisible(w)) { ShowWindow(w, 0 /* SW_HIDE */); if (!_mine.Contains(w)) _mine.Add(w); }
        }

        private static void Tick()
        {
            List<IntPtr> trays = Trays();
            if (trays.Count == 0) return;
            IntPtr tb = trays[0];
            if (!AppBar.TaskbarAutoHide) { if (_mine.Count > 0) ShowAll(); _allowed = false; return; }
            System.Drawing.Rectangle s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
            DateTime now = DateTime.Now;
            bool corner = p.X >= s.Right - Ui.P(70) && p.Y >= s.Bottom - Ui.P(3);
            if (!corner) _cornerAt = DateTime.MinValue;
            else if (_cornerAt == DateTime.MinValue) _cornerAt = now;
            // pojok baru membuka taskbar setelah kursor diam di sana selama jeda yang diatur
            bool cornerOk = corner && (_allowed || (now - _cornerAt).TotalSeconds >= DelaySeconds);

            bool onBar = false, busy = false;
            if (_allowed)
            {
                foreach (IntPtr w in trays)
                {
                    Native.RECT r;
                    if (Native.IsWindowVisible(w) && Native.GetWindowRect(w, out r) && r.Top < s.Bottom - Ui.P(8) && p.Y >= r.Top - Ui.P(2) && p.X >= r.Left && p.X < r.Right) onBar = true;
                }
                // menu/popup milik taskbar (ikon tersembunyi, jam, volume) sedang dipakai
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    string fc = Native.ClassOf(fg);
                    if (trays.Contains(fg)) busy = true;
                    else if (fc == "NotifyIconOverflowWindow" || fc == "TopLevelWindowForOverflowXamlIsland" || fc == "Xaml_WindowedPopupClass")
                    {
                        uint pf, pt; Native.GetWindowThreadProcessId(fg, out pf); Native.GetWindowThreadProcessId(tb, out pt);
                        busy = pf == pt;
                    }
                }
            }

            if (cornerOk || onBar || busy)
            {
                _leftAt = DateTime.MinValue;
                if (!_allowed) { _allowed = true; ShowAll(); }
                else foreach (IntPtr w in trays) if (!Native.IsWindowVisible(w)) ShowWindow(w, 8 /* SW_SHOWNA */);
                return;
            }
            if (_allowed)
            {
                if (_leftAt == DateTime.MinValue) { _leftAt = now; return; }
                if ((now - _leftAt).TotalMilliseconds <= 450) return;
                _allowed = false;
            }
            // Tidak diizinkan tampil: sembunyikan lagi, termasuk bila Windows memunculkannya sendiri
            // (aplikasi berkedip minta perhatian, notifikasi, Explorer dimuat ulang, selesai animasi minimize).
            HideAll(trays);
        }
    }
}
