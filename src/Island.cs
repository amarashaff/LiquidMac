// LiquidMac - data kartu Dynamic Island: cuaca (wttr.in) dan timer.
using System;
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
            if (!Settings.Get("IslandWeather", true) || _busy) return;
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
                        string s = wc.DownloadString("https://wttr.in/?format=%l|%t|%C|%h|%w&lang=id");
                        string[] p = s.Trim().Split('|');
                        if (p.Length >= 5)
                        {
                            WeatherInfo w = new WeatherInfo();
                            w.Place = p[0].Split(',')[0].Trim();
                            w.Temp = p[1].Trim().Replace("+", "");
                            w.Cond = Translate(p[2].Trim());
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
