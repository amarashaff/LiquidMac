using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Web.Script.Serialization;

namespace LiquidMac
{
    internal sealed class ChromeProf
    {
        public string Name, Initials, Dir, Browser;
        public Color Color;
        public DateTime Seen;
    }

    // Profil browser (Chrome/Edge/Brave) per jendela, dibaca dari tombol avatar di toolbar lewat UI Automation.
    internal static class ChromeProfiles
    {
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
        public static int Version;
        private static readonly Dictionary<IntPtr, ChromeProf> _cache = new Dictionary<IntPtr, ChromeProf>();
        private static readonly Dictionary<IntPtr, int> _tries = new Dictionary<IntPtr, int>();
        private static readonly Dictionary<IntPtr, DateTime> _retry = new Dictionary<IntPtr, DateTime>();
        private static readonly Queue<IntPtr> _queue = new Queue<IntPtr>();
        private static readonly HashSet<IntPtr> _queued = new HashSet<IntPtr>();
        private static readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private static Thread _worker;
        private static List<ChromeProf> _known;
        private static List<Dictionary<string, object>> _raw;
        private static DateTime _knownAt;
        private static readonly ChromeProf None = new ChromeProf();

        public static string BrowserOf(string exe)
        {
            string f = Path.GetFileNameWithoutExtension(exe ?? "").ToLowerInvariant();
            return f == "msedge" ? "edge" : f;   // chrome, edge, brave
        }

