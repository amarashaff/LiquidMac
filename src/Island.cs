// LiquidMac - data kartu Dynamic Island: cuaca (wttr.in) dan timer.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace LiquidMac
{
    internal sealed class WeatherInfo
    {
        public string Place, Temp, Cond, Humidity, Wind;
        public DateTime At;
    }

    internal static class Weather
    {
        private static WeatherInfo _cur;
        private static DateTime _last = DateTime.MinValue;
        private static volatile bool _busy;
        public static int Version;

        public static WeatherInfo Current { get { return _cur; } }

        // Ambil cuaca paling sering tiap 30 menit, di thread latar.
        public static void Refresh(bool force)
        {
            if ((!Settings.Get("IslandWeather", true) && !Settings.Get("DockWidgetWeather", false) && !Settings.Get("MacLock", false) && !Settings.Get("LockAnim", false)) || _busy) return;
            if (!force && (DateTime.Now - _last).TotalMinutes < 30) return;
            _busy = true; _last = DateTime.Now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "curl/8.0 LiquidMac";
                        string s = wc.DownloadString("https://wttr.in/?format=%l|%t|%C|%h|%w&lang=" + (L.En ? "en" : "id"));
                        string[] p = s.Trim().Split('|');
                        if (p.Length >= 5)
                        {
                            WeatherInfo w = new WeatherInfo();
                            w.Place = p[0].Split(',')[0].Trim();
                            w.Temp = p[1].Trim().Replace("+", "");
                            w.Cond = L.En ? p[2].Trim() : Translate(p[2].Trim());
                            w.Humidity = p[3].Trim();
                            w.Wind = p[4].Trim();
                            w.At = DateTime.Now;
                            _cur = w; Version++;
                        }
                    }
                }
                catch (Exception ex) { Log.Write("cuaca: " + ex.Message); _last = DateTime.Now.AddMinutes(-25); }
                _busy = false;
            });
        }

        private static readonly string[][] Phrases = {
            new string[] { "thundery outbreaks possible", "Kemungkinan badai petir" },
            new string[] { "thundery outbreaks in nearby", "Badai petir di sekitar" },
            new string[] { "patchy light rain with thunder", "Hujan ringan setempat disertai petir" },
            new string[] { "moderate or heavy rain with thunder", "Hujan sedang-lebat disertai petir" },
            new string[] { "patchy rain possible", "Kemungkinan hujan setempat" },
            new string[] { "patchy rain nearby", "Hujan setempat di sekitar" },
            new string[] { "torrential rain shower", "Hujan sangat lebat" },
            new string[] { "moderate or heavy rain shower", "Hujan deras sesaat" },
            new string[] { "light rain shower", "Hujan ringan sesaat" },
            new string[] { "rain shower", "Hujan sesaat" },
            new string[] { "shower in vicinity", "Hujan di sekitar" },
            new string[] { "patchy light drizzle", "Gerimis ringan setempat" },
            new string[] { "light drizzle", "Gerimis ringan" },
            new string[] { "patchy light rain", "Hujan ringan setempat" },
            new string[] { "moderate rain at times", "Kadang hujan sedang" },
            new string[] { "heavy rain at times", "Kadang hujan lebat" },
            new string[] { "moderate rain", "Hujan sedang" },
            new string[] { "heavy rain", "Hujan lebat" },
            new string[] { "light rain", "Hujan ringan" },
            new string[] { "partly cloudy", "Berawan sebagian" },
            new string[] { "freezing fog", "Kabut beku" },
            new string[] { "smoky haze", "Kabut asap" },
            new string[] { "thunderstorm", "Badai petir" },
            new string[] { "overcast", "Mendung" },
            new string[] { "cloudy", "Berawan" },
            new string[] { "sunny", "Cerah" },
            new string[] { "clear", "Cerah" },
            new string[] { "mist", "Berkabut tipis" },
            new string[] { "fog", "Berkabut" },
            new string[] { "haze", "Berkabut asap" },
            new string[] { "smoke", "Berasap" },
            new string[] { "dust", "Berdebu" },
            new string[] { "drizzle", "Gerimis" },
            new string[] { "rain", "Hujan" },
            new string[] { "thunder", "Petir" },
            new string[] { "snow", "Salju" } };

        // wttr.in tidak menerjemahkan teks cuaca -> terjemahkan sendiri (frasa terpanjang dulu).
        public static string Translate(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            string l = en.ToLowerInvariant().Replace(",", "").Trim();
            foreach (string[] p in Phrases) if (l == p[0]) return p[1];
            foreach (string[] p in Phrases) if (l.Contains(p[0])) return p[1];
            return en;
        }

        // Ikon cuaca sederhana yang digambar sendiri.
        public static void DrawIcon(Graphics g, string cond, RectangleF r, int alpha)
        {
            string c = (cond ?? "").ToLowerInvariant();
            bool rain = c.Contains("hujan") || c.Contains("rain") || c.Contains("gerimis") || c.Contains("drizzle") || c.Contains("shower");
            bool thunder = c.Contains("petir") || c.Contains("badai") || c.Contains("thunder");
            bool sun = c.Contains("cerah") || c.Contains("sun") || c.Contains("clear");
            bool partly = c.Contains("sebagian") || c.Contains("partly") || c.Contains("patchy");
            if (c.Contains("mendung") || c.Contains("berawan sebagian")) partly = c.Contains("sebagian");
            bool cloud = !sun || partly || rain || thunder || c.Contains("awan") || c.Contains("mendung") || c.Contains("cloud") || c.Contains("kabut") || c.Contains("mist") || c.Contains("fog");
            float s = r.Width;
            if (sun || partly)
            {
                RectangleF sr = cloud ? new RectangleF(r.X + s * 0.38f, r.Y + s * 0.05f, s * 0.5f, s * 0.5f) : new RectangleF(r.X + s * 0.2f, r.Y + s * 0.2f, s * 0.6f, s * 0.6f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 200, 40))) g.FillEllipse(b, sr);
            }
            if (cloud)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 225, 230, 240)))
                {
                    g.FillEllipse(b, r.X + s * 0.08f, r.Y + s * 0.38f, s * 0.42f, s * 0.36f);
                    g.FillEllipse(b, r.X + s * 0.28f, r.Y + s * 0.24f, s * 0.46f, s * 0.46f);
                    g.FillEllipse(b, r.X + s * 0.52f, r.Y + s * 0.40f, s * 0.38f, s * 0.34f);
                    g.FillRectangle(b, r.X + s * 0.25f, r.Y + s * 0.52f, s * 0.5f, s * 0.22f);
                }
            }
            if (rain)
                using (Pen p = new Pen(Color.FromArgb(alpha, 90, 170, 255), Math.Max(1.5f, s / 16f)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    for (int i = 0; i < 3; i++) g.DrawLine(p, r.X + s * (0.3f + i * 0.18f), r.Y + s * 0.8f, r.X + s * (0.26f + i * 0.18f), r.Y + s * 0.95f);
                }
            if (thunder)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 214, 10)))
                    g.FillPolygon(b, new PointF[] { new PointF(r.X + s * 0.52f, r.Y + s * 0.62f), new PointF(r.X + s * 0.40f, r.Y + s * 0.84f), new PointF(r.X + s * 0.5f, r.Y + s * 0.84f), new PointF(r.X + s * 0.44f, r.Y + s), new PointF(r.X + s * 0.64f, r.Y + s * 0.76f), new PointF(r.X + s * 0.54f, r.Y + s * 0.76f) });
        }
    }

    internal static class IslandTimer
    {
        private static DateTime _end;
        private static TimeSpan _left = TimeSpan.FromMinutes(25), _total = TimeSpan.FromMinutes(25);
        private static bool _running;
        public static event Action Finished;

        public static bool Running { get { return _running; } }
        public static bool Active { get { return _running || _left < _total; } }
        public static TimeSpan Total { get { return _total; } }

        public static TimeSpan Left
        {
            get
            {
                if (!_running) return _left;
                TimeSpan l = _end - DateTime.Now;
                if (l <= TimeSpan.Zero)
                {
                    _running = false; _left = _total;
                    if (Finished != null) Finished();
                    return TimeSpan.Zero;
                }
                return l;
            }
        }

        public static void Set(int minutes) { _running = false; _total = _left = TimeSpan.FromMinutes(minutes); }
        public static void Toggle()
        {
            if (_running) { _left = _end - DateTime.Now; _running = false; }
            else { _end = DateTime.Now + _left; _running = true; }
        }
        public static void Reset() { _running = false; _left = _total; }

        public static string Text(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }
    }
}

