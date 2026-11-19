using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  WIDGET KARTU: Kalender & Cuaca bergaya kartu kaca (seperti widget macOS).
    //  Tampil di desktop (DesktopWidgets), Dock (DockWidgetCards), dan layar kunci (LockWidgets). Semua bawaan mati.
    // ============================================================
    internal static class CardWidgets
    {
        public const float CardW = 344f, CardH = 160f, Gap = 16f;   // ukuran desain kartu (dikali skala)
        private static readonly Color Red = Color.FromArgb(255, 69, 58);

        public static bool LockOn { get { return Settings.Get("LockWidgets", false); } }

        private static Font Fnt(float px, bool bold)
        {
            using (Font f = Ui.F(10f, bold ? FontStyle.Bold : FontStyle.Regular)) return new Font(f.FontFamily, Math.Max(1f, px), f.Style, GraphicsUnit.Pixel);
        }

        // Teks satu baris; maxW > 0 = dipotong dengan elipsis. align: 0 kiri, 1 tengah.
        private static void Txt(Graphics g, string s, float px, bool bold, Color c, float x, float y, float maxW, int align)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (Font f = Fnt(px, bold)) using (SolidBrush b = new SolidBrush(c)) using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.Alignment = align == 1 ? StringAlignment.Center : StringAlignment.Near;
                float w = maxW > 0 ? maxW : 4000f;
                g.DrawString(s, f, b, new RectangleF(align == 1 ? x - w / 2f : x, y, w, px * 1.5f), sf);
            }
        }

        // Dasar kartu kaca: bayangan lembut (opsional), isi gradasi tembus pandang, kilau di atas, tepi terang tipis.
        public static void Base(Graphics g, RectangleF r, float rad, float s, Color top, Color bottom, bool shadow)
        {
            if (shadow)
                for (int i = 7; i >= 1; i--)
                {
                    float k = i * 2.4f * s;
                    RectangleF sr = r; sr.Inflate(k * 0.8f, k * 0.6f); sr.Offset(0, 6f * s);
                    using (GraphicsPath sp = Glass.Round(sr, rad + k * 0.7f))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(10, 0, 0, 0))) g.FillPath(sb, sp);
                }
            RectangleF gr = new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2);
            using (GraphicsPath p = Glass.Round(r, rad))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(gr, top, bottom, 90f)) g.FillPath(b, p);
                Region old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                RectangleF shr = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.45f);
                using (LinearGradientBrush sb = new LinearGradientBrush(new RectangleF(shr.X, shr.Y - 1, shr.Width, shr.Height + 2), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillRectangle(sb, shr);
                g.Clip = old; old.Dispose();
                using (LinearGradientBrush pb = new LinearGradientBrush(gr, Color.FromArgb(120, 255, 255, 255), Color.FromArgb(26, 255, 255, 255), 90f))
                using (Pen pen = new Pen(pb, Math.Max(1f, 1.2f * s))) g.DrawPath(pen, p);
            }
        }

        public static bool Night { get { int h = DateTime.Now.Hour; return h >= 18 || h < 6; } }
        private static void SkyColors(out Color a, out Color b)
        {
            if (Night) { a = Color.FromArgb(228, 40, 52, 104); b = Color.FromArgb(238, 12, 16, 42); }
            else { a = Color.FromArgb(228, 70, 146, 232); b = Color.FromArgb(238, 28, 88, 178); }
        }

        private static string TempText(WeatherInfo wi)
        {
            if (wi == null || string.IsNullOrEmpty(wi.Temp)) return "--";
            string t = wi.Temp.Trim();
            if (t.EndsWith("C") || t.EndsWith("F")) t = t.Substring(0, t.Length - 1);
            return t.Trim();
        }

        // Nama kota; wttr.in kadang hanya memberi koordinat (mis. "-6.175000"), tampilkan "Cuaca" saja.
        public static string PlaceText(WeatherInfo wi)
        {
            string p = wi == null ? "" : (wi.Place ?? "").Trim();
            double dummy;
            if (p.Length == 0 || double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out dummy)) return L.T("Cuaca");
            return p;
        }

        // Pengingat berikutnya (hari ini / besok) untuk baris bawah kartu kalender.
        private static string NextReminder()
        {
            try
            {
                DateTime now = DateTime.Now;
                foreach (Reminder r in Reminders.All())
                {
                    if (r.At < now) continue;
                    if (r.At.Date > now.Date.AddDays(1)) break;
                    string txt = (r.Text ?? "").Replace('\u001F', ' ');
                    return (r.At.Date == now.Date ? "" : L.T("Besok") + " ") + r.At.ToString("HH:mm") + "  " + txt;
                }
            }
            catch { }
            return L.T("Tidak ada pengingat");
        }

        // ---------- kartu sedang (desktop & layar kunci) ----------
        public static void DrawCalendar(Graphics g, RectangleF r, float s)
        {
            Base(g, r, 24f * s, s, Color.FromArgb(214, 38, 40, 52), Color.FromArgb(226, 16, 17, 24), true);
            CultureInfo ci = L.Culture; DateTime today = DateTime.Today;
            float x = r.X + 18 * s, y = r.Y;
            Txt(g, today.ToString("dddd", ci).ToUpper(ci), 13 * s, true, Red, x, y + 16 * s, 124 * s, 0);
            Txt(g, today.Day.ToString(), 54 * s, true, Color.White, x - 3 * s, y + 31 * s, 0, 0);
            Txt(g, today.ToString("MMMM yyyy", ci), 14 * s, false, Color.FromArgb(205, 255, 255, 255), x, y + 99 * s, 126 * s, 0);
            Txt(g, NextReminder(), 11.5f * s, false, Color.FromArgb(150, 255, 255, 255), x, y + 125 * s, 126 * s, 0);

            // kalender bulan ini (Senin di kiri, sama seperti Pusat Notifikasi)
            float gx = r.X + 152 * s, gw = r.Right - 14 * s - gx, cw = gw / 7f, top = y + 17 * s;
            string[] days = { L.T("Sen"), L.T("Sel"), L.T("Rab"), L.T("Kam"), L.T("Jum"), L.T("Sab"), L.T("Min") };
            for (int i = 0; i < 7; i++)
                Txt(g, days[i].ToUpper(ci), 9f * s, true, i >= 5 ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255), gx + i * cw + cw / 2f, top, cw, 1);
            DateTime first = new DateTime(today.Year, today.Month, 1);
            int lead = ((int)first.DayOfWeek + 6) % 7, dim = DateTime.DaysInMonth(today.Year, today.Month);
            int rows = (lead + dim + 6) / 7;
            float gy = top + 19 * s, rh = (r.Bottom - 11 * s - gy) / rows;
            for (int d = 1; d <= dim; d++)
            {
                int idx = lead + d - 1, col = idx % 7, row = idx / 7;
                float cx = gx + col * cw + cw / 2f, cy = gy + row * rh + rh / 2f;
                bool isToday = d == today.Day;
                if (isToday)
                {
                    float dd = Math.Min(rh, cw) + 1 * s;
                    using (SolidBrush rb = new SolidBrush(Red)) g.FillEllipse(rb, cx - dd / 2f, cy - dd / 2f, dd, dd);
                }
                float fp = 11 * s;
                Color c = isToday ? Color.White : col >= 5 ? Color.FromArgb(130, 255, 255, 255) : Color.FromArgb(225, 255, 255, 255);
                Txt(g, d.ToString(), fp, isToday, c, cx, cy - fp * 0.62f, cw, 1);
            }
        }

        public static void DrawWeather(Graphics g, RectangleF r, float s)
        {
            Color a, b; SkyColors(out a, out b);
            Base(g, r, 24f * s, s, a, b, true);
            WeatherInfo wi = Weather.Current;
            float x = r.X + 18 * s, y = r.Y;
            if (wi == null)
            {
                Txt(g, L.T("Cuaca"), 14 * s, true, Color.White, x, y + 16 * s, 0, 0);
                Txt(g, L.T("Memuat cuaca..."), 13 * s, false, Color.FromArgb(200, 255, 255, 255), x, y + 44 * s, 0, 0);
                Weather.DrawIcon(g, "", new RectangleF(r.Right - 96 * s, y + 20 * s, 78 * s, 78 * s), 150);
                return;
            }
            Txt(g, PlaceText(wi), 14 * s, true, Color.White, x, y + 16 * s, r.Width - 130 * s, 0);
            Txt(g, TempText(wi), 54 * s, true, Color.White, x - 2 * s, y + 31 * s, 0, 0);
            Weather.DrawIcon(g, wi.Cond ?? "", new RectangleF(r.Right - 96 * s, y + 18 * s, 78 * s, 78 * s), 255);
            Txt(g, wi.Cond, 13.5f * s, true, Color.White, x, y + 101 * s, r.Width - 36 * s, 0);
            List<string> det = new List<string>();
            if (!string.IsNullOrEmpty(wi.Humidity)) det.Add(L.T("Kelembapan") + " " + wi.Humidity);
            if (!string.IsNullOrEmpty(wi.Wind)) det.Add(L.T("Angin") + " " + wi.Wind);
            Txt(g, string.Join("  ·  ", det.ToArray()), 11.5f * s, false, Color.FromArgb(200, 255, 255, 255), x, y + 125 * s, r.Width - 36 * s, 0);
        }

        // Dua kartu berdampingan di tengah layar (gambar layar kunci & tirai kunci animasi).
        public static void DrawLockRow(Graphics g, float w, float h, float top)
        {
            float s = h / 1080f * 1.1f;
            float cw = CardW * s, gap = 22 * s, x = (w - (cw * 2 + gap)) / 2f;
            DrawCalendar(g, new RectangleF(x, top, cw, CardH * s), s);
            DrawWeather(g, new RectangleF(x + cw + gap, top, cw, CardH * s), s);
        }

        // ---------- kartu kecil seukuran ikon Dock (128x128) ----------
        public static Bitmap DockTile(string kind)
        {
            Bitmap o = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAlias;
                RectangleF r = new RectangleF(8, 8, 112, 112);
                if (kind == "calendar")
                {
                    Base(g, r, 28f, 1.6f, Color.FromArgb(214, 38, 40, 52), Color.FromArgb(230, 16, 17, 24), false);
                    DateTime t = DateTime.Today;
                    Txt(g, t.ToString("ddd", L.Culture).ToUpper(L.Culture).TrimEnd('.'), 19, true, Red, 64, 19, 108, 1);
                    Txt(g, t.Day.ToString(), 56, true, Color.White, 64, 39, 112, 1);
                }
                else
                {
                    Color a, b; SkyColors(out a, out b);
                    Base(g, r, 28f, 1.6f, a, b, false);
                    WeatherInfo wi = Weather.Current;
                    Weather.DrawIcon(g, wi == null ? "" : (wi.Cond ?? ""), new RectangleF(36, 16, 56, 56), wi == null ? 150 : 255);
                    Txt(g, TempText(wi), 27, true, Color.White, 64, 74, 112, 1);
                }
            }
            return o;
        }

        // uji: LM_CARDS=1 pada --snapshot
        public static void SnapshotAll(string dir)
        {
            Weather.Refresh(true);
            for (int i = 0; i < 40 && Weather.Current == null; i++) System.Threading.Thread.Sleep(250);
            using (DesktopWidgetsForm f = new DesktopWidgetsForm()) f.Snapshot(System.IO.Path.Combine(dir, "cards-desktop.png"));
            using (Bitmap b = DockTile("calendar")) b.Save(System.IO.Path.Combine(dir, "cards-dock-calendar.png"));
            using (Bitmap b = DockTile("weather")) b.Save(System.IO.Path.Combine(dir, "cards-dock-weather.png"));
            using (Bitmap b = new Bitmap(1920, 1080))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, 1920, 1080), Color.FromArgb(40, 52, 120), Color.FromArgb(120, 40, 110), 60f)) g.FillRectangle(bg, 0, 0, 1920, 1080);
                DrawLockRow(g, 1920, 1080, 1080 * 0.33f);
                b.Save(System.IO.Path.Combine(dir, "cards-lock.png"), ImageFormat.Png);
            }
        }
    }

    // Widget kartu di desktop: jendela tembus pandang di lapisan desktop (di atas wallpaper, di bawah semua jendela), pojok kanan atas.
    internal sealed class DesktopWidgetsForm : OverlayForm
    {
        private static DesktopWidgetsForm _f;
        public static bool Enabled { get { return Settings.Get("DesktopWidgets", false); } }
        public static void Start()
        {
            if (_f != null && !_f.IsDisposed) return;
            try { Weather.Refresh(false); _f = new DesktopWidgetsForm(); _f.Show(); }
            catch (Exception ex) { Log.Write("widget desktop: " + ex.Message); }
        }
        public static void Stop()
        {
            DesktopWidgetsForm f = _f; _f = null;
            if (f != null && !f.IsDisposed) try { f.Close(); f.Dispose(); } catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string c, string t);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetLong32(IntPtr h, int i, int v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetLong32(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [StructLayout(LayoutKind.Sequential)] private struct WINDOWPOS { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }

        private const float Pad = 26f;
        private readonly RectangleF[] _cards = new RectangleF[2];
        private string _sig = "";
        private int _tick;

        protected override bool Topmost { get { return false; } }

        // Pemilik = jendela desktop sejak dibuat (WinForms memakai jendela parkir sebagai pemilik bila ShowInTaskbar mati).
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                IntPtr pm = FindWindow("Progman", null);
                if (pm != IntPtr.Zero) cp.Parent = pm;
                return cp;
            }
        }

        public DesktopWidgetsForm()
        {
            Configure((int)Math.Ceiling((CardWidgets.CardW + Pad * 2) * Ui.S), (int)Math.Ceiling((CardWidgets.CardH * 2 + CardWidgets.Gap + Pad * 2) * Ui.S));
        }

        public override void Reposition()
        {
            Rectangle sb = Screen.PrimaryScreen.Bounds;
            Location = new Point(sb.Right - Bw + Ui.P(Pad - 20), sb.Top + Ui.P(28 + 18 - Pad));
            Dirty = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            KeepOnDesktop();
        }

        // Dimiliki jendela desktop (Progman): selalu tepat di atas wallpaper, di bawah semua jendela aplikasi.
        private void KeepOnDesktop()
        {
            IntPtr pm = FindWindow("Progman", null);
            if (pm == IntPtr.Zero || !IsHandleCreated) return;
            IntPtr cur = IntPtr.Size == 8 ? GetLongPtr64(Handle, -8) : new IntPtr(GetLong32(Handle, -8));
            if (cur == pm) return;
            if (IntPtr.Size == 8) SetLongPtr64(Handle, -8, pm); else SetLong32(Handle, -8, pm.ToInt32());
            SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x13);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0046 && m.LParam != IntPtr.Zero)   // WM_WINDOWPOSCHANGING: jangan pernah naik ke atas jendela lain
            {
                WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                if ((wp.flags & 0x0004) == 0) { wp.after = new IntPtr(1); Marshal.StructureToPtr(wp, m.LParam, false); }
            }
            base.WndProc(ref m);
        }

        protected override bool OnFrame()
        {
            if (++_tick % 30 != 0) return false;
            if (_tick % 300 == 0) KeepOnDesktop();             // tiap ~5 dtk: pemilik masih desktop (Explorer dimuat ulang -> Progman baru)
            if (_tick % 18000 == 0) Weather.Refresh(false);    // tiap ~5 mnt (Refresh sendiri membatasi 30 mnt)
            string sig = DateTime.Now.ToString("yyyyMMddHHmm") + "|" + Weather.Version + "|" + CardWidgets.Night;
            if (sig == _sig) return false;
            _sig = sig; return true;
        }

        protected override void Render()
        {
            G.Clear(Color.Transparent);
            G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float s = Ui.S;
            for (int i = 0; i < 2; i++)
            {
                RectangleF r = new RectangleF(Pad * s, (Pad + i * (CardWidgets.CardH + CardWidgets.Gap)) * s, CardWidgets.CardW * s, CardWidgets.CardH * s);
                _cards[i] = r;
                if (i == 0) CardWidgets.DrawCalendar(G, r, s); else CardWidgets.DrawWeather(G, r, s);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = _cards[0].Contains(e.Location) ? 0 : _cards[1].Contains(e.Location) ? 1 : -1;
            if (i < 0) return;
            if (e.Button == MouseButtons.Right) { ShowMenu(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (i == 0) Program.NotificationCenter(); else Weather.Refresh(true);
        }

        private void ShowMenu()
        {
            ContextMenuStrip m = Menus.Create();
            Menus.Add(m, L.T("Widget desktop"), null);
            Menus.Sep(m);
            Menus.Add(m, L.T("Segarkan cuaca"), delegate { Weather.Refresh(true); });
            Menus.Add(m, L.T("Sembunyikan widget desktop"), delegate { Settings.Set("DesktopWidgets", false); BeginInvoke(new MethodInvoker(Stop)); });
            Menus.Sep(m);
            Menus.Add(m, L.T("Pengaturan LiquidMac..."), delegate { Program.RequestSettings(); });
            m.Show(Cursor.Position);
        }
    }
}
