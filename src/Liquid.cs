using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiquidMac
{
    // ---------- Animasi standby "cair" untuk notch: tetesan, bebek berenang, lampu lava ----------
    internal sealed partial class NotchForm
    {
        private static readonly string[] IdleKinds = { "air", "jantung", "tetes", "bebek", "lava" };

        private float IdleLen()
        {
            switch (_idleKind)
            {
                case "tetes": return 3.95f;
                case "bebek": return 4.8f;
                case "lava": return 4.6f;
                default: return IdleDur;
            }
        }

        private bool DrawIdleLiquid(float x0, float w, float h)
        {
            if (_idleKind == "tetes") { DrawDrip(x0, w, h); return true; }
            if (_idleKind == "bebek") { DrawDuck(x0, w, h); return true; }
            if (_idleKind == "lava") { DrawLava(x0, w, h); return true; }
            return false;
        }

        private GraphicsPath IdlePill(float x0, float w, float h)
        {
            GraphicsPath path = Glass.Round(new RectangleF(x0, 0.5f, w, h), Math.Min(h / 2f, Ui.Pf(30)));
            using (SolidBrush b = new SolidBrush(Color.Black)) G.FillPath(b, path);
            using (Pen rim = new Pen(Color.FromArgb(55, 255, 255, 255), 1f)) G.DrawPath(rim, path);
            return path;
        }

        private static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        // Tetesan: notch "meneteskan" setitik cairan, jatuh, mendarat, melirik kiri-kanan, lalu melompat pulang dan menyatu.
        private void DrawDrip(float x0, float w, float h)
        {
            float t = _idleT, cx = x0 + w / 2f, B = 0.5f + h, R = h * 0.30f;
            float floor = B + Ui.Pf(46);
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
            float dy = -1000, rx = R, ry = R, stretch = 0, look = 0, blink = 1;
            bool neck = false, eyes = false;
            if (t < 0.95f)
            {
                float u = EaseIO(t / 0.95f);
                dy = B - R * 0.7f + u * R * 3.1f; rx = R * (0.55f + 0.45f * u); ry = rx * (1f + 0.15f * u); neck = true; stretch = u;
            }
            else if (t < 1.35f)
            {
                float u = (t - 0.95f) / 0.40f, y0 = B + R * 2.4f;
                dy = y0 + (floor - R - y0) * u * u; rx = R * (1f - 0.14f * u); ry = R * (1f + 0.28f * u);
            }
            else if (t < 1.60f)
            {
                float sq = (float)Math.Sin((t - 1.35f) / 0.25f * Math.PI);
                rx = R * (1f + 0.42f * sq); ry = R * (1f - 0.36f * sq); dy = floor - ry;
            }
            else if (t < 2.65f)
            {
                float u = (t - 1.60f) / 1.05f, br = (float)Math.Sin(u * Math.PI * 4) * 0.04f;
                rx = R * (1.06f + br); ry = R * (0.96f - br); dy = floor - ry; eyes = true;
                look = (float)Math.Sin(u * Math.PI * 2);
                blink = (u > 0.44f && u < 0.52f) || (u > 0.86f && u < 0.92f) ? 0.15f : 1f;
            }
            else if (t < 2.85f)
            {
                float sq = (float)Math.Sin((t - 2.65f) / 0.20f * Math.PI / 2);
                rx = R * (1.06f + 0.36f * sq); ry = R * (0.96f - 0.34f * sq); dy = floor - ry; eyes = true;
            }
            else if (t < 3.25f)
            {
                float u = (t - 2.85f) / 0.40f, e = 1f - (1f - u) * (1f - u), ys = floor - R * 0.62f;
                dy = ys + (B - R * 0.2f - ys) * e; rx = R * (1f - 0.16f * (1f - u)); ry = R * (1f + 0.34f * (1f - u)); eyes = u < 0.45f;
            }

            Region old = G.Clip;
            G.SetClip(new RectangleF(x0 - Ui.Pf(40), B - 0.5f, w + Ui.Pf(80), Ui.Pf(120)), CombineMode.Intersect);   // hanya di bawah notch
            using (SolidBrush ink = new SolidBrush(Color.Black)) using (Pen rim = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
            {
                // riak di "lantai" saat mendarat
                if (t >= 1.35f && t < 2.0f)
                {
                    float u = (t - 1.35f) / 0.65f, rw = R * (1.2f + 2.6f * u);
                    using (Pen rp = new Pen(Color.FromArgb((int)(90 * (1 - u)), 190, 215, 255), Ui.Pf(1.1f))) G.DrawEllipse(rp, cx - rw, floor - R * 0.22f, rw * 2, R * 0.44f);
                }
                // cipratan kecil
                if (t >= 1.35f && t < 1.78f)
                {
                    float u = (t - 1.35f) / 0.43f, sr = R * 0.22f * (1 - u * 0.7f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float sx = cx + s * (R * 1.2f + u * R * 2.0f), sy = floor - R * 0.3f - (float)Math.Sin(u * Math.PI) * R * 1.3f;
                        G.FillEllipse(ink, sx - sr, sy - sr, sr * 2, sr * 2); G.DrawEllipse(rim, sx - sr, sy - sr, sr * 2, sr * 2);
                    }
                }
                // sisa leher yang menyusut setelah tetesan lepas
                if (t >= 0.95f && t < 1.25f)
                {
                    float u = 1f - (t - 0.95f) / 0.30f;
                    RectangleF nb = new RectangleF(cx - R * 1.1f * u, B - R * 0.9f * u, R * 2.2f * u, R * 1.8f * u);
                    G.FillEllipse(ink, nb); G.DrawEllipse(rim, nb);
                }
                // notch bergoyang saat tetesan menyatu kembali
                if (t >= 3.2f)
                {
                    float u = (t - 3.2f) / 0.7f, amp = R * 0.95f * (float)(Math.Exp(-3.5 * u) * Math.Cos(u * 13));
                    if (amp > 0.3f) { RectangleF wb = new RectangleF(cx - R * 1.9f, B - amp, R * 3.8f, amp * 2); G.FillEllipse(ink, wb); G.DrawEllipse(rim, wb); }
                }
                if (dy > -500)
                {
                    using (GraphicsPath dp = new GraphicsPath())
                    {
                        if (neck)
                        {
                            float nbw = R * (2.2f - 0.9f * stretch), nm = R * (0.95f - 0.72f * stretch), my = B + (dy - B) * 0.45f;
                            dp.AddClosedCurve(new PointF[] {
                                new PointF(cx - nbw, B - 2f), new PointF(cx - nm, my), new PointF(cx - rx, dy), new PointF(cx - rx * 0.7f, dy + ry * 0.7f),
                                new PointF(cx, dy + ry), new PointF(cx + rx * 0.7f, dy + ry * 0.7f), new PointF(cx + rx, dy), new PointF(cx + nm, my), new PointF(cx + nbw, B - 2f) }, 0.45f);
                        }
                        else dp.AddEllipse(cx - rx, dy - ry, rx * 2, ry * 2);
                        G.FillPath(ink, dp); G.DrawPath(rim, dp);
                    }
                    // kilau basah
                    using (SolidBrush hl = new SolidBrush(Color.FromArgb(85, 255, 255, 255))) G.FillEllipse(hl, cx - rx * 0.55f, dy - ry * 0.62f, rx * 0.42f, ry * 0.28f);
                    if (eyes)
                    {
                        float er = Math.Max(1.4f, rx * 0.2f), ey = dy - ry * 0.05f;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float ex = cx + s * rx * 0.38f + look * rx * 0.10f;
                            using (SolidBrush wb = new SolidBrush(Color.White)) G.FillEllipse(wb, ex - er, ey - er * blink, er * 2, er * 2 * blink);
                            if (blink > 0.5f) G.FillEllipse(ink, ex - er * 0.45f + look * er * 0.4f, ey - er * 0.45f, er * 0.9f, er * 0.9f);
                        }
                    }
                }
            }
            G.Clip = old; old.Dispose();
        }

        // Bebek: notch terisi air yang bergoyang, bebek karet kuning berenang dari kiri ke kanan, lalu air surut.
        private void DrawDuck(float x0, float w, float h)
        {
            float t = _idleT, top = 0.5f, cx = x0 + w / 2f, r = Math.Min(h / 2f, Ui.Pf(30));
            float fill = t < 0.7f ? EaseIO(t / 0.7f) : t < 4.05f ? 1f : 1f - EaseIO((t - 4.05f) / 0.7f);
            using (GraphicsPath pill = IdlePill(x0, w, h))
            {
                if (fill <= 0.01f) return;
                Region old = G.Clip; G.SetClip(pill, CombineMode.Intersect);
                float level = top + h - fill * h * 0.50f;
                float a1 = h * 0.045f, a2 = h * 0.03f, kx = 0.11f / Ui.Pf(1);
                float tilt = (float)Math.Sin(t * 2.3) * h * 0.16f * fill;
                Func<float, float> surf = delegate (float x)
                {
                    return level + (float)Math.Sin(x * kx + t * 5.2) * a1 + (float)Math.Sin(x * kx * 0.55 - t * 3.3) * a2 + (x - cx) / w * tilt;
                };

                // bebek (digambar dulu: bagian bawahnya terendam air)
                float sc = Clamp01((t - 0.55f) / 0.4f); sc = sc <= 0 ? 0 : EaseBack(sc);
                if (t > 3.85f) sc *= 1f - Clamp01((t - 3.85f) / 0.3f);
                if (sc > 0.02f)
                {
                    float u = Clamp01((t - 0.7f) / 3.1f);
                    float dx = x0 + r * 1.1f + (w - r * 2.2f) * u, dyy = surf(dx);
                    float slope = (surf(dx + 3f) - surf(dx - 3f)) / 6f;
                    float s = h * 0.40f;
                    GraphicsState gs = G.Save();
                    G.TranslateTransform(dx, dyy + s * 0.14f);
                    G.RotateTransform((float)(Math.Atan(slope) * 180 / Math.PI) + (float)Math.Sin(t * 6.5) * 4f);
                    G.ScaleTransform(sc, sc);
                    Color yel = Color.FromArgb(255, 210, 46), yelD = Color.FromArgb(236, 172, 20);
                    using (SolidBrush yb = new SolidBrush(yel)) using (SolidBrush ob = new SolidBrush(Color.FromArgb(255, 138, 30)))
                    {
                        G.FillPolygon(yb, new PointF[] { new PointF(-0.58f * s, -0.30f * s), new PointF(-0.90f * s, -0.62f * s), new PointF(-0.40f * s, -0.46f * s) });   // ekor
                        G.FillEllipse(yb, -0.68f * s, -0.56f * s, 1.32f * s, 0.78f * s);          // badan
                        G.FillEllipse(yb, 0.10f * s, -0.98f * s, 0.60f * s, 0.60f * s);           // kepala
                        G.FillPolygon(ob, new PointF[] { new PointF(0.64f * s, -0.74f * s), new PointF(0.96f * s, -0.64f * s), new PointF(0.64f * s, -0.54f * s) });      // paruh
                    }
                    using (Pen wp = new Pen(yelD, Math.Max(1f, s * 0.09f))) { wp.StartCap = wp.EndCap = LineCap.Round; G.DrawArc(wp, -0.38f * s, -0.42f * s, 0.62f * s, 0.40f * s, 15, 150); }   // sayap
                    float er = Math.Max(1f, s * 0.06f);
                    using (SolidBrush eb = new SolidBrush(Color.FromArgb(40, 30, 20))) G.FillEllipse(eb, 0.46f * s - er, -0.72f * s - er, er * 2, er * 2);
                    G.Restore(gs);
                }

                // air
                List<PointF> pts = new List<PointF>();
                for (float x = x0 - 2; x <= x0 + w + 4; x += 3f) pts.Add(new PointF(x, surf(x)));
                PointF[] line = pts.ToArray();
                pts.Add(new PointF(x0 + w + 4, top + h + 2)); pts.Add(new PointF(x0 - 2, top + h + 2));
                using (LinearGradientBrush wb = new LinearGradientBrush(new RectangleF(x0, level - a1 - a2 - h * 0.2f, w, h), Color.FromArgb(205, 96, 196, 255), Color.FromArgb(235, 24, 96, 236), 90f))
                    G.FillPolygon(wb, pts.ToArray());
                using (Pen sp = new Pen(Color.FromArgb(170, 215, 240, 255), Ui.Pf(1.1f))) G.DrawLines(sp, line);
                // gelembung naik
                for (int i = 0; i < 6; i++)
                {
                    float ph = (t * (0.55f + i * 0.07f) + i * 0.37f) % 1f;
                    float bx = x0 + r * 0.8f + (w - r * 1.6f) * ((i * 0.618f + 0.13f) % 1f) + (float)Math.Sin(t * 3 + i) * Ui.Pf(1.5f);
                    float by = top + h - ph * (top + h - surf(bx)), br = Ui.Pf(0.9f + (i % 3) * 0.45f);
                    using (Pen bp = new Pen(Color.FromArgb((int)(150 * fill * (1 - ph * 0.6f)), 225, 245, 255), 1f)) G.DrawEllipse(bp, bx - br, by - br, br * 2, br * 2);
                }
                G.Clip = old; old.Dispose();
                using (Pen rim = new Pen(Color.FromArgb(55, 255, 255, 255), 1f)) G.DrawPath(rim, pill);
            }
        }

        // Lampu lava: gumpalan cairan berwarna menyatu dan memisah di dalam notch (metaball).
        private Bitmap _lavaBmp; private int[] _lavaBuf;
        private static readonly float[,] LavaB = {   // amplitudo-x, kecepatan-x, fase-x, kecepatan-y, fase-y, jari-jari
            { 0.86f, 0.93f, 0.0f, 1.9f, 0.4f, 0.30f }, { 0.70f, 1.31f, 2.1f, 2.4f, 1.7f, 0.25f }, { 0.92f, 0.71f, 4.0f, 1.5f, 3.1f, 0.33f },
            { 0.50f, 1.67f, 1.2f, 2.9f, 5.0f, 0.22f }, { 0.78f, 1.13f, 5.3f, 2.1f, 2.2f, 0.27f }, { 0.30f, 0.83f, 3.3f, 1.7f, 0.9f, 0.29f } };

        private void DrawLava(float x0, float w, float h)
        {
            float t = _idleT, len = IdleLen();
            float env = Math.Min(Clamp01(t / 0.6f), Clamp01((len - t) / 0.6f));
            env = env * env * (3 - 2 * env);
            using (GraphicsPath pill = IdlePill(x0, w, h))
            {
                if (env <= 0.01f) return;
                int bw = Math.Max(2, (int)Math.Ceiling(w)), bh = Math.Max(2, (int)Math.Ceiling(h));
                if (_lavaBmp == null || _lavaBmp.Width != bw || _lavaBmp.Height != bh)
                {
                    if (_lavaBmp != null) _lavaBmp.Dispose();
                    _lavaBmp = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb); _lavaBuf = new int[bw * bh];
                }
                int n = LavaB.GetLength(0);
                float[] bx = new float[n], by = new float[n], br = new float[n];
                float rad = Math.Min(h / 2f, Ui.Pf(30));
                for (int i = 0; i < n; i++)
                {
                    bx[i] = bw / 2f + (float)Math.Sin(t * LavaB[i, 1] + LavaB[i, 2]) * (bw / 2f - rad * 0.7f) * LavaB[i, 0];
                    by[i] = bh / 2f + (float)Math.Sin(t * LavaB[i, 3] + LavaB[i, 4]) * bh * 0.30f;
                    float rr = bh * LavaB[i, 5] * env; br[i] = rr * rr;
                }
                float shift = (float)Math.Sin(t * 0.9) * 0.12f;
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                    {
                        float f = 0;
                        for (int i = 0; i < n; i++) { float dx = x - bx[i], dy = y - by[i]; f += br[i] / (dx * dx + dy * dy + 0.5f); }
                        if (f < 0.82f) { _lavaBuf[y * bw + x] = 0; continue; }
                        float a = Clamp01((f - 0.82f) / 0.30f); a = a * a * (3 - 2 * a);
                        float u = Clamp01(x / (float)bw + shift), cr, cg, cb;
                        if (u < 0.5f) { float k = u * 2; cr = 130 + (255 - 130) * k; cg = 96 + (86 - 96) * k; cb = 255 + (176 - 255) * k; }
                        else { float k = (u - 0.5f) * 2; cr = 255; cg = 86 + (176 - 86) * k; cb = 176 + (70 - 176) * k; }
                        float glow = Clamp01((f - 1.1f) * 0.45f) * 0.45f + (1f - y / (float)bh) * 0.10f;   // inti lebih terang, sisi atas berkilau
                        cr += (255 - cr) * glow; cg += (255 - cg) * glow; cb += (255 - cb) * glow;
                        int A = (int)(a * 255);
                        _lavaBuf[y * bw + x] = (A << 24) | ((int)(cr * a) << 16) | ((int)(cg * a) << 8) | (int)(cb * a);
                    }
                BitmapData d = _lavaBmp.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                for (int y = 0; y < bh; y++) Marshal.Copy(_lavaBuf, y * bw, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), bw);
                _lavaBmp.UnlockBits(d);
                Region old = G.Clip; G.SetClip(pill, CombineMode.Intersect);
                G.DrawImageUnscaled(_lavaBmp, (int)Math.Round(x0), 0);
                G.Clip = old; old.Dispose();
                using (Pen rim = new Pen(Color.FromArgb(55, 255, 255, 255), 1f)) G.DrawPath(rim, pill);
            }
        }
    }

    // ---------- Dock: gaya ikon & lampu LED ----------
    internal sealed partial class DockForm
    {
        public static readonly string[] IconStyles = { "bawaan", "minimal", "kaca", "bulat" };
        public static string IconStyleName(string st)
        {
            switch (st) { case "minimal": return "Minimalis"; case "kaca": return "Kaca"; case "bulat": return "Bulat"; default: return "Bawaan"; }
        }

        // Ikon mentah -> ikon Dock bergaya (128x128). null = gaya bawaan (tidak diubah).
        private static Bitmap StyleTile(Bitmap raw)
        {
            string st = Settings.GetS("DockIconStyle", "bawaan");
            if (raw == null || st == "bawaan" || Array.IndexOf(IconStyles, st) < 0) return null;
            Bitmap o = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (st == "minimal")
                {
                    // kotak matte gelap + garis tepi sehalus rambut, ikon monokrom terang: bersih dan tenang
                    RectangleF r = new RectangleF(9, 9, 110, 110);
                    using (GraphicsPath p = Glass.Round(r, 27f))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(244, 60, 60, 68), Color.FromArgb(244, 32, 32, 38), 90f)) g.FillPath(b, p);
                        using (LinearGradientBrush pb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(16, 255, 255, 255), 90f))
                        using (Pen pen = new Pen(pb, 1.6f)) g.DrawPath(pen, p);
                    }
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ColorMatrix cm = new ColorMatrix(new float[][] {
                            new float[] { 0.18f, 0.18f, 0.18f, 0, 0 }, new float[] { 0.36f, 0.36f, 0.36f, 0, 0 }, new float[] { 0.07f, 0.07f, 0.07f, 0, 0 },
                            new float[] { 0, 0, 0, 1, 0 }, new float[] { 0.40f, 0.40f, 0.42f, 0, 1 } });
                        ia.SetColorMatrix(cm);
                        g.DrawImage(raw, new Rectangle(33, 33, 62, 62), 0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                else if (st == "kaca")
                {
                    // kotak kaca bening: isi tembus pandang, kilau di atas, tepi terang
                    RectangleF r = new RectangleF(8, 8, 112, 112);
                    using (GraphicsPath p = Glass.Round(r, 28f))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(96, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), 90f)) g.FillPath(b, p);
                        Region old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                        using (GraphicsPath sh = new GraphicsPath())
                        {
                            sh.AddEllipse(r.X - 30, r.Y - 62, r.Width + 60, 104);
                            using (LinearGradientBrush sb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, 46), Color.FromArgb(78, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(sb, sh);
                        }
                        g.Clip = old; old.Dispose();
                        using (LinearGradientBrush pb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(190, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), 90f))
                        using (Pen pen = new Pen(pb, 2f)) g.DrawPath(pen, p);
                    }
                    g.DrawImage(raw, new Rectangle(29, 29, 70, 70));
                }
                else
                {
                    // lingkaran gelap bernuansa warna ikonnya
                    Color avg = AvgColor(raw);
                    Color c1 = Blend(avg, Color.FromArgb(52, 52, 60), 0.62), c2 = Blend(avg, Color.FromArgb(18, 18, 22), 0.72);
                    RectangleF r = new RectangleF(7, 7, 114, 114);
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(246, c1), Color.FromArgb(246, c2), 90f)) g.FillEllipse(b, r);
                    using (LinearGradientBrush pb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(22, 255, 255, 255), 90f))
                    using (Pen pen = new Pen(pb, 1.8f)) g.DrawEllipse(pen, r);
                    g.DrawImage(raw, new Rectangle(32, 32, 64, 64));
                }
            }
            return o;
        }

        private static Color Blend(Color a, Color b, double k) { return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k)); }

        private static Color AvgColor(Bitmap raw)
        {
            try
            {
                using (Bitmap s = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(s)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(raw, 0, 0, 8, 8); }
                    double r = 0, gg = 0, b = 0, wsum = 0;
                    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        {
                            Color c = s.GetPixel(x, y);
                            double sat = (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))) / 255.0, wgt = c.A / 255.0 * (0.15 + sat);
                            r += c.R * wgt; gg += c.G * wgt; b += c.B * wgt; wsum += wgt;
                        }
                    if (wsum > 0.01) return Color.FromArgb((int)(r / wsum), (int)(gg / wsum), (int)(b / wsum));
                }
            }
            catch { }
            return Color.FromArgb(90, 90, 110);
        }

        // Ikon yang tidak lewat MakeTile (Launchpad): ikut gaya bila bukan bawaan.
        private static Bitmap Restyle(Bitmap icon)
        {
            Bitmap o = StyleTile(icon);
            if (o == null) return icon;
            icon.Dispose(); return o;
        }

        // uji: simpan Dock dengan tiap gaya ikon & mode LED (pengaturan dikembalikan setelahnya)
        public void SnapshotStyles(string dir)
        {
            string st0 = Settings.GetS("DockIconStyle", "bawaan"), led0 = Settings.GetS("DockLed", "off");
            foreach (string st in IconStyles) { Settings.SetTemp("DockIconStyle", st); ReloadItems(); Snapshot(System.IO.Path.Combine(dir, "dock-" + st + ".png")); }
            Settings.SetTemp("DockIconStyle", st0); ReloadItems();
            foreach (string m in new string[] { "glow", "run" }) { Settings.SetTemp("DockLed", m); Snapshot(System.IO.Path.Combine(dir, "dock-led-" + m + ".png")); }
            Settings.SetTemp("DockLed", led0);
        }

        // ----- lampu LED di bawah ikon: "glow" (menyala, bernapas) atau "run" (berjalan dari ujung ke ujung) -----
        private static string LedMode { get { string m = Settings.GetS("DockLed", "off"); return m == "glow" || m == "run" ? m : "off"; } }

        private static Color Hsv(double h, double s, double v)
        {
            h = (h % 1 + 1) % 1 * 6; int i = (int)h; double f = h - i, p = v * (1 - s), q = v * (1 - s * f), u = v * (1 - s * (1 - f)), r, g, b;
            switch (i) { case 0: r = v; g = u; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = u; break; case 3: r = p; g = q; b = v; break; case 4: r = u; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        private static double LedPos(double ts)
        {
            double ph = (ts * 0.42) % 2; if (ph < 0) ph += 2;
            double u = ph < 1 ? ph : 2 - ph;
            return 0.5 - 0.5 * Math.Cos(Math.PI * u);
        }

        private void DrawLed(float baseline, string mode)
        {
            float l = _bgX + PadX * 0.8f, r = _bgX + _bgW - PadX * 0.8f;
            if (r - l < Ui.Pf(40)) return;
            float y;
            if (Settings.Get("DockShelf", true))
            {
                float depth = Math.Max(Ui.Pf(7), IconBase * 0.17f), lip = Math.Max(Ui.Pf(2.5f), IconBase * 0.055f);
                y = baseline - depth * 0.55f + depth + lip * 0.5f;   // di bibir depan papan
            }
            else y = baseline + Ui.Pf(2.5f);
            double ts = (Environment.TickCount & 0x7FFFFFFF) / 1000.0;
            if (mode == "glow")
            {
                double breath = 0.62 + 0.38 * Math.Sin(ts * 1.5);
                float[] widths = { Ui.Pf(11), Ui.Pf(5.5f), Ui.Pf(1.9f) }; double[] alphas = { 26, 70, 235 };
                for (int pass = 0; pass < 3; pass++)
                    using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(l - 1, y), new PointF(r + 1, y), Color.White, Color.White))
                    {
                        ColorBlend cb = new ColorBlend(7); int a = (int)(alphas[pass] * breath);
                        for (int i = 0; i < 7; i++) { cb.Positions[i] = i / 6f; cb.Colors[i] = Color.FromArgb(a, Hsv(i / 6.0 * 0.55 + ts * 0.05, pass == 2 ? 0.45 : 0.8, 1)); }
                        lb.InterpolationColors = cb;
                        using (Pen p = new Pen(lb, widths[pass])) { p.StartCap = p.EndCap = LineCap.Round; G.DrawLine(p, l, y, r, y); }
                    }
                return;
            }
            // berjalan: rel redup + komet berekor yang bolak-balik
            using (Pen rail = new Pen(Color.FromArgb(34, 255, 255, 255), Ui.Pf(1.4f))) { rail.StartCap = rail.EndCap = LineCap.Round; G.DrawLine(rail, l, y, r, y); }
            Color col = Hsv(ts * 0.07, 0.75, 1), core = Hsv(ts * 0.07, 0.25, 1);
            const int K = 16;
            float prev = l + (float)(LedPos(ts) * (r - l));
            float head = prev;
            for (int i = 1; i <= K; i++)
            {
                float x = l + (float)(LedPos(ts - i * 0.024) * (r - l));
                double k = 1.0 - i / (double)K;
                if (Math.Abs(x - prev) > 0.05f)
                {
                    using (Pen gp = new Pen(Color.FromArgb((int)(70 * k * k), col), Ui.Pf(7f))) { gp.StartCap = gp.EndCap = LineCap.Round; G.DrawLine(gp, prev, y, x, y); }
                    using (Pen tp = new Pen(Color.FromArgb((int)(235 * k * k), col), Ui.Pf(1f + 1.6f * (float)k))) { tp.StartCap = tp.EndCap = LineCap.Round; G.DrawLine(tp, prev, y, x, y); }
                }
                prev = x;
            }
            float gr = Math.Max(Ui.Pf(16), IconBase * 0.42f);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(head - gr, y - gr * 0.62f, gr * 2, gr * 1.24f);
                using (PathGradientBrush pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(150, col); pg.SurroundColors = new Color[] { Color.FromArgb(0, col) }; G.FillPath(pg, gp); }
            }
            float hr = Ui.Pf(2.3f);
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(255, core))) G.FillEllipse(hb, head - hr, y - hr, hr * 2, hr * 2);
        }
    }
}
