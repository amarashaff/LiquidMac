using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    // ---------- Notch: animasi standby kucing mengintip & kelelawar, cahaya berpendar di sekeliling notch ----------
    internal sealed partial class NotchForm
    {
        private bool DrawIdleCreature(float x0, float w, float h)
        {
            if (_idleKind == "kucing") { DrawCat(x0, w, h); return true; }
            if (_idleKind == "kelelawar") { DrawBat(x0, w, h); return true; }
            return false;
        }

        // Kucing hitam mengintip dari tepi bawah notch: dua cakar mencengkeram tepi, lalu telinga dan mata muncul
        // (kepala terbalik, seperti kucing yang menengok ke bawah dari atas lemari), melirik kiri-kanan, berkedip, lalu sembunyi lagi.
        private void DrawCat(float x0, float w, float h)
        {
            float t = _idleT, cx = x0 + w / 2f, B = 0.5f + h, R = h * 0.62f;
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
            float paws = t < 0.45f ? EaseIO(t / 0.45f) : t < 3.75f ? 1f : 1f - EaseIO((t - 3.75f) / 0.3f);
            float peek = t < 0.40f ? 0f : t < 1.05f ? EaseBack((t - 0.40f) / 0.65f) : t < 3.30f ? 1f : 1f - EaseIO((t - 3.30f) / 0.40f);
            if (paws <= 0.01f && peek <= 0.01f) return;
            float hx = cx + (float)Math.Sin(t * 1.9) * R * 0.07f * peek;
            float yc = B - R * 1.62f + peek * R * 1.74f;          // pusat kepala; saat mengintip sedikit di atas tepi bawah notch
            float u = Clamp01((t - 1.15f) / 2.0f);
            float look = t > 1.15f && t < 3.2f ? (float)Math.Sin(u * Math.PI * 2) : 0f;
            float blink = (t > 1.75f && t < 1.87f) || (t > 2.75f && t < 2.87f) ? 0.12f : 1f;
            float twitch = Win(t, 2.2f, 2.5f) * 9f;

            Region old = G.Clip;
            G.SetClip(new RectangleF(x0 - Ui.Pf(40), B - 0.5f, w + Ui.Pf(80), Ui.Pf(120)), CombineMode.Intersect);   // hanya di bawah notch
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(14, 14, 18)))
            using (Pen rim = new Pen(Color.FromArgb(95, 255, 255, 255), Ui.Pf(1.6f)))
            using (SolidBrush pink = new SolidBrush(Color.FromArgb(255, 158, 180)))
            {
                rim.LineJoin = LineJoin.Round;
                // telinga (menunjuk ke bawah) + kepala: garis tepi dulu, lalu isi hitam menutup bagian dalamnya
                PointF[][] ears = new PointF[2][];
                PointF[][] inner = new PointF[2][];
                for (int s = -1; s <= 1; s += 2)
                {
                    int i = s < 0 ? 0 : 1;
                    float tw = s > 0 ? twitch : 0f;
                    double a0 = (90 - s * 62) * Math.PI / 180, a1 = (90 - s * 18) * Math.PI / 180, am = (90 - s * (44 + tw)) * Math.PI / 180;
                    PointF p0 = new PointF(hx + (float)Math.Cos(a0) * R * 0.92f, yc + (float)Math.Sin(a0) * R * 0.92f);
                    PointF p1 = new PointF(hx + (float)Math.Cos(a1) * R * 0.92f, yc + (float)Math.Sin(a1) * R * 0.92f);
                    PointF tip = new PointF(hx + (float)Math.Cos(am) * R * 1.52f, yc + (float)Math.Sin(am) * R * 1.52f);
                    ears[i] = new PointF[] { p0, tip, p1 };
                    PointF mid = new PointF((p0.X + p1.X) / 2f, (p0.Y + p1.Y) / 2f);
                    inner[i] = new PointF[] {
                        new PointF(mid.X + (p0.X - mid.X) * 0.5f, mid.Y + (p0.Y - mid.Y) * 0.5f),
                        new PointF(mid.X + (tip.X - mid.X) * 0.68f, mid.Y + (tip.Y - mid.Y) * 0.68f),
                        new PointF(mid.X + (p1.X - mid.X) * 0.5f, mid.Y + (p1.Y - mid.Y) * 0.5f) };
                }
                RectangleF head = new RectangleF(hx - R, yc - R * 0.92f, R * 2, R * 1.84f);
                if (peek > 0.01f)
                {
                    G.DrawPolygon(rim, ears[0]); G.DrawPolygon(rim, ears[1]); G.DrawEllipse(rim, head);
                    G.FillPolygon(ink, ears[0]); G.FillPolygon(ink, ears[1]); G.FillEllipse(ink, head);
                    G.FillPolygon(pink, inner[0]); G.FillPolygon(pink, inner[1]);
                    // mata
                    float er = R * 0.235f, ey = yc + R * 0.36f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float ex = hx + s * R * 0.42f;
                        using (SolidBrush eb = new SolidBrush(Color.FromArgb(206, 240, 96))) G.FillEllipse(eb, ex - er, ey - er * blink, er * 2, er * 2 * blink);
                        if (blink > 0.5f)
                        {
                            float px = ex + look * er * 0.45f, pw = er * 0.42f;
                            using (SolidBrush pb = new SolidBrush(Color.FromArgb(10, 10, 12))) G.FillEllipse(pb, px - pw / 2f, ey - er * 0.82f, pw, er * 1.64f);
                            using (SolidBrush hb = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) G.FillEllipse(hb, px - pw * 0.9f, ey - er * 0.55f, er * 0.32f, er * 0.32f);
                        }
                    }
                }
                // cakar di tepi notch
                if (paws > 0.01f)
                {
                    float prx = R * 0.34f, pry = R * 0.30f * paws;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float px = hx + s * R * 1.42f, py = B + pry * 0.25f;
                        RectangleF pr = new RectangleF(px - prx, py - pry, prx * 2, pry * 2);
                        G.DrawEllipse(rim, pr); G.FillEllipse(ink, pr);
                        using (Pen toe = new Pen(Color.FromArgb(120, 255, 255, 255), Ui.Pf(0.9f)))
                        {
                            G.DrawLine(toe, px - prx * 0.34f, py + pry * 0.25f, px - prx * 0.34f, py + pry * 0.85f);
                            G.DrawLine(toe, px + prx * 0.34f, py + pry * 0.25f, px + prx * 0.34f, py + pry * 0.85f);
                        }
                    }
                }
            }
            G.Clip = old; old.Dispose();
        }

        // Kelelawar hitam lucu: turun menggantung terbalik di bawah notch, membuka mata dan sayap,
        // lepas dan terbang berputar-putar, lalu pulang dan menyatu lagi dengan notch.
        private void DrawBat(float x0, float w, float h)
        {
            float t = _idleT, cx = x0 + w / 2f, B = 0.5f + h, s = h * 0.58f;
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
            float bx = cx, by, ang = 0, flap = 0, fold = 0, sc = 1f; bool eyes = true;
            float hangY = B + s * 1.02f;
            if (t < 0.6f) { float u = EaseBack(t / 0.6f); by = B - s * 1.3f + u * (hangY - (B - s * 1.3f)); ang = 180; fold = 1; eyes = false; }
            else if (t < 1.6f)
            {
                float u = (t - 0.6f) / 1.0f;
                by = hangY; ang = 180 + (float)Math.Sin(u * Math.PI * 3) * 9f; flap = -0.2f;
                fold = 1f - EaseIO((u - 0.55f) / 0.4f);
                eyes = u > 0.15f && !(u > 0.42f && u < 0.50f);
            }
            else if (t < 3.75f)
            {
                float u = (t - 1.6f) / 2.15f, turn = EaseIO((t - 1.6f) / 0.32f);
                float fx = cx + (float)Math.Sin(u * Math.PI * 2) * Math.Min(Ui.Pf(120), w * 0.72f);
                float fy = B + Ui.Pf(40) + (float)Math.Sin(u * Math.PI * 4) * Ui.Pf(13);
                bx = cx + (fx - cx) * turn; by = hangY + (fy - hangY) * turn;
                ang = 180 * (1 - turn) + (float)Math.Cos(u * Math.PI * 2) * 14f * turn;
                flap = (float)Math.Sin(t * 21);
            }
            else if (t < 4.25f)
            {
                float u = EaseIO((t - 3.75f) / 0.5f), sy = B + Ui.Pf(40);
                by = sy + (B - s * 0.9f - sy) * u; flap = (float)Math.Sin(t * 21); fold = 0.5f * u; sc = 1f - 0.35f * u;
            }
            else by = -1000;

            Region old = G.Clip;
            G.SetClip(new RectangleF(x0 - Ui.Pf(170), B - 0.5f, w + Ui.Pf(340), Ui.Pf(150)), CombineMode.Intersect);   // hanya di bawah notch
            // notch bergoyang saat kelelawar menyatu kembali
            if (t >= 4.2f)
            {
                float u = (t - 4.2f) / 0.4f, amp = s * 0.7f * (float)(Math.Exp(-3.5 * u) * Math.Cos(u * 11));
                if (amp > 0.3f)
                    using (SolidBrush ink = new SolidBrush(Color.Black)) using (Pen rim = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                    { RectangleF wb = new RectangleF(cx - s * 1.7f, B - amp, s * 3.4f, amp * 2); G.FillEllipse(ink, wb); G.DrawEllipse(rim, wb); }
            }
            if (by > -500)
            {
                GraphicsState gs = G.Save();
                G.TranslateTransform(bx, by); G.RotateTransform(ang); G.ScaleTransform(sc, sc);
                BatShape(s, flap, fold, eyes);
                G.Restore(gs);
            }
            G.Clip = old; old.Dispose();
        }

        // Kelelawar tegak di titik asal: s = satuan ukuran, flap -1..1 (kepak), fold 0..1 (sayap terlipat).
        private void BatShape(float s, float flap, float fold, bool eyes)
        {
            Color bodyC = Color.FromArgb(20, 18, 28), wingC = Color.FromArgb(56, 48, 78);
            using (Pen rim = new Pen(Color.FromArgb(95, 255, 255, 255), Ui.Pf(1.4f)))
            using (SolidBrush body = new SolidBrush(bodyC))
            using (SolidBrush wing = new SolidBrush(wingC))
            {
                rim.LineJoin = LineJoin.Round;
                float k = 1f - fold * 0.78f, lift = flap * s * 0.62f * (1f - fold);
                for (int d = -1; d <= 1; d += 2)
                {
                    PointF[] wp = {
                        new PointF(d * s * 0.26f, -s * 0.34f),
                        new PointF(d * s * 0.95f * k, -s * 0.62f - lift * 0.55f),
                        new PointF(d * s * 1.62f * k, -s * 0.30f - lift),
                        new PointF(d * s * 1.30f * k, s * 0.10f - lift * 0.62f),
                        new PointF(d * s * 1.02f * k, -s * 0.06f - lift * 0.45f),
                        new PointF(d * s * 0.78f * k, s * 0.30f - lift * 0.30f),
                        new PointF(d * s * 0.52f * k, s * 0.10f - lift * 0.15f),
                        new PointF(d * s * 0.24f, s * 0.36f) };
                    G.DrawPolygon(rim, wp); G.FillPolygon(wing, wp);
                }
                // telinga
                PointF[] e1 = { new PointF(-s * 0.36f, -s * 0.70f), new PointF(-s * 0.30f, -s * 1.22f), new PointF(-s * 0.04f, -s * 0.86f) };
                PointF[] e2 = { new PointF(s * 0.36f, -s * 0.70f), new PointF(s * 0.30f, -s * 1.22f), new PointF(s * 0.04f, -s * 0.86f) };
                RectangleF bd = new RectangleF(-s * 0.40f, -s * 0.42f, s * 0.80f, s * 0.98f), hd = new RectangleF(-s * 0.40f, -s * 0.98f, s * 0.80f, s * 0.76f);
                G.DrawPolygon(rim, e1); G.DrawPolygon(rim, e2); G.DrawEllipse(rim, bd); G.DrawEllipse(rim, hd);
                G.FillPolygon(body, e1); G.FillPolygon(body, e2); G.FillEllipse(body, bd); G.FillEllipse(body, hd);
                // kaki kecil
                using (Pen leg = new Pen(Color.FromArgb(200, 200, 200, 215), Ui.Pf(1f))) { G.DrawLine(leg, -s * 0.12f, s * 0.52f, -s * 0.12f, s * 0.70f); G.DrawLine(leg, s * 0.12f, s * 0.52f, s * 0.12f, s * 0.70f); }
                float er = Math.Max(1.3f, s * 0.115f), ey = -s * 0.62f;
                for (int d = -1; d <= 1; d += 2)
                {
                    float ex = d * s * 0.165f;
                    if (eyes)
                    {
                        using (SolidBrush wb = new SolidBrush(Color.White)) G.FillEllipse(wb, ex - er, ey - er, er * 2, er * 2);
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(20, 16, 30))) G.FillEllipse(pb, ex - er * 0.5f, ey - er * 0.35f, er, er);
                    }
                    else using (Pen cl = new Pen(Color.FromArgb(210, 255, 255, 255), Ui.Pf(1f))) G.DrawLine(cl, ex - er, ey, ex + er, ey);
                }
                // taring mungil
                using (SolidBrush fb = new SolidBrush(Color.White))
                {
                    G.FillPolygon(fb, new PointF[] { new PointF(-s * 0.10f, -s * 0.40f), new PointF(-s * 0.03f, -s * 0.40f), new PointF(-s * 0.065f, -s * 0.29f) });
                    G.FillPolygon(fb, new PointF[] { new PointF(s * 0.10f, -s * 0.40f), new PointF(s * 0.03f, -s * 0.40f), new PointF(s * 0.065f, -s * 0.29f) });
                }
            }
        }

        // ----- cahaya berpendar di sekeliling notch (Pengaturan: NotchGlow, NotchGlowColor rgb|putih|merah|hijau|biru|kuning|oranye|ungu|pink|tosca, NotchGlowLevel tipis|sedang|terang) -----
        private static bool GlowOn { get { return Settings.Get("NotchGlow", false); } }

        private static Color GlowHsv(double hh, double sat, double v)
        {
            hh = (hh % 1 + 1) % 1 * 6; int i = (int)hh; double f = hh - i, p = v * (1 - sat), q = v * (1 - sat * f), u = v * (1 - sat * (1 - f)), r, g, b;
            switch (i) { case 0: r = v; g = u; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = u; break; case 3: r = p; g = q; b = v; break; case 4: r = u; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        private static readonly byte[,] GlowDither = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
        private Bitmap _glowBmp;

        internal static bool GlowTint(string name, out Color c)
        {
            switch (name)
            {
                case "putih": c = Color.FromArgb(236, 242, 255); return true;
                case "merah": c = Color.FromArgb(255, 48, 56); return true;
                case "hijau": c = Color.FromArgb(48, 225, 96); return true;
                case "biru": c = Color.FromArgb(50, 120, 255); return true;
                case "kuning": c = Color.FromArgb(255, 214, 40); return true;
                case "oranye": c = Color.FromArgb(255, 140, 30); return true;
                case "ungu": c = Color.FromArgb(170, 80, 255); return true;
                case "pink": c = Color.FromArgb(255, 80, 170); return true;
                case "tosca": c = Color.FromArgb(40, 220, 210); return true;
            }
            c = Color.White; return false;   // rgb
        }

        // Jangkauan 30% lebar notch; alpha dihitung per piksel dari jarak ke tepi notch, jadi makin jauh makin tipis tanpa batas tegas.
        private unsafe void DrawGlow(float x, float w, float h, float r)
        {
            if (h < Ui.Pf(8) || w < Ui.Pf(20)) return;
            string lv = Settings.GetS("NotchGlowLevel", "sedang");
            float k = lv == "tipis" ? 0.55f : lv == "terang" ? 1.7f : 1f;
            Color tint; bool solid = GlowTint(Settings.GetS("NotchGlowColor", "rgb"), out tint);
            double ts = PromoTime >= 0 ? PromoTime : (Environment.TickCount & 0x7FFFFFFF) / 1000.0;
            float breath = 0.80f + 0.20f * (float)Math.Sin(ts * 1.4);
            float reach = Math.Min(w * 0.30f, Ui.Pf(GlowPad));
            int pad = (int)Math.Ceiling(reach) + 2, ix = (int)Math.Floor(x), bw = (int)Math.Ceiling(w) + 2 * pad + 1, bh = (int)Math.Ceiling(h) + 2 * pad + 1;
            if (_glowBmp == null || _glowBmp.Width < bw || _glowBmp.Height < bh)
            {
                int nw = Math.Max(bw, _glowBmp == null ? 0 : _glowBmp.Width), nh = Math.Max(bh, _glowBmp == null ? 0 : _glowBmp.Height);
                if (_glowBmp != null) _glowBmp.Dispose();
                _glowBmp = new Bitmap(nw, nh, PixelFormat.Format32bppPArgb);
            }
            // tabel falloff per 1/4 piksel: badan cahaya melebar + garis tepi tipis yang terang
            int steps = (int)(reach * 4) + 2; float[] fall = new float[steps]; float rim = Ui.Pf(2.5f), gain = k * breath * 255f;
            for (int i = 0; i < steps; i++)
            {
                float d = i / 4f, t = Math.Min(1f, d / reach), body = (float)Math.Pow(1 - t, 1.8);
                fall[i] = (0.38f * body + 0.30f * (float)Math.Exp(-d / rim) * (1 - t)) * gain;
            }
            byte[] cr = new byte[bw], cg = new byte[bw], cb = new byte[bw];
            for (int i = 0; i < bw; i++)
            {
                Color c = solid ? tint : GlowHsv((i / (float)bw) * 0.85 + ts * 0.09, 0.85, 1);
                cr[i] = c.R; cg[i] = c.G; cb[i] = c.B;
            }
            float cx = x + w / 2f - (ix - pad), cy = 0.5f + h / 2f + pad, hx = w / 2f - r, hy = h / 2f - r, inner = Ui.Pf(3f);
            BitmapData bd = _glowBmp.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int yy = 0; yy < bh; yy++)
                {
                    uint* row = (uint*)((byte*)bd.Scan0 + yy * bd.Stride);
                    float qy = Math.Abs(yy + 0.5f - cy) - hy, my = Math.Max(qy, 0f);
                    for (int xx = 0; xx < bw; xx++)
                    {
                        float qx = Math.Abs(xx + 0.5f - cx) - hx, mx = Math.Max(qx, 0f);
                        float d = (float)Math.Sqrt(mx * mx + my * my) + Math.Min(Math.Max(qx, qy), 0f) - r;
                        float a;
                        if (d >= reach || d < -inner) { row[xx] = 0; continue; }
                        if (d >= 0) a = fall[(int)(d * 4f)]; else a = fall[0] * (1 + d / inner);
                        int ai = (int)(a + GlowDither[yy & 3, xx & 3] / 16f);
                        if (ai <= 0) { row[xx] = 0; continue; }
                        if (ai > 255) ai = 255;
                        row[xx] = ((uint)ai << 24) | ((uint)(cr[xx] * ai / 255) << 16) | ((uint)(cg[xx] * ai / 255) << 8) | (uint)(cb[xx] * ai / 255);
                    }
                }
            }
            finally { _glowBmp.UnlockBits(bd); }
            G.DrawImage(_glowBmp, new Rectangle(ix - pad, -pad, bw, bh), new Rectangle(0, 0, bw, bh), GraphicsUnit.Pixel);
        }

        // Area cahaya tidak boleh menahan klik ke jendela di bawahnya; notch hanya menerima klik saat kursor di atasnya.
        private bool _glowPass;
        private void GlowClickThrough(bool on)
        {
            if (on == _glowPass || !IsHandleCreated) return;
            _glowPass = on;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            GlowSetLong(Handle, Native.GWL_EXSTYLE, on ? ex | 0x20 : ex & ~0x20);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int GlowSetLong(IntPtr h, int i, int v);

        // uji: LM_TLTEST=1, traffic light pada jendela uji di luar layar (tl-test.txt)
        private sealed class TlTestForm : Form { protected override bool ShowWithoutActivation { get { return true; } } }
        public void TestTl(string dir, OverlayForm dock)
        {
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            TlTestForm tf = new TlTestForm();
            tf.Text = "lm-tltest"; tf.ShowInTaskbar = false; tf.StartPosition = FormStartPosition.Manual; tf.Bounds = new Rectangle(-3000, 100, 900, 600);
            tf.Show();
            TrafficLightForm tl = new TrafficLightForm(tf.Handle);
            tl.Show(); tl.Pump();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(16); tl.Pump(); }
            log.AppendLine(tl.TestGate());
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            int off = 0, n = 200;
            for (int i = 0; i < n; i++)
            {
                tf.Location = new Point(-3000 + i * 3, 100 + (i % 40));
                sw.Start(); tl.Pump(); sw.Stop();
                Native.RECT f, b; TrafficLightForm.Geometry(tf.Handle, out f, out b);
                if (tl.Right != f.Right || tl.Top != f.Top) off++;
            }
            log.AppendLine("traffic light mengikuti jendela: " + (n - off) + "/" + n + " langkah tepat, rata-rata " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.00") + " ms per langkah");
            sw.Reset();
            for (int i = 0; i < 60; i++) { dock.Invalidate2(); sw.Start(); dock.Pump(); sw.Stop(); }
            log.AppendLine("Dock gambar ulang: rata-rata " + (sw.Elapsed.TotalMilliseconds / 60).ToString("0.00") + " ms");
            sw.Reset();
            for (int i = 0; i < 60; i++) { Invalidate2(); sw.Start(); Pump(); sw.Stop(); }
            log.AppendLine("Notch gambar ulang: rata-rata " + (sw.Elapsed.TotalMilliseconds / 60).ToString("0.00") + " ms");
            tl.Close(); tl.Dispose(); tf.Close(); tf.Dispose();
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "tl-test.txt"), log.ToString());
        }

        // uji: LM_NOTCHHOVER=1, area buka notch memakai kursor tiruan (notch-hover.txt)
        public void TestHover(string dir)
        {
            Settings.SetTemp("IslandHoverSound", "0");
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            Action<int, int, int> at = delegate (int dx, int dy, int frames)
            {
                FakeCursor = new Point(Left + MaxW / 2 + dx, Top + _yOff + dy);
                for (int i = 0; i < frames; i++) OnFrame();
            };
            Action reset = delegate { _pinned = false; at(0, 600, 150); };
            reset();
            int w = (int)_w, h = (int)_h;
            log.AppendLine("tertutup w=" + w + " h=" + h + " expanded=" + _expanded);
            string[] names = { "samping luar", "di bawah notch", "tepi kiri notch", "tepi kanan notch", "tepi bawah notch", "tengah sekilas 3 frame", "tengah lewat 6 frame" };
            int[,] c = { { w / 2 + Ui.P(20), h / 2, 40 }, { 0, h + Ui.P(10), 40 }, { -(w / 2 - Ui.P(5)), h / 2, 40 }, { w / 2 - Ui.P(5), h / 2, 40 }, { 0, h - Ui.P(2), 40 }, { 0, h / 2, 3 }, { 0, h / 2, 6 } };
            for (int i = 0; i < names.Length; i++)
            {
                reset(); at(c[i, 0], c[i, 1], c[i, 2]);
                log.AppendLine(names[i] + ": terbuka=" + _expanded + " (harus False)");
            }
            reset(); at(0, h / 2, 12); log.AppendLine("tengah 12 frame: terbuka=" + _expanded + " (harus True)");
            reset(); at(0, -_yOff, 12); log.AppendLine("atas tepi layar 12 frame: terbuka=" + _expanded + " (harus True)");
            reset(); at(w / 4, h / 3, 12); log.AppendLine("agak samping di dalam area 12 frame: terbuka=" + _expanded + " (harus True)");
            at(0, h / 2, 60); int ew = (int)_w, eh = (int)_h;
            at(ew / 2 + Ui.P(20), eh + Ui.P(10), 30); log.AppendLine("sudah terbuka, kursor di tepi longgar: terbuka=" + _expanded + " (harus True)");
            at(0, 600, 30); log.AppendLine("kursor pergi jauh: terbuka=" + _expanded + " (harus False)");
            // notifikasi berjalan: kembali ke standby sendiri (2 putaran / beberapa detik), tidak tertahan area longgar
            Func<string, int> life = delegate (string mode)
            {
                Notif tn = new Notif(); tn.AppId = "test"; tn.App = "WhatsApp"; tn.Title = "Budi"; tn.Body = "pesan panjang"; tn.Arrival = DateTime.Now.ToFileTimeUtc();
                _hud = null; _nq.Clear(); ShowNotif(tn);
                int f = 0;
                while (_hud == "notif" && f < 60 * 40)
                {
                    _nNeed = Ui.Pf(500);   // tanpa Render: anggap teks lebih panjang dari pil
                    int pw = (int)_w, ph = (int)_h;
                    if (mode == "jauh") at(0, 600, 1);
                    else if (mode == "longgar") at(pw / 2 + Ui.P(18), ph + Ui.P(12), 1);
                    else at(0, ph / 2, 1);
                    f++;
                }
                return f;
            };
            int fj = life("jauh"), fl = life("longgar"), fp = life("pil");
            log.AppendLine("notif, kursor jauh: hilang setelah " + fj / 60.0 + " dtk (harus beberapa dtk)");
            log.AppendLine("notif, kursor di area longgar dekat notch: hilang setelah " + fl / 60.0 + " dtk (harus sama dgn jauh)");
            log.AppendLine("notif, kursor tepat di pil: hilang setelah " + fp / 60.0 + " dtk (ditahan, maks ~20 dtk)");
            at(0, 600, 30);
            FakeCursor = null;
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "notch-hover.txt"), log.ToString());
        }

        // uji: bingkai animasi kucing/kelelawar dan cahaya notch
        public void SnapshotExtras(string dir)
        {
            _hud = null; _expanded = false; _pinned = false; _w = ColW; _h = ColH;
            foreach (string lk in new string[] { "kucing", "kelelawar" })
            {
                _idleKind = lk; float ln = IdleLen();
                for (int i = 0; i < 14; i++) { _idleT = ln * (i + 0.5f) / 14f; Snapshot(System.IO.Path.Combine(dir, lk + "-" + i.ToString("00") + ".png")); }
            }
            _idleT = -1;
            string g0 = Settings.GetS("NotchGlow", "0"), c0 = Settings.GetS("NotchGlowColor", "rgb"), l0 = Settings.GetS("NotchGlowLevel", "sedang");
            Settings.SetTemp("NotchGlow", "1"); _yOff = Offset;
            foreach (string c in new string[] { "rgb", "putih", "merah", "hijau", "biru", "kuning", "oranye", "ungu", "pink", "tosca" })
                foreach (string l in new string[] { "tipis", "sedang", "terang" })
                { Settings.SetTemp("NotchGlowColor", c); Settings.SetTemp("NotchGlowLevel", l); Snapshot(System.IO.Path.Combine(dir, "glow-" + c + "-" + l + ".png")); }
            Settings.SetTemp("NotchGlow", g0); Settings.SetTemp("NotchGlowColor", c0); Settings.SetTemp("NotchGlowLevel", l0); _yOff = 0;
        }
    }

    // ---------- Dock: widget seukuran ikon (RAM + pembersihan, cuaca, baterai) ----------
    internal sealed partial class DockForm
    {
        public static readonly string[] WidgetKinds = { "ram", "weather", "battery" };
        public static string WidgetSetting(string kind) { return kind == "ram" ? "DockWidgetRam" : kind == "weather" ? "DockWidgetWeather" : "DockWidgetBattery"; }
        private readonly Dictionary<string, DockItem> _widgets = new Dictionary<string, DockItem>();
        private readonly Dictionary<string, string> _widgetSig = new Dictionary<string, string>();
        private volatile bool _ramBusy;
        private volatile string _ramResult;
        private DateTime _ramResultAt = DateTime.MinValue;

        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        // Dipanggil dari RefreshRunning: widget aktif ditaruh di ujung kanan, sebelum Tong Sampah.
        private void AppendWidgets(List<DockItem> final)
        {
            foreach (string kind in WidgetKinds)
            {
                DockItem it;
                if (!Settings.Get(WidgetSetting(kind), false))
                {
                    if (_widgets.TryGetValue(kind, out it)) { if (it.Icon != null) it.Icon.Dispose(); _widgets.Remove(kind); _widgetSig.Remove(kind); }
                    continue;
                }
                if (!_widgets.TryGetValue(kind, out it))
                {
                    it = new DockItem(); it.Widget = kind; it.Key = "widget|" + kind; it.Name = kind;
                    _widgets[kind] = it;
                    UpdateWidget(it);
                }
                final.Add(it);
            }
        }

        // Perbarui isi widget (dipanggil berkala dari OnFrame). true = ada yang berubah.
        private bool UpdateWidgets()
        {
            if (_widgets.Count == 0) return false;
            bool ch = false;
            foreach (DockItem it in _widgets.Values) if (UpdateWidget(it)) ch = true;
            return ch;
        }

        private static void MemInfo(out int pct, out double usedGb, out double totalGb)
        {
            pct = 0; usedGb = 0; totalGb = 0;
            Native.MEMORYSTATUSEX m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (!Native.GlobalMemoryStatusEx(ref m)) return;
            pct = (int)m.dwMemoryLoad;
            totalGb = m.ullTotalPhys / 1073741824.0; usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }

        private bool UpdateWidget(DockItem it)
        {
            string sig, name;
            if (it.Widget == "ram")
            {
                int pct; double used, total; MemInfo(out pct, out used, out total);
                bool done = _ramResult != null && (DateTime.Now - _ramResultAt).TotalSeconds < 6;
                if (!done && !_ramBusy) _ramResult = null;
                sig = pct + "|" + _ramBusy + "|" + done;
                name = _ramBusy ? L.T("Membersihkan RAM...")
                    : done ? _ramResult
                    : "RAM " + used.ToString("0.0") + " / " + total.ToString("0.0") + " GB (" + pct + "%) - " + L.T("klik untuk membersihkan");
            }
            else if (it.Widget == "weather")
            {
                Weather.Refresh(false);
                WeatherInfo wi = Weather.Current;
                sig = wi == null ? "-" : wi.Temp + "|" + wi.Cond;
                name = wi == null ? L.T("Cuaca: memuat...") : wi.Place + " - " + wi.Temp + " - " + wi.Cond;
            }
            else
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                bool has = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryLifePercent <= 1f;
                int pct = has ? (int)Math.Round(ps.BatteryLifePercent * 100) : -1;
                bool plug = ps.PowerLineStatus == PowerLineStatus.Online;
                sig = pct + "|" + plug;
                name = !has ? L.T("Tanpa baterai (daya listrik)") : L.T("Baterai") + " " + pct + "%" + (plug ? " - " + L.T("mengisi") : "");
            }
            bool changed = it.Name != name;
            it.Name = name;
            string old;
            if (it.Icon != null && _widgetSig.TryGetValue(it.Widget, out old) && old == sig) return changed;
            _widgetSig[it.Widget] = sig;
            Bitmap nb = RenderWidget(it.Widget);
            Bitmap ob = it.Icon; it.Icon = nb;
            if (ob != null) ob.Dispose();
            return true;
        }

        private static Font WFont(float px, bool bold)
        {
            using (Font f = Ui.F(10f, bold ? FontStyle.Bold : FontStyle.Regular)) return new Font(f.FontFamily, px, f.Style, GraphicsUnit.Pixel);
        }

        private static void WText(Graphics g, string s, float px, bool bold, Color c, RectangleF r)
        {
            using (Font f = WFont(px, bold)) using (SolidBrush b = new SolidBrush(c)) using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, r, sf);
            }
        }

        // Ubin kaca cair 128x128: dasar gelap tembus pandang, kilau di atas, tepi terang tipis.
        private Bitmap RenderWidget(string kind)
        {
            Bitmap o = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAlias;
                RectangleF r = new RectangleF(8, 8, 112, 112);
                using (GraphicsPath p = Glass.Round(r, 28f))
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(150, 46, 48, 62), Color.FromArgb(175, 16, 17, 24), 90f)) g.FillPath(b, p);
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(46, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90f)) g.FillPath(b, p);
                    Region old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                    using (GraphicsPath sh = new GraphicsPath())
                    {
                        sh.AddEllipse(r.X - 30, r.Y - 66, r.Width + 60, 104);
                        using (LinearGradientBrush sb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, 42), Color.FromArgb(52, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(sb, sh);
                    }
                    g.Clip = old; old.Dispose();
                    using (LinearGradientBrush pb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), 90f))
                    using (Pen pen = new Pen(pb, 1.8f)) g.DrawPath(pen, p);
                }
                if (kind == "ram") WidgetRam(g);
                else if (kind == "weather") WidgetWeather(g);
                else WidgetBattery(g);
            }
            return o;
        }

        private void WidgetRam(Graphics g)
        {
            int pct; double used, total; MemInfo(out pct, out used, out total);
            bool done = _ramResult != null && !_ramBusy;
            Color col = pct < 60 ? Color.FromArgb(48, 209, 88) : pct < 85 ? Color.FromArgb(255, 159, 10) : Color.FromArgb(255, 69, 58);
            RectangleF ring = new RectangleF(31, 21, 66, 66);
            using (Pen tr = new Pen(Color.FromArgb(46, 255, 255, 255), 8f)) g.DrawEllipse(tr, ring);
            using (Pen ar = new Pen(col, 8f)) { ar.StartCap = ar.EndCap = LineCap.Round; g.DrawArc(ar, ring, -90, Math.Max(4f, 360f * pct / 100f)); }
            if (_ramBusy) WText(g, "...", 24, true, Color.White, new RectangleF(31, 15, 66, 66));
            else if (done)
                using (Pen ck = new Pen(Color.FromArgb(48, 209, 88), 6f)) { ck.StartCap = ck.EndCap = LineCap.Round; ck.LineJoin = LineJoin.Round; g.DrawLines(ck, new PointF[] { new PointF(52, 55), new PointF(61, 64), new PointF(77, 46) }); }
            else WText(g, pct.ToString(), 25, true, Color.White, new RectangleF(31, 22, 66, 66));
            WText(g, "RAM", 15, true, Color.FromArgb(215, 255, 255, 255), new RectangleF(8, 90, 112, 22));
        }

        private void WidgetBattery(Graphics g)
        {
            PowerStatus ps = SystemInformation.PowerStatus;
            bool has = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryLifePercent <= 1f;
            float f = has ? Math.Max(0f, Math.Min(1f, ps.BatteryLifePercent)) : 1f;
            bool plug = ps.PowerLineStatus == PowerLineStatus.Online;
            Color col = !has ? Color.FromArgb(160, 164, 180) : plug ? Color.FromArgb(48, 209, 88) : f <= 0.2f ? Color.FromArgb(255, 69, 58) : f <= 0.4f ? Color.FromArgb(255, 204, 0) : Color.FromArgb(48, 209, 88);
            RectangleF body = new RectangleF(28, 34, 64, 34);
            using (GraphicsPath bp = Glass.Round(body, 10f)) using (Pen pen = new Pen(Color.FromArgb(215, 255, 255, 255), 3.4f)) g.DrawPath(pen, bp);
            using (GraphicsPath cap = Glass.Round(new RectangleF(95, 44, 6, 14), 3f)) using (SolidBrush cb = new SolidBrush(Color.FromArgb(215, 255, 255, 255))) g.FillPath(cb, cap);
            RectangleF fill = new RectangleF(34, 40, Math.Max(7f, 52f * f), 22);
            using (GraphicsPath fp = Glass.Round(fill, 5f)) using (SolidBrush fb = new SolidBrush(col)) g.FillPath(fb, fp);
            if (plug && has)
            {
                PointF[] bolt = { new PointF(63, 38), new PointF(51, 53), new PointF(59, 53), new PointF(56, 65), new PointF(69, 49), new PointF(61, 49) };
                using (Pen bo = new Pen(Color.FromArgb(150, 0, 0, 0), 3f)) { bo.LineJoin = LineJoin.Round; g.DrawPolygon(bo, bolt); }
                using (SolidBrush bb = new SolidBrush(Color.White)) g.FillPolygon(bb, bolt);
            }
            WText(g, has ? (int)Math.Round(f * 100) + "%" : "AC", 21, true, Color.White, new RectangleF(8, 76, 112, 30));
        }

        private void WidgetWeather(Graphics g)
        {
            WeatherInfo wi = Weather.Current;
            string cond = wi == null ? "" : (wi.Cond ?? "").ToLowerInvariant();
            bool thunder = cond.Contains("petir") || cond.Contains("thunder");
            bool rain = thunder || cond.Contains("hujan") || cond.Contains("gerimis") || cond.Contains("rain") || cond.Contains("drizzle") || cond.Contains("shower");
            bool snow = cond.Contains("salju") || cond.Contains("snow") || cond.Contains("sleet");
            bool fog = cond.Contains("kabut") || cond.Contains("fog") || cond.Contains("mist") || cond.Contains("haze") || cond.Contains("asap");
            bool partly = cond.Contains("sebagian") || cond.Contains("partly") || cond.Contains("cerah berawan");
            bool cloud = rain || snow || fog || partly || cond.Contains("awan") || cond.Contains("cloud") || cond.Contains("mendung") || cond.Contains("overcast");
            bool night = DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 6;
            bool sun = wi != null && (!cloud || partly);
            if (sun)
            {
                float sx = cloud ? 50 : 64, sy = cloud ? 40 : 46, sr = cloud ? 15 : 19;
                if (night)
                {
                    using (GraphicsPath mp = new GraphicsPath())
                    {
                        mp.AddEllipse(sx - sr, sy - sr, sr * 2, sr * 2);
                        using (Region mr = new Region(mp)) using (GraphicsPath cut = new GraphicsPath())
                        {
                            cut.AddEllipse(sx - sr * 0.35f, sy - sr * 1.25f, sr * 2, sr * 2); mr.Exclude(cut);
                            using (SolidBrush mb = new SolidBrush(Color.FromArgb(235, 238, 255))) g.FillRegion(mb, mr);
                        }
                    }
                }
                else
                {
                    using (Pen ray = new Pen(Color.FromArgb(255, 204, 0), 3.6f))
                    {
                        ray.StartCap = ray.EndCap = LineCap.Round;
                        for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(ray, sx + (float)Math.Cos(a) * (sr + 5), sy + (float)Math.Sin(a) * (sr + 5), sx + (float)Math.Cos(a) * (sr + 11), sy + (float)Math.Sin(a) * (sr + 11)); }
                    }
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(255, 204, 0))) g.FillEllipse(sb, sx - sr, sy - sr, sr * 2, sr * 2);
                }
            }
            if (cloud || wi == null)
            {
                float ox = sun ? 6 : 0, oy = rain || snow ? -5 : 0;
                using (SolidBrush cb = new SolidBrush(wi == null ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(244, 246, 252)))
                {
                    g.FillEllipse(cb, 36 + ox, 44 + oy, 30, 26); g.FillEllipse(cb, 52 + ox, 34 + oy, 32, 34); g.FillEllipse(cb, 70 + ox, 46 + oy, 24, 22);
                    using (GraphicsPath bp = Glass.Round(new RectangleF(38 + ox, 54 + oy, 54, 16), 8f)) g.FillPath(cb, bp);
                }
                if (rain)
                    using (Pen dp = new Pen(Color.FromArgb(100, 190, 255), 3.4f))
                    { dp.StartCap = dp.EndCap = LineCap.Round; for (int i = 0; i < 3; i++) g.DrawLine(dp, 52 + i * 14 + ox, 72 + oy, 48 + i * 14 + ox, 81 + oy); }
                if (thunder)
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 214, 10)))
                        g.FillPolygon(tb, new PointF[] { new PointF(68 + ox, 58 + oy), new PointF(58 + ox, 72 + oy), new PointF(65 + ox, 72 + oy), new PointF(61 + ox, 84 + oy), new PointF(74 + ox, 67 + oy), new PointF(67 + ox, 67 + oy) });
                if (snow)
                    using (SolidBrush sb = new SolidBrush(Color.White)) for (int i = 0; i < 3; i++) g.FillEllipse(sb, 49 + i * 14 + ox, 73 + oy, 6, 6);
                if (fog)
                    using (Pen fp = new Pen(Color.FromArgb(190, 255, 255, 255), 3.2f)) { fp.StartCap = fp.EndCap = LineCap.Round; g.DrawLine(fp, 40, 76, 88, 76); }
            }
            string temp = wi == null ? "--" : (wi.Temp ?? "").Replace("C", "").Replace("F", "").Trim();
            WText(g, temp, 23, true, Color.White, new RectangleF(8, 80, 116, 30));
        }

        // klik kiri pada widget
        private void ClickWidget(DockItem it)
        {
            if (it.Widget == "ram") CleanRam();
            else if (it.Widget == "weather") Weather.Refresh(true);
            else Program.ControlPanel("battery", Left + (int)_bgX + (int)_bgW);
        }

        // Pembersihan RAM: kosongkan working set semua proses yang boleh diakses (memori yang tidak dipakai dikembalikan ke sistem).
        private void CleanRam()
        {
            if (_ramBusy) return;
            _ramBusy = true; _ramResult = null;
            Native.MEMORYSTATUSEX m0 = new Native.MEMORYSTATUSEX();
            m0.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            Native.GlobalMemoryStatusEx(ref m0);
            ulong before = m0.ullAvailPhys;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    foreach (Process p in Process.GetProcesses())
                    {
                        try
                        {
                            IntPtr h = OpenProcess(0x0100 | 0x0400 /* SET_QUOTA | QUERY_INFORMATION */, false, p.Id);
                            if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                    GC.Collect();
                    Thread.Sleep(700);
                }
                catch (Exception ex) { Log.Write("ram: " + ex.Message); }
                Native.MEMORYSTATUSEX m1 = new Native.MEMORYSTATUSEX();
                m1.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
                Native.GlobalMemoryStatusEx(ref m1);
                long freed = (long)m1.ullAvailPhys - (long)before;
                _ramResult = freed > 8 * 1048576L
                    ? L.T("RAM dibersihkan:") + " +" + (freed >= 1073741824L ? (freed / 1073741824.0).ToString("0.0") + " GB" : (freed / 1048576L) + " MB") + " " + L.T("lega")
                    : L.T("RAM sudah bersih");
                _ramResultAt = DateTime.Now;
                _ramBusy = false;
            });
        }

        private void ShowWidgetMenu(DockItem it)
        {
            ContextMenuStrip m = Menus.Create();
            string kind = it.Widget;
            Menus.Add(m, kind == "ram" ? "Widget RAM" : kind == "weather" ? L.T("Widget Cuaca") : L.T("Widget Baterai"), null);
            Menus.Sep(m);
            if (kind == "ram") Menus.Add(m, L.T("Bersihkan RAM sekarang"), delegate { CleanRam(); });
            else if (kind == "weather") Menus.Add(m, L.T("Segarkan cuaca"), delegate { Weather.Refresh(true); });
            else Menus.Add(m, L.T("Buka panel baterai"), delegate { ClickWidget(it); });
            Menus.Add(m, L.T("Sembunyikan widget ini"), delegate { Settings.Set(WidgetSetting(kind), false); ReloadItems(); });
            Menus.Sep(m);
            Menus.Add(m, L.T("Pengaturan LiquidMac..."), delegate { Program.RequestSettings(); });
            m.Show(Cursor.Position);
        }

        // uji: Dock dengan ketiga widget
        public void SnapshotWidgets(string dir)
        {
            foreach (string k in WidgetKinds) Settings.SetTemp(WidgetSetting(k), "1");
            ReloadItems(); Snapshot(System.IO.Path.Combine(dir, "dock-widgets.png"));
            foreach (string k in WidgetKinds) { DockItem it; if (_widgets.TryGetValue(k, out it) && it.Icon != null) it.Icon.Save(System.IO.Path.Combine(dir, "widget-" + k + ".png")); }
            foreach (string k in WidgetKinds) Settings.SetTemp(WidgetSetting(k), "0");
            ReloadItems();
        }
    }
}
