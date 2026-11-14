using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  BAYANGAN JENDELA GAYA macOS (setting MacShadows, bawaan mati)
    //  Bayangan bawaan Windows dimatikan (SPI_SETDROPSHADOW, nilai asli disimpan & dikembalikan), lalu tiap
    //  jendela aplikasi diberi bayangan lembut buatan LiquidMac: lebih lebar & gelap untuk jendela aktif.
    //  Bayangan = 4 jendela tipis (atas, bawah, kiri, kanan) tepat di bawah jendelanya, tembus klik.
    // ============================================================
    internal static class MacShadows
    {
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int a, int b, ref int v, int f);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int a, int b, IntPtr v, int f);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out Native.RECT r, int s);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int n);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr d, IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr d);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dcDst, IntPtr pptDst, ref Native.SIZE size, IntPtr dcSrc, ref Native.POINT src, int key, ref Native.BLENDFUNCTION bf, int fl);

        private const int SPI_GETDROPSHADOW = 0x1024, SPI_SETDROPSHADOW = 0x1025;
        private const int MaxWindows = 16;

        private static readonly Dictionary<IntPtr, Shade> _map = new Dictionary<IntPtr, Shade>();
        private static readonly List<IntPtr> _hooks = new List<IntPtr>();
        private static WinEventProc _proc;
        private static Timer _sweep;
        private static IntPtr _active;

        public static bool Enabled { get { return Settings.Get("MacShadows", false); } }

        // ---------- bayangan bawaan Windows ----------
        private static int GetSys() { int v = 1; SystemParametersInfo(SPI_GETDROPSHADOW, 0, ref v, 0); return v; }
        private static void SetSys(int v) { SystemParametersInfo(SPI_SETDROPSHADOW, 0, (IntPtr)v, 3 /* SPIF_UPDATEINIFILE | SPIF_SENDCHANGE */); }

        public static void RestoreSystem()
        {
            if (Program.SnapshotDir != null) return;
            try
            {
                string o = Settings.GetS("OrigDropShadow", "");
                if (o == "") return;
                int v; if (int.TryParse(o, out v) && GetSys() != v) SetSys(v);
                Settings.SetS("OrigDropShadow", "");
            }
            catch { }
        }

        public static void Start()
        {
            if (_proc != null || Program.SnapshotDir != null) return;
            try
            {
                if (Settings.GetS("OrigDropShadow", "") == "") Settings.SetS("OrigDropShadow", GetSys().ToString());
                if (GetSys() != 0) SetSys(0);
            }
            catch (Exception ex) { Log.Write("bayangan sys: " + ex.Message); }
            _proc = OnEvent;
            Hook(0x0003, 0x0003);   // SYSTEM_FOREGROUND
            Hook(0x000A, 0x000B);   // MOVESIZESTART/END
            Hook(0x0016, 0x0017);   // MINIMIZESTART/END
            Hook(0x8001, 0x8003);   // DESTROY, SHOW, HIDE
            Hook(0x800B, 0x800B);   // LOCATIONCHANGE
            Hook(0x8017, 0x8018);   // CLOAKED/UNCLOAKED
            _sweep = new Timer(); _sweep.Interval = 1000; _sweep.Tick += delegate { Sweep(); }; _sweep.Start();
            Sweep();
        }

        // Bayangan bawaan bisa dinyalakan lagi oleh aplikasi lain / setelan Windows.
        public static void EnsureSystemOff()
        {
            try { if (_proc != null && GetSys() != 0) SetSys(0); } catch { }
        }

        public static void Stop()
        {
            foreach (IntPtr h in _hooks) if (h != IntPtr.Zero) UnhookWinEvent(h);
            _hooks.Clear(); _proc = null;
            if (_sweep != null) { _sweep.Stop(); _sweep.Dispose(); _sweep = null; }
            foreach (Shade s in _map.Values) s.Dispose();
            _map.Clear();
            RestoreSystem();
        }

        private static void Hook(uint a, uint b) { _hooks.Add(SetWinEventHook(a, b, IntPtr.Zero, _proc, 0, 0, 0)); }

        private static void OnEvent(IntPtr hook, uint ev, IntPtr h, int obj, int child, uint thread, uint time)
        {
            if (_proc == null) return;
            try
            {
                if (ev == 0x0003) { Sweep(); return; }
                if (obj != 0 || h == IntPtr.Zero) return;
                Shade s;
                if (ev == 0x8001 || ev == 0x8003 || ev == 0x0016 || ev == 0x8017)
                {
                    if (_map.TryGetValue(h, out s)) { if (ev == 0x8001) { s.Dispose(); _map.Remove(h); } else s.Hide(); }
                    return;
                }
                if (_map.TryGetValue(h, out s)) Place(h, s);
                else if (ev != 0x800B && Eligible(h)) Sweep();
            }
            catch (Exception ex) { Log.Write("bayangan: " + ex.Message); }
        }

        // Jendela aplikasi biasa yang pantas diberi bayangan.
        private static bool Eligible(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsZoomed(h)) return false;
            int st = Native.GetWindowLong(h, -16), ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | 0x08000000 /* NOACTIVATE */)) != 0) return false;
            if ((st & 0x00C00000) != 0x00C00000 && (st & 0x00040000) == 0) return false;     // berbingkai / bisa di-resize
            if ((st & 0x40000000) != 0) return false;                                          // WS_CHILD
            Control c = Control.FromHandle(h);
            if (c is OverlayForm || c is AnimForm) return false;
            int cloaked; if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            try { if (Windows.IsFullscreen(h)) return false; } catch { }
            Native.RECT r; if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0) return false;
            return r.Right - r.Left >= 60 && r.Bottom - r.Top >= 40;
        }

        private static void Sweep()
        {
            if (_proc == null) return;
            _active = Native.GetForegroundWindow();
            // urutan z dari atas: jendela teratas didahulukan bila melebihi batas
            List<IntPtr> want = new List<IntPtr>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l) { if (want.Count < MaxWindows && Eligible(h)) want.Add(h); return true; }, IntPtr.Zero);
            List<IntPtr> gone = new List<IntPtr>();
            foreach (IntPtr h in _map.Keys) if (!want.Contains(h)) gone.Add(h);
            foreach (IntPtr h in gone) { _map[h].Dispose(); _map.Remove(h); }
            foreach (IntPtr h in want)
            {
                Shade s; if (!_map.TryGetValue(h, out s)) { s = new Shade(); _map[h] = s; }
                Place(h, s);
            }
        }

        private static void Place(IntPtr h, Shade s)
        {
            if (!Eligible(h)) { s.Hide(); return; }
            Native.RECT r; if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0) { s.Hide(); return; }
            s.Show(h, System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), h == _active);
        }

        // ---------- satu bayangan: 4 potong ----------
        private sealed class Shade : IDisposable
        {
            private readonly Strip[] _s = { new Strip(), new Strip(), new Strip(), new Strip() };
            private System.Drawing.Size _size; private bool _act, _drawn, _shown;
            private System.Drawing.Rectangle _last;

            private static int Blur(bool a) { return Ui.P(a ? 22 : 12); }
            private static int Dy(bool a) { return Ui.P(a ? 12 : 5); }
            private static double Op(bool a) { return a ? 0.46 : 0.26; }
            private static int Ext(bool a) { return Blur(a) * 2 + Dy(a); }

            public void Show(IntPtr owner, System.Drawing.Rectangle f, bool active)
            {
                int E = Ext(active);
                bool fresh = false;
                if (!_drawn || f.Size != _size || active != _act) { Render(f.Width, f.Height, active); _size = f.Size; _act = active; _drawn = true; fresh = true; }
                if (!fresh && _shown && f == _last && !_zDirty(owner)) return;   // ukuran potong berubah = posisi juga harus ikut
                _last = f;
                int W = f.Width + 2 * E, H = f.Height + 2 * E;
                int x0 = f.Left - E, y0 = f.Top - E;
                IntPtr d = BeginDeferWindowPos(4);
                uint fl = 0x0010 /* NOACTIVATE */ | 0x0040 /* SHOWWINDOW */ | 0x0001 /* NOSIZE */;
                d = Pos(d, _s[0], owner, x0, y0, fl);
                d = Pos(d, _s[1], owner, x0, f.Bottom, fl);
                d = Pos(d, _s[2], owner, x0, f.Top, fl);
                d = Pos(d, _s[3], owner, f.Right, f.Top, fl);
                if (d != IntPtr.Zero) EndDeferWindowPos(d);
                _shown = true;
            }

            // z: potong harus tepat di bawah jendela pemiliknya
            private bool _zDirty(IntPtr owner)
            {
                IntPtr n = Native.GetWindow(owner, 2 /* GW_HWNDNEXT */);
                foreach (Strip s in _s) if (s.Handle == n) return false;
                return true;
            }

            private static IntPtr Pos(IntPtr d, Strip s, IntPtr after, int x, int y, uint fl)
            {
                if (s.Handle == IntPtr.Zero || s.W == 0 || s.H == 0) return d;
                if (d == IntPtr.Zero) { SetWindowPos(s.Handle, after, x, y, 0, 0, fl); return d; }
                return DeferWindowPos(d, s.Handle, after, x, y, 0, 0, fl);
            }

            public void Hide()
            {
                if (!_shown) return;
                foreach (Strip s in _s) s.Hide();
                _shown = false;
            }

            public void Dispose() { foreach (Strip s in _s) s.Destroy(); }

            // Bayangan persegi kabur = perkalian dua profil 1D (erf). Bagian di dalam jendela tidak digambar.
            private void Render(int w, int h, bool a)
            {
                int E = Ext(a), dy = Dy(a); double sig = Blur(a) * 0.5, op = Op(a);
                int W = w + 2 * E, H = h + 2 * E;
                double[] gx = new double[W], gy = new double[H];
                for (int x = 0; x < W; x++) gx[x] = Prof(x + 0.5, E, E + w, sig);
                for (int y = 0; y < H; y++) gy[y] = Prof(y + 0.5, E + dy, E + dy + h, sig) * 0.85 + Prof(y + 0.5, E, E + h, sig) * 0.15;
                _s[0].Paint(gx, gy, 0, 0, W, E, op);
                _s[1].Paint(gx, gy, 0, E + h, W, E, op);
                _s[2].Paint(gx, gy, 0, E, E, h, op);
                _s[3].Paint(gx, gy, E + w, E, E, h, op);
            }

            private static double Prof(double v, double a, double b, double sig)
            {
                double k = 1 / (sig * Math.Sqrt(2));
                return 0.5 * (Erf((v - a) * k) - Erf((v - b) * k));
            }

            private static double Erf(double x)
            {
                double s = x < 0 ? -1 : 1; x = Math.Abs(x);
                double t = 1 / (1 + 0.3275911 * x);
                double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
                return s * y;
            }
        }

        // Satu jendela berlapis tipis yang tembus klik.
        private sealed class Strip : NativeWindow
        {
            public int W, H;

            private void Ensure()
            {
                if (Handle != IntPtr.Zero) return;
                CreateParams cp = new CreateParams();
                cp.Caption = "LiquidMac shadow";
                cp.Style = unchecked((int)0x80000000);   // WS_POPUP
                cp.ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | 0x08000000 /* NOACTIVATE */ | 0x00000020 /* TRANSPARENT */;
                CreateHandle(cp);
            }

            public unsafe void Paint(double[] gx, double[] gy, int sx, int sy, int w, int h, double op)
            {
                W = w; H = h;
                if (w <= 0 || h <= 0) { Hide(); return; }
                Ensure();
                IntPtr scr = Native.GetDC(IntPtr.Zero), mem = CreateCompatibleDC(scr);
                BITMAPINFO bi = new BITMAPINFO(); bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
                IntPtr bits; IntPtr bmp = CreateDIBSection(scr, ref bi, 0, out bits, IntPtr.Zero, 0);
                IntPtr old = SelectObject(mem, bmp);
                try
                {
                    for (int y = 0; y < h; y++)
                    {
                        uint* row = (uint*)((byte*)bits + y * w * 4);
                        double ky = gy[sy + y] * op;
                        for (int x = 0; x < w; x++)
                        {
                            int al = (int)(gx[sx + x] * ky * 255 + 0.5);
                            row[x] = al <= 0 ? 0u : (uint)Math.Min(255, al) << 24;   // hitam premultiplied
                        }
                    }
                    Native.SIZE size = new Native.SIZE(w, h);
                    Native.POINT src = new Native.POINT(0, 0);
                    Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION(); bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
                    UpdateLayeredWindow(Handle, scr, IntPtr.Zero, ref size, mem, ref src, 0, ref bf, Native.ULW_ALPHA);
                }
                finally
                {
                    SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, scr);
                }
            }

            public void Hide() { if (Handle != IntPtr.Zero) Native.ShowWindow(Handle, 0); }
            public void Destroy() { if (Handle != IntPtr.Zero) DestroyHandle(); }

            [StructLayout(LayoutKind.Sequential)]
            private struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
            [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
            [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
            [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
            [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        }
    }
}