        public static List<ChromeProf> Known(string browser)
        {
            List<ChromeProf> r = new List<ChromeProf>();
            lock (_loadLock) { LoadKnown(); foreach (ChromeProf p in _known) if (p.Browser == browser) r.Add(p); }
            r.Sort(delegate (ChromeProf a, ChromeProf b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return r;
        }

        public static ChromeProf ByDir(string browser, string dir)
        {
            foreach (ChromeProf p in Known(browser)) if (string.Equals(p.Dir, dir, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private static readonly object _loadLock = new object();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        // Tetapkan profil jendela secara langsung (mis. jendela yang dibuka dari ikon profil di Dock)
        public static void Assign(IntPtr h, string browser, string dir)
        {
            ChromeProf k = ByDir(browser, dir);
            if (k == null) return;
            ChromeProf p = new ChromeProf();
            p.Name = k.Name; p.Initials = k.Initials; p.Color = k.Color; p.Dir = k.Dir; p.Browser = k.Browser; p.Seen = DateTime.MinValue;
            lock (_cache) { _cache[h] = p; _queued.Remove(h); _retry.Remove(h); Version++; }
        }

        public static bool IsBrowser(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            string f = Path.GetFileName(target).ToLowerInvariant();
            return f == "chrome.exe" || f == "msedge.exe" || f == "brave.exe";
        }

        // Profil untuk jendela h (null bila belum diketahui; pencarian dijalankan di latar belakang).
        public static ChromeProf For(IntPtr h)
        {
            lock (_cache)
            {
                ChromeProf p;
                if (_cache.TryGetValue(h, out p)) return p == None ? null : p;
                DateTime r;
                if (_retry.TryGetValue(h, out r) && DateTime.Now < r) return null;
                if (_queued.Add(h)) _queue.Enqueue(h);
            }
            if (_worker == null)
            {
                _worker = new Thread(Work); _worker.IsBackground = true; _worker.Name = "chrome-profile";
                _worker.SetApartmentState(ApartmentState.MTA); _worker.Start();
            }
            _wake.Set();
            return null;
        }

        private static void Work()
        {
            while (true)
            {
                IntPtr h = IntPtr.Zero;
                lock (_cache) { if (_queue.Count > 0) h = _queue.Dequeue(); }
                if (h == IntPtr.Zero) { _wake.WaitOne(5000); Prune(); continue; }
                ChromeProf p = null;
                try { p = Lookup(h); } catch (Exception ex) { Log.Write("chrome profile: " + ex.Message); }
                lock (_cache)
                {
                    _queued.Remove(h);
                    if (p != null) { _cache[h] = p; Version++; }
                    else
                    {
                        int n; _tries.TryGetValue(h, out n); _tries[h] = ++n;
                        if (n >= 4) _cache[h] = None;            // bukan jendela browser berprofil
                        else _retry[h] = DateTime.Now.AddSeconds(4);
                    }
                }
            }
        }

        private static void Prune()
        {
            lock (_cache)
            {
                List<IntPtr> dead = new List<IntPtr>();
                foreach (IntPtr h in _cache.Keys) if (!IsWindow(h)) dead.Add(h);
                foreach (IntPtr h in dead) { _cache.Remove(h); _tries.Remove(h); _retry.Remove(h); }
            }
        }

        private static ChromeProf Lookup(IntPtr h)
        {
            if (!IsWindow(h)) return null;
            AutomationElement w = AutomationElement.FromHandle(h);
            AutomationElement b = w.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "AvatarToolbarButton"));
            if (b == null) return null;
            string label = (b.Current.Name ?? "").Trim();
            if (label.Length == 0) return null;
            string browser = "chrome";
            try { uint pid; GetWindowThreadProcessId(h, out pid); browser = BrowserOf(System.Diagnostics.Process.GetProcessById((int)pid).ProcessName); } catch { }
            lock (_loadLock) return Match(label, browser);
        }

        private static readonly string[] Generic = { "work", "school", "kerja", "sekolah", "kantor", "guest", "tamu" };

        private static ChromeProf Match(string label, string browser)
        {
            LoadKnown();
            string l = label.ToLowerInvariant();
            ChromeProf hit = null;
            foreach (ChromeProf k in _known) if (k.Browser == browser && k.Name.Equals(label, StringComparison.OrdinalIgnoreCase)) { hit = k; break; }
            if (hit == null && Array.IndexOf(Generic, l) >= 0)
            {
                List<ChromeProf> managed = new List<ChromeProf>();
                for (int i = 0; i < _known.Count; i++)
                {
                    string hd = Str(_raw[i], "hosted_domain");
                    if (hd.Length > 0 && hd != "NO_HOSTED_DOMAIN" && _known[i].Browser == browser) managed.Add(_known[i]);
                }
                if (managed.Count == 1) hit = managed[0];
            }
            ChromeProf p = new ChromeProf();
            p.Name = hit != null ? hit.Name : label;
            p.Dir = hit != null ? hit.Dir : null; p.Browser = browser;
            p.Color = hit != null ? hit.Color : Palette(label);
            p.Initials = InitialsOf(p.Name);
            p.Seen = DateTime.Now;
            return p;
        }

        private static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToString(o) : ""; }

        private static void LoadKnown()
        {
            if (_known != null && (DateTime.Now - _knownAt).TotalMinutes < 5) return;
            List<ChromeProf> list = new List<ChromeProf>(); List<Dictionary<string, object>> raw = new List<Dictionary<string, object>>();
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] rels = { @"Google\Chrome\User Data\Local State", @"Microsoft\Edge\User Data\Local State", @"BraveSoftware\Brave-Browser\User Data\Local State" };
            string[] keys = { "chrome", "edge", "brave" };
            for (int bi = 0; bi < rels.Length; bi++)
            {
                string f = Path.Combine(la, rels[bi]);
                if (!File.Exists(f)) continue;
                try
                {
                    string json;
                    using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (StreamReader sr = new StreamReader(fs, Encoding.UTF8)) json = sr.ReadToEnd();
                    JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                    Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                    Dictionary<string, object> prof = root != null && root.ContainsKey("profile") ? root["profile"] as Dictionary<string, object> : null;
                    Dictionary<string, object> cache = prof != null && prof.ContainsKey("info_cache") ? prof["info_cache"] as Dictionary<string, object> : null;
                    if (cache == null) continue;
                    foreach (KeyValuePair<string, object> kv in cache)
                    {
                        Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                        if (v == null) continue;
                        ChromeProf p = new ChromeProf();
                        p.Name = Str(v, "name"); if (p.Name.Length == 0) continue;
                        p.Dir = kv.Key; p.Browser = keys[bi]; p.Initials = InitialsOf(p.Name); p.Seen = DateTime.MinValue;
                        int c; string cs = Str(v, "profile_highlight_color"); if (cs.Length == 0) cs = Str(v, "default_avatar_fill_color");
                        p.Color = int.TryParse(cs, out c) && c != 0 ? Color.FromArgb(255, Color.FromArgb(c)) : Palette(p.Name);
                        list.Add(p); raw.Add(v);
                    }
                }
                catch (Exception ex) { Log.Write("local state: " + ex.Message); }
            }
            _known = list; _raw = raw; _knownAt = DateTime.Now;
        }

        public static string InitialsOf(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return "?";
            string[] parts = n.Split(new char[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            string s = "";
            foreach (string part in parts) { if (char.IsLetterOrDigit(part[0])) s += char.ToUpperInvariant(part[0]); if (s.Length == 2) break; }
            return s.Length > 0 ? s : n.Substring(0, 1).ToUpperInvariant();
        }

        private static Color Palette(string s)
        {
            Color[] pal = { Color.FromArgb(10, 132, 255), Color.FromArgb(191, 90, 242), Color.FromArgb(255, 55, 95), Color.FromArgb(255, 159, 10), Color.FromArgb(48, 209, 88), Color.FromArgb(100, 210, 255), Color.FromArgb(94, 92, 230) };
            int h = 0; foreach (char ch in s) h = h * 31 + ch;
            return pal[(h & 0x7fffffff) % pal.Length];
        }

        // Badge bulat HD di pojok kanan bawah ikon: warna profil, cincin putih tipis, inisial tajam.
        public static void DrawBadges(Graphics g, RectangleF ir, List<ChromeProf> profs)
        {
            if (profs.Count == 0) return;
            SmoothingMode sm = g.SmoothingMode; TextRenderingHint th = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float d = Math.Max(Ui.Pf(13), ir.Width * 0.42f);
            int show = Math.Min(2, profs.Count);
            for (int k = show - 1; k >= 0; k--)
            {
                ChromeProf p = profs[k];
                float a = (float)Math.Min(1.0, (DateTime.Now - p.Seen).TotalMilliseconds / 260.0);
                float sc = 0.7f + 0.3f * (1 - (1 - a) * (1 - a));
                float dd = d * sc;
                float cx = ir.Right - d * 0.30f - k * d * 0.62f, cy = ir.Bottom - d * 0.30f;
                RectangleF r = new RectangleF(cx - dd / 2f, cy - dd / 2f, dd, dd);
                int A = (int)(255 * a);
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(A * 70 / 255, 0, 0, 0))) g.FillEllipse(sh, r.X, r.Y + Math.Max(1f, dd * 0.06f), r.Width, r.Height);
                Color c = p.Color, hi = Color.FromArgb(Math.Min(255, c.R + 55), Math.Min(255, c.G + 55), Math.Min(255, c.B + 55));
                using (LinearGradientBrush lg = new LinearGradientBrush(r, Color.FromArgb(A, hi), Color.FromArgb(A, c), 90f)) g.FillEllipse(lg, r);
                using (Pen ring = new Pen(Color.FromArgb(A * 235 / 255, 255, 255, 255), Math.Max(1.2f, dd * 0.085f))) g.DrawEllipse(ring, r);
                string txt = k == 1 && profs.Count > 2 ? "+" + (profs.Count - 1) : p.Initials;
                float fs = dd * (txt.Length > 1 ? 0.40f : 0.50f);
                bool light = c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 186;
                using (Font f = new Font(Ui.Family == "Inter" ? "Inter Semi Bold" : "Segoe UI Semibold", fs, FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(A, light ? Color.FromArgb(30, 30, 36) : Color.White)))
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(txt, f, tb, new RectangleF(r.X, r.Y + dd * 0.03f, r.Width, r.Height), sf);
                }
            }
            g.SmoothingMode = sm; g.TextRenderingHint = th;
        }
    }
}