namespace LiquidMac
{
    internal sealed class Reminder { public DateTime At; public string Text; }

    // Pengingat/catatan dengan alarm. Disimpan di %LOCALAPPDATA%\LiquidMac\reminders.txt (ticks|teks per baris).
    internal static class Reminders
    {
        private static List<Reminder> _l;
        private static string PathOf { get { return System.IO.Path.Combine(Log.Dir, "reminders.txt"); } }

        private static void Load()
        {
            if (_l != null) return;
            _l = new List<Reminder>();
            try
            {
                if (File.Exists(PathOf))
                    foreach (string line in File.ReadAllLines(PathOf, Encoding.UTF8))
                    {
                        int bar = line.IndexOf('|'); long ticks;
                        if (bar <= 0 || !long.TryParse(line.Substring(0, bar), out ticks)) continue;
                        Reminder r = new Reminder(); r.At = new DateTime(ticks); r.Text = line.Substring(bar + 1).Replace('\u001F', '\n'); _l.Add(r);
                    }
            }
            catch (Exception ex) { Log.Write("reminders: " + ex.Message); }
            Sort();
        }
        private static void Sort() { _l.Sort(delegate (Reminder a, Reminder b) { return a.At.CompareTo(b.At); }); }
        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (Reminder r in _l) lines.Add(r.At.Ticks + "|" + (r.Text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace('\n', '\u001F'));
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllLines(PathOf, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Write("reminders save: " + ex.Message); }
        }
        public static List<Reminder> All() { Load(); return new List<Reminder>(_l); }
        public static void Add(DateTime at, string text) { Load(); Reminder r = new Reminder(); r.At = at; r.Text = string.IsNullOrEmpty(text) ? L.T("Pengingat") : text; _l.Add(r); Sort(); Save(); }
        public static void Remove(Reminder r) { Load(); _l.Remove(r); Save(); }
        public static void Update(Reminder r, DateTime at, string text)
        {
            Load();
            r.At = at; r.Text = string.IsNullOrEmpty(text) ? L.T("Pengingat") : text;
            if (!_l.Contains(r)) _l.Add(r);
            Sort(); Save();
        }
        public static Reminder PopDue()
        {
            Load();
            if (_l.Count == 0 || _l[0].At > DateTime.Now) return null;
            Reminder r = _l[0]; _l.RemoveAt(0); Save(); return r;
        }
    }

