using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  SPOTLIGHT (setting Spotlight, bawaan aktif): Alt+Space atau ikon kaca pembesar di bilah atas.
    //  Cari aplikasi, berkas (indeks Windows Search), pengaturan, kalkulator, dan web.
    // ============================================================
    internal sealed class SpotItem
    {
        public string Kind;          // app | file | folder | calc | setting | web
        public string Title, Sub, Key;
        public Bitmap Icon;          // milik item (dibuang saat hasil diganti), kecuali ikon aplikasi (milik Apps)
        public bool OwnIcon;
        public Action Run, Reveal;
    }

    internal static class Spotlight
    {
        public static bool Enabled { get { return Settings.Get("Spotlight", true); } }
        private static SpotlightForm _form;
        private static HotkeyWindow _hk;
        public static string HotkeyText = "Alt+Space";

        public static void Toggle()
        {
            try
            {
                if (_form == null || _form.IsDisposed) _form = new SpotlightForm();
                _form.Toggle();
            }
            catch (Exception ex) { Log.Write("spotlight: " + ex.Message); }
        }

        // Pintasan global: Alt+Space (bila sudah dipakai aplikasi lain, mis. PowerToys Run -> Ctrl+Space).
        public static void StartHotkey()
        {
            if (_hk != null) return;
            _hk = new HotkeyWindow();
            if (_hk.Register(0x0001, "Alt+Space")) return;
            if (_hk.Register(0x0002, "Ctrl+Space")) { Log.Write("spotlight: Alt+Space sudah dipakai aplikasi lain, pakai Ctrl+Space"); return; }
            Log.Write("spotlight: pintasan tidak bisa didaftarkan (Alt/Ctrl+Space dipakai aplikasi lain)");
        }

        public static void StopHotkey()
        {
            if (_hk == null) return;
            _hk.Unregister(); _hk.DestroyHandle(); _hk = null;
            if (_form != null && !_form.IsDisposed && _form.Visible) _form.Hide();
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
            [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
            private bool _on;
            public HotkeyWindow() { CreateHandle(new CreateParams()); }
            public bool Register(uint mod, string text)
            {
                if (!RegisterHotKey(Handle, 0x5370, mod | 0x4000 /* NOREPEAT */, 0x20 /* SPACE */)) return false;
                _on = true; HotkeyText = text; return true;
            }
            public void Unregister() { if (_on) UnregisterHotKey(Handle, 0x5370); _on = false; }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312 && m.WParam.ToInt32() == 0x5370) { Toggle(); return; }
                base.WndProc(ref m);
            }
        }

        // ---------------- pencarian ----------------
        private sealed class SettingEntry { public string Title, Uri, Words; }
        private static List<SettingEntry> _settings;
        private static List<SettingEntry> SettingsList()
        {
            if (_settings != null) return _settings;
            List<SettingEntry> l = new List<SettingEntry>();
            Action<string, string, string> add = delegate (string t, string u, string w) { SettingEntry e = new SettingEntry(); e.Title = t; e.Uri = u; e.Words = (t + " " + w).ToLowerInvariant(); l.Add(e); };
            add("Wi-Fi", "ms-settings:network-wifi", "wifi internet jaringan network");
            add("Bluetooth", "ms-settings:bluetooth", "perangkat devices");
            add(L.T("Tampilan"), "ms-settings:display", "display layar screen resolusi resolution kecerahan brightness");
            add(L.T("Suara"), "ms-settings:sound", "sound audio volume speaker mikrofon microphone");
            add(L.T("Daya & Baterai"), "ms-settings:powersleep", "power battery baterai daya sleep tidur");
            add("Windows Update", "ms-settings:windowsupdate", "update pembaruan");
            add(L.T("Aplikasi terpasang"), "ms-settings:appsfeatures", "apps aplikasi uninstall hapus program");
            add(L.T("Latar belakang"), "ms-settings:personalization-background", "wallpaper background latar personalisasi personalization");
            add(L.T("Penyimpanan"), "ms-settings:storagesense", "storage disk penyimpanan");
            add(L.T("Pengaturan Windows"), "ms-settings:", "settings pengaturan control");
            add(L.T("Pengaturan LiquidMac"), null, "liquidmac settings pengaturan preferensi preferences");
            _settings = l;
            return l;
        }

        private static int Score(string name, string q)
        {
            string n = name.ToLowerInvariant();
            if (n == q) return 100;
            string[] words = n.Split(new char[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string w in words) if (w == q) return 88 - Math.Min(8, words.Length);      // kata utuh: "chrome" -> Google Chrome
            if (n.StartsWith(q)) return 80 - Math.Min(8, words.Length);
            foreach (string w in words) if (w.StartsWith(q)) return 60;
            if (q.Length >= 3 && n.Contains(q)) return 30;
            return 0;
        }

        // Hasil cepat (tanpa berkas): kalkulator, aplikasi, pengaturan.
        public static List<SpotItem> Quick(string query)
        {
            List<SpotItem> res = new List<SpotItem>();
            string q = (query ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return res;

            double v;
            if (Calc.TryEval(query, out v))
            {
                SpotItem c = new SpotItem(); c.Kind = "calc"; c.Key = "calc";
                string shown = Calc.Format(v);
                c.Title = "= " + shown; c.Sub = L.T("Kalkulator - Enter untuk menyalin hasil");
                c.Run = delegate { try { Clipboard.SetText(shown); } catch { } };
                res.Add(c);
            }

            List<KeyValuePair<int, AppEntry>> apps = new List<KeyValuePair<int, AppEntry>>();
            foreach (AppEntry e in Apps.All())
            {
                int s = Score(e.Name, q);
                if (s <= 0) continue;
                s += (int)Math.Min(15, LaunchStats.Count(e.Id));   // yang sering dibuka naik ke atas
                apps.Add(new KeyValuePair<int, AppEntry>(s, e));
            }
            apps.Sort(delegate (KeyValuePair<int, AppEntry> a, KeyValuePair<int, AppEntry> b) { int c = b.Key.CompareTo(a.Key); return c != 0 ? c : string.Compare(a.Value.Name, b.Value.Name, StringComparison.CurrentCultureIgnoreCase); });
            for (int i = 0; i < apps.Count && i < 6; i++)
            {
                AppEntry e = apps[i].Value; string id = e.Id;
                SpotItem it = new SpotItem(); it.Kind = "app"; it.Key = "app|" + id; it.Title = e.Name; it.Sub = L.T("Aplikasi");
                it.Run = delegate { LaunchStats.Record(id); Apps.Launch(id); };
                res.Add(it);
            }

            if (q.Length >= 2)
            {
                int n = 0;
                foreach (SettingEntry se in SettingsList())
                {
                    bool hit = false;
                    foreach (string w in se.Words.Split(' ')) if (w.StartsWith(q)) { hit = true; break; }
                    if (!hit) continue;
                    SettingEntry s0 = se;
                    SpotItem it = new SpotItem(); it.Kind = "setting"; it.Key = "set|" + se.Title; it.Title = se.Title; it.Sub = L.T("Pengaturan");
                    it.Run = delegate { if (s0.Uri == null) Program.RequestSettings(); else Shell.Run(s0.Uri, null); };
                    res.Add(it);
                    if (++n >= 3) break;
                }
            }
            return res;
        }

        public static SpotItem WebItem(string query)
        {
            string q = query.Trim();
            SpotItem w = new SpotItem(); w.Kind = "web"; w.Key = "web";
            w.Title = L.T("Cari di web:") + " “" + q + "”"; w.Sub = L.T("Buka di browser");
            w.Run = delegate { Shell.Run("https://www.google.com/search?q=" + Uri.EscapeDataString(q), null); };
            return w;
        }

        // Berkas & folder dari indeks Windows Search (dipanggil di thread latar). Nama cocok di awal kata, terbaru dulu.
        public static List<string[]> Files(string query, int max)
        {
            List<string[]> res = new List<string[]>();
            string q = (query ?? "").Trim();
            if (q.Length < 2) return res;
            string esc = q.Replace("'", "''").Replace("\"", "");
            if (esc.Length == 0) return res;
            string sql = "SELECT TOP " + max + " System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex " +
                         "WHERE System.ItemUrl LIKE 'file:%' AND CONTAINS(System.ItemNameDisplay, '\"" + esc + "*\"') " +
                         "AND NOT System.ItemPathDisplay LIKE '%\\AppData\\%' ORDER BY System.DateModified DESC";
            try
            {
                using (System.Data.OleDb.OleDbConnection con = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                {
                    con.Open();
                    using (System.Data.OleDb.OleDbCommand cmd = new System.Data.OleDb.OleDbCommand(sql, con))
                    using (System.Data.OleDb.OleDbDataReader r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            string path = r.IsDBNull(0) ? null : Convert.ToString(r.GetValue(0));
                            if (string.IsNullOrEmpty(path)) continue;
                            string name = r.IsDBNull(1) ? Path.GetFileName(path) : Convert.ToString(r.GetValue(1));
                            string type = r.IsDBNull(2) ? "" : Convert.ToString(r.GetValue(2));
                            res.Add(new string[] { path, name, type });
                        }
                }
            }
            catch (Exception ex) { Log.Write("spotlight berkas: " + ex.Message); }
            return res;
        }
    }

    // Kalkulator kecil: + - * / x : % ^ ( ), koma atau titik desimal.
    internal static class Calc
    {
        public static bool TryEval(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Replace(" ", "").Replace(",", ".").Replace("×", "*").Replace("x", "*").Replace("X", "*").Replace(":", "/").Replace("÷", "/");
            if (t.Length < 3) return false;
            bool digit = false, op = false;
            foreach (char c in t)
            {
                if (char.IsDigit(c)) digit = true;
                else if ("+-*/%^()".IndexOf(c) >= 0) op = true;
                else if (c != '.') return false;
            }
            if (!digit || !op) return false;
            try
            {
                int i = 0; v = Expr(t, ref i);
                return i == t.Length && !double.IsNaN(v) && !double.IsInfinity(v);
            }
            catch { return false; }
        }

        public static string Format(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15) return Math.Round(v).ToString("#,##0", L.Culture);
            return v.ToString("#,##0.##########", L.Culture);
        }

        private static double Expr(string t, ref int i)
        {
            double v = Term(t, ref i);
            while (i < t.Length && (t[i] == '+' || t[i] == '-')) { char o = t[i++]; double r = Term(t, ref i); v = o == '+' ? v + r : v - r; }
            return v;
        }
        private static double Term(string t, ref int i)
        {
            double v = Pow(t, ref i);
            while (i < t.Length && (t[i] == '*' || t[i] == '/' || t[i] == '%'))
            {
                char o = t[i++];
                if (o == '%' && (i >= t.Length || "+-*/)".IndexOf(t[i]) >= 0)) { v = v / 100.0; continue; }   // 50% = 0,5
                double r = Pow(t, ref i);
                v = o == '*' ? v * r : o == '/' ? v / r : v % r;
            }
            return v;
        }
        private static double Pow(string t, ref int i)
        {
            double b = Unary(t, ref i);
            if (i < t.Length && t[i] == '^') { i++; return Math.Pow(b, Pow(t, ref i)); }
            return b;
        }
        private static double Unary(string t, ref int i)
        {
            if (i < t.Length && t[i] == '-') { i++; return -Unary(t, ref i); }
            if (i < t.Length && t[i] == '+') { i++; return Unary(t, ref i); }
            if (i < t.Length && t[i] == '(')
            {
                i++; double v = Expr(t, ref i);
                if (i >= t.Length || t[i] != ')') throw new FormatException();
                i++; return v;
            }
            int s = i;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.')) i++;
            if (i == s) throw new FormatException();
            return double.Parse(t.Substring(s, i - s), CultureInfo.InvariantCulture);
        }
    }

    internal sealed class SpotlightForm : GlassPopup
    {
        private string _q = "";
        private List<SpotItem> _items = new List<SpotItem>();
        private List<SpotItem> _files = new List<SpotItem>();
        private int _sel, _ver, _hoverRow = -1;
        private bool _caret = true;
        private readonly System.Windows.Forms.Timer _blink = new System.Windows.Forms.Timer(), _debounce = new System.Windows.Forms.Timer();
        private readonly Font _qf = Ui.F(17f, FontStyle.Regular), _tf = Ui.F(10.5f, FontStyle.Regular), _sf = Ui.F(8.5f, FontStyle.Regular), _hf = Ui.F(8f, FontStyle.Bold);
        private readonly Font _glyph = Glyphs.Font((float)Math.Round(22 * Ui.S)), _glyphS = Glyphs.Font((float)Math.Round(15 * Ui.S));
        private readonly Dictionary<string, Bitmap> _iconCache = new Dictionary<string, Bitmap>();
        private readonly List<RectangleF> _rows = new List<RectangleF>();
        private int BarH { get { return Ui.P(56); } }
        private int RowH { get { return Ui.P(42); } }
        private int W { get { return Ui.P(680); } }
        private const int MaxRows = 10;

        public SpotlightForm()
        {
            Text = "Spotlight";
            Deactivate += delegate { Hide(); };
            _blink.Interval = 530; _blink.Tick += delegate { _caret = !_caret; Invalidate(new Rectangle(0, 0, Width, BarH)); };
            _debounce.Interval = 170; _debounce.Tick += delegate { _debounce.Stop(); StartFileSearch(); };
        }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            Rectangle s = Screen.PrimaryScreen.Bounds;
            int maxH = BarH + Ui.P(12) + MaxRows * RowH + Ui.P(3) * 22;
            Rectangle full = new Rectangle(s.Left + (s.Width - W) / 2, s.Top + (int)(s.Height * 0.22), W, maxH);
            _q = ""; _items.Clear(); ClearFiles(); _sel = 0; _hoverRow = -1;
            ShowAt(full, 0.42f);   // latar blur diambil untuk ukuran terbesar, lalu tinggi jendela mengikuti jumlah hasil
            Round(true);
            Fit();
            _caret = true; _blink.Start();
            if (Apps.All().Count == 0) Log.Write("spotlight: daftar aplikasi kosong");
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { _blink.Stop(); _debounce.Stop(); _ver++; ClearFiles(); foreach (Bitmap b in _iconCache.Values) if (b != null) b.Dispose(); _iconCache.Clear(); }
        }

        private void ClearFiles() { foreach (SpotItem f in _files) if (f.OwnIcon && f.Icon != null) f.Icon.Dispose(); _files.Clear(); }

        private List<SpotItem> All()
        {
            List<SpotItem> l = new List<SpotItem>(_items);
            l.AddRange(_files);
            if (_q.Trim().Length > 0) l.Add(Spotlight.WebItem(_q));
            if (l.Count > MaxRows) l.RemoveRange(MaxRows - 1, l.Count - MaxRows);
            return l;
        }

        private void QueryChanged()
        {
            _items = Spotlight.Quick(_q);
            ClearFiles(); _ver++;
            _sel = 0; _caret = true;
            _debounce.Stop(); if (_q.Trim().Length >= 2) _debounce.Start();
            Fit(); Invalidate();
        }

        private void StartFileSearch()
        {
            int ver = _ver; string q = _q;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string[]> found = Spotlight.Files(q, 6);
                if (ver != _ver || found.Count == 0) return;
                try { BeginInvoke(new MethodInvoker(delegate { ShowFiles(ver, found); })); } catch { }
            });
        }

        private void ShowFiles(int ver, List<string[]> found)
        {
            if (ver != _ver || !Visible) return;
            ClearFiles();
            foreach (string[] f in found)
            {
                string path = f[0];
                bool dir = Directory.Exists(path);
                SpotItem it = new SpotItem(); it.Kind = dir ? "folder" : "file"; it.Key = "file|" + path;
                it.Title = f[1]; it.Sub = Path.GetDirectoryName(path);
                it.Run = delegate { if (dir) Shell.Explore(path); else Shell.Run(path, null); };
                it.Reveal = delegate { Shell.Run("explorer.exe", "/select,\"" + path + "\""); };
                _files.Add(it);
            }
            Fit(); Invalidate();
        }

        private void Fit()
        {
            List<SpotItem> l = All();
            int h = BarH;
            if (l.Count > 0) h += Ui.P(8) + l.Count * RowH + Sections(l) * Ui.P(22) + Ui.P(6);
            if (Height != h) Height = h;
        }

        private static string Section(SpotItem it)
        {
            switch (it.Kind)
            {
                case "calc": return L.T("Kalkulator");
                case "app": return L.T("Aplikasi");
                case "setting": return L.T("Pengaturan");
                case "file": case "folder": return L.T("Dokumen & Folder");
                default: return L.T("Web");
            }
        }
        private static int Sections(List<SpotItem> l) { int n = 0; string last = null; foreach (SpotItem it in l) { string s = Section(it); if (s != last) { n++; last = s; } } return n; }

        // ---------------- keyboard ----------------
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            List<SpotItem> l = All();
            switch (key)
            {
                case Keys.Escape: if (_q.Length > 0) { _q = ""; QueryChanged(); } else Hide(); return true;
                case Keys.Down: if (l.Count > 0) { _sel = (_sel + 1) % l.Count; Invalidate(); } return true;
                case Keys.Up: if (l.Count > 0) { _sel = (_sel - 1 + l.Count) % l.Count; Invalidate(); } return true;
                case Keys.Enter: Open(l, _sel, false); return true;
                case Keys.Control | Keys.Enter: Open(l, _sel, true); return true;
                case Keys.Back: if (_q.Length > 0) { _q = _q.Substring(0, _q.Length - 1); QueryChanged(); } return true;
                case Keys.Control | Keys.Back: { string t = _q.TrimEnd(); int sp = t.LastIndexOf(' '); _q = sp < 0 ? "" : t.Substring(0, sp + 1); QueryChanged(); return true; }
                case Keys.Control | Keys.V:
                    try { string c = Clipboard.GetText(); if (!string.IsNullOrEmpty(c)) { _q += c.Replace("\r", " ").Replace("\n", " "); if (_q.Length > 200) _q = _q.Substring(0, 200); QueryChanged(); } } catch { }
                    return true;
                case Keys.Control | Keys.A: case Keys.Delete: _q = ""; QueryChanged(); return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsControl(e.KeyChar) || _q.Length >= 200) return;
            _q += e.KeyChar; e.Handled = true;
            QueryChanged();
        }

        private void Open(List<SpotItem> l, int i, bool reveal)
        {
            if (i < 0 || i >= l.Count) return;
            SpotItem it = l[i];
            Hide();
            try { if (reveal && it.Reveal != null) it.Reveal(); else if (it.Run != null) it.Run(); }
            catch (Exception ex) { Log.Write("spotlight buka: " + ex.Message); }
        }

        // ---------------- mouse ----------------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int r = RowAt(e.Location);
            if (r != _hoverRow) { _hoverRow = r; if (r >= 0) _sel = r; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int r = RowAt(e.Location);
            if (r >= 0 && e.Button == MouseButtons.Left) Open(All(), r, (ModifierKeys & Keys.Control) != 0);
        }
        private int RowAt(Point p) { for (int i = 0; i < _rows.Count; i++) if (_rows[i].Contains(p)) return i; return -1; }

        // ---------------- gambar ----------------
        private Bitmap IconFor(SpotItem it)
        {
            if (it.Icon != null) return it.Icon;
            Bitmap b;
            if (_iconCache.TryGetValue(it.Key, out b)) return b;
            b = null;
            try
            {
                if (it.Kind == "app")
                {
                    string id = it.Key.Substring(4);
                    foreach (AppEntry e in Apps.All()) if (e.Id == id) { Bitmap t = Apps.TileFor(e); if (t != null) b = new Bitmap(t); break; }
                }
                else if (it.Kind == "file" || it.Kind == "folder") b = ShellImage.Get(it.Key.Substring(5), 64);
            }
            catch { }
            _iconCache[it.Key] = b;
            return b;
        }

        private void GlyphTile(Graphics g, RectangleF r, string glyph, Color c1, Color c2)
        {
            using (GraphicsPath p = Glass.Round(r, r.Width * 0.24f))
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), c1, c2, 90f)) g.FillPath(b, p);
            using (SolidBrush w = new SolidBrush(Color.White))
            {
                if (glyph.Length == 1 && glyph[0] < 0x2000)   // teks biasa (mis. "="), bukan ikon Segoe
                    using (Font f = Ui.F(13f, FontStyle.Bold)) using (StringFormat sf = new StringFormat()) { sf.Alignment = sf.LineAlignment = StringAlignment.Center; g.DrawString(glyph, f, w, r, sf); }
                else Glyphs.Draw(g, glyph, _glyphS, w, r);
            }
        }

        protected override void OnPaint(PaintEventArgs e) { Paint(e.Graphics); }

        public void Paint(Graphics g)
        {
            Hq(g); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _rows.Clear();
            float w = ClientSize.Width;
            using (Pen border = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(150, 235, 235, 245)))
            using (SolidBrush faint = new SolidBrush(Color.FromArgb(110, 235, 235, 245)))
            {
                // kolom pencarian
                Glyphs.Draw(g, Glyphs.Search, _glyph, soft, new RectangleF(Ui.Pf(14), 0, Ui.Pf(30), BarH));
                float tx = Ui.Pf(52), ty = (BarH - _qf.GetHeight(g)) / 2f;
                if (_q.Length == 0) g.DrawString(L.T("Pencarian Spotlight"), _qf, faint, tx, ty);
                else g.DrawString(_q, _qf, white, tx, ty);
                if (_caret && ActiveForm == this)
                {
                    float cw = _q.Length == 0 ? 0 : MeasureW(g, _q, _qf);
                    using (Pen cp = new Pen(Color.FromArgb(230, 10, 132, 255), Ui.Pf(2))) g.DrawLine(cp, tx + cw + Ui.Pf(1), BarH * 0.28f, tx + cw + Ui.Pf(1), BarH * 0.72f);
                }
                List<SpotItem> l = All();
                if (l.Count == 0) return;
                using (Pen sep = new Pen(Color.FromArgb(45, 255, 255, 255))) g.DrawLine(sep, 0, BarH - 0.5f, w, BarH - 0.5f);
                if (_sel >= l.Count) _sel = l.Count - 1;

                float y = BarH + Ui.Pf(6); string last = null;
                for (int i = 0; i < l.Count; i++)
                {
                    SpotItem it = l[i];
                    string sec = Section(it);
                    if (sec != last) { g.DrawString(sec.ToUpper(L.Culture), _hf, faint, Ui.Pf(18), y + Ui.Pf(5)); y += Ui.Pf(22); last = sec; }
                    RectangleF row = new RectangleF(Ui.Pf(8), y, w - Ui.Pf(16), RowH);
                    _rows.Add(row);
                    if (i == _sel)
                        using (GraphicsPath rp = Glass.Round(row, Ui.Pf(9)))
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(215, 10, 110, 230))) g.FillPath(hb, rp);
                    RectangleF ic = new RectangleF(row.X + Ui.Pf(8), row.Y + (RowH - Ui.Pf(28)) / 2f, Ui.Pf(28), Ui.Pf(28));
                    if (it.Kind == "calc") GlyphTile(g, ic, "=", Color.FromArgb(255, 160, 40), Color.FromArgb(230, 110, 20));
                    else if (it.Kind == "setting") GlyphTile(g, ic, Glyphs.Gear, Color.FromArgb(140, 146, 160), Color.FromArgb(90, 96, 110));
                    else if (it.Kind == "web") GlyphTile(g, ic, Glyphs.Search, Color.FromArgb(70, 150, 255), Color.FromArgb(20, 100, 220));
                    else
                    {
                        Bitmap b = IconFor(it);
                        if (b != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(b, ic); }
                        else GlyphTile(g, ic, it.Kind == "folder" ? "" : "", Color.FromArgb(120, 170, 255), Color.FromArgb(60, 110, 220));
                    }
                    bool selRow = i == _sel;
                    float x = ic.Right + Ui.Pf(12), maxTitle = row.Right - x - Ui.Pf(12);
                    string sub = it.Sub ?? "";
                    float subW = sub.Length == 0 ? 0 : Math.Min(MeasureW(g, sub, _sf), maxTitle * 0.5f);
                    float titleW = Math.Min(MeasureW(g, it.Title, _tf), maxTitle - subW - Ui.Pf(14));
                    DrawClip(g, it.Title, _tf, white, x, row.Y + (RowH - _tf.GetHeight(g)) / 2f, titleW);
                    if (sub.Length > 0)
                        using (SolidBrush sb = new SolidBrush(selRow ? Color.FromArgb(215, 255, 255, 255) : Color.FromArgb(140, 235, 235, 245)))
                            DrawClip(g, sub, _sf, sb, row.Right - Ui.Pf(12) - subW, row.Y + (RowH - _sf.GetHeight(g)) / 2f, subW);
                    y += RowH;
                }
            }
        }

        private static float MeasureW(Graphics g, string s, Font f)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic)) { sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces; return g.MeasureString(s, f, 4000, sf).Width; }
        }
        private static void DrawClip(Graphics g, string s, Font f, Brush b, float x, float y, float w)
        {
            if (w <= 4) return;
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisPath;
                g.DrawString(s, f, b, new RectangleF(x, y, w + 2, f.GetHeight(g) * 1.3f), sf);
            }
        }

        // uji: LM_SPOT="kueri|kueri2" pada --snapshot
        public static void Snapshot(string dir, string queries)
        {
            using (SpotlightForm f = new SpotlightForm())
            {
                Rectangle s = Screen.PrimaryScreen.Bounds;
                int n = 0;
                foreach (string q in queries.Split('|'))
                {
                    f._q = q; f._items = Spotlight.Quick(q); f.ClearFiles(); f._sel = 0;
                    foreach (string[] fl in Spotlight.Files(q, 6))
                    {
                        SpotItem it = new SpotItem(); it.Kind = Directory.Exists(fl[0]) ? "folder" : "file"; it.Key = "file|" + fl[0]; it.Title = fl[1]; it.Sub = Path.GetDirectoryName(fl[0]);
                        f._files.Add(it);
                    }
                    f.Width = f.W; f.Fit();
                    using (Bitmap b = new Bitmap(f.W, f.Height))
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, b.Width, b.Height), Color.FromArgb(46, 50, 74), Color.FromArgb(28, 24, 44), 90f)) g.FillRectangle(bg, 0, 0, b.Width, b.Height);
                        f.Paint(g);
                        b.Save(Path.Combine(dir, "spotlight-" + (n++) + ".png"), ImageFormat.Png);
                    }
                }
            }
        }
    }
}
