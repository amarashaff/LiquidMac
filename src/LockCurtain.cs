using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiquidMac
{
    // Layar kunci animasi (setting LockAnim, bawaan mati). Windows tidak mengizinkan animasi di layar kunci asli,
    // jadi saat mengunci lewat LiquidMac tampil tirai layar penuh dulu: wallpaper cair bergerak, jam besar, cuaca.
    // Klik / tombol apa saja / geser mouse jauh -> layar kunci Windows asli (sandi). Tanpa sentuhan 60 dtk -> dikunci asli.
    internal sealed class LockCurtain : Form
    {
        private static LockCurtain _open;
        private readonly Timer _t = new Timer();
        private readonly DateTime _start = DateTime.Now;
        private const int AutoLockSec = 60;
        private Bitmap _wall, _blob;
        private Color[] _pal;
        private Point _mouse0 = new Point(int.MinValue, 0);
        private bool _leaving;
        private float _fadeIn;

        public static void ShowCurtain()
        {
            if (_open != null && !_open.IsDisposed) { _open.Activate(); return; }
            try { _open = new LockCurtain(); _open.Show(); _open.Activate(); }
            catch (Exception ex) { Log.Write("tirai kunci: " + ex.Message); MacLock.LockReal(); }
        }

        private LockCurtain()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = Color.Black; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            try { Weather.Refresh(false); } catch { }
            BuildPalette();
            _blob = new Bitmap(192, 108, PixelFormat.Format32bppRgb);
            _t.Interval = 33;
            _t.Tick += delegate { Tick(); };
            _t.Start();
            Cursor.Hide();
        }

        // Warna wallpaper (rata-rata 4 kuadran + tengah) jadi warna gumpalan cair; tanpa wallpaper: palet ala macOS.
        private void BuildPalette()
        {
            Bitmap src = null;
            try { src = MacLock.WallpaperSmall(); } catch { }
            if (src != null)
            {
                _wall = src;
                Color[] c = new Color[5];
                int W = src.Width, H = src.Height;
                Rectangle[] q = { new Rectangle(0, 0, W / 2, H / 2), new Rectangle(W / 2, 0, W / 2, H / 2), new Rectangle(0, H / 2, W / 2, H / 2), new Rectangle(W / 2, H / 2, W / 2, H / 2), new Rectangle(W / 4, H / 4, W / 2, H / 2) };
                for (int i = 0; i < 5; i++) c[i] = Boost(Avg(src, q[i]));
                _pal = c;
            }
            else _pal = new Color[] { Color.FromArgb(40, 70, 190), Color.FromArgb(150, 60, 190), Color.FromArgb(20, 150, 170), Color.FromArgb(230, 110, 80), Color.FromArgb(60, 40, 130) };
        }

        private static Color Avg(Bitmap b, Rectangle r)
        {
            long R = 0, G = 0, B = 0, n = 0;
            for (int y = r.Top; y < r.Bottom; y += 3) for (int x = r.Left; x < r.Right; x += 3) { Color c = b.GetPixel(x, y); R += c.R; G += c.G; B += c.B; n++; }
            if (n == 0) return Color.FromArgb(60, 60, 120);
            return Color.FromArgb((int)(R / n), (int)(G / n), (int)(B / n));
        }

        // sedikit lebih jenuh agar gumpalan terlihat hidup
        private static Color Boost(Color c)
        {
            float avg = (c.R + c.G + c.B) / 3f;
            Func<int, int> f = delegate (int v) { return Math.Max(0, Math.Min(255, (int)(avg + (v - avg) * 1.5f + 8))); };
            return Color.FromArgb(f(c.R), f(c.G), f(c.B));
        }

        private void Tick()
        {
            if (_leaving) return;
            if ((DateTime.Now - _start).TotalSeconds >= AutoLockSec) { Leave(); return; }
            _fadeIn = Math.Min(1f, _fadeIn + 0.08f);
            Invalidate();
        }

        // Wallpaper cair: 5 gumpalan bergerak pelan di bitmap kecil, lalu diperbesar halus (gradasi lembut, murah).
        private void DrawBlobs(double t)
        {
            using (Graphics g = Graphics.FromImage(_blob))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Mix(_pal[4], Color.Black, 0.45f));
                int W = _blob.Width, H = _blob.Height;
                for (int i = 0; i < 5; i++)
                {
                    double ph = i * 1.7;
                    float cx = (float)(W * (0.5 + 0.38 * Math.Sin(t * (0.11 + i * 0.023) + ph)));
                    float cy = (float)(H * (0.5 + 0.36 * Math.Cos(t * (0.09 + i * 0.019) + ph * 1.3)));
                    float r = (float)(H * (0.55 + 0.18 * Math.Sin(t * 0.21 + i)));
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        using (PathGradientBrush pg = new PathGradientBrush(gp))
                        {
                            pg.CenterColor = Color.FromArgb(200, _pal[i]);
                            pg.SurroundColors = new Color[] { Color.FromArgb(0, _pal[i]) };
                            g.FillPath(pg, gp);
                        }
                    }
                }
            }
        }

        private static Color Mix(Color a, Color b, float k)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            double t = (DateTime.Now - _start).TotalSeconds + 7 + _tShift;
            DrawBlobs(t);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_blob, new Rectangle(-8, -8, w + 16, h + 16));
            // wallpaper asli samar di atasnya, bernapas pelan
            if (_wall != null)
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = (float)(0.22 + 0.08 * Math.Sin(t * 0.4));
                    ia.SetColorMatrix(cm);
                    g.DrawImage(_wall, new Rectangle(0, 0, w, h), 0, 0, _wall.Width, _wall.Height, GraphicsUnit.Pixel, ia);
                }
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int a = (int)(255 * _fadeIn);
            DateTime now = DateTime.Now;
            string fam = MacFont.Present ? MacFont.Family : "Segoe UI";
            using (Font df = new Font(MacFont.Present ? MacFont.Semi : "Segoe UI Semibold", h * 0.024f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font tf = new Font(MacFont.Present ? MacFont.Semi : "Segoe UI Semibold", h * 0.135f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font hf = new Font(fam, h * 0.0145f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                string date = now.ToString("dddd, d MMMM", L.En ? new System.Globalization.CultureInfo("en-US") : new System.Globalization.CultureInfo("id-ID"));
                using (SolidBrush db = new SolidBrush(Color.FromArgb(a * 230 / 255, 255, 255, 255))) g.DrawString(date, df, db, new RectangleF(0, h * 0.085f, w, h * 0.05f), sf);
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(a * 240 / 255, 255, 255, 255))) g.DrawString(now.ToString("HH:mm"), tf, tb, new RectangleF(0, h * 0.11f, w, h * 0.2f), sf);
                if (_fadeIn >= 1f) MacLock.DrawWeather(g, w, h, h * 0.36f);
                float k = (float)(0.55 + 0.35 * Math.Sin(t * 2.2));
                using (SolidBrush hb = new SolidBrush(Color.FromArgb((int)(a * k * 0.8f), 255, 255, 255)))
                    g.DrawString(L.T("Klik atau tekan tombol apa saja untuk membuka"), hf, hb, new RectangleF(0, h * 0.90f, w, h * 0.04f), sf);
            }
        }

        private void Leave()
        {
            if (_leaving) return;
            _leaving = true; _t.Stop();
            Cursor.Show();
            MacLock.LockReal();
            // tutup sedikit setelah layar kunci asli muncul, supaya desktop tidak sempat terlihat
            Timer c = new Timer(); c.Interval = 700;
            c.Tick += delegate { c.Stop(); c.Dispose(); Close(); };
            c.Start();
        }

        protected override void OnKeyDown(KeyEventArgs e) { e.Handled = true; Leave(); }
        protected override void OnMouseDown(MouseEventArgs e) { Leave(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mouse0.X == int.MinValue) { _mouse0 = e.Location; return; }
            if (Math.Abs(e.X - _mouse0.X) + Math.Abs(e.Y - _mouse0.Y) > 60) Leave();
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!_leaving) Leave(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _t.Stop(); _t.Dispose();
            if (_blob != null) { _blob.Dispose(); _blob = null; }
            if (_wall != null) { _wall.Dispose(); _wall = null; }
            if (!_leaving) Cursor.Show();
            if (_open == this) _open = null;
            MemoryTrim.Soon();
        }

        // uji: render satu frame ke PNG
        public static void Snapshot(string path, double t)
        {
            using (LockCurtain c = new LockCurtain())
            {
                Cursor.Show(); c._t.Stop(); c._fadeIn = 1f;
                Rectangle s = Screen.PrimaryScreen.Bounds;
                using (Bitmap b = new Bitmap(s.Width, s.Height))
                using (Graphics g = Graphics.FromImage(b))
                {
                    c.ClientSize = s.Size;
                    c._tShift = t;
                    c.OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, s.Size)));
                    b.Save(path, ImageFormat.Png);
                }
            }
        }
        private double _tShift;
    }
}
