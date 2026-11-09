// Pop-up bawaan Windows (volume/kecerahan) disembunyikan; kecerahan dipantau untuk HUD island.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LiquidMac
{
    // Pop-up hardware Windows 11 tinggal di jendela XamlExplorerHostIslandWindow milik explorer
    // (kecil, di tengah bawah layar). Explorer memindahkannya kembali setiap kali tampil,
    // jadi kita tangkap event "tampil/pindah" dan langsung buang ke luar layar.
    internal static class OsdSuppressor
    {
        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();

        private static readonly WinEventProc _proc = OnEvent;
        private static IntPtr _hShow, _hLoc;
        private static uint _pid;
        private static System.Windows.Forms.Timer _watch;
        public static event Action Suppressed;   // pop-up Windows baru saja dicegah

        public static bool Running { get { return _hShow != IntPtr.Zero; } }

        public static void Start()
        {
            if (_watch == null)
            {
                _watch = new System.Windows.Forms.Timer();
                _watch.Interval = 5000;            // explorer bisa dimulai ulang: pasang ulang hook
                _watch.Tick += delegate { if (ExplorerPid() != _pid) { Unhook(); Hook(); } };
            }
            Hook();
            _watch.Start();
        }

        public static void Stop()
        {
            if (_watch != null) _watch.Stop();
            Unhook();
        }

        private static uint ExplorerPid()
        {
            uint pid = 0;
            IntPtr sh = GetShellWindow();
            if (sh != IntPtr.Zero) Native.GetWindowThreadProcessId(sh, out pid);
            return pid;
        }

        private static void Hook()
        {
            _pid = ExplorerPid();
            if (_pid == 0) return;
            _hShow = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, _proc, _pid, 0, 0);   // EVENT_OBJECT_SHOW
            _hLoc = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, _proc, _pid, 0, 0);    // EVENT_OBJECT_LOCATIONCHANGE
        }

        private static void Unhook()
        {
            if (_hShow != IntPtr.Zero) { UnhookWinEvent(_hShow); _hShow = IntPtr.Zero; }
            if (_hLoc != IntPtr.Zero) { UnhookWinEvent(_hLoc); _hLoc = IntPtr.Zero; }
        }

        private static bool IsOsd(IntPtr h, out Native.RECT r)
        {
            r = new Native.RECT();
            StringBuilder c = new StringBuilder(40);
            GetClassName(h, c, 40);
            if (c.ToString() != "XamlExplorerHostIslandWindow") return false;
            if (!Native.GetWindowRect(h, out r)) return false;
            int w = r.Right - r.Left, hh = r.Bottom - r.Top;
            if (r.Left <= -30000) return false;                                 // sudah dibuang
            if (w <= 0 || hh <= 0 || w > Ui.P(420) || hh > Ui.P(130)) return false;
            Rectangle s = Screen.FromRectangle(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)).Bounds;
            int cx = (r.Left + r.Right) / 2;
            return Math.Abs(cx - (s.Left + s.Width / 2)) <= Ui.P(60) && r.Top > s.Top + s.Height * 0.6;   // tengah bawah
        }

        private static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                Native.RECT r;
                if (!IsOsd(hwnd, out r)) return;
                SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, 0x0415 /* NOSIZE|NOZORDER|NOACTIVATE|NOOWNERZORDER */);
                Action a = Suppressed; if (a != null) a();
            }
            catch (Exception ex) { Log.Write("osd: " + ex.Message); }
        }
    }

    // Perubahan kecerahan (tombol Fn, slider, sensor) lewat event WMI.
    internal static class BrightnessWatch
    {
        private static ManagementEventWatcher _w;
        private static Control _invoker;
        private static Action<int> _onChange;

        public static void Start(Control invoker, Action<int> onChange)
        {
            if (_w != null) return;
            _invoker = invoker; _onChange = onChange;
            try
            {
                _w = new ManagementEventWatcher(new ManagementScope("root\\WMI"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _w.EventArrived += delegate (object s, EventArrivedEventArgs e)
                {
                    try
                    {
                        int level = Convert.ToInt32(e.NewEvent["Brightness"]);
                        if (_invoker != null && _invoker.IsHandleCreated) _invoker.BeginInvoke(_onChange, level);
                    }
                    catch { }
                };
                _w.Start();
            }
            catch (Exception ex) { Log.Write("brightness watch: " + ex.Message); _w = null; }
        }

        public static void Stop()
        {
            if (_w == null) return;
            try { _w.Stop(); _w.Dispose(); } catch { }
            _w = null;
        }
    }
}