    internal sealed class ReminderDialog : System.Windows.Forms.Form
    {
        private readonly System.Windows.Forms.TextBox _note = new System.Windows.Forms.TextBox();
        private readonly System.Windows.Forms.DateTimePicker _time = new System.Windows.Forms.DateTimePicker();
        public string Note { get { return _note.Text.Trim(); } }
        public DateTime At
        {
            get
            {
                DateTime v = _time.Value; v = new DateTime(v.Year, v.Month, v.Day, v.Hour, v.Minute, 0);
                while (v <= DateTime.Now) v = v.AddDays(1);
                return v;
            }
        }

        public ReminderDialog()
        {
            Text = L.T("Pengingat baru"); FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(32, 32, 36); ForeColor = Color.White; Font = Ui.F(9.5f, FontStyle.Regular);
            ClientSize = new Size(Ui.P(360), Ui.P(252));
            System.Windows.Forms.Label l1 = new System.Windows.Forms.Label(); l1.Text = L.T("Catatan"); l1.AutoSize = true; l1.Location = new Point(Ui.P(16), Ui.P(14));
            _note.Location = new Point(Ui.P(16), Ui.P(36)); _note.Width = Ui.P(328);
            _note.Multiline = true; _note.ScrollBars = System.Windows.Forms.ScrollBars.Vertical; _note.Height = Ui.P(64);   // catatan boleh beberapa baris (Ctrl+Enter)
            _note.BackColor = Color.FromArgb(52, 52, 58); _note.ForeColor = Color.White; _note.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            System.Windows.Forms.Label l2 = new System.Windows.Forms.Label(); l2.Text = L.T("Waktu"); l2.AutoSize = true; l2.Location = new Point(Ui.P(16), Ui.P(112));
            _time.Format = System.Windows.Forms.DateTimePickerFormat.Custom; _time.CustomFormat = "dd MMM yyyy   HH:mm";
            _time.Location = new Point(Ui.P(16), Ui.P(134)); _time.Width = Ui.P(180); _time.Value = DateTime.Now.AddMinutes(10);
            Controls.Add(l1); Controls.Add(_note); Controls.Add(l2); Controls.Add(_time);
            string[] q = { L.T("+10 mnt"), L.T("+30 mnt"), L.T("+1 jam") }; int[] mins = { 10, 30, 60 };
            int bx = Ui.P(204);
            for (int i = 0; i < 3; i++)
            {
                int m = mins[i];
                System.Windows.Forms.Button b = Btn(q[i], new Point(bx, Ui.P(132)), Ui.P(44));
                b.Click += delegate { _time.Value = DateTime.Now.AddMinutes(m); };
                Controls.Add(b); bx += Ui.P(47);
            }
            System.Windows.Forms.Button ok = Btn(L.T("Simpan"), new Point(Ui.P(176), Ui.P(200)), Ui.P(80));
            ok.BackColor = Color.FromArgb(255, 159, 10); ok.ForeColor = Color.Black; ok.DialogResult = System.Windows.Forms.DialogResult.OK;
            System.Windows.Forms.Button no = Btn(L.T("Batal"), new Point(Ui.P(264), Ui.P(200)), Ui.P(80));
            no.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(no); AcceptButton = ok; CancelButton = no;
            Shown += delegate { Activate(); _note.Focus(); };
        }

