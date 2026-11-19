using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiquidMac
{
    // ============================================================
    //  SKIN NOTCH (setting NotchSkin): bawaan | notch (menempel di tepi atas, seperti MacBook) | island (mengambang, seperti iPhone)
    // ============================================================
    internal sealed partial class NotchForm
    {
        private static string Skin { get { return Settings.GetS("NotchSkin", "bawaan"); } }

        // Notch menempel: sisi atas rata dengan tepi layar, hanya sudut bawah membulat, "telinga" cekung kecil di kiri-kanan atas.
        private GraphicsPath FlushPath(float x, float w, float h)
        {
            float top = -(float)Math.Round(_drop) - _yOff - 1f, bottom = 0.5f + h;
            float rb = Math.Max(0.5f, Math.Min(Math.Min(h * 0.62f, Ui.Pf(30)), w / 2f - 1f));
            float e = Math.Max(0.5f, Math.Min(Ui.Pf(7), (bottom - top) * 0.5f));
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(x - e, top, x - e * 0.45f, top, x, top + e * 0.55f, x, top + e);
            p.AddLine(x, top + e, x, bottom - rb);
            p.AddArc(x, bottom - 2 * rb, 2 * rb, 2 * rb, 180, -90);
            p.AddLine(x + rb, bottom, x + w - rb, bottom);
            p.AddArc(x + w - 2 * rb, bottom - 2 * rb, 2 * rb, 2 * rb, 90, -90);
            p.AddLine(x + w, bottom - rb, x + w, top + e);
            p.AddBezier(x + w, top + e, x + w, top + e * 0.55f, x + w + e * 0.45f, top, x + w + e, top);
            p.CloseFigure();
            return p;
        }

        // Bayangan lembut di bawah notch/island (beberapa lapis tipis bertumpuk, tanpa tepi tegas).
        private void SkinShadow(float x, float w, float h, float r, bool flush)
        {
            if (h < Ui.Pf(6) || w < Ui.Pf(20)) return;
            float dy = Ui.Pf(flush ? 3 : 6);
            int a = flush ? 9 : 13;
            for (int i = 7; i >= 1; i--)
            {
                float k = Ui.Pf(2.3f) * i;
                RectangleF sr = new RectangleF(x - k * 0.6f, 0.5f + dy - k * 0.25f, w + k * 1.2f, h + k * 0.85f);
                using (GraphicsPath sp = Glass.Round(sr, Math.Min(r + k * 0.6f, sr.Height / 2f)))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(a, 0, 0, 0))) G.FillPath(sb, sp);
            }
        }

        // Kilau kaca tipis di bagian atas island mengambang.
        private void SkinGloss(GraphicsPath path, float x, float w, float h)
        {
            if (h < Ui.Pf(8) || h > ColH * 1.6f) return;   // hanya saat diam / HUD, tidak menutupi kartu yang terbuka
            Region old = G.Clip; G.SetClip(path, CombineMode.Intersect);
            RectangleF gr = new RectangleF(x, 0.5f, w, h * 0.55f);
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(gr.X, gr.Y - 1, gr.Width, gr.Height + 2), Color.FromArgb(17, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                G.FillRectangle(b, gr);
            G.Clip = old; old.Dispose();
        }

        // Lensa kamera kecil (notch: di tengah, island: di kanan) saat island diam.
        private void DrawLens(float cx, float cy)
        {
            float d = Ui.Pf(9);
            RectangleF lr = new RectangleF(cx - d / 2f, cy - d / 2f, d, d);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 12, 15, 22))) G.FillEllipse(b, lr);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(lr);
                using (PathGradientBrush pg = new PathGradientBrush(gp))
                {
                    pg.CenterPoint = new PointF(cx - d * 0.1f, cy - d * 0.12f);
                    pg.CenterColor = Color.FromArgb(255, 44, 66, 104);
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, 12, 15, 22) };
                    G.FillPath(pg, gp);
                }
            }
            float hl = d * 0.24f;
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(150, 150, 190, 255))) G.FillEllipse(hb, cx - d * 0.2f, cy - d * 0.24f, hl, hl);
        }

        // uji: LM_CARDS=1, notch tiap skin (diam & terbuka)
        public void SnapshotSkins(string dir)
        {
            string s0 = Settings.GetS("NotchSkin", "bawaan");
            foreach (string sk in new string[] { "bawaan", "notch", "island" })
            {
                Settings.SetTemp("NotchSkin", sk);
                _yOff = (sk == "notch" || GlowOn) ? Offset : 0;
                _hud = null; _idleT = -1; _expanded = false; _pinned = false; _w = ColW; _h = ColH;
                Snapshot(System.IO.Path.Combine(dir, "skin-" + sk + "-diam.png"));
                _pinned = true; _expanded = true; _card = "clock"; _slide = 0; _w = ExpW; _h = MediaExpH;
                Snapshot(System.IO.Path.Combine(dir, "skin-" + sk + "-buka.png"));
            }
            Settings.SetTemp("NotchSkin", s0);
            _pinned = false; _expanded = false; _w = ColW; _h = ColH; _yOff = 0;
        }
    }
}
