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
            while (_order.Count > 6) Forget(_order[0]);
        }

        public static void Forget(IntPtr h)
        {
            Snap s;
            if (_snaps.TryGetValue(h, out s)) { if (s.Img != null) s.Img.Dispose(); _snaps.Remove(h); }
            _order.Remove(h);
        }

        // ---------- aksi ----------
        public static void Minimize(IntPtr h)
        {
            if (!Enabled || !Native.IsWindow(h)) { ShowWindowAsync(h, 6); return; }
            Rectangle f = Frame(h);
            Stopwatch dsw = Stopwatch.StartNew();
            Bitmap img = Capture(h, f, 0.5f);
            if (Environment.GetEnvironmentVariable("LIQUIDMAC_DEBUG") == "1" || System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug"))) Log.Write("anim min frame=" + f + " img=" + (img != null) + " enabled=" + Enabled + " capture_ms=" + dsw.ElapsedMilliseconds);
            if (img == null) { ShowWindowAsync(h, 6); return; }
            Rectangle to = TargetFor(h, f);
            Keep(h, new Snap { Img = img, Frame = f });
            AnimForm a = new AnimForm(img, f, to, 260, AnimForm.Mode.Minimize, null);
            a.Start();
            ShowWindowAsync(h, 6);
        }

        public static void Restore(IntPtr h, Rectangle from)
        {
            Snap s;
            if (!Enabled || !Native.IsIconic(h) || !_snaps.TryGetValue(h, out s)) { Shell.Activate(h); return; }
            _snaps.Remove(h); _order.Remove(h);
            if (from.IsEmpty) from = TargetFor(h, s.Frame);
            Bitmap img = s.Img;
            AnimForm a = new AnimForm(img, from, s.Frame, 240, AnimForm.Mode.Restore, delegate
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
            Bitmap img = Capture(h, f, 0.6f);
            if (img == null) { ShowWindowAsync(h, toMax ? 3 : 9); return; }
            AnimForm a = new AnimForm(img, f, Rectangle.Empty, 220, AnimForm.Mode.Zoom, null);
            a.Follow = h;
            a.Start();
            ShowWindowAsync(h, toMax ? 3 : 9);
        }
    }

    // Jendela berlapis seukuran gambar yang sedang dianimasikan (tembus klik, tidak mengambil fokus).
    // Setiap frame: StretchBlt dari gambar sumber ke DIB kerja, lalu UpdateLayeredWindow dengan alfa konstan.
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

        private IntPtr _srcDc, _srcBmp, _srcOld, _workDc, _workBmp, _workOld;
        private int _maxW, _maxH;

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr p);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);

        public AnimForm(Bitmap img, Rectangle from, Rectangle to, int ms, Mode mode, Action done)
        {
            _img = img; _from = from; _to = to; _ms = ms; _mode = mode; _done = done;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = from;
            Rectangle scr = Screen.FromRectangle(from).Bounds;
            _maxW = Math.Max(1, Math.Max(scr.Width, from.Width)); _maxH = Math.Max(1, Math.Max(scr.Height, from.Height));
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
            _srcOld = SelectObject(_srcDc, _srcBmp);
            _workDc = CreateCompatibleDC(scr);
            _workBmp = CreateCompatibleBitmap(scr, _maxW, _maxH);
            _workOld = SelectObject(_workDc, _workBmp);
            SetStretchBltMode(_workDc, 3 /* COLORONCOLOR: cepat, cukup halus saat bergerak */);
            ReleaseDC(IntPtr.Zero, scr);
            _sw.Start();
            Show();
            Step();
            _t.Start();
        }

        private static double EaseIn(double t) { return t * t * t; }
        private static double EaseOut(double t) { double u = 1 - t; return 1 - u * u * u; }

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
            Draw(t);
            if (t >= 1)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug"))) Log.Write("anim " + _mode + " selesai: " + _frames + " frame");
                _finished = true;
                if (_done != null) _done();
                if (_mode != Mode.Restore) Finish();
            }
        }

        private void Draw(double t)
        {
            Rectangle to = _to.IsEmpty ? _from : _to;
            double k; double alpha;
            switch (_mode)
            {
                case Mode.Minimize: k = EaseIn(t); alpha = 1 - 0.85 * k; break;
                case Mode.Restore: k = EaseOut(t); alpha = 0.25 + 0.75 * k; break;
                default: k = EaseOut(t); alpha = t < 0.5 ? 1 : 1 - (t - 0.5) * 2; break;
            }
            int x = (int)Math.Round(_from.X + (to.X - _from.X) * k);
            int y = (int)Math.Round(_from.Y + (to.Y - _from.Y) * k);
            int w = Math.Max(2, Math.Min(_maxW, (int)Math.Round(_from.Width + (to.Width - _from.Width) * k)));
            int h = Math.Max(2, Math.Min(_maxH, (int)Math.Round(_from.Height + (to.Height - _from.Height) * k)));
            StretchBlt(_workDc, 0, 0, w, h, _srcDc, 0, 0, _img.Width, _img.Height, 0x00CC0020);
            IntPtr scr = GetDC(IntPtr.Zero);
            try
            {
                Native.SIZE size = new Native.SIZE(w, h);
                Native.POINT src = new Native.POINT(0, 0);
                Native.POINT dst = new Native.POINT(x, y);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)(alpha * 255)));
                bf.AlphaFormat = 0;   // alfa konstan untuk seluruh gambar
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
        }
    }
}
