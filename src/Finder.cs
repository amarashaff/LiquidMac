using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  FINDER gaya macOS (pengganti tampilan Explorer, bisa dimatikan)
    // ============================================================
    internal sealed class FinderItem
    {
        public string Name, Path, Type;
        public bool IsDir;
        public long Size;
        public DateTime Modified;
    }

    internal sealed class FinderForm : Form
    {
        // ---------- daftar jendela Finder terbuka ----------
        public static readonly List<FinderForm> All = new List<FinderForm>();
        public static bool Enabled { get { return Settings.Get("LiquidFinder", true); } }

        public static void OpenAt(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            FinderForm f = new FinderForm(path);
            f.Show(); f.Activate();
        }

        public static List<IntPtr> Handles
        {
            get { List<IntPtr> r = new List<IntPtr>(); foreach (FinderForm f in All) if (!f.IsDisposed && f.IsHandleCreated && f.Visible) r.Add(f.Handle); return r; }
        }

        // shell:Downloads dll. -> jalur folder sebenarnya (null bila bukan folder biasa)
        public static string Resolve(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            if (Directory.Exists(target)) return target;
            string t = target.ToLowerInvariant();
            if (t == "shell:downloads") return Known("{374DE290-123F-4565-9164-39C4925E467B}");
            if (t == "shell:mycomputerfolder" || t.StartsWith("shell:recyclebin") || t.StartsWith("shell:network") || t.StartsWith("shell:appsfolder")) return null;
            return null;
        }

        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, int flags, IntPtr token, out IntPtr path);
        private static string Known(string guid)
        {
            Guid g = new Guid(guid); IntPtr p;
            if (SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out p) != 0) return null;
            string s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); return s;
        }

        // ---------- keadaan ----------
        private string _path;
        private readonly List<string> _back = new List<string>(), _fwd = new List<string>();
        private List<FinderItem> _items = new List<FinderItem>(), _view = new List<FinderItem>();
        private readonly HashSet<string> _sel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _anchor = -1, _hoverIdx = -1;
        private string _mode = Settings.GetS("FinderView", "icon");      // icon | list
        private string _sortKey = "name"; private bool _sortDesc;
        private float _scroll, _scrollT; private float _contentH;
        private string _query = "";
        private FileSystemWatcher _fw;
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();
        private bool _dirty;
        private string _hoverSide, _hoverTool;
        private int _hoverLight = -1;
        private TextBox _search, _rename;
        private string _renamePath;
        private string _qlPath; private Image _qlImg; private string _qlText;   // Quick Look
        private Point _downPt; private bool _down; private int _downIdx = -1;

        // --- efek ---
        private DateTime _navAt = DateTime.MinValue, _qlAt = DateTime.MinValue, _lastThumbAt = DateTime.MinValue;
        private float _hoverA; private int _hoverShown = -1;
        private readonly Dictionary<string, DateTime> _thumbAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static float EaseOut(double t) { t = Math.Max(0, Math.Min(1, t)); return (float)(1 - Math.Pow(1 - t, 3)); }
        private float NavT { get { return EaseOut((DateTime.Now - _navAt).TotalMilliseconds / 240.0); } }
        private float QlT { get { return EaseOut((DateTime.Now - _qlAt).TotalMilliseconds / 190.0); } }

        // --- tampilan kolom ---
        private string _colRoot;
        private readonly Dictionary<string, List<FinderItem>> _colCache = new Dictionary<string, List<FinderItem>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _colScroll = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, RectangleF>> _colHits = new List<KeyValuePair<string, RectangleF>>();
        private readonly List<KeyValuePair<string, RectangleF>> _colAreas = new List<KeyValuePair<string, RectangleF>>();
        private float _colX, _colXT;
        private int ColW { get { return Ui.P(236); } }

        // tata letak
        private readonly int SideW = Ui.P(208), TopH = Ui.P(52), BarH = Ui.P(28);
        private int Pad { get { return WindowState == FormWindowState.Maximized ? Ui.P(8) : 0; } }
        private Rectangle ContentRect { get { int p = Pad; return new Rectangle(p + SideW, p + TopH, ClientSize.Width - 2 * p - SideW, ClientSize.Height - 2 * p - TopH - BarH); } }

        private static readonly Color CSide = Color.FromArgb(38, 38, 44), CMain = Color.FromArgb(28, 28, 32), CTool = Color.FromArgb(34, 34, 40), CLine = Color.FromArgb(58, 58, 66);
        private static readonly Color CText = Color.FromArgb(236, 236, 242), CDim = Color.FromArgb(150, 152, 162), CAccent = Color.FromArgb(10, 132, 255);
        private readonly Font _f = Ui.F(9.5f, FontStyle.Regular), _fb = Ui.F(9.5f, FontStyle.Bold), _fs = Ui.F(8.5f, FontStyle.Regular), _fsb = Ui.F(8f, FontStyle.Bold), _ft = Ui.F(11f, FontStyle.Bold);
        private readonly Font _gl = Glyphs.Font(Ui.Pf(14)), _glS = Glyphs.Font(Ui.Pf(11));

        // thumbnail
        private readonly Dictionary<string, Bitmap> _thumbs = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _thumbQ = new Queue<string>();
        private readonly HashSet<string> _thumbPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Thread _thumbT; private volatile bool _closed;
        private readonly AutoResetEvent _thumbWake = new AutoResetEvent(false);

        public FinderForm(string path)
        {
            All.Add(this);
            Text = "Finder";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Ui.P(1040), Ui.P(640));
            MinimumSize = new Size(Ui.P(620), Ui.P(380));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int n = All.Count - 1;
            Location = new Point(wa.Left + (wa.Width - Width) / 2 + n * Ui.P(28), wa.Top + Math.Max(Ui.P(20), (wa.Height - Height) / 2) + n * Ui.P(28));
            BackColor = CMain;
            KeyPreview = true;
            AllowDrop = true;
            Icon = TrayApp.MakeIcon();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            _search = new TextBox();
            _search.BorderStyle = BorderStyle.None; _search.BackColor = Color.FromArgb(54, 54, 62); _search.ForeColor = CText; _search.Font = _f;
            _search.TextChanged += delegate { _query = _search.Text; ApplyView(); Invalidate(); };
            _search.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { _search.Text = ""; Focus(); e.Handled = true; } if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { Focus(); if (_view.Count > 0) { SelectOnly(0); } e.Handled = true; } };
            Controls.Add(_search);

            _tick.Interval = 16;
            _tick.Tick += delegate { Step(); };
            _tick.Start();

            _thumbT = new Thread(ThumbLoop); _thumbT.IsBackground = true; _thumbT.SetApartmentState(ApartmentState.MTA); _thumbT.Start();
            Navigate(path, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 1; try { DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }      // mode gelap
            v = 2; try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }          // sudut membulat
            MARGINS m = new MARGINS(); m.cyTopHeight = 1;
            try { DwmExtendFrameIntoClientArea(Handle, ref m); } catch { }
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027 /* NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED */);
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Baris judul bawaan dihilangkan, bingkai (resize, snap, bayangan) tetap dari Windows.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0083 /* WM_NCCALCSIZE */ && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x0084 /* WM_NCHITTEST */)
            {
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                int b = Ui.P(6), W = ClientSize.Width, H = ClientSize.Height;
                if (WindowState != FormWindowState.Maximized)
                {
                    bool l = p.X < b, r = p.X >= W - b, t = p.Y < b, bt = p.Y >= H - b;
                    if (t && l) { m.Result = (IntPtr)13; return; }
                    if (t && r) { m.Result = (IntPtr)14; return; }
                    if (bt && l) { m.Result = (IntPtr)16; return; }
                    if (bt && r) { m.Result = (IntPtr)17; return; }
                    if (l) { m.Result = (IntPtr)10; return; }
                    if (r) { m.Result = (IntPtr)11; return; }
                    if (t) { m.Result = (IntPtr)12; return; }
                    if (bt) { m.Result = (IntPtr)15; return; }
                }
                if (p.Y < Pad + TopH && ToolHit(p) == null && LightHit(p) < 0 && !SearchRect.Contains(p)) { m.Result = (IntPtr)2; return; }   // HTCAPTION: seret / snap
                m.Result = (IntPtr)1; return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closed = true; _thumbWake.Set();
            All.Remove(this);
            if (_fw != null) { _fw.Dispose(); _fw = null; }
            _tick.Stop();
            lock (_thumbs) { foreach (Bitmap b in _thumbs.Values) if (b != null) b.Dispose(); _thumbs.Clear(); }
            if (_qlImg != null) _qlImg.Dispose();
            Settings.SetS("FinderView", _mode);
            base.OnFormClosed(e);
            MemoryTrim.Soon();
        }

        // ---------- navigasi ----------
        private void Navigate(string path, bool record)
        {
            try { path = Path.GetFullPath(path); } catch { return; }
            if (!Directory.Exists(path)) return;
            if (record && _path != null && !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)) { _back.Add(_path); _fwd.Clear(); }
            _path = path;
            _sel.Clear(); _anchor = -1; _scroll = _scrollT = 0;
            _navAt = DateTime.Now; _colCache.Clear();
            if (_colRoot == null || !Under(_path, _colRoot)) _colRoot = _path;
            if (_search != null && _search.Text.Length > 0) _search.Text = "";
            CloseQuickLook();
            Load();
            Watch();
            Text = Title(_path) + " — Finder";
            Invalidate();
        }

        private static bool Under(string p, string root)
        {
            string a = p.TrimEnd('\\') + "\\", b = root.TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        private List<string> Chain()
        {
            List<string> l = new List<string>();
            if (_path == null) return l;
            for (DirectoryInfo d = new DirectoryInfo(_path); d != null; d = d.Parent)
            {
                l.Insert(0, d.FullName);
                if (string.Equals(d.FullName.TrimEnd('\\'), (_colRoot ?? _path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
            }
            return l;
        }

        private List<FinderItem> ColList(string dir)
        {
            List<FinderItem> l;
            if (_colCache.TryGetValue(dir, out l)) return l;
            l = new List<FinderItem>();
            try
            {
                foreach (FileSystemInfo fi in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    try
                    {
                        if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        FinderItem it = new FinderItem(); it.Name = fi.Name; it.Path = fi.FullName; it.Modified = fi.LastWriteTime;
                        it.IsDir = (fi.Attributes & FileAttributes.Directory) != 0;
                        if (!it.IsDir) { it.Size = ((FileInfo)fi).Length; string ext = Path.GetExtension(fi.Name); it.Type = ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " berkas" : "Berkas"; } else it.Type = "Folder";
                        l.Add(it);
                    }
                    catch { }
                }
            }
            catch { }
            l.Sort(delegate (FinderItem a, FinderItem b) { if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1; return NaturalCompare(a.Name, b.Name); });
            _colCache[dir] = l;
            return l;
        }

        private static string Title(string p)
        {
            string n = Path.GetFileName(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(n)) { try { DriveInfo d = new DriveInfo(p); return (d.IsReady && d.VolumeLabel.Length > 0 ? d.VolumeLabel : "Disk Lokal") + " (" + p.TrimEnd('\\') + ")"; } catch { return p; } }
            return n;
        }

        private void GoBack() { if (_back.Count == 0) return; string p = _back[_back.Count - 1]; _back.RemoveAt(_back.Count - 1); _fwd.Add(_path); Navigate(p, false); }
        private void GoFwd() { if (_fwd.Count == 0) return; string p = _fwd[_fwd.Count - 1]; _fwd.RemoveAt(_fwd.Count - 1); _back.Add(_path); Navigate(p, false); }
        private void GoUp() { DirectoryInfo d = Directory.GetParent(_path.TrimEnd('\\')); if (d != null) Navigate(d.FullName, true); }

        private void Load()
        {
            List<FinderItem> l = new List<FinderItem>();
            try
            {
                DirectoryInfo di = new DirectoryInfo(_path);
                foreach (FileSystemInfo fi in di.EnumerateFileSystemInfos())
                {
                    try
                    {
                        if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        FinderItem it = new FinderItem();
                        it.Name = fi.Name; it.Path = fi.FullName; it.Modified = fi.LastWriteTime;
                        it.IsDir = (fi.Attributes & FileAttributes.Directory) != 0;
                        if (!it.IsDir) { it.Size = ((FileInfo)fi).Length; string ext = Path.GetExtension(fi.Name); it.Type = ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " berkas" : "Berkas"; }
                        else it.Type = "Folder";
                        if (fi.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) { it.Name = Path.GetFileNameWithoutExtension(fi.Name); it.Type = "Pintasan"; }
                        l.Add(it);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Write("finder load: " + ex.Message); }
            _items = l;
            ApplyView();
        }

        private void ApplyView()
        {
            List<FinderItem> v = _query.Length == 0 ? new List<FinderItem>(_items) : _items.FindAll(delegate (FinderItem i) { return i.Name.IndexOf(_query, StringComparison.CurrentCultureIgnoreCase) >= 0; });
            Comparison<FinderItem> cmp;
            switch (_sortKey)
            {
                case "date": cmp = delegate (FinderItem a, FinderItem b) { return a.Modified.CompareTo(b.Modified); }; break;
                case "size": cmp = delegate (FinderItem a, FinderItem b) { return a.Size.CompareTo(b.Size); }; break;
                case "type": cmp = delegate (FinderItem a, FinderItem b) { return string.Compare(a.Type, b.Type, StringComparison.CurrentCultureIgnoreCase); }; break;
                default: cmp = delegate (FinderItem a, FinderItem b) { return NaturalCompare(a.Name, b.Name); }; break;
            }
            v.Sort(delegate (FinderItem a, FinderItem b)
            {
                if (a.IsDir != b.IsDir && _sortKey == "name") return a.IsDir ? -1 : 1;
                int c = cmp(a, b); if (c == 0) c = NaturalCompare(a.Name, b.Name);
                return _sortDesc ? -c : c;
            });
            _view = v;
            _hoverIdx = -1;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrCmpLogicalW(string a, string b);
        private static int NaturalCompare(string a, string b) { return StrCmpLogicalW(a ?? "", b ?? ""); }

        private void Watch()
        {
            if (_fw != null) { _fw.Dispose(); _fw = null; }
            try
            {
                _fw = new FileSystemWatcher(_path); _fw.IncludeSubdirectories = false;
                _fw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite;
                FileSystemEventHandler h = delegate { _dirty = true; };
                _fw.Created += h; _fw.Deleted += h; _fw.Changed += h; _fw.Renamed += delegate { _dirty = true; };
                _fw.EnableRaisingEvents = true;
            }
            catch { }
        }

        private int _dirtyWait;
        private void Step()
        {
            bool inv = false;
            if (_dirty) { if (++_dirtyWait > 15) { _dirty = false; _dirtyWait = 0; HashSet<string> keep = new HashSet<string>(_sel, StringComparer.OrdinalIgnoreCase); Load(); foreach (string s in keep) if (_items.Exists(delegate (FinderItem i) { return string.Equals(i.Path, s, StringComparison.OrdinalIgnoreCase); })) _sel.Add(s); inv = true; } }
            if (Math.Abs(_scroll - _scrollT) > 0.5f) { _scroll += (_scrollT - _scroll) * 0.28f; inv = true; } else if (_scroll != _scrollT) { _scroll = _scrollT; inv = true; }
            if (_thumbsReady) { _thumbsReady = false; inv = true; }
            if ((DateTime.Now - _lastThumbAt).TotalMilliseconds < 260) inv = true;          // thumbnail memudar masuk
            if ((DateTime.Now - _navAt).TotalMilliseconds < 280) inv = true;               // transisi pindah folder
            if (_qlPath != null && (DateTime.Now - _qlAt).TotalMilliseconds < 230) inv = true;
            if (_hoverShown != _hoverIdx) { _hoverShown = _hoverIdx; _hoverA = 0; }
            if (_hoverIdx >= 0 && _hoverA < 1) { _hoverA = Math.Min(1, _hoverA + 0.18f); inv = true; }
            if (Math.Abs(_colX - _colXT) > 0.5f) { _colX += (_colXT - _colX) * 0.22f; inv = true; } else _colX = _colXT;
            if (inv) Invalidate();
        }

        // ---------- thumbnail (thread STA terpisah) ----------
        private volatile bool _thumbsReady;
        private int ThumbSize { get { return _mode == "icon" ? Ui.P(64) : Ui.P(20); } }

        private Bitmap Thumb(FinderItem it) { return ThumbOf(it, ThumbSize); }

        private Bitmap ThumbOf(FinderItem it, int size)
        {
            if (it.IsDir) return null;   // folder digambar sendiri (biru gaya macOS)
            string key = it.Path + "|" + size;
            lock (_thumbs)
            {
                Bitmap b;
                if (_thumbs.TryGetValue(key, out b)) return b;
                if (_thumbPending.Add(key)) { _thumbQ.Enqueue(key); _thumbWake.Set(); }
            }
            return null;
        }

        private void ThumbLoop()
        {
            while (!_closed)
            {
                string key = null;
                lock (_thumbs) if (_thumbQ.Count > 0) key = _thumbQ.Dequeue();
                if (key == null) { _thumbWake.WaitOne(500); continue; }
                int bar = key.LastIndexOf('|'); string path = key.Substring(0, bar); int sz = int.Parse(key.Substring(bar + 1));
                Bitmap b = null;
                try { b = ShellImage.Get(path, sz * 2, 0); } catch { }
                if (b != null && (b.Width > sz || b.Height > sz))
                {
                    float k = Math.Min((float)sz / b.Width, (float)sz / b.Height);
                    Bitmap s = new Bitmap(Math.Max(1, (int)(b.Width * k)), Math.Max(1, (int)(b.Height * k)), PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(s)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(b, 0, 0, s.Width, s.Height); }
                    b.Dispose(); b = s;
                }
                lock (_thumbs) { _thumbs[key] = b; _thumbPending.Remove(key); _thumbAt[key] = DateTime.Now; }
                _lastThumbAt = DateTime.Now;
                _thumbsReady = true;
            }
        }

        // ---------- geometri ----------
        private int Cols { get { return Math.Max(1, (ContentRect.Width - Ui.P(16)) / Ui.P(112)); } }
        private int CellW { get { return (ContentRect.Width - Ui.P(16)) / Cols; } }
        private int CellH { get { return Ui.P(112); } }
        private int RowH { get { return Ui.P(26); } }
        private int ListTop { get { return _mode == "column" ? ContentRect.Top : ContentRect.Top + Ui.P(26); } }
        private float LastColX;   // diperbarui saat kolom digambar

        private RectangleF ItemRect(int i)
        {
            Rectangle c = ContentRect;
            if (_mode == "icon")
            {
                int col = i % Cols, row = i / Cols;
                return new RectangleF(c.Left + Ui.P(8) + col * CellW, c.Top + Ui.P(10) + row * CellH - _scroll, CellW, CellH);
            }
            if (_mode == "column") return new RectangleF(LastColX + Ui.P(4), c.Top + Ui.P(6) + i * RowH - _scroll, ColW - Ui.P(8), RowH);
            return new RectangleF(c.Left, ListTop + i * RowH - _scroll, c.Width, RowH);
        }

        private float ContentHeight { get { return _mode == "icon" ? Ui.P(20) + ((_view.Count + Cols - 1) / Cols) * CellH : _mode == "column" ? Ui.P(12) + _view.Count * RowH : Ui.P(30) + _view.Count * RowH; } }

        private int HitItem(Point p)
        {
            Rectangle c = ContentRect;
            if (!c.Contains(p)) return -1;
            if (_mode == "list" && p.Y < ListTop) return -1;
            if (_mode == "column") { float lx = LastColX; if (p.X < lx || p.X > lx + ColW) return -1; }
            for (int i = 0; i < _view.Count; i++)
            {
                RectangleF r = ItemRect(i);
                if (r.Bottom < c.Top || r.Top > c.Bottom) continue;
                if (_mode == "icon") { RectangleF hit = new RectangleF(r.X + Ui.P(10), r.Y + Ui.P(2), r.Width - Ui.P(20), r.Height - Ui.P(6)); if (hit.Contains(p)) return i; }
                else if (r.Contains(p)) return i;
            }
            return -1;
        }

        private void DrawThumb(Graphics g, FinderItem it, int size, Bitmap t, float x, float y)
        {
            DateTime at; double ms = 999;
            lock (_thumbs) if (_thumbAt.TryGetValue(it.Path + "|" + size, out at)) ms = (DateTime.Now - at).TotalMilliseconds;
            float a = (float)Math.Min(1, ms / 200.0);
            if (a >= 0.999f) { g.DrawImage(t, x, y, t.Width, t.Height); return; }
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = a; ia.SetColorMatrix(cm);
                g.DrawImage(t, new Rectangle((int)x, (int)y, t.Width, t.Height), 0, 0, t.Width, t.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private Rectangle SearchRect { get { int p = Pad; return new Rectangle(ClientSize.Width - p - Ui.P(232), p + Ui.P(13), Ui.P(216), Ui.P(26)); } }

        private RectangleF LightRect(int i) { int p = Pad; float d = Ui.Pf(13); return new RectangleF(p + Ui.P(18) + i * Ui.Pf(21), p + (TopH - d) / 2f, d, d); }
        private int LightHit(Point p) { for (int i = 0; i < 3; i++) { RectangleF r = LightRect(i); r.Inflate(Ui.Pf(3), Ui.Pf(3)); if (r.Contains(p)) return i; } return -1; }

        private readonly Dictionary<string, RectangleF> _tools = new Dictionary<string, RectangleF>();
        private string ToolHit(Point p) { foreach (KeyValuePair<string, RectangleF> kv in _tools) if (kv.Value.Contains(p)) return kv.Key; return null; }

        private readonly List<KeyValuePair<string, RectangleF>> _sideHits = new List<KeyValuePair<string, RectangleF>>();
        private readonly List<KeyValuePair<string, RectangleF>> _crumbs = new List<KeyValuePair<string, RectangleF>>();
        private readonly Dictionary<string, RectangleF> _heads = new Dictionary<string, RectangleF>();

        // ---------- menggambar ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int p = Pad, W = ClientSize.Width, H = ClientSize.Height;
            using (SolidBrush b = new SolidBrush(CMain)) g.FillRectangle(b, 0, 0, W, H);
            DrawSidebar(g, p, H);
            DrawToolbar(g, p, W);
            DrawContent(g);
            DrawPathBar(g, p, W, H);
            if (_qlPath != null) DrawQuickLook(g, W, H);
        }

        private static readonly string[,] Favs = {
            { "Rumah", "", "home" }, { "Desktop", "", "desktop" }, { "Dokumen", "", "docs" },
            { "Unduhan", "", "dl" }, { "Gambar", "", "pics" }, { "Musik", "", "music" }, { "Video", "", "video" } };

        private string FavPath(string id)
        {
            switch (id)
            {
                case "home": return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                case "desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                case "docs": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "dl": return Known("{374DE290-123F-4565-9164-39C4925E467B}");
                case "pics": return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                case "music": return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                case "video": return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            }
            return id;
        }

        private void DrawSidebar(Graphics g, int p, int H)
        {
            _sideHits.Clear();
            using (SolidBrush b = new SolidBrush(CSide)) g.FillRectangle(b, p, p, SideW, H - 2 * p);
            using (Pen ln = new Pen(CLine)) g.DrawLine(ln, p + SideW - 1, p, p + SideW - 1, H - p);
            // traffic light
            Color[] on = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            string[] gly = { "", "", WindowState == FormWindowState.Maximized ? "" : "" };
            bool act = Form.ActiveForm == this || _hoverLight >= 0;
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = LightRect(i);
                using (SolidBrush lb = new SolidBrush(act ? on[i] : Color.FromArgb(80, 80, 88))) g.FillEllipse(lb, r);
                if (_hoverLight >= 0) using (Font f = Glyphs.Font(Ui.Pf(8))) using (SolidBrush gb = new SolidBrush(Color.FromArgb(190, 60, 20, 10))) Glyphs.Draw(g, gly[i], f, gb, r);
            }
            float y = p + TopH + Ui.Pf(4);
            Section(g, "Favorit", p, ref y);
            for (int i = 0; i < Favs.GetLength(0); i++) SideItem(g, Favs[i, 0], Favs[i, 1], FavPath(Favs[i, 2]), p, ref y);
            y += Ui.Pf(8);
            Section(g, "Lokasi", p, ref y);
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable && d.DriveType != DriveType.Network)) continue;
                    string label = (d.VolumeLabel.Length > 0 ? d.VolumeLabel : "Disk Lokal") + " (" + d.Name.TrimEnd('\\') + ")";
                    SideItem(g, label, d.DriveType == DriveType.Removable ? "" : "", d.RootDirectory.FullName, p, ref y);
                }
                catch { }
            }
            SideItem(g, "Tong Sampah", "", "shell:RecycleBinFolder", p, ref y);
        }

        private void Section(Graphics g, string t, int p, ref float y)
        {
            using (SolidBrush b = new SolidBrush(CDim)) g.DrawString(t, _fsb, b, p + Ui.Pf(16), y);
            y += Ui.Pf(22);
        }

        private void SideItem(Graphics g, string name, string glyph, string target, int p, ref float y)
        {
            RectangleF r = new RectangleF(p + Ui.Pf(8), y, SideW - Ui.Pf(16), Ui.Pf(26));
            bool cur = target != null && _path != null && string.Equals(target.TrimEnd('\\'), _path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (cur || _hoverSide == target)
                using (GraphicsPath gp = Glass.Round(r, Ui.Pf(6))) using (SolidBrush b = new SolidBrush(cur ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(30, 255, 255, 255))) g.FillPath(b, gp);
            using (SolidBrush ib = new SolidBrush(CAccent)) Glyphs.Draw(g, glyph, _glS, ib, new RectangleF(r.X + Ui.Pf(6), r.Y, Ui.Pf(20), r.Height));
            using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(Fit(g, name, _f, r.Width - Ui.Pf(36)), _f, tb, r.X + Ui.Pf(32), r.Y + (r.Height - _f.GetHeight(g)) / 2f);
            _sideHits.Add(new KeyValuePair<string, RectangleF>(target, r));
            y += Ui.Pf(28);
        }

        private void DrawToolbar(Graphics g, int p, int W)
        {
            _tools.Clear();
            Rectangle t = new Rectangle(p + SideW, p, W - 2 * p - SideW, TopH);
            using (SolidBrush b = new SolidBrush(CTool)) g.FillRectangle(b, t);
            using (Pen ln = new Pen(CLine)) g.DrawLine(ln, t.Left, t.Bottom - 1, t.Right, t.Bottom - 1);
            float x = t.Left + Ui.Pf(12), cy = t.Top + TopH / 2f;
            ToolBtn(g, "back", "", ref x, cy, _back.Count > 0);
            ToolBtn(g, "fwd", "", ref x, cy, _fwd.Count > 0);
            x += Ui.Pf(8);
            using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(Fit(g, Title(_path ?? ""), _ft, Ui.Pf(320)), _ft, tb, x, cy - _ft.GetHeight(g) / 2f);
            // mode tampilan (segmen)
            Rectangle s = SearchRect;
            float segW = Ui.Pf(96), segH = Ui.Pf(26);
            RectangleF seg = new RectangleF(s.Left - Ui.Pf(16) - segW, cy - segH / 2f, segW, segH);
            using (GraphicsPath sp = Glass.Round(seg, Ui.Pf(6))) using (SolidBrush sb = new SolidBrush(Color.FromArgb(54, 54, 62))) g.FillPath(sb, sp);
            string[] modes = { "icon", "list", "column" }; string[] mg = { "\uF0E2", "\uE8FD", null };
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = new RectangleF(seg.X + i * segW / 3f, seg.Y, segW / 3f, segH);
                if (_mode == modes[i]) { RectangleF rr = r; rr.Inflate(-Ui.Pf(2), -Ui.Pf(2)); using (GraphicsPath sp = Glass.Round(rr, Ui.Pf(5))) using (SolidBrush ab = new SolidBrush(Color.FromArgb(96, 96, 108))) g.FillPath(ab, sp); }
                else if (_hoverTool == "mode_" + modes[i]) { RectangleF rr = r; rr.Inflate(-Ui.Pf(2), -Ui.Pf(2)); using (GraphicsPath sp = Glass.Round(rr, Ui.Pf(5))) using (SolidBrush ab = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(ab, sp); }
                if (mg[i] != null) using (SolidBrush ib = new SolidBrush(CText)) Glyphs.Draw(g, mg[i], _glS, ib, r);
                else using (Pen pn = new Pen(CText, Ui.Pf(1.3)))   // ikon kolom: tiga kolom
                {
                    float w = Ui.Pf(14), h = Ui.Pf(11), x0 = r.X + (r.Width - w) / 2f, y0 = r.Y + (r.Height - h) / 2f;
                    g.DrawRectangle(pn, x0, y0, w, h); g.DrawLine(pn, x0 + w / 3f, y0, x0 + w / 3f, y0 + h); g.DrawLine(pn, x0 + 2 * w / 3f, y0, x0 + 2 * w / 3f, y0 + h);
                }
                _tools["mode_" + modes[i]] = r;
            }
            // kolom cari
            RectangleF sr = s;
            using (GraphicsPath sp = Glass.Round(sr, Ui.Pf(7))) using (SolidBrush sb = new SolidBrush(Color.FromArgb(54, 54, 62))) g.FillPath(sb, sp);
            using (SolidBrush ib = new SolidBrush(CDim)) Glyphs.Draw(g, Glyphs.Search, _glS, ib, new RectangleF(sr.X + Ui.Pf(4), sr.Y, Ui.Pf(22), sr.Height));
            int th = _search.PreferredHeight;
            Rectangle tr = new Rectangle(s.Left + Ui.P(28), s.Top + (s.Height - th) / 2 + 1, s.Width - Ui.P(36), th);
            if (_search.Bounds != tr) _search.Bounds = tr;
            if (_search.Text.Length == 0 && !_search.Focused)
                using (SolidBrush pb = new SolidBrush(CDim)) g.DrawString("Cari", _f, pb, tr.X, tr.Y);
        }

        private void ToolBtn(Graphics g, string id, string glyph, ref float x, float cy, bool enabled)
        {
            RectangleF r = new RectangleF(x, cy - Ui.Pf(14), Ui.Pf(30), Ui.Pf(28));
            if (_hoverTool == id && enabled) using (GraphicsPath gp = Glass.Round(r, Ui.Pf(6))) using (SolidBrush hb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(hb, gp);
            using (SolidBrush b = new SolidBrush(enabled ? CText : Color.FromArgb(90, 90, 98))) Glyphs.Draw(g, glyph, _gl, b, r);
            _tools[id] = r;
            x += r.Width + Ui.Pf(2);
        }

        private void DrawContent(Graphics g)
        {
            Rectangle c = ContentRect;
            Region old = g.Clip; g.SetClip(c);
            _contentH = ContentHeight;
            float nt = NavT;
            GraphicsState gs = g.Save();
            if (nt < 1) g.TranslateTransform(_mode == "column" ? 0 : 0, (1 - nt) * Ui.Pf(12));
            if (_mode == "list") DrawList(g, c); else if (_mode == "column") DrawColumns(g, c); else DrawIcons(g, c);
            g.Restore(gs);
            if (nt < 1) using (SolidBrush fade = new SolidBrush(Color.FromArgb((int)(220 * (1 - nt)), CMain))) g.FillRectangle(fade, c);   // memudar masuk
            if (_view.Count == 0)
                using (SolidBrush b = new SolidBrush(CDim)) using (StringFormat sf = new StringFormat())
                { sf.Alignment = sf.LineAlignment = StringAlignment.Center; g.DrawString(_query.Length > 0 ? "Tidak ada yang cocok dengan “" + _query + "”" : "Folder ini kosong", _f, b, c, sf); }
            g.Clip = old;
            // bilah gulir tipis
            float viewH = _mode == "list" ? c.Height - Ui.P(26) : c.Height;
            if (_contentH > viewH + 1 && _mode != "column")
            {
                float k = viewH / _contentH, bh = Math.Max(Ui.Pf(30), viewH * k), by = (_mode == "list" ? ListTop : c.Top) + (viewH - bh) * (_scroll / Math.Max(1, _contentH - viewH));
                using (GraphicsPath sp = Glass.Round(new RectangleF(c.Right - Ui.Pf(7), by, Ui.Pf(4), bh), Ui.Pf(2))) using (SolidBrush sb = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillPath(sb, sp);
            }
        }

        private void DrawIcons(Graphics g, Rectangle c)
        {
            using (StringFormat sf = new StringFormat())
            using (SolidBrush tb = new SolidBrush(CText)) using (SolidBrush selTb = new SolidBrush(Color.White))
            {
                sf.Alignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
                for (int i = 0; i < _view.Count; i++)
                {
                    RectangleF r = ItemRect(i);
                    if (r.Bottom < c.Top || r.Top > c.Bottom) continue;
                    FinderItem it = _view[i];
                    bool sel = _sel.Contains(it.Path);
                    float isz = Ui.Pf(64);
                    RectangleF ir = new RectangleF(r.X + (r.Width - isz) / 2f, r.Y + Ui.Pf(6), isz, isz);
                    if (sel || i == _hoverIdx)
                        using (GraphicsPath gp = Glass.Round(new RectangleF(ir.X - Ui.Pf(6), ir.Y - Ui.Pf(4), isz + Ui.Pf(12), isz + Ui.Pf(8)), Ui.Pf(8)))
                        using (SolidBrush hb = new SolidBrush(sel ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb((int)(26 * _hoverA), 255, 255, 255))) g.FillPath(hb, gp);
                    Bitmap t = Thumb(it);
                    if (t != null) DrawThumb(g, it, ThumbSize, t, ir.X + (isz - t.Width) / 2f, ir.Y + (isz - t.Height) / 2f);
                    else DrawFallbackIcon(g, it, ir);
                    RectangleF lr = new RectangleF(r.X + Ui.Pf(6), ir.Bottom + Ui.Pf(8), r.Width - Ui.Pf(12), _f.GetHeight(g) * 2 + 2);
                    string nm = it.Name;
                    if (sel)
                    {
                        SizeF ns = g.MeasureString(nm, _f, (int)lr.Width, sf);
                        RectangleF pill = new RectangleF(r.X + (r.Width - Math.Min(lr.Width, ns.Width + Ui.Pf(10))) / 2f, lr.Y - Ui.Pf(1), Math.Min(lr.Width, ns.Width + Ui.Pf(10)), Math.Min(lr.Height, ns.Height) + Ui.Pf(2));
                        using (GraphicsPath gp = Glass.Round(pill, Ui.Pf(4))) using (SolidBrush ab = new SolidBrush(CAccent)) g.FillPath(ab, gp);
                    }
                    if (_renamePath != it.Path) g.DrawString(nm, _f, sel ? selTb : tb, lr, sf);
                }
            }
        }

        // ---------- tampilan kolom ----------
        private void DrawColumns(Graphics g, Rectangle c)
        {
            _colHits.Clear(); _colAreas.Clear();
            List<string> chain = Chain();
            LastColX = c.Left + (chain.Count - 1) * ColW - _colX;
            FinderItem one = _sel.Count == 1 ? Primary() : null;
            bool preview = one != null && !one.IsDir;
            int cols = chain.Count + (preview ? 1 : 0);
            _colXT = Math.Max(0, cols * ColW + (preview ? Ui.P(60) : 0) - c.Width);
            for (int k = 0; k < chain.Count; k++)
            {
                string dir = chain[k];
                float x = c.Left + k * ColW - _colX;
                if (x + ColW < c.Left || x > c.Right) continue;
                RectangleF col = new RectangleF(x, c.Top, ColW, c.Height);
                _colAreas.Add(new KeyValuePair<string, RectangleF>(dir, col));
                using (Pen ln = new Pen(CLine)) g.DrawLine(ln, col.Right - 1, c.Top, col.Right - 1, c.Bottom);
                bool last = k == chain.Count - 1;
                List<FinderItem> items = last ? _view : ColList(dir);
                string next = last ? null : chain[k + 1];
                float sy = last ? _scroll : ColScrollFor(dir, items, next, c.Height);
                for (int i = 0; i < items.Count; i++)
                {
                    RectangleF r = new RectangleF(x + Ui.Pf(4), c.Top + Ui.Pf(6) + i * RowH - sy, ColW - Ui.Pf(8), RowH);
                    if (r.Bottom < c.Top || r.Top > c.Bottom) continue;
                    FinderItem it = items[i];
                    bool onPath = next != null && string.Equals(it.Path, next, StringComparison.OrdinalIgnoreCase);
                    bool sel = last && _sel.Contains(it.Path);
                    if (sel || onPath)
                        using (GraphicsPath gp = Glass.Round(r, Ui.Pf(5))) using (SolidBrush sb = new SolidBrush(sel ? CAccent : Color.FromArgb(70, 255, 255, 255))) g.FillPath(sb, gp);
                    else if (last && i == _hoverIdx)
                        using (GraphicsPath gp = Glass.Round(r, Ui.Pf(5))) using (SolidBrush hb = new SolidBrush(Color.FromArgb((int)(24 * _hoverA), 255, 255, 255))) g.FillPath(hb, gp);
                    RectangleF ir = new RectangleF(r.X + Ui.Pf(6), r.Y + (r.Height - Ui.Pf(18)) / 2f, Ui.Pf(18), Ui.Pf(18));
                    Bitmap t = ThumbOf(it, Ui.P(20));
                    if (t != null) DrawThumb(g, it, Ui.P(20), t, ir.X + (ir.Width - t.Width) / 2f, ir.Y + (ir.Height - t.Height) / 2f); else DrawFallbackIcon(g, it, ir);
                    using (SolidBrush tb = new SolidBrush(sel ? Color.White : CText))
                        if (!(last && _renamePath == it.Path)) g.DrawString(Fit(g, it.Name, _f, r.Width - Ui.Pf(48)), _f, tb, r.X + Ui.Pf(30), r.Y + (r.Height - _f.GetHeight(g)) / 2f);
                    if (it.IsDir) using (SolidBrush cb = new SolidBrush(sel || onPath ? Color.White : CDim)) Glyphs.Draw(g, "\uE76C", Glyphs.Font(Ui.Pf(9)), cb, new RectangleF(r.Right - Ui.Pf(18), r.Y, Ui.Pf(14), r.Height));
                    if (!last) _colHits.Add(new KeyValuePair<string, RectangleF>(it.Path, r));
                }
                if (items.Count == 0)
                    using (SolidBrush db = new SolidBrush(CDim)) using (StringFormat sf = new StringFormat()) { sf.Alignment = StringAlignment.Center; g.DrawString("Kosong", _fs, db, new RectangleF(x, c.Top + Ui.Pf(20), ColW, Ui.Pf(20)), sf); }
            }
            if (preview)
            {
                float x = c.Left + chain.Count * ColW - _colX;
                RectangleF pv = new RectangleF(x, c.Top, Math.Max(ColW, c.Right - x), c.Height);
                float sz = Math.Min(pv.Width - Ui.Pf(40), Ui.Pf(200));
                Bitmap big = ThumbOf(one, Ui.P(200));
                RectangleF ir = new RectangleF(pv.X + (pv.Width - sz) / 2f, pv.Y + Ui.Pf(26), sz, sz);
                if (big != null) { float k2 = Math.Min(sz / big.Width, sz / big.Height); float w = big.Width * k2, h = big.Height * k2; g.DrawImage(big, ir.X + (sz - w) / 2f, ir.Y + (sz - h) / 2f, w, h); }
                else DrawFallbackIcon(g, one, ir);
                using (StringFormat sf = new StringFormat()) using (SolidBrush tb = new SolidBrush(CText)) using (SolidBrush db = new SolidBrush(CDim))
                {
                    sf.Alignment = StringAlignment.Center;
                    float ty = ir.Bottom + Ui.Pf(14);
                    g.DrawString(one.Name, _fb, tb, new RectangleF(pv.X + Ui.Pf(10), ty, pv.Width - Ui.Pf(20), Ui.Pf(40)), sf);
                    string info = one.Type + " \u00B7 " + Human(one.Size) + "\nDiubah " + one.Modified.ToString("d MMMM yyyy HH.mm", new System.Globalization.CultureInfo("id-ID"));
                    g.DrawString(info, _fs, db, new RectangleF(pv.X + Ui.Pf(10), ty + Ui.Pf(42), pv.Width - Ui.Pf(20), Ui.Pf(40)), sf);
                }
            }
        }

        // Kolom induk menggulir otomatis agar folder yang sedang dibuka terlihat (kecuali digulir manual).
        private float ColScrollFor(string dir, List<FinderItem> items, string next, float h)
        {
            float v;
            if (_colScroll.TryGetValue(dir, out v)) return v;
            int idx = next == null ? -1 : items.FindIndex(delegate (FinderItem i) { return string.Equals(i.Path, next, StringComparison.OrdinalIgnoreCase); });
            float want = idx < 0 ? 0 : Math.Max(0, idx * RowH - h / 2f + RowH);
            return Math.Max(0, Math.Min(want, Math.Max(0, items.Count * RowH + Ui.P(12) - h)));
        }

        private void DrawFallbackIcon(Graphics g, FinderItem it, RectangleF r)
        {
            if (it.IsDir)
            {
                RectangleF f = new RectangleF(r.X + r.Width * 0.1f, r.Y + r.Height * 0.22f, r.Width * 0.8f, r.Height * 0.6f);
                using (GraphicsPath tab = Glass.Round(new RectangleF(f.X, f.Y - r.Height * 0.08f, f.Width * 0.4f, r.Height * 0.2f), Ui.Pf(3))) using (SolidBrush b = new SolidBrush(Color.FromArgb(80, 160, 245))) g.FillPath(b, tab);
                using (GraphicsPath fp = Glass.Round(f, Ui.Pf(5))) using (LinearGradientBrush lb = new LinearGradientBrush(f, Color.FromArgb(120, 190, 255), Color.FromArgb(60, 145, 240), 90f)) g.FillPath(lb, fp);
            }
            else
                using (GraphicsPath fp = Glass.Round(new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.1f, r.Width * 0.6f, r.Height * 0.8f), Ui.Pf(4))) using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, fp);
        }

        private void DrawList(Graphics g, Rectangle c)
        {
            _heads.Clear();
            // kolom
            float nameW = c.Width - Ui.Pf(420);
            string[] heads = { "Nama", "Tanggal Diubah", "Ukuran", "Jenis" };
            string[] keys = { "name", "date", "size", "type" };
            float[] xs = { c.Left + Ui.Pf(14), c.Left + nameW, c.Left + nameW + Ui.Pf(170), c.Left + nameW + Ui.Pf(270) };
            float[] ws = { nameW - Ui.Pf(20), Ui.Pf(160), Ui.Pf(90), Ui.Pf(140) };
            for (int i = 0; i < _view.Count; i++)
            {
                RectangleF r = ItemRect(i);
                if (r.Bottom < ListTop || r.Top > c.Bottom) continue;
                FinderItem it = _view[i];
                bool sel = _sel.Contains(it.Path);
                if (sel) using (SolidBrush sb = new SolidBrush(CAccent)) g.FillRectangle(sb, r.X + Ui.Pf(6), r.Y + 1, r.Width - Ui.Pf(12), r.Height - 2);
                else if (i % 2 == 1) using (SolidBrush zb = new SolidBrush(Color.FromArgb(10, 255, 255, 255))) g.FillRectangle(zb, r.X + Ui.Pf(6), r.Y + 1, r.Width - Ui.Pf(12), r.Height - 2);
                if (!sel && i == _hoverIdx) using (SolidBrush hb = new SolidBrush(Color.FromArgb((int)(22 * _hoverA), 255, 255, 255))) g.FillRectangle(hb, r.X + Ui.Pf(6), r.Y + 1, r.Width - Ui.Pf(12), r.Height - 2);
                float ty = r.Y + (r.Height - _f.GetHeight(g)) / 2f;
                Bitmap t = Thumb(it);
                RectangleF ir = new RectangleF(xs[0], r.Y + (r.Height - Ui.Pf(20)) / 2f, Ui.Pf(20), Ui.Pf(20));
                if (t != null) DrawThumb(g, it, ThumbSize, t, ir.X + (ir.Width - t.Width) / 2f, ir.Y + (ir.Height - t.Height) / 2f); else DrawFallbackIcon(g, it, ir);
                using (SolidBrush tb = new SolidBrush(sel ? Color.White : CText)) using (SolidBrush db = new SolidBrush(sel ? Color.FromArgb(225, 235, 255) : CDim))
                {
                    if (_renamePath != it.Path) g.DrawString(Fit(g, it.Name, _f, ws[0] - Ui.Pf(30)), _f, tb, xs[0] + Ui.Pf(28), ty);
                    g.DrawString(it.Modified.ToString("d MMM yyyy HH.mm", new System.Globalization.CultureInfo("id-ID")), _f, db, xs[1], ty);
                    g.DrawString(it.IsDir ? "--" : Human(it.Size), _f, db, xs[2], ty);
                    g.DrawString(Fit(g, it.Type, _f, ws[3]), _f, db, xs[3], ty);
                }
            }
            // kepala kolom (di atas baris yang tergulir)
            RectangleF hr = new RectangleF(c.Left, c.Top, c.Width, Ui.Pf(26));
            using (SolidBrush hb = new SolidBrush(CMain)) g.FillRectangle(hb, hr);
            using (Pen ln = new Pen(CLine)) g.DrawLine(ln, hr.Left, hr.Bottom - 1, hr.Right, hr.Bottom - 1);
            for (int k = 0; k < 4; k++)
            {
                bool on = _sortKey == keys[k];
                using (SolidBrush b = new SolidBrush(on ? CText : CDim)) g.DrawString(heads[k] + (on ? (_sortDesc ? "  ▾" : "  ▴") : ""), on ? _fsb : _fs, b, xs[k], hr.Y + Ui.Pf(6));
                _heads[keys[k]] = new RectangleF(xs[k] - Ui.Pf(6), hr.Y, ws[k], hr.Height);
            }
        }

        private void DrawPathBar(Graphics g, int p, int W, int H)
        {
            _crumbs.Clear();
            Rectangle b = new Rectangle(p + SideW, H - p - BarH, W - 2 * p - SideW, BarH);
            using (SolidBrush bb = new SolidBrush(CTool)) g.FillRectangle(bb, b);
            using (Pen ln = new Pen(CLine)) g.DrawLine(ln, b.Left, b.Top, b.Right, b.Top);
            if (_path == null) return;
            List<string> parts = new List<string>();
            for (DirectoryInfo d = new DirectoryInfo(_path); d != null; d = d.Parent) parts.Insert(0, d.FullName);
            float x = b.Left + Ui.Pf(12), ty = b.Top + (BarH - _fs.GetHeight(g)) / 2f;
            using (SolidBrush tb = new SolidBrush(CText)) using (SolidBrush db = new SolidBrush(CDim))
                for (int i = 0; i < parts.Count; i++)
                {
                    string nm = Title(parts[i]);
                    SizeF s = g.MeasureString(nm, _fs);
                    RectangleF r = new RectangleF(x - Ui.Pf(3), b.Top + Ui.Pf(3), s.Width + Ui.Pf(6), BarH - Ui.Pf(6));
                    if (_hoverTool == "crumb" + i) using (GraphicsPath gp = Glass.Round(r, Ui.Pf(4))) using (SolidBrush hb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(hb, gp);
                    g.DrawString(nm, _fs, i == parts.Count - 1 ? tb : db, x, ty);
                    _crumbs.Add(new KeyValuePair<string, RectangleF>(parts[i], r));
                    x += s.Width + Ui.Pf(4);
                    if (i < parts.Count - 1) { g.DrawString("›", _fs, db, x, ty); x += Ui.Pf(12); }
                }
            // status: jumlah item & ruang kosong
            string st = (_sel.Count > 0 ? _sel.Count + " dari " : "") + _view.Count + " item";
            try { DriveInfo d = new DriveInfo(Path.GetPathRoot(_path)); if (d.IsReady) st += ", " + Human(d.AvailableFreeSpace) + " tersedia"; } catch { }
            SizeF ss = g.MeasureString(st, _fs);
            using (SolidBrush db = new SolidBrush(CDim)) g.DrawString(st, _fs, db, b.Right - ss.Width - Ui.Pf(12), ty);
        }

        private static string Human(long b)
        {
            string[] u = { "byte", "KB", "MB", "GB", "TB" }; double v = b; int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
            return i == 0 ? b + " byte" : v.ToString(v >= 100 ? "0" : "0.#") + " " + u[i];
        }

        private static string Fit(Graphics g, string s, Font f, float w)
        {
            if (string.IsNullOrEmpty(s) || g.MeasureString(s, f).Width <= w) return s ?? "";
            while (s.Length > 1 && g.MeasureString(s + "…", f).Width > w) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        // ---------- Quick Look ----------
        private static readonly string[] TextExt = { ".txt", ".md", ".log", ".json", ".xml", ".csv", ".ini", ".cs", ".js", ".ts", ".py", ".html", ".css", ".bat", ".ps1", ".yml", ".yaml", ".srt", ".c", ".cpp", ".h", ".java" };
        private static readonly string[] ImgExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" };

        private void ToggleQuickLook()
        {
            if (_qlPath != null) { CloseQuickLook(); return; }
            FinderItem it = Primary(); if (it == null) return;
            _qlAt = DateTime.Now;
            OpenQuickLook(it);
        }

        private void OpenQuickLook(FinderItem it)
        {
            CloseQuickLook();
            if (_qlPath == null) _qlAt = DateTime.Now;
            _qlPath = it.Path;
            string ext = Path.GetExtension(it.Path).ToLowerInvariant();
            try
            {
                if (!it.IsDir && Array.IndexOf(ImgExt, ext) >= 0 && it.Size < 80L * 1024 * 1024)
                    using (FileStream fs = new FileStream(it.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (Image src = Image.FromStream(fs)) _qlImg = new Bitmap(src);
                else if (!it.IsDir && Array.IndexOf(TextExt, ext) >= 0)
                {
                    using (StreamReader sr = new StreamReader(it.Path, Encoding.UTF8, true))
                    { char[] buf = new char[12000]; int n = sr.Read(buf, 0, buf.Length); _qlText = new string(buf, 0, n); }
                }
                else _qlImg = ShellImage.Get(it.Path, Ui.P(256), 0);
            }
            catch { }
            Invalidate();
        }

        private void CloseQuickLook() { if (_qlPath == null) return; _qlPath = null; _qlText = null; if (_qlImg != null) { _qlImg.Dispose(); _qlImg = null; } Invalidate(); }

        private void DrawQuickLook(Graphics g, int W, int H)
        {
            float qt = QlT;
            using (SolidBrush dim = new SolidBrush(Color.FromArgb((int)(150 * qt), 0, 0, 0))) g.FillRectangle(dim, 0, 0, W, H);
            float pw = Math.Min(W - Ui.Pf(80), Ui.Pf(820)), ph = Math.Min(H - Ui.Pf(80), Ui.Pf(560));
            float sc = 0.92f + 0.08f * qt;
            g.TranslateTransform(W / 2f, H / 2f); g.ScaleTransform(sc, sc); g.TranslateTransform(-W / 2f, -H / 2f);
            RectangleF pr = new RectangleF((W - pw) / 2f, (H - ph) / 2f, pw, ph);
            using (GraphicsPath gp = Glass.Round(pr, Ui.Pf(14)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 40, 40, 46))) g.FillPath(b, gp);
                using (Pen pn = new Pen(CLine)) g.DrawPath(pn, gp);
            }
            FinderItem it = _items.Find(delegate (FinderItem i) { return i.Path == _qlPath; });
            string title = it != null ? it.Name : Path.GetFileName(_qlPath);
            using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(Fit(g, title, _fb, pw - Ui.Pf(40)), _fb, tb, pr.X + Ui.Pf(18), pr.Y + Ui.Pf(12));
            RectangleF area = new RectangleF(pr.X + Ui.Pf(16), pr.Y + Ui.Pf(42), pw - Ui.Pf(32), ph - Ui.Pf(80));
            if (_qlText != null)
                using (Font mono = new Font("Consolas", Ui.Pf(11), GraphicsUnit.Pixel)) using (SolidBrush tb = new SolidBrush(Color.FromArgb(220, 224, 236)))
                { Region old = g.Clip; g.SetClip(area); g.DrawString(_qlText, mono, tb, area); g.Clip = old; }
            else if (_qlImg != null)
            {
                float k = Math.Min(1f, Math.Min(area.Width / _qlImg.Width, area.Height / _qlImg.Height));
                if (_qlImg.Width < area.Width / 3 && it != null && !it.IsDir && Array.IndexOf(ImgExt, Path.GetExtension(it.Path).ToLowerInvariant()) < 0) k = Math.Min(area.Width, area.Height) * 0.5f / _qlImg.Width;
                float w = _qlImg.Width * k, h = _qlImg.Height * k;
                g.DrawImage(_qlImg, area.X + (area.Width - w) / 2f, area.Y + (area.Height - h) / 2f, w, h);
            }
            if (it != null)
            {
                string info = (it.IsDir ? "Folder" : it.Type + " · " + Human(it.Size)) + " · diubah " + it.Modified.ToString("d MMM yyyy HH.mm", new System.Globalization.CultureInfo("id-ID"));
                using (SolidBrush db = new SolidBrush(CDim)) using (StringFormat sf = new StringFormat()) { sf.Alignment = StringAlignment.Center; g.DrawString(info, _fs, db, new RectangleF(pr.X, pr.Bottom - Ui.Pf(30), pw, Ui.Pf(20)), sf); }
            }
        }

        // ---------- seleksi ----------
        private FinderItem Primary()
        {
            foreach (FinderItem it in _view) if (_sel.Contains(it.Path)) return it;
            return null;
        }
        private int IndexOfPath(string p) { for (int i = 0; i < _view.Count; i++) if (string.Equals(_view[i].Path, p, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
        private void SelectOnly(int i) { _sel.Clear(); if (i >= 0 && i < _view.Count) { _sel.Add(_view[i].Path); _anchor = i; EnsureVisible(i); } Invalidate(); }
        private List<string> Selected() { List<string> r = new List<string>(); foreach (FinderItem it in _view) if (_sel.Contains(it.Path)) r.Add(it.Path); return r; }

        private void EnsureVisible(int i)
        {
            Rectangle c = ContentRect;
            float top = _mode == "icon" ? Ui.P(10) + (i / Cols) * CellH : i * RowH, h = _mode == "icon" ? CellH : RowH;
            float viewH = _mode == "list" ? c.Height - Ui.P(26) : c.Height;
            if (top < _scrollT) _scrollT = top;
            else if (top + h > _scrollT + viewH) _scrollT = top + h - viewH;
            ClampScroll();
        }

        private void ClampScroll()
        {
            Rectangle c = ContentRect;
            float viewH = _mode == "list" ? c.Height - Ui.P(26) : c.Height;
            _scrollT = Math.Max(0, Math.Min(Math.Max(0, ContentHeight - viewH), _scrollT));
        }

        // ---------- membuka ----------
        private void OpenItem(FinderItem it)
        {
            if (it.IsDir) { Navigate(it.Path, true); return; }
            if (it.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string tgt = null;
                try { Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t); object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { it.Path }); tgt = sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string; } catch { }
                if (!string.IsNullOrEmpty(tgt) && Directory.Exists(tgt)) { Navigate(tgt, true); return; }
            }
            Shell.Run(it.Path, null);
        }

        // ---------- operasi berkas (SHFileOperation: ada progres & bisa di-undo) ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        private const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3;

        private void FileOp(uint func, List<string> src, string dest)
        {
            if (src.Count == 0) return;
            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.hwnd = Handle; op.wFunc = func;
            op.pFrom = string.Join("\0", src.ToArray()) + "\0\0";
            op.pTo = dest == null ? null : dest + "\0\0";
            op.fFlags = (ushort)(func == FO_DELETE ? 0x0040 /* ALLOWUNDO */ : 0x0040 | 0x0008 /* RENAMEONCOLLISION */);
            SHFileOperation(ref op);
            _dirty = true;
        }

        private void CopySel(bool cut)
        {
            List<string> s = Selected(); if (s.Count == 0) return;
            StringCollection sc = new StringCollection(); sc.AddRange(s.ToArray());
            DataObject d = new DataObject(); d.SetFileDropList(sc);
            d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            Clipboard.SetDataObject(d, true);
        }

        private void Paste()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return;
                List<string> files = new List<string>(); foreach (string f in Clipboard.GetFileDropList()) files.Add(f);
                bool cut = false;
                IDataObject d = Clipboard.GetDataObject();
                if (d != null && d.GetDataPresent("Preferred DropEffect")) { MemoryStream ms = d.GetData("Preferred DropEffect") as MemoryStream; if (ms != null) { byte[] b = new byte[4]; ms.Read(b, 0, 4); cut = (BitConverter.ToInt32(b, 0) & 2) != 0; } }
                FileOp(cut ? FO_MOVE : FO_COPY, files, _path);
                if (cut) Clipboard.Clear();
            }
            catch (Exception ex) { Log.Write("finder paste: " + ex.Message); }
        }

        private void NewFolder()
        {
            string baseName = "Folder tanpa judul", p = Path.Combine(_path, baseName); int n = 2;
            while (Directory.Exists(p) || File.Exists(p)) p = Path.Combine(_path, baseName + " " + n++);
            try { Directory.CreateDirectory(p); Load(); int i = IndexOfPath(p); SelectOnly(i); BeginRename(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Finder"); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool SHObjectProperties(IntPtr h, int type, string name, string page);

        private void BeginRename()
        {
            FinderItem it = Primary(); if (it == null) return;
            int i = IndexOfPath(it.Path); EnsureVisible(i); _scroll = _scrollT;
            RectangleF r = ItemRect(i);
            if (_rename == null)
            {
                _rename = new TextBox(); _rename.BorderStyle = BorderStyle.FixedSingle; _rename.Font = _f; _rename.BackColor = Color.FromArgb(54, 54, 62); _rename.ForeColor = CText;
                _rename.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { EndRename(true); e.Handled = e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { EndRename(false); e.Handled = e.SuppressKeyPress = true; } };
                _rename.LostFocus += delegate { EndRename(true); };
                Controls.Add(_rename);
            }
            _renamePath = it.Path;
            _rename.Text = Path.GetFileName(it.Path);
            if (_mode == "icon") _rename.Bounds = new Rectangle((int)(r.X + Ui.P(4)), (int)(r.Y + Ui.P(78)), (int)r.Width - Ui.P(8), _rename.PreferredHeight);
            else _rename.Bounds = new Rectangle((int)(r.X + Ui.P(40)), (int)(r.Y + 2), Math.Max(Ui.P(200), (int)(r.Width * 0.4f)), _rename.PreferredHeight);
            _rename.Visible = true; _rename.BringToFront(); _rename.Focus();
            string nm = _rename.Text; int dot = it.IsDir ? -1 : nm.LastIndexOf('.');
            _rename.Select(0, dot > 0 ? dot : nm.Length);
            Invalidate();
        }

        private void EndRename(bool commit)
        {
            if (_renamePath == null || _rename == null) return;
            string oldP = _renamePath; _renamePath = null;
            string nn = _rename.Text.Trim(); _rename.Visible = false; Focus();
            if (commit && nn.Length > 0 && nn != Path.GetFileName(oldP))
            {
                string np = Path.Combine(Path.GetDirectoryName(oldP), nn);
                try { if (Directory.Exists(oldP)) Directory.Move(oldP, np); else File.Move(oldP, np); Load(); SelectOnly(IndexOfPath(np)); }
                catch (Exception ex) { MessageBox.Show(this, "Tidak bisa mengganti nama: " + ex.Message, "Finder"); }
            }
            Invalidate();
        }

        // ---------- menu klik kanan ----------
        private void ShowMenu(Point at, bool onItem)
        {
            ContextMenuStrip m = Menus.Create();
            List<string> sel = Selected();
            if (onItem && sel.Count > 0)
            {
                FinderItem it = Primary();
                Menus.Add(m, "Buka", delegate { foreach (FinderItem x in _view) if (_sel.Contains(x.Path)) { OpenItem(x); break; } });
                if (!it.IsDir) Menus.Add(m, "Buka Dengan…", delegate { Shell.Run("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + it.Path); });
                if (it.IsDir) Menus.Add(m, "Buka di Jendela Baru", delegate { OpenAt(it.Path); });
                Menus.Add(m, "Lihat Sekilas (Spasi)", delegate { OpenQuickLook(it); });
                Menus.Sep(m);
                Menus.Add(m, "Salin", delegate { CopySel(false); });
                Menus.Add(m, "Potong", delegate { CopySel(true); });
                Menus.Add(m, "Ganti Nama", delegate { BeginRename(); });
                Menus.Add(m, "Salin Jalur", delegate { Clipboard.SetText(string.Join(Environment.NewLine, sel.ToArray())); });
                Menus.Sep(m);
                Menus.Add(m, "Pindahkan ke Tong Sampah", delegate { FileOp(FO_DELETE, sel, null); });
                Menus.Sep(m);
                Menus.Add(m, "Tampilkan di Explorer", delegate { Shell.Run("explorer.exe", "/select,\"" + it.Path + "\""); });
                Menus.Add(m, "Properti", delegate { SHObjectProperties(Handle, 2, it.Path, null); });
            }
            else
            {
                Menus.Add(m, "Folder Baru", delegate { NewFolder(); });
                ToolStripMenuItem paste = Menus.Add(m, "Tempel", delegate { Paste(); });
                try { paste.Enabled = Clipboard.ContainsFileDropList(); } catch { }
                Menus.Sep(m);
                Menus.Add(m, "Tampilan Ikon", delegate { SetMode("icon"); });
                Menus.Add(m, "Tampilan Daftar", delegate { SetMode("list"); });
                Menus.Add(m, "Tampilan Kolom", delegate { SetMode("column"); });
                Menus.Add(m, "Urutkan: Nama", delegate { SetSort("name"); });
                Menus.Add(m, "Urutkan: Tanggal", delegate { SetSort("date"); });
                Menus.Add(m, "Urutkan: Ukuran", delegate { SetSort("size"); });
                Menus.Sep(m);
                Menus.Add(m, "Buka di Terminal", delegate { try { ProcessStartInfo si = new ProcessStartInfo("wt.exe", "-d \"" + _path + "\""); si.UseShellExecute = true; Process.Start(si); } catch { Shell.Run("cmd.exe", "/k cd /d \"" + _path + "\""); } });
                Menus.Add(m, "Buka di Explorer", delegate { Shell.Run("explorer.exe", "\"" + _path + "\""); });
                Menus.Add(m, "Properti Folder", delegate { SHObjectProperties(Handle, 2, _path, null); });
            }
            m.Show(this, at);
        }

        private void SetMode(string mode) { if (_mode == mode) return; _mode = mode; _scroll = _scrollT = 0; _navAt = DateTime.Now; _colRoot = _path; _colX = _colXT = 0; Settings.SetS("FinderView", mode); Invalidate(); }
        private void SetSort(string key) { if (_sortKey == key) _sortDesc = !_sortDesc; else { _sortKey = key; _sortDesc = key == "date" || key == "size"; } ApplyView(); Invalidate(); }

        // ---------- mouse ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_qlPath != null) return;
            if (_down && _downIdx >= 0 && (Math.Abs(e.X - _downPt.X) > 6 || Math.Abs(e.Y - _downPt.Y) > 6))
            {
                _down = false;
                List<string> s = Selected();
                if (s.Count > 0) { StringCollection sc = new StringCollection(); sc.AddRange(s.ToArray()); DataObject d = new DataObject(); d.SetFileDropList(sc); DoDragDrop(d, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); _dirty = true; }
                return;
            }
            string side = null; foreach (KeyValuePair<string, RectangleF> kv in _sideHits) if (kv.Value.Contains(e.Location)) side = kv.Key;
            string tool = ToolHit(e.Location);
            if (tool == null) for (int i = 0; i < _crumbs.Count; i++) if (_crumbs[i].Value.Contains(e.Location)) tool = "crumb" + i;
            int li = -1; { RectangleF all = RectangleF.Union(LightRect(0), LightRect(2)); all.Inflate(Ui.Pf(6), Ui.Pf(6)); if (all.Contains(e.Location)) li = LightHit(e.Location) >= 0 ? LightHit(e.Location) : 9; }
            int hi = HitItem(e.Location);
            if (side != _hoverSide || tool != _hoverTool || li != _hoverLight || hi != _hoverIdx) { _hoverSide = side; _hoverTool = tool; _hoverLight = li; _hoverIdx = hi; Invalidate(); }
            Cursor = (side != null || tool != null || li >= 0) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hoverSide = _hoverTool = null; _hoverLight = -1; _hoverIdx = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_renamePath != null) EndRename(true);
            if (_qlPath != null) { CloseQuickLook(); return; }
            if (!_search.Bounds.Contains(e.Location) && _search.Focused) Focus();
            if (e.Button == MouseButtons.XButton1) { GoBack(); return; }
            if (e.Button == MouseButtons.XButton2) { GoFwd(); return; }
            int lh = LightHit(e.Location);
            if (lh >= 0 && e.Button == MouseButtons.Left)
            {
                if (lh == 0) Close();
                else if (lh == 1) WinAnim.Minimize(Handle);
                else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                return;
            }
            string tool = ToolHit(e.Location);
            if (tool != null && e.Button == MouseButtons.Left)
            {
                if (tool == "back") GoBack(); else if (tool == "fwd") GoFwd();
                else if (tool.StartsWith("mode_")) SetMode(tool.Substring(5));
                return;
            }
            foreach (KeyValuePair<string, RectangleF> kv in _crumbs) if (kv.Value.Contains(e.Location)) { Navigate(kv.Key, true); return; }
            foreach (KeyValuePair<string, RectangleF> kv in _heads) if (kv.Value.Contains(e.Location) && _mode == "list") { SetSort(kv.Key); return; }
            foreach (KeyValuePair<string, RectangleF> kv in _sideHits)
                if (kv.Value.Contains(e.Location))
                {
                    if (kv.Key.StartsWith("shell:")) Shell.Run("explorer.exe", kv.Key);
                    else if (e.Button == MouseButtons.Right) { ContextMenuStrip m = Menus.Create(); string t = kv.Key; Menus.Add(m, "Buka di Jendela Baru", delegate { OpenAt(t); }); Menus.Add(m, "Buka di Explorer", delegate { Shell.Run("explorer.exe", "\"" + t + "\""); }); m.Show(this, e.Location); }
                    else { _colRoot = kv.Key; Navigate(kv.Key, true); }
                    return;
                }
            if (_mode == "column" && e.Button == MouseButtons.Left)
                foreach (KeyValuePair<string, RectangleF> kv in _colHits)
                    if (kv.Value.Contains(e.Location))
                    {
                        if (Directory.Exists(kv.Key)) Navigate(kv.Key, true);
                        else { string sel = kv.Key; Navigate(Path.GetDirectoryName(sel), true); SelectOnly(IndexOfPath(sel)); }
                        return;
                    }
            int i = HitItem(e.Location);
            if (!ContentRect.Contains(e.Location)) return;
            Focus();
            if (i >= 0)
            {
                string p = _view[i].Path;
                if ((ModifierKeys & Keys.Control) != 0) { if (!_sel.Remove(p)) _sel.Add(p); _anchor = i; }
                else if ((ModifierKeys & Keys.Shift) != 0 && _anchor >= 0) { _sel.Clear(); for (int k = Math.Min(_anchor, i); k <= Math.Max(_anchor, i); k++) _sel.Add(_view[k].Path); }
                else if (!_sel.Contains(p) || e.Button == MouseButtons.Left && e.Clicks == 1 && _sel.Count == 1) { _sel.Clear(); _sel.Add(p); _anchor = i; }
                if (e.Button == MouseButtons.Left && e.Clicks >= 2) { OpenItem(_view[i]); return; }
                if (_mode == "column" && e.Button == MouseButtons.Left && _view[i].IsDir && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0) { Navigate(_view[i].Path, true); return; }
                if (e.Button == MouseButtons.Left) { _down = true; _downPt = e.Location; _downIdx = i; }
                if (e.Button == MouseButtons.Right) { Invalidate(); ShowMenu(e.Location, true); return; }
            }
            else
            {
                _sel.Clear();
                if (e.Button == MouseButtons.Right) { Invalidate(); ShowMenu(e.Location, false); return; }
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; _downIdx = -1; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_qlPath != null) return;
            if (_mode == "column")
            {
                foreach (KeyValuePair<string, RectangleF> kv in _colAreas)
                    if (kv.Value.Contains(e.Location) && !string.Equals(kv.Key, _path, StringComparison.OrdinalIgnoreCase))
                    {
                        List<FinderItem> items = ColList(kv.Key);
                        List<string> ch = Chain(); int k = ch.IndexOf(kv.Key);
                        float cur = ColScrollFor(kv.Key, items, k >= 0 && k + 1 < ch.Count ? ch[k + 1] : null, ContentRect.Height);
                        _colScroll[kv.Key] = Math.Max(0, Math.Min(Math.Max(0, items.Count * RowH + Ui.P(12) - ContentRect.Height), cur - e.Delta * Ui.Pf(0.9)));
                        Invalidate(); return;
                    }
            }
            _scrollT -= e.Delta * Ui.Pf(0.9);
            ClampScroll();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ClampScroll(); Invalidate(); }

        // ---------- seret & lepas masuk ----------
        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }
        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            string[] f = (string[])e.Data.GetData(DataFormats.FileDrop);
            string dest = DropTarget(PointToClient(new Point(e.X, e.Y)));
            bool same = f.Length > 0 && string.Equals(Path.GetPathRoot(f[0]), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase);
            e.Effect = (e.KeyState & 8) != 0 ? DragDropEffects.Copy : (e.KeyState & 4) != 0 ? DragDropEffects.Move : same ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] f = (string[])e.Data.GetData(DataFormats.FileDrop);
            string dest = DropTarget(PointToClient(new Point(e.X, e.Y)));
            List<string> src = new List<string>();
            foreach (string s in f) if (!string.Equals(Path.GetDirectoryName(s), dest, StringComparison.OrdinalIgnoreCase) && !string.Equals(s, dest, StringComparison.OrdinalIgnoreCase)) src.Add(s);
            if (src.Count > 0) FileOp(e.Effect == DragDropEffects.Move ? FO_MOVE : FO_COPY, src, dest);
        }
        private string DropTarget(Point p)
        {
            int i = HitItem(p);
            if (i >= 0 && _view[i].IsDir) return _view[i].Path;
            foreach (KeyValuePair<string, RectangleF> kv in _sideHits) if (kv.Value.Contains(p) && Directory.Exists(kv.Key)) return kv.Key;
            return _path;
        }

        // ---------- keyboard ----------
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_search.Focused || (_rename != null && _rename.Focused)) return base.ProcessCmdKey(ref msg, keyData);
            Keys k = keyData & Keys.KeyCode; bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;
            if (_qlPath != null && (k == Keys.Space || k == Keys.Escape)) { CloseQuickLook(); return true; }
            if (alt && k == Keys.Left) { GoBack(); return true; }
            if (alt && k == Keys.Right) { GoFwd(); return true; }
            if (alt && k == Keys.Up || k == Keys.Back) { GoUp(); return true; }
            if (ctrl && k == Keys.F) { _search.Focus(); return true; }
            if (ctrl && k == Keys.A) { _sel.Clear(); foreach (FinderItem it in _view) _sel.Add(it.Path); Invalidate(); return true; }
            if (ctrl && k == Keys.C) { CopySel(false); return true; }
            if (ctrl && k == Keys.X) { CopySel(true); return true; }
            if (ctrl && k == Keys.V) { Paste(); return true; }
            if (ctrl && shift && k == Keys.N) { NewFolder(); return true; }
            if (ctrl && k == Keys.N) { OpenAt(_path); return true; }
            if (ctrl && k == Keys.W) { Close(); return true; }
            if (ctrl && k == Keys.D1) { SetMode("icon"); return true; }
            if (ctrl && k == Keys.D2) { SetMode("list"); return true; }
            if (ctrl && k == Keys.D3) { SetMode("column"); return true; }
            if (k == Keys.F5) { Load(); Invalidate(); return true; }
            if (k == Keys.F2) { BeginRename(); return true; }
            if (k == Keys.Delete) { FileOp(FO_DELETE, Selected(), null); return true; }
            if (k == Keys.Space) { ToggleQuickLook(); return true; }
            if (k == Keys.Enter) { FinderItem it = Primary(); if (it != null) OpenItem(it); return true; }
            if (_mode == "column" && (k == Keys.Left || k == Keys.Right))
            {
                if (k == Keys.Left)
                {
                    if (!string.Equals(_path.TrimEnd('\\'), (_colRoot ?? _path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    { string cur = _path; Navigate(Path.GetDirectoryName(cur.TrimEnd('\\')), true); SelectOnly(IndexOfPath(cur)); }
                }
                else { FinderItem it = Primary(); if (it != null && it.IsDir) { Navigate(it.Path, true); if (_view.Count > 0) SelectOnly(0); } }
                return true;
            }
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End)
            {
                if (_view.Count == 0) return true;
                FinderItem cur = Primary(); int i = cur == null ? -1 : IndexOfPath(cur.Path);
                int step = _mode == "icon" ? Cols : 1;
                int n = i < 0 ? 0 : k == Keys.Left ? i - (_mode == "icon" ? 1 : 0) : k == Keys.Right ? i + (_mode == "icon" ? 1 : 0) : k == Keys.Up ? i - step : k == Keys.Down ? i + step : k == Keys.Home ? 0 : _view.Count - 1;
                n = Math.Max(0, Math.Min(_view.Count - 1, n));
                SelectOnly(n);
                if (_qlPath != null) OpenQuickLook(_view[n]);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ketik nama untuk melompat ke item
        private string _typed = ""; private DateTime _typedAt;
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_search.Focused || char.IsControl(e.KeyChar) || e.KeyChar == ' ') return;
            if ((DateTime.Now - _typedAt).TotalMilliseconds > 900) _typed = "";
            _typed += e.KeyChar; _typedAt = DateTime.Now;
            int i = _view.FindIndex(delegate (FinderItem it) { return it.Name.StartsWith(_typed, StringComparison.CurrentCultureIgnoreCase); });
            if (i >= 0) SelectOnly(i);
        }

        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
        public static void Snapshot(string dir)
        {
            FinderForm f = new FinderForm(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            f.Size = new Size(Ui.P(1040), Ui.P(640));
            f.ShowInTaskbar = false; f.Location = new Point(-5000, 100); f.Show();
            Action<string> shot = delegate (string n)
            {
                for (int i = 0; i < 220; i++) { Application.DoEvents(); Thread.Sleep(30); f.Step(); if (i % 20 == 0) f.Refresh(); }
                f.Refresh();
                Native.RECT wr; Native.GetWindowRect(f.Handle, out wr);
                using (Bitmap b = new Bitmap(wr.Right - wr.Left, wr.Bottom - wr.Top))
                {
                    using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); }
                    b.Save(Path.Combine(dir, n));
                }
            };
            shot("finder-icon.png");
            f.SetMode("list"); shot("finder-list.png");
            f.SetMode("column"); f._colRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            f.Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), true);
            int ji = f._view.FindIndex(delegate (FinderItem x) { return x.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase); });
            f.SelectOnly(ji); shot("finder-col.png");
            f.SetMode("icon");
            f.Close();
        }

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Invalidate(); }
    }
}
