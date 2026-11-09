using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  PENGINGAT DONASI (seminggu sekali, hilang selamanya setelah tautan diklik)
    // ============================================================
    internal sealed class DonateForm : GlassPopup
    {
        private readonly Font _title = Ui.F(17f, FontStyle.Bold), _t = Ui.F(10.5f, FontStyle.Regular), _b = Ui.F(11f, FontStyle.Bold), _s = Ui.F(9f, FontStyle.Regular);
        private readonly Font _note = new Font("Segoe UI Symbol", Ui.Pf(22), FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _xf = Glyphs.Font(Ui.Pf(11));
        private RectangleF _later, _go, _close;
        private int _hover;   // 0 none, 1 nanti, 2 donasi, 3 tutup
        private readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer();
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        public static int W { get { return Ui.P(520); } }
        public static int H { get { return Ui.P(600); } }

        public DonateForm()
        {
            Text = "Dukung LiquidMac";
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
            _anim.Interval = 16;
            _anim.Tick += delegate
            {
                double t = _sw.Elapsed.TotalSeconds;
                if (t < 0.25) Opacity = Math.Min(1, t / 0.25);
                Invalidate();
            };
        }

        public void Popup()
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            Opacity = 0;
            ShowAt(new Rectangle(s.Left + (s.Width - W) / 2, s.Top + (s.Height - H) / 2, W, H), 0.5f);
            Round(true);
            _sw.Restart(); _anim.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { _anim.Stop(); _sw.Stop(); }
        }

        protected override void OnPaint(PaintEventArgs e) { PaintAll(e.Graphics, Width, Height, _sw.Elapsed.TotalSeconds); }

        // ---------- kucing joget (digambar sendiri) ----------
        private static void Cat(Graphics g, float cx, float cy, float s, double t)
        {
            double beat = t * Math.PI * 2 * 1.9;                     // ~114 bpm
            float bounce = (float)(Math.Abs(Math.Sin(beat)) * s * 0.10);
            float tilt = (float)(Math.Sin(beat) * 11);
            float sway = (float)(Math.Sin(beat) * s * 0.08);
            Color fur = Color.FromArgb(255, 168, 72), furD = Color.FromArgb(226, 122, 40), belly = Color.FromArgb(255, 226, 180);
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                float shw = s * (0.95f - bounce / s * 1.4f);
                g.FillEllipse(sh, cx - shw / 2f, cy + s * 0.98f, shw, s * 0.14f);
            }
            GraphicsState st = g.Save();
            g.TranslateTransform(cx + sway, cy - bounce);
            g.RotateTransform(tilt * 0.45f);
            using (SolidBrush fb = new SolidBrush(fur)) using (SolidBrush fd = new SolidBrush(furD)) using (SolidBrush bb = new SolidBrush(belly))
            using (Pen line = new Pen(Color.FromArgb(120, 70, 30), s * 0.035f))
            {
                line.StartCap = line.EndCap = LineCap.Round;
                // ekor melambai
                float tw = (float)Math.Sin(beat * 0.5 + 1) * s * 0.35f;
                using (Pen tail = new Pen(furD, s * 0.13f)) { tail.StartCap = tail.EndCap = LineCap.Round; g.DrawBezier(tail, s * 0.32f, s * 0.70f, s * 0.80f, s * 0.70f, s * 0.70f + tw * 0.4f, s * 0.15f, s * 0.55f + tw, -s * 0.05f); }
                // kaki bergantian
                float lk = (float)Math.Max(0, Math.Sin(beat)) * s * 0.08f, rk = (float)Math.Max(0, -Math.Sin(beat)) * s * 0.08f;
                g.FillEllipse(fd, -s * 0.36f, s * 0.80f - lk, s * 0.30f, s * 0.20f);
                g.FillEllipse(fd, s * 0.06f, s * 0.80f - rk, s * 0.30f, s * 0.20f);
                // badan
                g.FillEllipse(fb, -s * 0.42f, s * 0.10f, s * 0.84f, s * 0.86f);
                g.FillEllipse(bb, -s * 0.24f, s * 0.30f, s * 0.48f, s * 0.58f);
                // tangan naik-turun (joget)
                float aL = (float)Math.Sin(beat) * 55 - 35, aR = (float)-Math.Sin(beat) * 55 - 35;
                Arm(g, fb, -s * 0.34f, s * 0.32f, s, 180 - aL);
                Arm(g, fb, s * 0.34f, s * 0.32f, s, aR);
                // kepala
                GraphicsState hs = g.Save();
                g.TranslateTransform(0, -s * 0.08f);
                g.RotateTransform(tilt);
                PointF[] earL = { new PointF(-s * 0.46f, -s * 0.30f), new PointF(-s * 0.40f, -s * 0.78f), new PointF(-s * 0.12f, -s * 0.48f) };
                PointF[] earR = { new PointF(s * 0.46f, -s * 0.30f), new PointF(s * 0.40f, -s * 0.78f), new PointF(s * 0.12f, -s * 0.48f) };
                g.FillPolygon(fb, earL); g.FillPolygon(fb, earR);
                using (SolidBrush ein = new SolidBrush(Color.FromArgb(255, 150, 160)))
                {
                    g.FillPolygon(ein, new PointF[] { new PointF(-s * 0.40f, -s * 0.36f), new PointF(-s * 0.37f, -s * 0.66f), new PointF(-s * 0.20f, -s * 0.47f) });
                    g.FillPolygon(ein, new PointF[] { new PointF(s * 0.40f, -s * 0.36f), new PointF(s * 0.37f, -s * 0.66f), new PointF(s * 0.20f, -s * 0.47f) });
                }
                g.FillEllipse(fb, -s * 0.55f, -s * 0.62f, s * 1.10f, s * 0.86f);
                // garis loreng
                using (Pen stripe = new Pen(furD, s * 0.05f)) { stripe.StartCap = stripe.EndCap = LineCap.Round; g.DrawLine(stripe, 0, -s * 0.60f, 0, -s * 0.46f); g.DrawLine(stripe, -s * 0.12f, -s * 0.58f, -s * 0.10f, -s * 0.47f); g.DrawLine(stripe, s * 0.12f, -s * 0.58f, s * 0.10f, -s * 0.47f); }
                // mata setengah tertutup + melirik = muka nyebelin
                float look = (float)Math.Sin(beat * 0.5) * s * 0.03f;
                using (SolidBrush white = new SolidBrush(Color.White)) using (SolidBrush pupil = new SolidBrush(Color.FromArgb(40, 30, 30)))
                {
                    foreach (float ex in new float[] { -s * 0.22f, s * 0.22f })
                    {
                        RectangleF eye = new RectangleF(ex - s * 0.13f, -s * 0.34f, s * 0.26f, s * 0.20f);
                        g.FillEllipse(white, eye);
                        g.FillEllipse(pupil, ex - s * 0.05f + s * 0.05f + look, -s * 0.28f, s * 0.10f, s * 0.12f);
                        g.FillRectangle(fb, eye.X - 1, eye.Y - 1, eye.Width + 2, eye.Height * 0.48f);   // kelopak
                        g.DrawLine(line, eye.X, eye.Y + eye.Height * 0.47f, eye.Right, eye.Y + eye.Height * 0.47f);
                    }
                }
                using (SolidBrush blush = new SolidBrush(Color.FromArgb(110, 255, 110, 120)))
                { g.FillEllipse(blush, -s * 0.46f, -s * 0.14f, s * 0.18f, s * 0.10f); g.FillEllipse(blush, s * 0.28f, -s * 0.14f, s * 0.18f, s * 0.10f); }
                using (SolidBrush nose = new SolidBrush(Color.FromArgb(240, 110, 120))) g.FillEllipse(nose, -s * 0.05f, -s * 0.15f, s * 0.10f, s * 0.07f);
                // senyum miring
                g.DrawBezier(line, -s * 0.12f, -s * 0.03f, -s * 0.04f, s * 0.04f, s * 0.08f, s * 0.03f, s * 0.20f, -s * 0.08f);
                using (Pen wh = new Pen(Color.FromArgb(150, 110, 60, 30), s * 0.018f))
                {
                    g.DrawLine(wh, -s * 0.30f, -s * 0.08f, -s * 0.62f, -s * 0.14f); g.DrawLine(wh, -s * 0.30f, -s * 0.03f, -s * 0.62f, -s * 0.01f);
                    g.DrawLine(wh, s * 0.30f, -s * 0.08f, s * 0.62f, -s * 0.14f); g.DrawLine(wh, s * 0.30f, -s * 0.03f, s * 0.62f, -s * 0.01f);
                }
                g.Restore(hs);
            }
            g.Restore(st);
        }

        private static void Arm(Graphics g, Brush b, float x, float y, float s, float angle)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(angle);
            g.FillEllipse(b, 0, -s * 0.07f, s * 0.36f, s * 0.14f);
            g.FillEllipse(b, s * 0.28f, -s * 0.09f, s * 0.17f, s * 0.17f);
            g.Restore(st);
        }

        private void PaintAll(Graphics g, int W, int H, double t)
        {
            Hq(g);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(185, 228, 230, 240)))
            using (StringFormat c = new StringFormat())
            {
                c.Alignment = StringAlignment.Center;
                // panggung: sorot lampu disko
                RectangleF stage = new RectangleF(Ui.Pf(24), Ui.Pf(24), W - Ui.Pf(48), Ui.Pf(290));
                using (GraphicsPath sp = Glass.Round(stage, Ui.Pf(22)))
                {
                    double hue = t * 60;
                    using (LinearGradientBrush lg = new LinearGradientBrush(stage, Hsv(hue, 0.55, 0.42), Hsv(hue + 120, 0.55, 0.30), 45f)) g.FillPath(lg, sp);
                    Region old = g.Clip; g.SetClip(sp, CombineMode.Intersect);
                    for (int i = 0; i < 3; i++)
                    {
                        double a = t * 1.3 + i * 2.1;
                        float bx = stage.X + stage.Width * (0.5f + 0.42f * (float)Math.Sin(a)), by = stage.Y + stage.Height * 0.35f;
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            gp.AddEllipse(bx - Ui.Pf(120), by - Ui.Pf(120), Ui.Pf(240), Ui.Pf(240));
                            using (PathGradientBrush pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(70, Hsv(hue + i * 110, 0.6, 1)); pg.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) }; g.FillPath(pg, gp); }
                        }
                    }
                    // not musik melayang
                    for (int i = 0; i < 4; i++)
                    {
                        double p = (t * 0.45 + i * 0.25) % 1.0;
                        float nx = stage.X + stage.Width * (0.18f + 0.64f * ((i * 0.37f) % 1f)) + (float)Math.Sin(p * 6 + i) * Ui.Pf(10);
                        float ny = stage.Bottom - Ui.Pf(30) - (float)p * stage.Height * 0.85f;
                        using (SolidBrush nb = new SolidBrush(Color.FromArgb((int)(220 * Math.Sin(p * Math.PI)), 255, 255, 255))) g.DrawString(i % 2 == 0 ? "♪" : "♫", _note, nb, nx, ny);
                    }
                    Cat(g, stage.X + stage.Width / 2f, stage.Y + Ui.Pf(142), Ui.Pf(132), t);
                    g.Clip = old; old.Dispose();
                }
                // tombol tutup (muncul lagi minggu depan)
                _close = new RectangleF(W - Ui.Pf(64), Ui.Pf(36), Ui.Pf(28), Ui.Pf(28));
                using (SolidBrush xb = new SolidBrush(Color.FromArgb(_hover == 3 ? 150 : 90, 0, 0, 0))) g.FillEllipse(xb, _close);
                Glyphs.Draw(g, "", _xf, white, _close);

                float y = stage.Bottom + Ui.Pf(22);
                g.DrawString("Halo, jangan lupa jajanin aku ya!", _title, white, new RectangleF(0, y, W, Ui.Pf(34)), c);
                g.DrawString("LiquidMac gratis dan dibuat di waktu luang.\nDukunganmu lewat Saweria bikin fitur baru terus hadir.", _t, soft, new RectangleF(Ui.Pf(24), y + Ui.Pf(44), W - Ui.Pf(48), Ui.Pf(50)), c);
                float bh = Ui.Pf(46), pad = Ui.Pf(32);
                _go = new RectangleF(pad, H - Ui.Pf(112), W - pad * 2, bh);
                float pulse = (float)(0.5 + 0.5 * Math.Sin(t * 4));
                using (GraphicsPath gp = Glass.Round(_go, bh / 2f))
                {
                    using (LinearGradientBrush gb = new LinearGradientBrush(_go, Color.FromArgb(255, 255, 120, 80), Color.FromArgb(255, 255, 50, 120), 0f)) g.FillPath(gb, gp);
                    using (Pen glow = new Pen(Color.FromArgb((int)(60 + 100 * pulse), 255, 220, 230), Ui.Pf(2))) g.DrawPath(glow, gp);
                }
                c.LineAlignment = StringAlignment.Center;
                g.DrawString("Donasi via Saweria", _b, white, _go, c);
                _later = new RectangleF(W / 2f - Ui.Pf(160), H - Ui.Pf(56), Ui.Pf(320), Ui.Pf(30));
                using (SolidBrush lb = new SolidBrush(Color.FromArgb(_hover == 1 ? 240 : 160, 228, 230, 240))) g.DrawString("Nanti saja (ingatkan minggu depan)", _s, lb, _later, c);
            }
        }

        private static Color Hsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360; int i = (int)(h / 60) % 6; double f = h / 60 - Math.Floor(h / 60);
            double p = v * (1 - s), q = v * (1 - f * s), u = v * (1 - (1 - f) * s);
            double r = 0, gg = 0, b = 0;
            switch (i) { case 0: r = v; gg = u; b = p; break; case 1: r = q; gg = v; b = p; break; case 2: r = p; gg = v; b = u; break; case 3: r = p; gg = q; b = v; break; case 4: r = u; gg = p; b = v; break; default: r = v; gg = p; b = q; break; }
            return Color.FromArgb((int)(r * 255), (int)(gg * 255), (int)(b * 255));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = _close.Contains(e.Location) ? 3 : _later.Contains(e.Location) ? 1 : _go.Contains(e.Location) ? 2 : 0;
            if (h != _hover) { _hover = h; Cursor = h > 0 ? Cursors.Hand : Cursors.Default; }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_go.Contains(e.Location)) { Settings.Set("DonateDone", true); Shell.Run(Program.DonateUrl, null); Hide(); }   // hilang permanen
            else if (_later.Contains(e.Location) || _close.Contains(e.Location)) Hide();                                     // muncul lagi minggu depan
        }

        public void SnapshotTo(string path, double t)
        {
            using (Bitmap b = new Bitmap(W, H))
            {
                using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(40, 38, 52)); PaintAll(g, W, H, t); }
                b.Save(path, ImageFormat.Png);
            }
        }
    }

    // ============================================================
    //  MATERI PROMOSI: frame animasi island + Dock dengan kursor buatan
    //  (LiquidMac.exe --snapshot <dir> dengan env LM_PROMO=1)
    // ============================================================
    internal static class Promo
    {
        private struct Key { public double T; public float X, Y; public Key(double t, float x, float y) { T = t; X = x; Y = y; } }

        private static Bitmap Art()
        {
            Bitmap b = new Bitmap(300, 300);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, 300, 300), Color.FromArgb(255, 150, 70), Color.FromArgb(90, 40, 160), 60f)) g.FillRectangle(lg, 0, 0, 300, 300);
                using (SolidBrush s = new SolidBrush(Color.FromArgb(120, 255, 230, 160))) g.FillEllipse(s, 150, 40, 110, 110);
                using (SolidBrush s = new SolidBrush(Color.FromArgb(90, 20, 10, 40))) g.FillRectangle(s, 0, 210, 300, 90);
                using (SolidBrush s = new SolidBrush(Color.FromArgb(230, 255, 200, 120))) g.FillRectangle(s, 60, 150, 40, 60);
            }
            return b;
        }

        private static double Ease(double v) { v = Math.Max(0, Math.Min(1, v)); return v < 0.5 ? 4 * v * v * v : 1 - Math.Pow(-2 * v + 2, 3) / 2; }

        private static PointF At(List<Key> k, double t)
        {
            for (int i = 0; i < k.Count - 1; i++)
                if (t <= k[i + 1].T)
                {
                    double u = Ease((t - k[i].T) / Math.Max(0.0001, k[i + 1].T - k[i].T));
                    return new PointF((float)(k[i].X + (k[i + 1].X - k[i].X) * u), (float)(k[i].Y + (k[i + 1].Y - k[i].Y) * u));
                }
            return new PointF(k[k.Count - 1].X, k[k.Count - 1].Y);
        }

        // Jendela contoh bergaya aplikasi catatan (untuk video animasi)
        private static Bitmap Mock(int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(252, 252, 253));
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(236, 236, 239))) g.FillRectangle(tb, 0, 0, w, 52);
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(242, 241, 246))) g.FillRectangle(sb, 0, 52, 210, h - 52);
                using (Pen ln = new Pen(Color.FromArgb(220, 220, 226))) { g.DrawLine(ln, 0, 52, w, 52); g.DrawLine(ln, 210, 52, 210, h); }
                Color[] tl = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
                for (int i = 0; i < 3; i++) using (SolidBrush c = new SolidBrush(tl[i])) g.FillEllipse(c, 20 + i * 22, 19, 14, 14);
                string fam = MacFont.Present ? "Inter" : "Segoe UI", semi = MacFont.Present ? "Inter Semi Bold" : "Segoe UI Semibold";
                using (Font tf = new Font(semi, 15, GraphicsUnit.Pixel)) using (SolidBrush dk = new SolidBrush(Color.FromArgb(40, 40, 46))) g.DrawString("Dokumen", tf, dk, 236, 16);
                using (Font sf = new Font(semi, 11, GraphicsUnit.Pixel)) using (SolidBrush gr = new SolidBrush(Color.FromArgb(150, 150, 160))) g.DrawString("FAVORIT", sf, gr, 22, 66);
                string[] side = { "Desktop", "Dokumen", "Unduhan", "Gambar", "Musik", "Video" };
                using (Font sf = new Font(fam, 14, GraphicsUnit.Pixel)) using (SolidBrush dk = new SolidBrush(Color.FromArgb(60, 60, 70)))
                    for (int i = 0; i < side.Length; i++)
                    {
                        if (i == 1) using (SolidBrush sel = new SolidBrush(Color.FromArgb(214, 226, 250))) g.FillRectangle(sel, 10, 86 + i * 32, 190, 28);
                        using (SolidBrush ic = new SolidBrush(Color.FromArgb(10, 132, 255))) g.FillRectangle(ic, 24, 94 + i * 32, 13, 11);
                        g.DrawString(side[i], sf, dk, 46, 91 + i * 32);
                    }
                string[] files = { "Proposal", "Foto Liburan", "Tugas Kuliah", "Desain", "Keuangan", "Musik", "Proyek LiquidMac", "Arsip" };
                using (Font ff = new Font(fam, 13, GraphicsUnit.Pixel)) using (SolidBrush dk = new SolidBrush(Color.FromArgb(50, 50, 58)))
                using (StringFormat c = new StringFormat()) 
                {
                    c.Alignment = StringAlignment.Center;
                    for (int i = 0; i < files.Length; i++)
                    {
                        float x = 250 + (i % 4) * 172, y = 90 + (i / 4) * 200;
                        RectangleF fr = new RectangleF(x + 28, y, 100, 76);
                        using (GraphicsPath tab = Glass.Round(new RectangleF(fr.X, fr.Y - 8, 44, 20), 5)) using (SolidBrush t2 = new SolidBrush(Color.FromArgb(80, 160, 245))) g.FillPath(t2, tab);
                        using (GraphicsPath fp = Glass.Round(fr, 9)) using (LinearGradientBrush lb = new LinearGradientBrush(fr, Color.FromArgb(120, 190, 255), Color.FromArgb(60, 145, 240), 90f)) g.FillPath(lb, fp);
                        g.DrawString(files[i], ff, dk, new RectangleF(x, y + 86, 156, 40), c);
                    }
                }
            }
            return b;
        }

        // Adegan tambahan untuk video Dynamic Island
        private static void Seq(NotchForm notch, string ad, string name, int frames, Action<int> step)
        {
            for (int i = 0; i < frames; i++)
            {
                NotchForm.PromoTime = 100 + i / 60.0;
                if (step != null) step(i);
                notch.PromoFrame(Path.Combine(ad, name + "_" + i.ToString("0000") + ".png"));
            }
        }

        private static void RunExtra(string ad, NotchForm notch)
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Point away = new Point(scr.Width / 2, scr.Height / 2), on = new Point(scr.Width / 2, 20);
            string tmp = Path.Combine(ad, "_settle.png");
            OverlayForm.FakeCursor = away;
            notch.PromoClearHud(); for (int i = 0; i < 40; i++) notch.PromoFrame(tmp);
            // 1) island dibuka: latar gradasi bergerak, geser kartu jam -> rekam
            notch.PromoPin(true, "clock");
            Seq(notch, ad, "xa", 300, delegate (int i) { if (i == 150) notch.PromoGo(1); });
            notch.PromoPin(false, null); OverlayForm.FakeCursor = away; for (int i = 0; i < 40; i++) notch.PromoFrame(tmp);
            // 2) pratinjau tangkapan layar
            Bitmap th = null;
            try { using (Bitmap wp = MacLock.LiveWallpaper()) if (wp != null) { th = new Bitmap(240, 135); using (Graphics g = Graphics.FromImage(th)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(wp, 0, 0, 240, 135); } } } catch { }
            if (th == null) th = new Bitmap(240, 135);
            Seq(notch, ad, "xs", 210, delegate (int i) { if (i == 15) notch.ShowShot(@"C:\x\Tangkapan Layar 2026-11-09 pukul 15.42.10.png", th, "shot"); if (i == 190) notch.PromoClearHud(); });
            for (int i = 0; i < 30; i++) notch.PromoFrame(tmp);
            // 3) kamera dipakai aplikasi
            Seq(notch, ad, "xc", 170, delegate (int i) { if (i == 15) notch.PromoHud("cam", 0, "Zoom Meetings", 130); });
            for (int i = 0; i < 30; i++) notch.PromoFrame(tmp);
            // 4) rekam suara: kartu lalu island mengecil dengan gelombang suara
            Random rnd = new Random(7); float lv = 0.3f;
            Rec.Kind = "audio";
            notch.PromoPin(true, "rec");
            Seq(notch, ad, "xr", 330, delegate (int i)
            {
                Rec.FakeElapsed = TimeSpan.FromSeconds(4 + i / 60.0);
                if (i % 4 == 0) { lv = Math.Max(0.05f, Math.Min(1f, lv * 0.6f + (float)rnd.NextDouble() * 0.7f)); lock (Rec.Levels) { Array.Copy(Rec.Levels, 1, Rec.Levels, 0, Rec.Levels.Length - 1); Rec.Levels[Rec.Levels.Length - 1] = lv; } }
                if (i == 160) notch.PromoPin(false, null);
            });
            Rec.Kind = null; Rec.FakeElapsed = null;
            for (int i = 0; i < 40; i++) notch.PromoFrame(tmp);
            // 5) HUD volume
            Seq(notch, ad, "xv", 150, delegate (int i) { if (i >= 10 && i <= 70 && i % 6 == 4) notch.PromoHud("vol", 0.3f + (i - 10) / 120f); });
        }

        public static void RunAnim(string dir, NotchForm notch, DockForm dock)
        {
            string ad = Path.Combine(dir, "anim");
            Directory.CreateDirectory(ad);
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Bitmap mock = Mock(960, 600);
            mock.Save(Path.Combine(ad, "mock.png"), ImageFormat.Png);
            Rectangle S = new Rectangle(scr.Width / 2 - 480, 170, 960, 600);
            OverlayForm.FakeCursor = new Point(scr.Width / 2, scr.Height / 2);
            for (int i = 0; i < 90; i++) dock.PromoFrame(Path.Combine(ad, "dock.png"));   // Dock diam, lalu ambil kotak ikon dari frame yang sama
            int k = 0;   // Finder
            Rectangle T = dock.PromoIcon(k);
            File.WriteAllText(Path.Combine(ad, "dockpos.txt"), dock.Left + " " + dock.Top + " " + dock.PromoName(k));
            File.WriteAllText(Path.Combine(ad, "rects.txt"), "S " + S.X + " " + S.Y + " " + S.Width + " " + S.Height + "\nT " + T.X + " " + T.Y + " " + T.Width + " " + T.Height + "\n");
            WinAnim.ForceStyle = "genie";
            new AnimForm(new Bitmap(mock), S, T, 430, AnimForm.Mode.Minimize, null).PromoRender(ad, "gm", 120);
            new AnimForm(new Bitmap(mock), T, S, 380, AnimForm.Mode.Restore, null).PromoRender(ad, "gr", 120);
            WinAnim.ForceStyle = "pocket";
            new AnimForm(new Bitmap(mock), S, T, 660, AnimForm.Mode.Minimize, null).PromoRender(ad, "pm", 120);
            new AnimForm(new Bitmap(mock), T, S, 580, AnimForm.Mode.Restore, null).PromoRender(ad, "pr", 120);
            WinAnim.ForceStyle = null;
            // animasi standby island (tanpa media, kursor jauh)
            notch.PromoMedia(null);
            OverlayForm.FakeCursor = new Point(scr.Width / 2, scr.Height / 2);
            string tmp = Path.Combine(ad, "_settle.png");
            for (int i = 0; i < 90; i++) notch.PromoFrame(tmp);
            foreach (string kind in new string[] { "air", "jantung" })
            {
                notch.PromoIdle(kind);
                for (int i = 0; i < 3.5 * 60; i++) notch.PromoFrame(Path.Combine(ad, (kind == "air" ? "ia_" : "ih_") + i.ToString("0000") + ".png"));
                for (int i = 0; i < 20; i++) notch.PromoFrame(tmp);
            }
            RunExtra(ad, notch);
            OverlayForm.FakeCursor = null;
            NotchForm.PromoTime = -1;
            Log.Write("promo anim selesai");
        }

        public static void Run(string dir, NotchForm notch, DockForm dock)
        {
            string fd = Path.Combine(dir, "frames");
            Directory.CreateDirectory(fd);
            try { using (Bitmap wp = MacLock.LiveWallpaper()) if (wp != null) wp.Save(Path.Combine(dir, "wallpaper.png"), ImageFormat.Png); } catch { }

            NowPlaying np = new NowPlaying();
            np.Title = "Lampu Teras"; np.Artist = "USER"; np.App = "Spotify"; np.Playing = true;
            np.Duration = TimeSpan.FromSeconds(214); np.Position = TimeSpan.FromSeconds(71); np.Sampled = DateTime.Now; np.Art = Art();
            notch.PromoMedia(np);

            Rectangle scr = Screen.PrimaryScreen.Bounds;
            RectangleF bar = dock.PromoBar;
            float cx = scr.Left + scr.Width / 2f;
            float dy = dock.Top + bar.Y + bar.Height * 0.55f, dl = dock.Left + bar.X + bar.Height * 0.6f, dr = dock.Left + bar.Right - bar.Height * 0.6f;
            List<Key> k = new List<Key>();
            k.Add(new Key(0, cx + 120, scr.Height * 0.58f)); k.Add(new Key(0.7, cx + 120, scr.Height * 0.58f));
            k.Add(new Key(1.5, cx, 18)); k.Add(new Key(5.0, cx + 6, 20));
            k.Add(new Key(5.9, dl, dy)); k.Add(new Key(8.4, dr, dy));
            k.Add(new Key(9.1, cx + 80, scr.Height * 0.55f)); k.Add(new Key(10.8, cx + 80, scr.Height * 0.55f));

            StringBuilder meta = new StringBuilder();
            meta.AppendLine("screen " + scr.Width + " " + scr.Height);
            meta.AppendLine("notch " + notch.Left + " " + notch.Top);
            meta.AppendLine("dock " + dock.Left + " " + dock.Top);
            int n = (int)(10.8 * 60);
            for (int i = 0; i < n; i++)
            {
                double t = i / 60.0;
                PointF c = At(k, t);
                OverlayForm.FakeCursor = new Point((int)Math.Round(c.X), (int)Math.Round(c.Y));
                NotchForm.PromoTime = t;
                if (i == (int)(2.7 * 60) || i == (int)(3.9 * 60)) notch.PromoGo(1);
                if (i == (int)(9.6 * 60)) notch.PromoHud("vol", 0.62f);
                notch.PromoFrame(Path.Combine(fd, "n" + i.ToString("0000") + ".png"));
                dock.PromoFrame(Path.Combine(fd, "d" + i.ToString("0000") + ".png"));
                meta.AppendLine("c " + i + " " + c.X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + c.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            OverlayForm.FakeCursor = null;
            File.WriteAllText(Path.Combine(dir, "promo.txt"), meta.ToString());
            Log.Write("promo: " + n + " frame");
        }
    }
}