        // Ubah pengingat yang sudah ada: catatan & waktunya terisi.
        public ReminderDialog(Reminder edit) : this()
        {
            if (edit == null) return;
            Text = L.T("Edit pengingat");
            _note.Text = (edit.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            try { _time.Value = edit.At; } catch { }
        }

        internal static System.Windows.Forms.Button Btn(string t, Point p, int w)
        {
            System.Windows.Forms.Button b = new System.Windows.Forms.Button();
            b.Text = t; b.Location = p; b.Size = new Size(w, Ui.P(28));
            b.FlatStyle = System.Windows.Forms.FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.FromArgb(58, 58, 64); b.ForeColor = Color.White; b.Font = Ui.F(8.5f, FontStyle.Regular);
            return b;
        }
    }

    // Lihat isi catatan pengingat. Result: 0 tutup, 1 edit, 2 hapus.
    internal sealed class ReminderView : System.Windows.Forms.Form
    {
        public int Result;

        public ReminderView(Reminder r, bool editable)
        {
            Text = L.T("Catatan pengingat"); FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(32, 32, 36); ForeColor = Color.White; Font = Ui.F(9.5f, FontStyle.Regular);
            ClientSize = new Size(Ui.P(380), Ui.P(262));
            System.Windows.Forms.Label when = new System.Windows.Forms.Label();
            when.Text = r.At.ToString("d MMM yyyy   HH:mm"); when.AutoSize = true; when.Location = new Point(Ui.P(16), Ui.P(14));
            when.ForeColor = Color.FromArgb(255, 159, 10); when.Font = Ui.F(10f, FontStyle.Bold);
            System.Windows.Forms.TextBox body = new System.Windows.Forms.TextBox();
            body.Multiline = true; body.ReadOnly = true; body.ScrollBars = System.Windows.Forms.ScrollBars.Vertical; body.TabStop = false;
            body.BorderStyle = System.Windows.Forms.BorderStyle.None; body.BackColor = Color.FromArgb(44, 44, 50); body.ForeColor = Color.White;
            body.Font = Ui.F(10.5f, FontStyle.Regular);
            body.Location = new Point(Ui.P(16), Ui.P(44)); body.Size = new Size(Ui.P(348), Ui.P(164));
            body.Text = (r.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            Controls.Add(when); Controls.Add(body);
            System.Windows.Forms.Button close = ReminderDialog.Btn(L.T("Tutup"), new Point(Ui.P(284), Ui.P(222)), Ui.P(80));
            close.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            Controls.Add(close); CancelButton = close; AcceptButton = close;
            if (editable)
            {
                System.Windows.Forms.Button ed = ReminderDialog.Btn(L.T("Edit"), new Point(Ui.P(108), Ui.P(222)), Ui.P(80));
                ed.BackColor = Color.FromArgb(255, 159, 10); ed.ForeColor = Color.Black;
                ed.Click += delegate { Result = 1; DialogResult = System.Windows.Forms.DialogResult.OK; };
                System.Windows.Forms.Button del = ReminderDialog.Btn(L.T("Hapus"), new Point(Ui.P(196), Ui.P(222)), Ui.P(80));
                del.Click += delegate { Result = 2; DialogResult = System.Windows.Forms.DialogResult.OK; };
                Controls.Add(ed); Controls.Add(del);
            }
            Shown += delegate { Activate(); body.SelectionStart = 0; body.SelectionLength = 0; close.Focus(); };
        }
    }
}
