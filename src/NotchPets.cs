using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiquidMac
{
    // ---------- Notch: hewan lucu untuk animasi standby (hamster, anak anjing, ikan, pinguin, kelinci) ----------
    internal sealed partial class NotchForm
    {
        private static float PetDuration(string kind)
        {
            switch (kind)
            {
                case "hamster": return 4.4f;
                case "anjing": return 4.6f;
                case "ikan": return 4.8f;
                case "pinguin": return 4.9f;
                case "kelinci": return 4.6f;
            }
            return 0f;
        }

        private bool DrawPet(float x0, float w, float h)
        {
            switch (_idleKind)
            {
                case "hamster": DrawHamster(x0, w, h); return true;
                case "anjing": DrawPuppy(x0, w, h); return true;
                case "ikan": DrawFish(x0, w, h); return true;
                case "pinguin": DrawPenguin(x0, w, h); return true;
                case "kelinci": DrawBunny(x0, w, h); return true;
            }
            return false;
        }

        // ----- bantuan kecil -----
        private static float PetLerp(float a, float b, float u) { return a + (b - a) * u; }

        // tonjolan notch yang bergoyang lalu tenang (dipakai saat hewan masuk kembali), amp sudah meluruh ke ~0 di akhir
        private void PetBump(float cx, float B, float hw, float amp)
        {
            if (amp <= 0.3f) return;
            using (SolidBrush ink = new SolidBrush(Color.Black)) using (Pen rim = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
            { RectangleF wb = new RectangleF(cx - hw, B - amp, hw * 2, amp * 2); G.FillEllipse(ink, wb); G.DrawEllipse(rim, wb); }
        }

        private static float PetSettle(float t, float t0, float dur, float amp, float freq)
        {
            if (t < t0) return 0f;
            float u = (t - t0) / dur;
            if (u >= 1f) return 0f;
            return amp * (float)(Math.Exp(-3.6 * u) * Math.Cos(u * freq)) * (1f - u);
        }

        private Region PetClipBelow(float x0, float w, float B, float side)
        {
            Region old = G.Clip;
            G.SetClip(new RectangleF(x0 - side, B - 0.5f, w + side * 2, Ui.Pf(150)), CombineMode.Intersect);
            return old;
        }

        private void PetEyes(float ex, float ey, float er, float blink, bool happy, float lookX, Color ink)
        {
            if (happy)
            {
                using (Pen hp = new Pen(ink, Math.Max(1.2f, er * 0.55f))) { hp.StartCap = hp.EndCap = LineCap.Round; G.DrawArc(hp, ex - er, ey - er * 0.5f, er * 2, er * 1.6f, 200, 140); }
                return;
            }
            using (SolidBrush eb = new SolidBrush(ink)) G.FillEllipse(eb, ex - er + lookX, ey - er * blink, er * 2, er * 2 * blink);
            if (blink > 0.5f)
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    G.FillEllipse(hb, ex - er * 0.55f + lookX, ey - er * 0.62f, er * 0.62f, er * 0.62f);
                    G.FillEllipse(hb, ex + er * 0.18f + lookX, ey + er * 0.22f, er * 0.28f, er * 0.28f);
                }
        }

        private void PetBlush(float x, float y, float r)
        {
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(120, 255, 120, 150))) G.FillEllipse(bb, x - r, y - r * 0.6f, r * 2, r * 1.2f);
        }

        private void PetHeart(float x, float y, float s, int alpha)
        {
            if (alpha <= 4 || s < 0.5f) return;
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddBezier(x, y + s * 0.9f, x - s * 1.3f, y - s * 0.1f, x - s * 0.6f, y - s * 1.1f, x, y - s * 0.35f);
                hp.AddBezier(x, y - s * 0.35f, x + s * 0.6f, y - s * 1.1f, x + s * 1.3f, y - s * 0.1f, x, y + s * 0.9f);
                hp.CloseFigure();
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(alpha, 255, 90, 130))) G.FillPath(hb, hp);
            }
        }

        // uji (LM_NOTCHPETS=1 di mode --snapshot): bingkai tiap animasi hewan
        public void SnapshotPets(string dir)
        {
            _hud = null; _expanded = false; _pinned = false; _w = ColW; _h = ColH;
            foreach (string lk in new string[] { "hamster", "anjing", "ikan", "pinguin", "kelinci" })
            {
                _idleKind = lk; float ln = IdleLen();
                for (int i = 0; i < 24; i++) { _idleT = ln * (i + 0.5f) / 24f; Snapshot(System.IO.Path.Combine(dir, "pet-" + lk + "-" + i.ToString("00") + ".png")); }
                _idleT = ln - 0.001f; Snapshot(System.IO.Path.Combine(dir, "pet-" + lk + "-end.png"));
                _idleT = 0f; Snapshot(System.IO.Path.Combine(dir, "pet-" + lk + "-start.png"));
            }
            _idleT = -1;
        }

        // ===== Hamster: kepala muncul dari bawah notch, sebutir biji jatuh ke cakarnya, digerogoti sampai pipinya menggembung, lalu sembunyi lagi =====
        private void DrawHamster(float x0, float w, float h)
        {
            float t = _idleT, cx = x0 + w / 2f, B = 0.5f + h, R = h * 0.56f;
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
            float pop = t < 0.55f ? EaseBack(t / 0.55f) : t < 3.80f ? 1f : 1f - EaseIO((t - 3.80f) / 0.42f);
            const float b0 = 1.20f, b1 = 3.05f, bp = 0.37f;   // lima gigitan
            float chew = t > b0 && t < b1 ? (float)Math.Abs(Math.Sin((t - b0) / bp * Math.PI)) : 0f;
            float eaten = Clamp01((t - b0) / (b1 - b0));
            float cheek = 0.1f + 0.9f * eaten + chew * 0.05f;
            if (t > b1) cheek += (float)Math.Sin((t - b1) * 18) * 0.06f * (float)Math.Exp(-(t - b1) * 3);
            float hx = cx + (float)Math.Sin(t * 1.7) * R * 0.05f * pop;
            float yc = B - R * 1.35f + pop * R * 2.32f + chew * R * 0.05f;
            bool happy = t > 3.12f && t < 3.72f;
            float blink = (t > 0.85f && t < 0.97f) ? 0.12f : 1f;
            float look = t > 0.55f && t < 1.0f ? (float)Math.Sin((t - 0.55f) / 0.45f * Math.PI) * R * 0.05f : 0f;

            Region old = PetClipBelow(x0, w, B, Ui.Pf(60));
            if (pop > 0.01f)
            {
                Color fur = Color.FromArgb(240, 166, 86), cream = Color.FromArgb(255, 240, 214), line = Color.FromArgb(150, 92, 48);
                using (Pen ol = new Pen(line, Ui.Pf(1.5f)))
                using (SolidBrush fb = new SolidBrush(fur)) using (SolidBrush cb = new SolidBrush(cream))
                using (SolidBrush pk = new SolidBrush(Color.FromArgb(255, 160, 175)))
                {
                    ol.LineJoin = LineJoin.Round;
                    RectangleF head = new RectangleF(hx - R * 0.95f, yc - R * 0.88f, R * 1.9f, R * 1.76f);
                    RectangleF[] ears = new RectangleF[2], cheeks = new RectangleF[2];
                    for (int s = -1; s <= 1; s += 2)
                    {
                        int i = s < 0 ? 0 : 1;
                        ears[i] = new RectangleF(hx + s * R * 0.62f - R * 0.27f, yc - R * 0.98f, R * 0.54f, R * 0.5f);
                        float crx = R * (0.36f + 0.24f * cheek), cry = R * (0.34f + 0.18f * cheek), ccx = hx + s * (R * 0.56f + R * 0.16f * cheek);
                        cheeks[i] = new RectangleF(ccx - crx, yc + R * 0.30f - cry, crx * 2, cry * 2);
                    }
                    // garis tepi luar dulu, lalu isi menutup garis bagian dalam
                    G.DrawEllipse(ol, ears[0]); G.DrawEllipse(ol, ears[1]); G.DrawEllipse(ol, head); G.DrawEllipse(ol, cheeks[0]); G.DrawEllipse(ol, cheeks[1]);
                    G.FillEllipse(fb, ears[0]); G.FillEllipse(fb, ears[1]); G.FillEllipse(fb, head);
                    for (int i = 0; i < 2; i++) { RectangleF e = ears[i]; e.Inflate(-R * 0.1f, -R * 0.1f); G.FillEllipse(pk, e); }
                    G.FillEllipse(cb, cheeks[0]); G.FillEllipse(cb, cheeks[1]);
                    G.FillEllipse(cb, hx - R * 0.5f, yc - R * 0.05f, R * 1.0f, R * 0.9f);              // moncong krem
                    using (SolidBrush st = new SolidBrush(Color.FromArgb(255, 236, 205))) G.FillEllipse(st, hx - R * 0.09f, yc - R * 0.86f, R * 0.18f, R * 0.42f);   // garis dahi
                    // mata, hidung, mulut
                    for (int s = -1; s <= 1; s += 2) PetEyes(hx + s * R * 0.38f, yc - R * 0.12f, R * 0.13f, blink, happy, look, Color.FromArgb(30, 20, 18));
                    for (int s = -1; s <= 1; s += 2) PetBlush(hx + s * R * (0.62f + 0.12f * cheek), yc + R * 0.24f, R * 0.17f);
                    float ny = yc + R * 0.14f;
                    G.FillEllipse(pk, hx - R * 0.09f, ny - R * 0.055f, R * 0.18f, R * 0.12f);
                    using (Pen mp = new Pen(line, Ui.Pf(1.1f)))
                    {
                        mp.StartCap = mp.EndCap = LineCap.Round;
                        G.DrawLine(mp, hx, ny + R * 0.06f, hx, ny + R * 0.15f);
                        G.DrawArc(mp, hx - R * 0.16f, ny + R * 0.06f, R * 0.16f, R * 0.14f, 0, 160);
                        G.DrawArc(mp, hx, ny + R * 0.06f, R * 0.16f, R * 0.14f, 20, 160);
                    }
                    if (chew > 0.05f)
                        using (SolidBrush tb = new SolidBrush(Color.White)) G.FillRectangle(tb, hx - R * 0.07f, ny + R * 0.20f, R * 0.14f, R * 0.09f + R * 0.06f * chew);
                    // biji jatuh dari notch ke cakar, mengecil setiap gigitan
                    float sy = yc + R * 0.80f, seed = 0f, syy = sy;
                    if (t > 0.62f && t < b1)
                    {
                        float fu = Clamp01((t - 0.62f) / 0.36f);
                        syy = PetLerp(B - R * 0.4f, sy, fu * fu);
                        seed = 1f - eaten * 0.92f;
                        if (chew > 0.6f) syy -= R * 0.04f;
                    }
                    if (seed > 0.02f)
                    {
                        float sw = R * 0.34f * (0.6f + 0.4f * seed), sh = R * 0.60f * seed;
                        RectangleF sr = new RectangleF(hx - sw / 2f, syy - sh / 2f, sw, sh);
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(62, 58, 64))) G.FillEllipse(sb, sr);
                        using (Pen sp = new Pen(Color.FromArgb(220, 236, 228, 210), Ui.Pf(1f))) { G.DrawLine(sp, hx - sw * 0.18f, syy - sh * 0.3f, hx - sw * 0.18f, syy + sh * 0.3f); G.DrawLine(sp, hx + sw * 0.18f, syy - sh * 0.3f, hx + sw * 0.18f, syy + sh * 0.3f); }
                    }
                    // remah kecil saat menggigit
                    if (chew > 0.5f && t < b1)
                        using (SolidBrush cr = new SolidBrush(Color.FromArgb((int)(220 * chew), 236, 228, 210)))
                        {
                            float k = (t - b0) / bp, ph = k - (float)Math.Floor(k), sgn = ((int)k % 2 == 0) ? 1 : -1;
                            G.FillEllipse(cr, hx + sgn * R * (0.25f + 0.5f * ph) - 1.2f, sy - R * 0.25f + ph * R * 0.6f, 2.4f, 2.4f);
                        }
                    // cakar mungil memegang biji
                    for (int s = -1; s <= 1; s += 2)
                    {
                        RectangleF pr = new RectangleF(hx + s * R * 0.22f - R * 0.13f, sy + R * 0.02f + (s > 0 ? chew * R * 0.03f : 0), R * 0.26f, R * 0.22f);
                        G.DrawEllipse(ol, pr); G.FillEllipse(pk, pr);
                    }
                }
            }
            PetBump(cx, B, R * 1.3f, PetSettle(t, 4.05f, 0.35f, R * 0.55f, 11f));
            G.Clip = old; old.Dispose();
        }

        // ===== Anak anjing: kepalanya menyembul dari ujung kanan island (island jadi badannya), ekor bergoyang di ujung kiri =====
        private void DrawPuppy(float x0, float w, float h)
        {
            float t = _idleT, B = 0.5f + h, R = h * 0.60f, xr = x0 + w, xl = x0;
            float out1 = t < 0.6f ? EaseBack(t / 0.6f) : t < 3.75f ? 1f : 1f - EaseIO((t - 3.75f) / 0.5f);
            float tail = t < 0.9f ? 0f : t < 1.25f ? EaseBack((t - 0.9f) / 0.35f) : t < 3.95f ? 1f : 1f - EaseIO((t - 3.95f) / 0.35f);
            float tilt = -8f + 22f * Win(t, 1.35f, 2.15f) - 18f * Win(t, 2.2f, 2.95f);
            float dTilt = 22f * (float)Math.Cos(Math.PI * (t - 1.35f) / 0.8f) * (t > 1.35f && t < 2.15f ? 1 : 0) - 18f * (float)Math.Cos(Math.PI * (t - 2.2f) / 0.75f) * (t > 2.2f && t < 2.95f ? 1 : 0);
            bool happy = t > 3.0f && t < 3.65f;
            float blink = (t > 0.95f && t < 1.07f) || (t > 2.5f && t < 2.6f) ? 0.12f : 1f;
            float pant = t > 0.75f && t < 3.8f ? Clamp01((t - 0.75f) / 0.2f) * (0.75f + 0.25f * (float)Math.Sin(t * (happy ? 26 : 15))) : 0f;

            // ekor: digambar di belakang island, menyembul dari ujung kiri
            if (tail > 0.01f)
            {
                float wag = (float)Math.Sin(t * 19) * 26f * Clamp01((t - 1.1f) / 0.3f);
                float bx = xl + h * 0.22f, by = 0.5f + h * 0.62f, L = h * 0.62f * tail;
                double a = (200 + wag) * Math.PI / 180;
                float ex = bx + (float)Math.Cos(a) * L, ey = by + (float)Math.Sin(a) * L;
                float mx = bx + (float)Math.Cos(a - 0.5) * L * 0.6f, my = by + (float)Math.Sin(a - 0.5) * L * 0.6f;
                using (Pen tp = new Pen(Color.FromArgb(150, 95, 55), h * 0.26f)) using (Pen tf = new Pen(Color.FromArgb(222, 170, 110), h * 0.17f))
                {
                    tp.StartCap = tp.EndCap = tf.StartCap = tf.EndCap = LineCap.Round;
                    PointF[] cv = { new PointF(bx, by), new PointF(mx, my), new PointF(ex, ey) };
                    G.DrawCurve(tp, cv); G.DrawCurve(tf, cv);
                }
            }
            if (out1 > 0.01f)
            {
                float hx = PetLerp(xr - R * 1.3f, xr + R * 0.98f, out1), hy = 0.5f + h * 0.5f + R * 0.42f;
                GraphicsState gs = G.Save();
                G.TranslateTransform(hx, hy + R * 0.7f); G.RotateTransform(tilt); G.TranslateTransform(0, -R * 0.7f);
                Color fur = Color.FromArgb(222, 170, 110), ear = Color.FromArgb(150, 95, 55), line = Color.FromArgb(110, 66, 36);
                using (Pen ol = new Pen(line, Ui.Pf(1.5f))) using (SolidBrush fb = new SolidBrush(fur)) using (SolidBrush eb = new SolidBrush(ear))
                {
                    ol.LineJoin = LineJoin.Round;
                    RectangleF head = new RectangleF(-R, -R * 0.9f, R * 2f, R * 1.8f);
                    G.DrawEllipse(ol, head); G.FillEllipse(fb, head);
                    using (SolidBrush sp = new SolidBrush(Color.FromArgb(190, 132, 78))) G.FillEllipse(sp, R * 0.12f, -R * 0.52f, R * 0.56f, R * 0.5f);   // bercak di mata
                    // telinga terkulai: berayun berlawanan dengan arah miring kepala
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float flop = -dTilt * 0.9f + (float)Math.Sin(t * 9 + s) * 3f;
                        GraphicsState es = G.Save();
                        G.TranslateTransform(s * R * 0.80f, -R * 0.60f); G.RotateTransform(-s * 22f + flop);
                        using (GraphicsPath ep = new GraphicsPath())
                        {
                            ep.AddClosedCurve(new PointF[] { new PointF(-s * R * 0.18f, -R * 0.05f), new PointF(s * R * 0.22f, -R * 0.08f), new PointF(s * R * 0.30f, R * 0.55f), new PointF(s * R * 0.08f, R * 0.85f), new PointF(-s * R * 0.14f, R * 0.5f) }, 0.55f);
                            G.DrawPath(ol, ep); G.FillPath(eb, ep);
                        }
                        G.Restore(es);
                    }
                    // moncong, hidung, lidah
                    RectangleF mz = new RectangleF(-R * 0.48f, R * 0.02f, R * 0.96f, R * 0.66f);
                    using (SolidBrush wb = new SolidBrush(Color.FromArgb(255, 246, 232))) G.FillEllipse(wb, mz);
                    if (pant > 0.01f)
                    {
                        float tl = R * (0.18f + 0.26f * pant);
                        using (GraphicsPath tg = new GraphicsPath())
                        {
                            tg.AddArc(-R * 0.15f, R * 0.40f + tl - R * 0.3f, R * 0.30f, R * 0.30f, 0, 180);
                            tg.AddLine(-R * 0.15f, R * 0.40f + tl - R * 0.15f, -R * 0.15f, R * 0.38f);
                            tg.AddLine(R * 0.15f, R * 0.38f, R * 0.15f, R * 0.40f + tl - R * 0.15f);
                            tg.CloseFigure();
                            using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 112, 140))) G.FillPath(tb, tg);
                            using (Pen tc = new Pen(Color.FromArgb(200, 70, 95), Ui.Pf(0.9f))) { G.DrawPath(tc, tg); G.DrawLine(tc, 0, R * 0.46f, 0, R * 0.32f + tl); }
                        }
                    }
                    using (Pen mp = new Pen(line, Ui.Pf(1.2f)))
                    {
                        mp.StartCap = mp.EndCap = LineCap.Round;
                        G.DrawLine(mp, 0, R * 0.2f, 0, R * 0.34f);
                        G.DrawArc(mp, -R * 0.26f, R * 0.18f, R * 0.26f, R * 0.24f, 10, 150);
                        G.DrawArc(mp, 0, R * 0.18f, R * 0.26f, R * 0.24f, 20, 150);
                    }
                    using (SolidBrush nb = new SolidBrush(Color.FromArgb(34, 24, 22))) G.FillEllipse(nb, -R * 0.17f, R * 0.06f, R * 0.34f, R * 0.22f);
                    using (SolidBrush nh = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) G.FillEllipse(nh, -R * 0.08f, R * 0.08f, R * 0.11f, R * 0.06f);
                    for (int s = -1; s <= 1; s += 2) PetEyes(s * R * 0.38f, -R * 0.22f, R * 0.14f, blink, happy, 0f, Color.FromArgb(30, 20, 18));
                    for (int s = -1; s <= 1; s += 2) PetBlush(s * R * 0.64f, R * 0.12f, R * 0.15f);
                }
                G.Restore(gs);
                if (t > 3.0f && t < 3.9f)
                {
                    float u = (t - 3.0f) / 0.9f;
                    PetHeart(hx - R * 0.2f + (float)Math.Sin(u * 9) * R * 0.12f, hy - R * 1.2f - u * R * 0.9f, R * 0.22f * (0.6f + 0.4f * Clamp01(u * 4)), (int)(255 * (1 - u * u)));
                }
            }
            // island digambar di atas: kepala dan ekor tampak menyembul dari belakangnya
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
        }

        // ===== Ikan mas: berenang keluar dari notch, berputar-putar, meniup gelembung yang pecah di notch, lalu pulang =====
        private static readonly float[] FishT = { 0f, 0.45f, 1.0f, 1.3f, 1.6f, 2.3f, 2.6f, 2.9f, 3.2f, 3.95f, 4.4f };
        private static readonly float[] FishX = { 0f, 16f, 82f, 100f, 74f, -66f, -92f, -62f, -8f, 4f, 22f };
        private static readonly float[] FishY = { -16f, 22f, 36f, 50f, 62f, 54f, 42f, 32f, 40f, 40f, -18f };

        private static PointF PetSpline(float[] kt, float[] kx, float[] ky, float t)
        {
            int n = kt.Length;
            if (t <= kt[0]) return new PointF(kx[0], ky[0]);
            if (t >= kt[n - 1]) return new PointF(kx[n - 1], ky[n - 1]);
            int i = 0; while (i < n - 2 && t > kt[i + 1]) i++;
            float u = (t - kt[i]) / (kt[i + 1] - kt[i]);
            int a = Math.Max(0, i - 1), d = Math.Min(n - 1, i + 2);
            float u2 = u * u, u3 = u2 * u;
            Func<float, float, float, float, float> cr = delegate (float p0, float p1, float p2, float p3)
            { return 0.5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3); };
            return new PointF(cr(kx[a], kx[i], kx[i + 1], kx[d]), cr(ky[a], ky[i], ky[i + 1], ky[d]));
        }

        private void DrawFish(float x0, float w, float h)
        {
            float t = _idleT, cx = x0 + w / 2f, B = 0.5f + h, s = h * 0.50f, k = Ui.Pf(1);
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
            bool hover = t > 3.2f && t < 3.95f;
            PointF p = PetSpline(FishT, FishX, FishY, t);
            float fx = cx + p.X * k, fy = B + p.Y * k;
            if (hover) fy += (float)Math.Sin((t - 3.2f) * 7) * Ui.Pf(1.6f);
            PointF p2 = PetSpline(FishT, FishX, FishY, t + 0.03f), p1 = PetSpline(FishT, FishX, FishY, t - 0.03f);
            float vx = p2.X - p1.X, vy = p2.Y - p1.Y, sp = (float)Math.Sqrt(vx * vx + vy * vy);
            float sx = hover || sp < 0.05f ? 1f : Math.Max(-1f, Math.Min(1f, vx / (0.3f * sp)));
            if (Math.Abs(sx) < 0.12f) sx = sx < 0 ? -0.12f : 0.12f;
            float rot = hover ? (float)Math.Sin((t - 3.2f) * 5) * 5f : (float)(Math.Atan2(vy, Math.Abs(vx)) * 180 / Math.PI);
            rot = Math.Max(-62f, Math.Min(62f, rot)) * (sx < 0 ? -1 : 1);
            float swim = hover ? 0.45f : 1f;
            bool blow = t > 3.25f && t < 3.95f;

            Region old = PetClipBelow(x0, w, B, Ui.Pf(160));
            PetBump(cx, B, s * 1.3f, PetSettle(t, 0f, 0.5f, s * 0.6f, 9f) * Clamp01(t / 0.08f));
            // gelembung naik lalu pecah di tepi notch
            for (int i = 0; i < 3; i++)
            {
                float t0 = 3.32f + i * 0.2f, bt = t - t0;
                if (bt < 0) continue;
                PointF sp0 = PetSpline(FishT, FishX, FishY, t0);
                PointF mp = new PointF(cx + sp0.X * k + 0.66f * s, B + sp0.Y * k + 0.1f * s);
                float br = Ui.Pf(2.2f) + Ui.Pf(1.6f) * Clamp01(bt / 0.35f) + i * Ui.Pf(0.5f);
                float by = mp.Y - bt * Ui.Pf(52) - bt * bt * Ui.Pf(18), bx = mp.X + (float)Math.Sin(bt * 9 + i) * Ui.Pf(2.5f) + i * Ui.Pf(2f);
                if (by - br > B + 0.5f)
                {
                    using (SolidBrush bf = new SolidBrush(Color.FromArgb(46, 170, 220, 255))) G.FillEllipse(bf, bx - br, by - br, br * 2, br * 2);
                    using (Pen bp = new Pen(Color.FromArgb(215, 200, 235, 255), Ui.Pf(1.1f))) G.DrawEllipse(bp, bx - br, by - br, br * 2, br * 2);
                    using (SolidBrush hl = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) G.FillEllipse(hl, bx - br * 0.55f, by - br * 0.6f, br * 0.55f, br * 0.45f);
                }
                else
                {
                    // pecah: percikan kecil
                    float pt = (B + 0.5f - (by - br)) / Ui.Pf(52) / 0.18f;
                    if (pt < 1f)
                        using (Pen pp = new Pen(Color.FromArgb((int)(220 * (1 - pt)), 200, 235, 255), Ui.Pf(1.1f)))
                        {
                            pp.StartCap = pp.EndCap = LineCap.Round;
                            float py = B + Ui.Pf(2f);
                            for (int j = 0; j < 5; j++)
                            {
                                double a = Math.PI * (0.15 + 0.175 * j);
                                float r0 = br * (0.6f + pt * 0.8f), r1 = br * (1.1f + pt * 1.3f);
                                G.DrawLine(pp, bx + (float)Math.Cos(a) * r0, py + (float)Math.Sin(a) * r0, bx + (float)Math.Cos(a) * r1, py + (float)Math.Sin(a) * r1);
                            }
                        }
                }
            }
            GraphicsState gs = G.Save();
            G.TranslateTransform(fx, fy); G.RotateTransform(rot); G.ScaleTransform(sx, 1f);
            FishShape(s, t, swim, blow);
            G.Restore(gs);
            PetBump(cx + 22f * k, B, s * 1.3f, PetSettle(t, 4.3f, 0.5f, s * 0.55f, 11f));
            G.Clip = old; old.Dispose();
        }

        // ikan menghadap +x di titik asal
        private void FishShape(float s, float t, float swim, bool blow)
        {
            Color line = Color.FromArgb(170, 70, 20);
            float wag = (float)Math.Sin(t * (swim > 0.6f ? 15 : 8)) * 0.22f * s;
            using (Pen ol = new Pen(line, Ui.Pf(1.3f)))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(235, 255, 160, 70)))
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(255, 124, 40)))
            {
                ol.LineJoin = LineJoin.Round;
                // ekor berumbai
                using (GraphicsPath tp = new GraphicsPath())
                {
                    tp.AddClosedCurve(new PointF[] {
                        new PointF(-0.5f * s, 0), new PointF(-1.05f * s, -0.62f * s + wag), new PointF(-1.28f * s, -0.42f * s + wag * 1.3f),
                        new PointF(-1.0f * s, wag * 0.6f), new PointF(-1.28f * s, 0.42f * s + wag * 1.3f), new PointF(-1.05f * s, 0.62f * s + wag) }, 0.4f);
                    G.DrawPath(ol, tp); G.FillPath(tb, tp);
                }
                using (Pen fin = new Pen(Color.FromArgb(200, 255, 200, 140), Ui.Pf(0.8f)))
                { G.DrawLine(fin, -0.62f * s, 0, -1.08f * s, -0.38f * s + wag); G.DrawLine(fin, -0.62f * s, 0, -1.08f * s, 0.38f * s + wag); }
                // sirip punggung
                using (GraphicsPath dp = new GraphicsPath())
                {
                    dp.AddClosedCurve(new PointF[] { new PointF(-0.35f * s, -0.30f * s), new PointF(-0.12f * s, -0.66f * s), new PointF(0.18f * s, -0.36f * s) }, 0.5f);
                    G.DrawPath(ol, dp); G.FillPath(tb, dp);
                }
                RectangleF body = new RectangleF(-0.68f * s, -0.46f * s, 1.36f * s, 0.92f * s);
                G.DrawEllipse(ol, body); G.FillEllipse(bb, body);
                using (SolidBrush be = new SolidBrush(Color.FromArgb(255, 196, 120))) G.FillEllipse(be, -0.42f * s, 0.02f * s, 0.86f * s, 0.36f * s);   // perut
                using (SolidBrush hl = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) G.FillEllipse(hl, -0.30f * s, -0.36f * s, 0.46f * s, 0.16f * s);
                // sirip samping mengepak
                float pf = (float)Math.Sin(t * 11) * 0.08f * s;
                using (GraphicsPath sf = new GraphicsPath())
                {
                    sf.AddClosedCurve(new PointF[] { new PointF(-0.02f * s, 0.10f * s), new PointF(-0.30f * s, 0.30f * s + pf), new PointF(-0.06f * s, 0.30f * s + pf) }, 0.5f);
                    G.FillPath(tb, sf);
                }
                // mata besar
                float er = 0.21f * s, ex = 0.30f * s, ey = -0.08f * s;
                using (SolidBrush wb = new SolidBrush(Color.White)) G.FillEllipse(wb, ex - er, ey - er, er * 2, er * 2);
                using (Pen ep = new Pen(line, Ui.Pf(0.8f))) G.DrawEllipse(ep, ex - er, ey - er, er * 2, er * 2);
                using (SolidBrush pb = new SolidBrush(Color.FromArgb(28, 22, 26))) G.FillEllipse(pb, ex - er * 0.35f, ey - er * 0.6f, er * 1.2f, er * 1.2f);
                using (SolidBrush hb = new SolidBrush(Color.White)) G.FillEllipse(hb, ex + er * 0.05f, ey - er * 0.45f, er * 0.45f, er * 0.45f);
                PetBlush(0.22f * s, 0.18f * s, 0.12f * s);
                // mulut: bulat saat meniup gelembung
                if (blow)
                    using (SolidBrush mb = new SolidBrush(Color.FromArgb(200, 60, 70))) G.FillEllipse(mb, 0.58f * s, 0.06f * s, 0.12f * s, 0.12f * s);
                else
                    using (Pen mp = new Pen(line, Ui.Pf(1f))) { mp.StartCap = mp.EndCap = LineCap.Round; G.DrawArc(mp, 0.48f * s, 0.02f * s, 0.16f * s, 0.14f * s, 20, 120); }
            }
        }

        // ===== Pinguin: island membeku, pinguin masuk dari kiri, berjalan megal-megol, meluncur dengan perut, melambai, lalu pergi ke kanan =====
        private void DrawPenguin(float x0, float w, float h)
        {
            float t = _idleT, top = 0.5f, r = Math.Min(h / 2f, Ui.Pf(30)), p = h * 0.42f, floor = top + h - h * 0.12f;
            using (GraphicsPath pill = IdlePill(x0, w, h))
            {
                float ice = t < 0.4f ? EaseIO(t / 0.4f) : t < 4.45f ? 1f : 1f - EaseIO((t - 4.45f) / 0.45f);
                if (ice <= 0.01f) return;
                Region old = G.Clip; G.SetClip(pill, CombineMode.Intersect);
                // lantai es
                using (LinearGradientBrush ib = new LinearGradientBrush(new RectangleF(x0, floor - h * 0.1f, w, top + h - floor + h * 0.1f + 1), Color.FromArgb(0, 150, 210, 255), Color.FromArgb((int)(120 * ice), 170, 225, 255), 90f))
                    G.FillRectangle(ib, x0, floor - h * 0.1f, w, top + h - floor + h * 0.1f + 1);
                using (Pen fl = new Pen(Color.FromArgb((int)(150 * ice), 210, 240, 255), Ui.Pf(1f))) G.DrawLine(fl, x0 + r * 0.5f, floor, x0 + w - r * 0.5f, floor);
                // kilau es
                for (int i = 0; i < 3; i++)
                {
                    float sx = x0 + w * (0.22f + 0.28f * i), tw = (float)Math.Max(0, Math.Sin(t * 3.1 + i * 2.1));
                    PetSparkle(sx, floor - h * 0.32f - i % 2 * h * 0.18f, h * 0.07f * tw * ice, (int)(200 * ice));
                }
                // lintasan pinguin
                float xs = x0 - p * 1.2f, xw = x0 + w * 0.30f, xe = x0 + w - r * 1.05f, xo = x0 + w + p * 1.3f;
                float px, py = floor, ang = 0, wave = 0, waddle = 0, sq = 0; bool slide = false, happy = false;
                float blink = (t > 1.05f && t < 1.15f) ? 0.12f : 1f;
                if (t < 1.45f)
                {
                    float u = Clamp01((t - 0.25f) / 1.2f);
                    px = PetLerp(xs, xw, u); waddle = (float)Math.Sin(u * Math.PI * 6); ang = waddle * 9f; py = floor - Math.Abs(waddle) * p * 0.08f;
                }
                else if (t < 1.75f)
                {
                    float u = (t - 1.45f) / 0.30f;   // jongkok lalu menjatuhkan diri
                    px = xw + u * p * 0.5f; sq = Win(u, 0f, 0.5f) * 0.18f; ang = 90f * EaseIO(Clamp01((u - 0.25f) / 0.75f));
                    py = floor - (float)Math.Sin(u * Math.PI) * p * 0.35f;
                }
                else if (t < 3.05f)
                {
                    float u = (t - 1.75f) / 1.30f, e = 1f - (1f - u) * (1f - u);
                    px = PetLerp(xw + p * 0.5f, xe - p * 0.3f, e); ang = 90f; slide = true;
                    // jejak luncuran
                    using (Pen tr = new Pen(Color.FromArgb((int)(150 * (1 - u)), 220, 245, 255), Ui.Pf(1.2f)))
                    { tr.StartCap = tr.EndCap = LineCap.Round; G.DrawLine(tr, PetLerp(xw, px, 0.25f), floor - 1f, px - p * 1.0f, floor - 1f); }
                    if (u < 0.75f)
                        for (int i = 0; i < 3; i++)
                        {
                            float q = (u * 7 + i * 0.33f) % 1f;
                            PetSparkle(px - p * (1.1f + q * 1.6f), floor - p * (0.25f + 0.5f * q * (i % 2 == 0 ? 1 : 0.6f)), p * 0.16f * (1 - q), (int)(230 * (1 - q)));
                        }
                }
                else if (t < 3.4f)
                {
                    float u = (t - 3.05f) / 0.35f;   // berdiri lagi
                    px = xe - p * 0.3f + u * p * 0.3f; ang = 90f * (1f - EaseBack(u)); py = floor - (float)Math.Sin(u * Math.PI) * p * 0.3f;
                }
                else if (t < 3.95f)
                {
                    float u = (t - 3.4f) / 0.55f;
                    px = xe; wave = (float)Math.Sin(u * Math.PI * 4); happy = true; ang = (float)Math.Sin(u * Math.PI * 2) * 4f;
                }
                else
                {
                    float u = Clamp01((t - 3.95f) / 0.6f);
                    px = PetLerp(xe, xo, u * u); float hop = (float)Math.Abs(Math.Sin(u * Math.PI * 3)); py = floor - hop * p * 0.3f; ang = 10f * hop;
                }
                GraphicsState gs = G.Save();
                // putar di sekitar pusat badan; saat berbaring (90 derajat) perutnya menempel lantai
                float lie = Clamp01(Math.Abs(ang) / 90f);
                G.TranslateTransform(px, py - p * 0.84f * (1 - lie) - p * 0.62f * lie); G.RotateTransform(ang); G.TranslateTransform(0, p * 0.84f);
                G.ScaleTransform(1f + sq, 1f - sq);
                PenguinShape(p, wave, slide, happy, blink);
                G.Restore(gs);
                G.Clip = old; old.Dispose();
            }
        }

        private void PetSparkle(float x, float y, float r, int alpha)
        {
            if (r < 0.4f || alpha <= 4) return;
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(alpha, 235, 248, 255)))
                G.FillPolygon(sb, new PointF[] { new PointF(x, y - r), new PointF(x + r * 0.25f, y - r * 0.25f), new PointF(x + r, y), new PointF(x + r * 0.25f, y + r * 0.25f), new PointF(x, y + r), new PointF(x - r * 0.25f, y + r * 0.25f), new PointF(x - r, y), new PointF(x - r * 0.25f, y - r * 0.25f) });
        }

        // pinguin tegak, titik asal di telapak kaki (tengah bawah)
        private void PenguinShape(float p, float wave, bool slide, bool happy, float blink)
        {
            using (Pen rim = new Pen(Color.FromArgb(150, 220, 235, 255), Ui.Pf(1.2f)))
            using (SolidBrush dark = new SolidBrush(Color.FromArgb(74, 96, 140)))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(250, 250, 252)))
            using (SolidBrush org = new SolidBrush(Color.FromArgb(255, 160, 40)))
            {
                rim.LineJoin = LineJoin.Round;
                // kaki
                for (int s = -1; s <= 1; s += 2) G.FillEllipse(org, s * p * 0.28f - p * 0.2f, -p * 0.12f, p * 0.4f, p * 0.2f);
                RectangleF body = new RectangleF(-p * 0.62f, -p * 1.62f, p * 1.24f, p * 1.56f);
                G.DrawEllipse(rim, body); G.FillEllipse(dark, body);
                // sirip
                for (int s = -1; s <= 1; s += 2)
                {
                    float a = s > 0 && wave != 0 ? 70f + wave * 45f : 24f;
                    GraphicsState fs = G.Save();
                    G.TranslateTransform(s * p * 0.50f, -p * 1.08f); G.RotateTransform(-s * (slide ? 8f : a));
                    RectangleF fr = new RectangleF(-p * 0.13f, 0, p * 0.26f, p * 0.62f);
                    G.DrawEllipse(rim, fr); G.FillEllipse(dark, fr);
                    G.Restore(fs);
                }
                G.FillEllipse(white, -p * 0.44f, -p * 1.12f, p * 0.88f, p * 1.0f);   // perut
                // wajah: topeng putih berbentuk hati
                G.FillEllipse(white, -p * 0.40f, -p * 1.42f, p * 0.46f, p * 0.44f);
                G.FillEllipse(white, -p * 0.06f, -p * 1.42f, p * 0.46f, p * 0.44f);
                for (int s = -1; s <= 1; s += 2) PetEyes(s * p * 0.17f, -p * 1.22f, p * 0.075f, blink, happy, 0f, Color.FromArgb(28, 30, 40));
                for (int s = -1; s <= 1; s += 2) PetBlush(s * p * 0.30f, -p * 1.09f, p * 0.08f);
                G.FillPolygon(org, new PointF[] { new PointF(-p * 0.1f, -p * 1.14f), new PointF(p * 0.1f, -p * 1.14f), new PointF(0, -p * 1.02f) });
            }
        }

        // ===== Kelinci: melompat turun dari notch, berjingkat tiga kali ke kiri, mengendus, lalu melompat pulang ke notch =====
        private void DrawBunny(float x0, float w, float h)
        {
            float t = _idleT, cx = x0 + w / 2f, B = 0.5f + h, b = h * 0.50f, F = B + Ui.Pf(48);
            using (GraphicsPath pill = IdlePill(x0, w, h)) { }
            float xa = cx + w * 0.24f, xb = cx - w * 0.26f;
            float bx, by, sxv = 1f, syv = 1f, ear = 0f, tilt = 0f; bool show = true, happy = false;
            float blink = (t > 2.6f && t < 2.72f) ? 0.12f : 1f;
            float nose = 0f;
            if (t < 0.5f)
            {
                float u = t / 0.5f;
                bx = xa; by = PetLerp(B - b * 0.4f, F, u * u); ear = -0.7f; syv = 1.08f; sxv = 0.94f;
            }
            else if (t < 0.68f)
            {
                float u = (t - 0.5f) / 0.18f, q = (float)Math.Sin(u * Math.PI);
                bx = xa; by = F; sxv = 1f + 0.2f * q; syv = 1f - 0.2f * q; ear = 0.5f * q;
            }
            else if (t < 0.95f)
            {
                bx = xa; by = F; nose = (float)Math.Sin(t * 40) * 0.5f + 0.5f; ear = (float)Math.Sin((t - 0.68f) * 20) * 0.12f;
            }
            else if (t < 2.35f)
            {
                float hu = (t - 0.95f) / 1.4f * 3f; int hi = Math.Min(2, (int)hu); float u = hu - hi;
                bx = PetLerp(xa, xb, (hi + EaseIO(u)) / 3f);
                float air = (float)Math.Sin(u * Math.PI);
                by = F - air * Ui.Pf(16);
                float land = u < 0.12f ? 1f - u / 0.12f : u > 0.88f ? (u - 0.88f) / 0.12f : 0f;
                sxv = 1f - 0.08f * air + 0.14f * land; syv = 1f + 0.10f * air - 0.14f * land; ear = -0.55f * air + 0.25f * land; tilt = (u - 0.5f) * 14f;
                if (hi > 0 && u < 0.3f) PetDust(PetLerp(xa, xb, hi / 3f), F, b, u / 0.3f);
            }
            else if (t < 3.3f)
            {
                float u = (t - 2.35f) / 0.95f;
                bx = xb; by = F; nose = (float)Math.Sin(t * 40) * 0.5f + 0.5f;
                if (u < 0.15f) PetDust(xb, F, b, u / 0.15f);
                ear = u < 0.1f ? 0.3f * (1 - u / 0.1f) : 0f;
                happy = u > 0.45f && u < 0.9f;
                if (u > 0.5f) PetHeart(xb - b * 1.25f, F - b * 2.3f - (u - 0.5f) * b * 1.6f, b * 0.28f * Clamp01((u - 0.5f) * 6f), (int)(255 * Clamp01((1f - u) * 4f)));
            }
            else if (t < 3.55f)
            {
                float u = (t - 3.3f) / 0.25f;   // ancang-ancang
                bx = xb; by = F; sxv = 1f + 0.15f * EaseIO(u); syv = 1f - 0.18f * EaseIO(u); ear = 0.3f * u;
            }
            else if (t < 4.05f)
            {
                float u = (t - 3.55f) / 0.5f;
                bx = xb; by = PetLerp(F, B - b * 1.9f, 1f - (1f - u) * (1f - u)); sxv = 0.9f; syv = 1.14f; ear = -0.9f;
                if (u < 0.35f) PetDust(xb, F, b, u / 0.35f);
            }
            else { bx = xb; by = -1000; show = false; }

            Region old = PetClipBelow(x0, w, B, Ui.Pf(80));
            if (show)
            {
                GraphicsState gs = G.Save();
                G.TranslateTransform(bx, by); G.RotateTransform(tilt); G.ScaleTransform(sxv, syv);
                BunnyShape(b, ear, nose, blink, happy);
                G.Restore(gs);
            }
            PetBump(xb, B, b * 1.4f, PetSettle(t, 3.85f, 0.75f, b * 0.8f, 12f));
            PetBump(xa, B, b * 1.2f, PetSettle(t, 0f, 0.45f, b * 0.6f, 10f) * Clamp01(t / 0.06f));
            G.Clip = old; old.Dispose();
        }

        private void PetDust(float x, float y, float b, float u)
        {
            if (u >= 1f) return;
            using (SolidBrush db = new SolidBrush(Color.FromArgb((int)(150 * (1 - u)), 235, 235, 240)))
                for (int s = -1; s <= 1; s += 2)
                {
                    float r = b * (0.12f + 0.16f * u), dx = x + s * b * (0.7f + 0.8f * u), dy = y - b * 0.1f - u * b * 0.25f;
                    G.FillEllipse(db, dx - r, dy - r, r * 2, r * 2);
                }
        }

        // kelinci menghadap kiri, titik asal di kaki (tengah bawah); ear: -1 terayun ke belakang .. +1 terkulai ke depan
        private void BunnyShape(float b, float ear, float nose, float blink, bool happy)
        {
            Color fur = Color.FromArgb(252, 250, 247), line = Color.FromArgb(150, 138, 150);
            using (Pen ol = new Pen(line, Ui.Pf(1.4f))) using (SolidBrush fb = new SolidBrush(fur))
            using (SolidBrush pk = new SolidBrush(Color.FromArgb(255, 170, 190)))
            {
                ol.LineJoin = LineJoin.Round;
                RectangleF body = new RectangleF(-b * 0.55f, -b * 1.15f, b * 1.4f, b * 1.15f);
                RectangleF head = new RectangleF(-b * 1.0f, -b * 1.9f, b * 1.18f, b * 1.04f);
                RectangleF tail = new RectangleF(b * 0.68f, -b * 0.95f, b * 0.42f, b * 0.42f);
                RectangleF foot = new RectangleF(-b * 0.25f, -b * 0.22f, b * 0.62f, b * 0.24f);
                // telinga
                RectangleF[] ears = new RectangleF[2]; float[] ea = new float[2];
                for (int i = 0; i < 2; i++)
                {
                    ea[i] = 8f + i * 18f - ear * 55f;
                    ears[i] = new RectangleF(-b * 0.17f, -b * 1.3f, b * 0.34f, b * 1.3f);
                }
                for (int i = 1; i >= 0; i--)
                {
                    GraphicsState es = G.Save();
                    G.TranslateTransform(-b * 0.36f + i * b * 0.2f, -b * 1.62f); G.RotateTransform(ea[i]);
                    G.DrawEllipse(ol, ears[i]); G.FillEllipse(fb, ears[i]);
                    RectangleF inn = ears[i]; inn.Inflate(-b * 0.07f, -b * 0.16f); inn.Offset(0, -b * 0.04f);
                    G.FillEllipse(pk, inn);
                    G.Restore(es);
                }
                G.DrawEllipse(ol, tail); G.DrawEllipse(ol, body); G.DrawEllipse(ol, head); G.DrawEllipse(ol, foot);
                G.FillEllipse(fb, tail); G.FillEllipse(fb, body); G.FillEllipse(fb, head); G.FillEllipse(fb, foot);
                using (Pen fp = new Pen(Color.FromArgb(140, line), Ui.Pf(1f))) G.DrawArc(fp, -b * 0.15f, -b * 0.75f, b * 0.6f, b * 0.6f, 180, 120);   // paha
                PetEyes(-b * 0.55f, -b * 1.42f, b * 0.12f, blink, happy, 0f, Color.FromArgb(40, 30, 40));
                PetBlush(-b * 0.58f, -b * 1.18f, b * 0.13f);
                float nw = b * (0.14f + 0.03f * nose);
                G.FillEllipse(pk, -b * 0.98f, -b * 1.36f - nose * b * 0.02f, nw, b * 0.11f);
                using (Pen wp = new Pen(Color.FromArgb(150, line), Ui.Pf(0.8f)))
                { G.DrawLine(wp, -b * 0.9f, -b * 1.26f, -b * 1.25f, -b * 1.33f - nose * b * 0.03f); G.DrawLine(wp, -b * 0.9f, -b * 1.24f, -b * 1.22f, -b * 1.16f + nose * b * 0.03f); }
            }
        }
    }
}
