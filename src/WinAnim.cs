// Animasi jendela LiquidMac (minimize ke Dock, buka dari Dock, zoom) pengganti animasi Windows,
// supaya traffic light ikut beranimasi dan tombol bawaan tidak sempat terlihat.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiquidMac
{
    internal static class WinAnim
    {
        [StructLayout(LayoutKind.Sequential)] private struct ANIMATIONINFO { public int cbSize, iMinAnimate; }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int act, int p, ref ANIMATIONINFO ai, int win);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT r, int size);

        public static bool Enabled { get { return Settings.Get("MacWindowAnim", true); } }

        // Diisi Dock: kotak ikon (koordinat layar) untuk jendela tertentu.
        public static Func<IntPtr, Rectangle> DockTarget;
        // Diisi TrafficLights: gambar traffic light di atas tangkapan jendela.
        public static Action<IntPtr, Graphics, Rectangle, float> PaintOverlay;

        private sealed class Snap { public Bitmap Img; public Rectangle Frame; }
        private static readonly Dictionary<IntPtr, Snap> _snaps = new Dictionary<IntPtr, Snap>();
        private static readonly List<IntPtr> _order = new List<IntPtr>();

        // ---------- animasi sistem ----------
        private static int GetSys()
        {
            ANIMATIONINFO ai = new ANIMATIONINFO(); ai.cbSize = Marshal.SizeOf(typeof(ANIMATIONINFO));
            return SystemParametersInfo(0x0048, ai.cbSize, ref ai, 0) ? ai.iMinAnimate : 1;
        }

        private static void SetSys(int v)
        {
            ANIMATIONINFO ai = new ANIMATIONINFO(); ai.cbSize = Marshal.SizeOf(typeof(ANIMATIONINFO)); ai.iMinAnimate = v;
            SystemParametersInfo(0x0049 /* SPI_SETANIMATION */, ai.cbSize, ref ai, 0x3 /* UPDATEINIFILE|SENDCHANGE */);
        }

        // Saat aktif: animasi minimize/maximize Windows dimatikan (nilai asli disimpan & dikembalikan saat keluar).
        public static void SyncSystem()
        {
            try
            {
                if (Enabled)
                {
                    if (Settings.GetS("OrigMinAnimate", "") == "") Settings.SetS("OrigMinAnimate", GetSys().ToString());
                    if (GetSys() != 0) SetSys(0);
                }
                else RestoreSystem();
            }
            catch (Exception ex) { Log.Write("winanim sys: " + ex.Message); }
        }

        public static void RestoreSystem()
        {
            ReleaseTaskbar();
            try
            {
                string o = Settings.GetS("OrigMinAnimate", "");
                if (o == "") return;
                int v; if (int.TryParse(o, out v) && GetSys() != v) SetSys(v);
                Settings.SetS("OrigMinAnimate", "");
            }
            catch { }
        }

        // ---------- bantuan ----------
        public static Rectangle Frame(IntPtr h)
        {
            Native.RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0) Native.GetWindowRect(h, out r);
            Rectangle f = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            Rectangle wa = Screen.FromHandle(h).Bounds;
            return Rectangle.Intersect(f, wa);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr p);

        private static Bitmap Capture(IntPtr h, Rectangle f, float scale)
        {
            if (f.Width < 20 || f.Height < 20) return null;
            try
            {
                int w = Math.Max(1, (int)(f.Width * scale)), hh = Math.Max(1, (int)(f.Height * scale));
                Bitmap small = new Bitmap(w, hh, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    IntPtr dst = g.GetHdc(), scr = GetDC(IntPtr.Zero);
                    try
                    {
                        SetStretchBltMode(dst, 4 /* HALFTONE */); SetBrushOrgEx(dst, 0, 0, IntPtr.Zero);
                        StretchBlt(dst, 0, 0, w, hh, scr, f.Left, f.Top, f.Width, f.Height, 0x00CC0020 /* SRCCOPY */);
                    }
                    finally { ReleaseDC(IntPtr.Zero, scr); g.ReleaseHdc(dst); }
                    // traffic light (tidak ikut tertangkap layar) digambar di atasnya
                    Action<IntPtr, Graphics, Rectangle, float> po = PaintOverlay;
                    if (po != null) { g.ScaleTransform(scale, scale); g.InterpolationMode = InterpolationMode.HighQualityBilinear; po(h, g, f, scale); }
                }
                return small;
            }
            catch (Exception ex) { Log.Write("winanim capture: " + ex.Message); return null; }
        }

        private static Rectangle TargetFor(IntPtr h, Rectangle frame)
        {
            Func<IntPtr, Rectangle> dt = DockTarget;
            Rectangle r = dt != null ? dt(h) : Rectangle.Empty;
            if (r.IsEmpty)
            {
                Rectangle s = Screen.FromHandle(h).Bounds;
                r = new Rectangle(s.Left + s.Width / 2 - 24, s.Bottom - 60, 48, 48);
            }
            return r;
        }

        private static void Keep(IntPtr h, Snap s)
        {
            Forget(h);
            _snaps[h] = s; _order.Add(h);
            while (_order.Count > 4) Forget(_order[0]);
        }

        // Buang tangkapan milik jendela yang sudah ditutup.
        public static void Prune()
        {
            for (int i = _order.Count - 1; i >= 0; i--) if (!Native.IsWindow(_order[i])) Forget(_order[i]);
        }

        public static void Forget(IntPtr h)
        {
            Snap s;
            if (_snaps.TryGetValue(h, out s)) { if (s.Img != null) s.Img.Dispose(); _snaps.Remove(h); }
            _order.Remove(h);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

        // Minimize tanpa membiarkan Windows memilih jendela aktif berikutnya (sering taskbar,
        // yang membuat taskbar sembunyi-otomatis ikut muncul). Kita pilih sendiri jendela aplikasi berikutnya.
        public static void MinimizeQuiet(IntPtr h)
        {
            IntPtr next = NextAppWindow(h);
            HoldTaskbar();
            ShowWindowAsync(h, 7 /* SW_SHOWMINNOACTIVE */);
            SetForegroundWindow(next != IntPtr.Zero ? next : GetShellWindow());
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        private static System.Windows.Forms.Timer _tbTimer;
        private static readonly List<IntPtr> _tbHidden = new List<IntPtr>();

        // Windows memunculkan taskbar sembunyi-otomatis setiap kali jendela di-minimize.
        // Bila taskbar memang sembunyi otomatis, sembunyikan sebentar lalu kembalikan.
        private static void HoldTaskbar()
        {
            try
            {
                if (!AppBar.TaskbarAutoHide) return;
                foreach (string cls in new string[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                    for (IntPtr w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, cls, null); w != IntPtr.Zero; w = FindWindowEx(IntPtr.Zero, w, cls, null))
                        if (Native.IsWindowVisible(w)) { Native.ShowWindow(w, 0 /* SW_HIDE */); _tbHidden.Add(w); }
                if (_tbTimer == null)
                {
                    _tbTimer = new System.Windows.Forms.Timer();
                    _tbTimer.Interval = 700;
                    _tbTimer.Tick += delegate { _tbTimer.Stop(); ReleaseTaskbar(); };
                }
                _tbTimer.Stop(); _tbTimer.Start();
            }
            catch (Exception ex) { Log.Write("taskbar hold: " + ex.Message); }
        }

        public static void ReleaseTaskbar()
        {
            foreach (IntPtr w in _tbHidden) if (Native.IsWindow(w)) Native.ShowWindow(w, 4 /* SW_SHOWNOACTIVATE */);
            _tbHidden.Clear();
        }

        private static IntPtr NextAppWindow(IntPtr h)
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            int guard = 0;
            for (IntPtr w = GetWindow(h, 2 /* GW_HWNDNEXT */); w != IntPtr.Zero && guard < 500; w = GetWindow(w, 2), guard++)
            {
                if (!Native.IsWindowVisible(w) || Native.IsIconic(w)) continue;
                if (Native.GetWindow(w, Native.GW_OWNER) != IntPtr.Zero) continue;
                int ex = Native.GetWindowLong(w, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) continue;
                int cl; if (Native.DwmGetWindowAttribute(w, 14, out cl, 4) == 0 && cl != 0) continue;
                uint pid; Native.GetWindowThreadProcessId(w, out pid);
                if (pid == me) continue;
                string c = Native.ClassOf(w);
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Progman" || c == "WorkerW") continue;
                Native.RECT r; if (!Native.GetWindowRect(w, out r) || r.Right - r.Left < 50 || r.Bottom - r.Top < 50) continue;
                return w;
            }
            return IntPtr.Zero;
        }

        // ---------- aksi ----------
        public static void Minimize(IntPtr h)
        {
            if (!Enabled || !Native.IsWindow(h)) { MinimizeQuiet(h); return; }
            Rectangle f = Frame(h);
            Stopwatch dsw = Stopwatch.StartNew();
            Bitmap img = Capture(h, f, 1f);
            if (Environment.GetEnvironmentVariable("LIQUIDMAC_DEBUG") == "1" || System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug"))) Log.Write("anim min frame=" + f + " img=" + (img != null) + " enabled=" + Enabled + " capture_ms=" + dsw.ElapsedMilliseconds);
            if (img == null) { MinimizeQuiet(h); return; }
            Rectangle to = TargetFor(h, f);
            Keep(h, new Snap { Img = img, Frame = f });
            AnimForm a = new AnimForm(img, f, to, 430, AnimForm.Mode.Minimize, null);
            a.Start();
            MinimizeQuiet(h);
        }

        public static void Restore(IntPtr h, Rectangle from)
        {
            Snap s;
            if (!Enabled || !Native.IsIconic(h) || !_snaps.TryGetValue(h, out s)) { Shell.Activate(h); return; }
            _snaps.Remove(h); _order.Remove(h);
            if (from.IsEmpty) from = TargetFor(h, s.Frame);
            Bitmap img = s.Img;
            AnimForm a = new AnimForm(img, from, s.Frame, 380, AnimForm.Mode.Restore, delegate
            {
                Shell.Activate(h);
            });
            a.Start();
        }

        public static void Zoom(IntPtr h)
        {
            bool toMax = !Native.IsZoomed(h);
            if (!Enabled) { ShowWindowAsync(h, toMax ? 3 : 9); return; }
            Rectangle f = Frame(h);
            Bitmap img = Capture(h, f, 1f);
            if (img == null) { ShowWindowAsync(h, toMax ? 3 : 9); return; }
            AnimForm a = new AnimForm(img, f, Rectangle.Empty, 220, AnimForm.Mode.Zoom, null);
            a.Follow = h;
            a.Start();
            ShowWindowAsync(h, toMax ? 3 : 9);
        }
    }

    // Jendela berlapis untuk animasi. Digambar per baris ke DIB 32-bit dengan alfa per piksel,
    // tepi kiri/kanan corong diberi alfa pecahan (anti-alias) agar lengkungannya halus.
    internal sealed class AnimForm : Form
    {
        public enum Mode { Minimize, Restore, Zoom }
        private readonly Bitmap _img;
        private Rectangle _from, _to;
        private readonly int _ms;
        private readonly Mode _mode;
        private readonly Action _done;
        private readonly Stopwatch _sw = new Stopwatch();
        private readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer();
        private bool _finished; private int _tail, _frames;
        public IntPtr Follow;

        private Rectangle _win, _icon, _union;
        private IntPtr _srcDc, _srcBmp, _srcOld, _workDc, _workBmp, _workOld, _bits;
        private int _stride, _imgW, _imgH;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, int usage, out IntPtr bits, IntPtr sec, int off);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("ntdll.dll")] private static extern IntPtr memset(IntPtr dst, int c, UIntPtr n);

        private bool Genie { get { return _mode != Mode.Zoom; } }

        public AnimForm(Bitmap img, Rectangle from, Rectangle to, int ms, Mode mode, Action done)
        {
            _img = img; _from = from; _to = to; _ms = ms; _mode = mode; _done = done;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Rectangle scr = Screen.FromRectangle(from).Bounds;
            if (mode == Mode.Minimize) { _win = from; _icon = to; }
            else if (mode == Mode.Restore) { _win = to; _icon = from; }
            _union = Genie ? Rectangle.Union(_win, _icon) : scr;
            _union.Inflate(2, 2);
            _t.Interval = 10;
            _t.Tick += delegate { Step(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | 0x20 /* TRANSPARENT */;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Start()
        {
            IntPtr scr = GetDC(IntPtr.Zero);
            _srcDc = CreateCompatibleDC(scr);
            _srcBmp = _img.GetHbitmap(Color.Black);
            _imgW = _img.Width; _imgH = _img.Height;   // setelah ini gambar asli tidak dipakai lagi (aman bila dibuang)
            _srcOld = SelectObject(_srcDc, _srcBmp);
            BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)); bi.biWidth = _union.Width; bi.biHeight = -_union.Height;   // top-down
            bi.biPlanes = 1; bi.biBitCount = 32;
            _workDc = CreateCompatibleDC(scr);
            _workBmp = CreateDIBSection(scr, ref bi, 0, out _bits, IntPtr.Zero, 0);
            _workOld = SelectObject(_workDc, _workBmp);
            _stride = _union.Width * 4;
            SetStretchBltMode(_workDc, 3 /* COLORONCOLOR */);
            ReleaseDC(IntPtr.Zero, scr);
            Bounds = _union;
            if (Settings.Get("WindowAnimSound", true)) AnimSound.Play(_mode == Mode.Minimize ? AnimSound.Suck : _mode == Mode.Restore ? AnimSound.Pop : null);
            _sw.Start();
            Step();
            Show();
            _t.Start();
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        private static double Smooth(double v) { v = Clamp01(v); return v * v * (3 - 2 * v); }
        private static double Smoother(double v) { v = Clamp01(v); return v * v * v * (v * (v * 6 - 15) + 10); }
        private static double Lerp(double a, double b, double k) { return a + (b - a) * k; }

        private void Step()
        {
            if (IsDisposed) return;
            if (_finished) { if (++_tail >= 3) Finish(); return; }
            if (_mode == Mode.Zoom && Follow != IntPtr.Zero)
            {
                Rectangle f = WinAnim.Frame(Follow);
                if (f != _from && f.Width > 0) _to = f;
            }
            _frames++;
            double t = Math.Min(1.0, _sw.ElapsedMilliseconds / (double)_ms);
            try { Draw(t); } catch (Exception ex) { Log.Write("anim draw: " + ex.Message); t = 1; }
            if (t >= 1)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug"))) Log.Write("anim " + _mode + " selesai: " + _frames + " frame");
                _finished = true;
                if (_done != null) _done();
                if (_mode != Mode.Restore) Finish();
            }
        }

        // Baris yang digambar frame ini (alfa diisi setelah semua baris disalin, cukup satu GdiFlush).
        private int _nRows;
        private int[] _ry = new int[4096];
        private double[] _rl = new double[4096], _rr = new double[4096];

        // Satu baris tujuan: salin baris sumber ke [l, r).
        private void Row(int y, double l, double r, int sy)
        {
            int yy = y - _union.Top;
            if (yy < 0 || yy >= _union.Height || r - l < 0.5 || _nRows >= _ry.Length) return;
            int x0 = (int)Math.Floor(l), x1 = (int)Math.Ceiling(r);
            if (Math.Min(_union.Width, x1 - _union.Left) <= Math.Max(0, x0 - _union.Left)) return;
            StretchBlt(_workDc, x0 - _union.Left, yy, x1 - x0, 1, _srcDc, 0, sy, _imgW, 1, 0x00CC0020);
            _ry[_nRows] = yy; _rl[_nRows] = l; _rr[_nRows] = r; _nRows++;
        }

        // Isi alfa semua baris; tepi diberi alfa pecahan (anti-alias).
        private unsafe void FillAlpha()
        {
            GdiFlush();
            for (int i = 0; i < _nRows; i++) AlphaRow(_ry[i], _rl[i], _rr[i]);
            _nRows = 0;
        }

        private unsafe void AlphaRow(int yy, double l, double r)
        {
            int x0 = (int)Math.Floor(l), x1 = (int)Math.Ceiling(r);
            int dx0 = Math.Max(0, x0 - _union.Left), dx1 = Math.Min(_union.Width, x1 - _union.Left);
            uint* p = (uint*)((byte*)_bits + yy * _stride);
            for (int x = dx0; x < dx1; x++) p[x] |= 0xFF000000;
            double covL = 1 - (l - x0), covR = 1 - (x1 - r);
            int ix0 = x0 - _union.Left, ix1 = x1 - 1 - _union.Left;
            if (ix0 >= 0 && ix0 < _union.Width) Scale(p + ix0, ix0 == ix1 ? covL * covR : covL);
            if (ix1 != ix0 && ix1 >= 0 && ix1 < _union.Width) Scale(p + ix1, covR);
        }

        private static unsafe void Scale(uint* px, double a)
        {
            if (a >= 0.999) return;
            uint c = *px; double k = Math.Max(0, a);
            uint A = (uint)((c >> 24) * k), R = (uint)(((c >> 16) & 0xFF) * k), G = (uint)(((c >> 8) & 0xFF) * k), B = (uint)((c & 0xFF) * k);
            *px = (A << 24) | (R << 16) | (G << 8) | B;
        }

        private void Clear() { memset(_bits, 0, (UIntPtr)(uint)(_stride * _union.Height)); }

        private void Draw(double t)
        {
            Clear();
            double alpha = 1;
            if (Genie)
            {
                double p = _mode == Mode.Minimize ? t : 1 - t;
                p = Smoother(p) * 0.7 + p * 0.3;
                Rectangle S = _win, T = _icon;
                double A = Smooth(p / 0.55);                       // corong terbentuk
                double B = Smooth((p - 0.28) / 0.72);              // jendela meluncur masuk
                double top = Lerp(S.Top, T.Top, B);
                double bottom = Lerp(S.Bottom, T.Bottom, Smooth(p / 0.62));
                if (bottom - top >= 1)
                {
                    double cs = Lerp(S.Bottom, S.Top - S.Height * 0.25, A);
                    double ce = Math.Max(cs + 1, T.Top);
                    int y0 = (int)Math.Floor(top), y1 = (int)Math.Ceiling(bottom);
                    double span = bottom - top;
                    for (int y = y0; y < y1; y++)
                    {
                        double ym = y + 0.5;
                        double k = ym <= cs ? 0 : Smooth((ym - cs) / (ce - cs));
                        double l = Lerp(S.Left, T.Left, k), r = Lerp(S.Right, T.Right, k);
                        int sy = (int)Math.Min(_imgH - 1, Math.Max(0, (ym - top) / span * _imgH));
                        Row(y, l, r, sy);
                    }
                }
                alpha = p > 0.8 ? Lerp(1, 0.15, (p - 0.8) / 0.2) : 1;
            }
            else
            {
                Rectangle to = _to.IsEmpty ? _from : _to;
                double k = 1 - Math.Pow(1 - t, 3);
                alpha = t < 0.5 ? 1 : 1 - (t - 0.5) * 2;
                double x = Lerp(_from.X, to.X, k), y = Lerp(_from.Y, to.Y, k), w = Lerp(_from.Width, to.Width, k), h = Lerp(_from.Height, to.Height, k);
                int ya = (int)Math.Floor(y), yb = (int)Math.Ceiling(y + h);
                for (int yy = ya; yy < yb; yy++)
                {
                    int sy = (int)Math.Min(_imgH - 1, Math.Max(0, (yy + 0.5 - y) / h * _imgH));
                    Row(yy, x, x + w, sy);
                }
            }
            FillAlpha();
            IntPtr scr = GetDC(IntPtr.Zero);
            try
            {
                Native.SIZE size = new Native.SIZE(_union.Width, _union.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.POINT dst = new Native.POINT(_union.Left, _union.Top);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)(alpha * 255)));
                bf.AlphaFormat = 1;   // AC_SRC_ALPHA: alfa per piksel (premultiplied)
                Native.UpdateLayeredWindow(Handle, scr, ref dst, ref size, _workDc, ref src, 0, ref bf, Native.ULW_ALPHA);
            }
            finally { ReleaseDC(IntPtr.Zero, scr); }
        }

        private void Finish()
        {
            if (IsDisposed) return;
            _t.Stop();
            Hide();
            if (_workDc != IntPtr.Zero) { SelectObject(_workDc, _workOld); DeleteObject(_workBmp); DeleteDC(_workDc); _workDc = IntPtr.Zero; }
            if (_srcDc != IntPtr.Zero) { SelectObject(_srcDc, _srcOld); DeleteObject(_srcBmp); DeleteDC(_srcDc); _srcDc = IntPtr.Zero; }
            if (_mode != Mode.Minimize && _img != null) _img.Dispose();
            Close(); Dispose();
            MemoryTrim.Soon();
        }
    }

    // Suara "tersedot" (minimize) dan "keluar" (buka dari Dock): disintesis, diputar dari memori.
    internal static class AnimSound
    {
        [DllImport("winmm.dll")] private static extern bool PlaySound(byte[] data, IntPtr mod, uint flags);
        private static byte[] _suck, _pop;
        public static byte[] Suck { get { if (_suck == null) _suck = Make(true); return _suck; } }
        public static byte[] Pop { get { if (_pop == null) _pop = Make(false); return _pop; } }

        public static void Play(byte[] wav)
        {
            if (wav == null) return;
            try { PlaySound(wav, IntPtr.Zero, 0x1 | 0x2 | 0x4 /* ASYNC|NODEFAULT|MEMORY */); } catch { }
        }

        // Bunyi "gelembung" lembut seperti di Dynamic Island: masuk = nada meluncur turun, keluar = naik.
        private static byte[] Make(bool suck)
        {
            const int rate = 44100;
            double dur = 0.24;
            int n = (int)(rate * dur);
            short[] smp = new short[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double g = 1 - Math.Exp(-t / 0.045);
                double f = suck ? 560 - 260 * g : 300 + 260 * g;   // lebih rendah dari bunyi island
                ph += 2 * Math.PI * f / rate;
                double env = Math.Min(1, t / 0.012) * Math.Exp(-t / 0.065);
                double v = Math.Sin(ph) * 0.92 + Math.Sin(ph * 1.5) * 0.06;
                smp[i] = (short)(v * env * 0.055 * 32767);
            }
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(ms))
            {
                int bytes = n * 2;
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
                foreach (short v in smp) bw.Write(v);
                bw.Flush();
                return ms.ToArray();
            }
        }

        private static double L(double a, double b, double k) { return a + (b - a) * Math.Max(0, Math.Min(1, k)); }
    }
}
