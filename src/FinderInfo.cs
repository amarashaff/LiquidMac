using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    // Jendela "Lihat Info" ala macOS (Get Info) untuk berkas/folder di Finder.
    internal sealed class FinderInfoForm : Form
    {
        private static readonly List<FinderInfoForm> All = new List<FinderInfoForm>();
        private static readonly CultureInfo Id = new CultureInfo("id-ID");

        public static void ShowFor(Form owner, string path)
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) return;
            foreach (FinderInfoForm o in All)
                if (!o.IsDisposed && string.Equals(o._path, path, StringComparison.OrdinalIgnoreCase)) { o.Activate(); return; }
            FinderInfoForm f = new FinderInfoForm(path);
            Rectangle wa = Screen.FromControl(owner).WorkingArea;
            int n = All.Count - 1;
            int x = owner.Right - f.Width - Ui.P(36) - n * Ui.P(26), y = owner.Top + Ui.P(64) + n * Ui.P(26);
            x = Math.Max(wa.Left + Ui.P(8), Math.Min(wa.Right - f.Width - Ui.P(8), x));
            y = Math.Max(wa.Top + Ui.P(8), Math.Min(wa.Bottom - Ui.P(420), y));
            f.Location = new Point(x, y);
            f.Show(owner);
        }

        private readonly string _path;
        private readonly bool _isDir, _isRoot;
        private readonly string _name, _kind, _where;
        private long _size = -1; private int _count = -1; private volatile bool _sizing, _closed;
        private DateTime _created, _modified, _accessed;
        private readonly List<KeyValuePair<string, string>> _more = new List<KeyValuePair<string, string>>();
        private Bitmap _icon, _preview;
        private bool _ro, _hidden;
        private readonly bool[] _openSec = { true, true, true, true };
        private readonly List<KeyValuePair<int, RectangleF>> _secHits = new List<KeyValuePair<int, RectangleF>>();
        private RectangleF _rClose, _rRo, _rHid, _rCopy, _rWin;
        private string _hover;
        private DateTime _copiedAt = DateTime.MinValue;
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();

        private static readonly Color CBg = Color.FromArgb(40, 40, 46), CBar = Color.FromArgb(48, 48, 55), CLine = Color.FromArgb(64, 64, 72);
        private static readonly Color CText = Color.FromArgb(236, 236, 242), CDim = Color.FromArgb(152, 154, 164), CAccent = Color.FromArgb(10, 132, 255);
        private readonly Font _f = Ui.F(9f, FontStyle.Regular), _fb = Ui.F(9f, FontStyle.Bold), _fn = Ui.F(11f, FontStyle.Bold), _fs = Ui.F(8.5f, FontStyle.Regular);
        private int TitleH { get { return Ui.P(34); } }

        private FinderInfoForm(string path)
        {
            _path = path;
            _isDir = Directory.Exists(path);
            string nm = Path.GetFileName(path.TrimEnd('\\'));
            _isRoot = string.IsNullOrEmpty(nm);
            _name = _isRoot ? path : nm;
            _where = _isRoot ? "Komputer Ini" : Path.GetDirectoryName(path.TrimEnd('\\'));
            _kind = KindOf(path, _isDir);
            try
            {
                FileSystemInfo fi = _isDir ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                _created = fi.CreationTime; _modified = fi.LastWriteTime; _accessed = fi.LastAccessTime;
                _ro = (fi.Attributes & FileAttributes.ReadOnly) != 0; _hidden = (fi.Attributes & FileAttributes.Hidden) != 0;
                if (!_isDir) _size = ((FileInfo)fi).Length;
            }
            catch { }
            LoadMore();
            try { _icon = ShellImage.Get(path, Ui.P(64)); } catch { }
            try { _preview = ShellImage.Get(path, Ui.P(256), 0); } catch { }

            All.Add(this);
            Text = "Info " + _name;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Ui.P(318), Ui.P(560));
            BackColor = CBg;
            KeyPreview = true;
            Icon = TrayApp.MakeIcon();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (Graphics g = CreateGraphics()) FitHeight(Compose(g, false));

            _tick.Interval = 250;
            _tick.Tick += delegate { if (_sizing || (DateTime.Now - _copiedAt).TotalSeconds < 2) Invalidate(); };
            _tick.Start();
            if (_isDir && !_isRoot) { _sizing = true; Thread t = new Thread(Measure); t.IsBackground = true; t.Start(); }
        }

        // ---------- data ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO fi, uint cb, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool SHObjectProperties(IntPtr h, int type, string name, string page);

        private static string KindOf(string path, bool dir)
        {
            try
            {
                SHFILEINFO fi = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x400 /* TYPENAME */);
                if (!string.IsNullOrEmpty(fi.szTypeName)) return fi.szTypeName;
            }
            catch { }
            if (dir) return "Folder";
            string ext = Path.GetExtension(path);
            return ext.Length > 1 ? "Berkas " + ext.Substring(1).ToUpperInvariant() : "Berkas";
        }

        private void LoadMore()
        {
            try
            {
                if (_isRoot)
                {
                    DriveInfo d = new DriveInfo(_path);
                    if (d.IsReady)
                    {
                        _size = d.TotalSize - d.AvailableFreeSpace;
                        _more.Add(new KeyValuePair<string, string>("Kapasitas", Human(d.TotalSize)));
                        _more.Add(new KeyValuePair<string, string>("Tersedia", Human(d.AvailableFreeSpace)));
                        _more.Add(new KeyValuePair<string, string>("Format", d.DriveFormat));
                    }
                    return;
                }
                if (_isDir) return;
                string ext = Path.GetExtension(_path).ToLowerInvariant();
                if (Array.IndexOf(new string[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }, ext) >= 0)
                    using (FileStream fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image im = Image.FromStream(fs, false, false))
                    {
                        _more.Add(new KeyValuePair<string, string>("Dimensi", im.Width + " × " + im.Height));
                        _more.Add(new KeyValuePair<string, string>("Resolusi", Math.Round(im.HorizontalResolution) + " dpi"));
                    }
                if (ext == ".exe" || ext == ".dll")
                {
                    FileVersionInfo v = FileVersionInfo.GetVersionInfo(_path);
                    if (!string.IsNullOrWhiteSpace(v.FileVersion)) _more.Add(new KeyValuePair<string, string>("Versi", v.FileVersion));
                    if (!string.IsNullOrWhiteSpace(v.CompanyName)) _more.Add(new KeyValuePair<string, string>("Penerbit", v.CompanyName));
                    if (!string.IsNullOrWhiteSpace(v.FileDescription)) _more.Add(new KeyValuePair<string, string>("Deskripsi", v.FileDescription));
                }
                if (ext.Length > 1) _more.Add(new KeyValuePair<string, string>("Ekstensi", ext));
            }
            catch { }
        }

        // ukuran folder dihitung di latar (tidak mengikuti tautan/junction)
        private void Measure()
        {
            long total = 0; int count = 0; DateTime last = DateTime.Now;
            Stack<string> st = new Stack<string>(); st.Push(_path);
            while (st.Count > 0 && !_closed)
            {
                string d = st.Pop();
                try
                {
                    foreach (FileSystemInfo fi in new DirectoryInfo(d).EnumerateFileSystemInfos())
                    {
                        if (_closed) return;
                        count++;
                        if ((fi.Attributes & FileAttributes.Directory) != 0) { if ((fi.Attributes & FileAttributes.ReparsePoint) == 0) st.Push(fi.FullName); }
                        else total += ((FileInfo)fi).Length;
                        if ((DateTime.Now - last).TotalMilliseconds > 200) { _size = total; _count = count; last = DateTime.Now; }
                    }
                }
                catch { }
            }
            _size = total; _count = count; _sizing = false;
            try { if (!_closed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { Invalidate(); }); } catch { }
        }

        private static string Human(long b)
        {
            string[] u = { "byte", "KB", "MB", "GB", "TB" }; double v = b; int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
            return i == 0 ? b + " byte" : v.ToString(v >= 100 ? "0" : "0.#", Id) + " " + u[i];
        }
        private static string Date(DateTime d) { return d.Year < 1700 ? "--" : d.ToString("dddd, d MMMM yyyy 'pukul' HH.mm", Id); }
        private string SizeShort() { return _size < 0 ? (_sizing ? "Menghitung…" : "--") : Human(_size); }
        private string SizeLong()
        {
            if (_size < 0) return _sizing ? "Menghitung…" : "--";
            string s = _size.ToString("N0", Id) + " byte (" + Human(_size) + ")";
            if (_isDir && _count >= 0) s += " untuk " + _count.ToString("N0", Id) + " item";
            if (_sizing) s += "…";
            return s;
        }

        // ---------- bingkai ----------
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 1; try { DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
            v = 2; try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
            MARGINS m = new MARGINS(); m.cyTopHeight = 1;
            try { DwmExtendFrameIntoClientArea(Handle, ref m); } catch { }
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0083 /* WM_NCCALCSIZE */ && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x0084 /* WM_NCHITTEST */)
            {
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                RectangleF c = _rClose; c.Inflate(Ui.Pf(4), Ui.Pf(4));
                m.Result = (IntPtr)(p.Y < TitleH && !c.Contains(p) ? 2 : 1);   // seret dari baris judul; ukuran tetap
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closed = true; _tick.Stop(); All.Remove(this);
            if (_icon != null) _icon.Dispose();
            if (_preview != null) _preview.Dispose();
            base.OnFormClosed(e);
        }

        private bool _fitting;
        private void FitHeight(float h)
        {
            int want = (int)Math.Ceiling(h);
            int max = Screen.FromPoint(Location).WorkingArea.Height - Ui.P(24);
            want = Math.Min(want, max);
            if (Math.Abs(want - Height) <= 1 || _fitting) return;
            _fitting = true; try { Size = new Size(Width, want); } finally { _fitting = false; }
        }

        // ---------- menggambar ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (SolidBrush b = new SolidBrush(CBg)) g.FillRectangle(b, ClientRectangle);
            float h = Compose(g, true);
            if (Math.Abs((int)Math.Ceiling(h) - Height) > 1 && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) { FitHeight(h); Invalidate(); } });
        }

        private float Compose(Graphics g, bool draw)
        {
            float W = ClientSize.Width, pad = Ui.Pf(16), y;
            _secHits.Clear();
            // baris judul + traffic light
            float d = Ui.Pf(13);
            _rClose = new RectangleF(Ui.Pf(14), (TitleH - d) / 2f, d, d);
            if (draw)
            {
                using (SolidBrush b = new SolidBrush(CBar)) g.FillRectangle(b, 0, 0, W, TitleH);
                using (Pen ln = new Pen(CLine)) g.DrawLine(ln, 0, TitleH - 1, W, TitleH - 1);
                bool act = Form.ActiveForm == this || _hover == "close";
                Color[] on = { Color.FromArgb(255, 95, 87), Color.FromArgb(84, 84, 92), Color.FromArgb(84, 84, 92) };
                for (int i = 0; i < 3; i++)
                    using (SolidBrush lb = new SolidBrush(act ? on[i] : Color.FromArgb(80, 80, 88))) g.FillEllipse(lb, _rClose.X + i * Ui.Pf(21), _rClose.Y, d, d);
                if (_hover == "close")
                    using (Pen xp = new Pen(Color.FromArgb(190, 60, 20, 10), Ui.Pf(1.3)))
                    {
                        float cx = _rClose.X + d / 2f, cy = _rClose.Y + d / 2f, k = d * 0.2f;
                        g.DrawLine(xp, cx - k, cy - k, cx + k, cy + k); g.DrawLine(xp, cx + k, cy - k, cx - k, cy + k);
                    }
                string title = Fit(g, "Info " + _name, _fb, W - Ui.Pf(170));
                SizeF ts = g.MeasureString(title, _fb);
                using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(title, _fb, tb, (W - ts.Width) / 2f, (TitleH - ts.Height) / 2f);
            }
            y = TitleH + Ui.Pf(12);

            // kepala: ikon, nama, ukuran, tanggal diubah
            float isz = Ui.Pf(52);
            if (draw)
            {
                RectangleF ir = new RectangleF(pad, y, isz, isz);
                if (_icon != null) g.DrawImage(_icon, ir);
                string sz = SizeShort();
                SizeF ss = g.MeasureString(sz, _f);
                float nx = pad + isz + Ui.Pf(12);
                using (SolidBrush tb = new SolidBrush(CText)) using (SolidBrush db = new SolidBrush(CDim))
                {
                    g.DrawString(Fit(g, _name, _fn, W - nx - pad - ss.Width - Ui.Pf(8)), _fn, tb, nx, y + Ui.Pf(5));
                    g.DrawString(sz, _f, db, W - pad - ss.Width, y + Ui.Pf(8));
                    g.DrawString(Fit(g, "Diubah: " + (_modified.Year < 1700 ? "--" : _modified.ToString("d MMMM yyyy HH.mm", Id)), _fs, W - nx - pad), _fs, db, nx, y + Ui.Pf(29));
                }
            }
            y += isz + Ui.Pf(12);

            // Umum
            if (Sec(g, draw, 0, "Umum:", ref y))
            {
                Row(g, draw, "Jenis", _kind, ref y);
                Row(g, draw, "Ukuran", SizeLong(), ref y);
                Row(g, draw, "Lokasi", _where, ref y);
                Row(g, draw, "Dibuat", Date(_created), ref y);
                Row(g, draw, "Diubah", Date(_modified), ref y);
                Row(g, draw, "Dibuka", Date(_accessed), ref y);
                y += Ui.Pf(4);
                float cx = Ui.Pf(100);
                _rRo = Check(g, draw, "Terkunci", _ro, "ro", cx, y);
                _rHid = Check(g, draw, "Tersembunyi", _hidden, "hid", _rRo.Right + Ui.Pf(14), y);
                y += Ui.Pf(26);
            }
            else { _rRo = _rHid = RectangleF.Empty; }

            // Info Lainnya
            if (_more.Count > 0 && Sec(g, draw, 1, "Info Lainnya:", ref y))
            {
                foreach (KeyValuePair<string, string> kv in _more) Row(g, draw, kv.Key, kv.Value, ref y);
                y += Ui.Pf(6);
            }

            // Nama & Ekstensi
            if (Sec(g, draw, 2, "Nama & Ekstensi:", ref y))
            {
                RectangleF fr = new RectangleF(pad + Ui.Pf(6), y, W - 2 * pad - Ui.Pf(6), Ui.Pf(26));
                if (draw)
                {
                    using (GraphicsPath gp = Glass.Round(fr, Ui.Pf(6))) { using (SolidBrush fb = new SolidBrush(Color.FromArgb(30, 30, 35))) g.FillPath(fb, gp); using (Pen fp = new Pen(CLine)) g.DrawPath(fp, gp); }
                    using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(Fit(g, _name, _f, fr.Width - Ui.Pf(16)), _f, tb, fr.X + Ui.Pf(8), fr.Y + (fr.Height - _f.GetHeight(g)) / 2f);
                }
                y += fr.Height + Ui.Pf(12);
            }

            // Pratinjau
            if (_preview != null && Sec(g, draw, 3, "Pratinjau:", ref y))
            {
                float box = Ui.Pf(150);
                float k = Math.Min(box / _preview.Width, box / _preview.Height);
                float pw = _preview.Width * k, ph = _preview.Height * k;
                if (draw) g.DrawImage(_preview, (W - pw) / 2f, y + (box - ph) / 2f, pw, ph);
                y += box + Ui.Pf(12);
            }

            // tombol
            if (draw) using (Pen ln = new Pen(CLine)) g.DrawLine(ln, 0, y, W, y);
            y += Ui.Pf(12);
            float bw = (W - 2 * pad - Ui.Pf(10)) / 2f, bh = Ui.Pf(28);
            _rCopy = new RectangleF(pad, y, bw, bh);
            _rWin = new RectangleF(pad + bw + Ui.Pf(10), y, bw, bh);
            if (draw)
            {
                bool copied = (DateTime.Now - _copiedAt).TotalSeconds < 1.4;
                Button2(g, _rCopy, copied ? "Jalur Tersalin ✓" : "Salin Jalur", "copy", true);
                Button2(g, _rWin, "Properti Windows…", "win", false);
            }
            y += bh + Ui.Pf(14);
            return y;
        }

        private bool Sec(Graphics g, bool draw, int idx, string title, ref float y)
        {
            float W = ClientSize.Width;
            RectangleF r = new RectangleF(0, y, W, Ui.Pf(26));
            _secHits.Add(new KeyValuePair<int, RectangleF>(idx, r));
            if (draw)
            {
                using (Pen ln = new Pen(CLine)) g.DrawLine(ln, 0, y, W, y);
                if (_hover == "sec" + idx) using (SolidBrush hb = new SolidBrush(Color.FromArgb(14, 255, 255, 255))) g.FillRectangle(hb, r);
                // segitiga buka/tutup
                float cx = Ui.Pf(20), cy = y + r.Height / 2f, k = Ui.Pf(3.6f);
                PointF[] tri = _openSec[idx]
                    ? new PointF[] { new PointF(cx - k, cy - k * 0.6f), new PointF(cx + k, cy - k * 0.6f), new PointF(cx, cy + k * 0.8f) }
                    : new PointF[] { new PointF(cx - k * 0.6f, cy - k), new PointF(cx + k * 0.8f, cy), new PointF(cx - k * 0.6f, cy + k) };
                using (SolidBrush tb = new SolidBrush(CDim)) g.FillPolygon(tb, tri);
                using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(title, _fb, tb, Ui.Pf(30), y + (r.Height - _fb.GetHeight(g)) / 2f);
            }
            y += r.Height + (_openSec[idx] ? Ui.Pf(2) : 0);
            return _openSec[idx];
        }

        private void Row(Graphics g, bool draw, string label, string value, ref float y)
        {
            float W = ClientSize.Width, vx = Ui.Pf(100), vw = W - vx - Ui.Pf(14);
            if (string.IsNullOrEmpty(value)) value = "--";
            SizeF vs = g.MeasureString(value, _f, (int)vw);
            float h = Math.Max(vs.Height, _f.GetHeight(g));
            if (draw)
            {
                SizeF ls = g.MeasureString(label + ":", _f);
                using (SolidBrush db = new SolidBrush(CDim)) g.DrawString(label + ":", _f, db, vx - Ui.Pf(4) - ls.Width, y);
                using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(value, _f, tb, new RectangleF(vx, y, vw, h + 2));
            }
            y += h + Ui.Pf(4);
        }

        private RectangleF Check(Graphics g, bool draw, string label, bool on, string id, float x, float y)
        {
            float s = Ui.Pf(14);
            SizeF ls = g.MeasureString(label, _f);
            RectangleF box = new RectangleF(x, y + Ui.Pf(2), s, s);
            if (draw)
            {
                using (GraphicsPath gp = Glass.Round(box, Ui.Pf(3.5f)))
                {
                    using (SolidBrush b = new SolidBrush(on ? CAccent : Color.FromArgb(_hover == id ? 84 : 66, 66 + (_hover == id ? 18 : 0), 76 + (_hover == id ? 18 : 0)))) g.FillPath(b, gp);
                    if (!on) using (Pen p = new Pen(Color.FromArgb(96, 96, 106))) g.DrawPath(p, gp);
                }
                if (on) using (Pen cp = new Pen(Color.White, Ui.Pf(1.6f)))
                    {
                        cp.StartCap = cp.EndCap = LineCap.Round; cp.LineJoin = LineJoin.Round;
                        g.DrawLines(cp, new PointF[] { new PointF(box.X + s * 0.24f, box.Y + s * 0.52f), new PointF(box.X + s * 0.43f, box.Y + s * 0.70f), new PointF(box.X + s * 0.76f, box.Y + s * 0.30f) });
                    }
                using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(label, _f, tb, box.Right + Ui.Pf(5), y + Ui.Pf(1));
            }
            return new RectangleF(x, y, s + Ui.Pf(5) + ls.Width, Ui.Pf(20));
        }

        private void Button2(Graphics g, RectangleF r, string text, string id, bool primary)
        {
            bool hov = _hover == id;
            using (GraphicsPath gp = Glass.Round(r, Ui.Pf(7)))
            using (SolidBrush b = new SolidBrush(primary ? (hov ? Color.FromArgb(40, 150, 255) : CAccent) : (hov ? Color.FromArgb(92, 92, 104) : Color.FromArgb(72, 72, 82))))
                g.FillPath(b, gp);
            SizeF ts = g.MeasureString(text, _f);
            using (SolidBrush tb = new SolidBrush(Color.White)) g.DrawString(text, _f, tb, r.X + (r.Width - ts.Width) / 2f, r.Y + (r.Height - ts.Height) / 2f);
        }

        private static string Fit(Graphics g, string s, Font f, float w)
        {
            if (string.IsNullOrEmpty(s) || g.MeasureString(s, f).Width <= w) return s ?? "";
            while (s.Length > 1 && g.MeasureString(s + "…", f).Width > w) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        // ---------- interaksi ----------
        private string HitAt(Point p)
        {
            RectangleF c = _rClose; c.Inflate(Ui.Pf(3), Ui.Pf(3));
            if (c.Contains(p)) return "close";
            if (_rRo.Contains(p)) return "ro";
            if (_rHid.Contains(p)) return "hid";
            if (_rCopy.Contains(p)) return "copy";
            if (_rWin.Contains(p)) return "win";
            foreach (KeyValuePair<int, RectangleF> kv in _secHits) if (kv.Value.Contains(p)) return "sec" + kv.Key;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); string h = HitAt(e.Location); if (h != _hover) { _hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != null) { _hover = null; Invalidate(); } }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            string h = HitAt(e.Location);
            if (h == null) return;
            if (h == "close") { Close(); return; }
            if (h == "copy") CopyPath();
            else if (h == "win") SHObjectProperties(Handle, 2, _path, null);
            else if (h == "ro") ToggleAttr(FileAttributes.ReadOnly, ref _ro);
            else if (h == "hid") ToggleAttr(FileAttributes.Hidden, ref _hidden);
            else if (h.StartsWith("sec")) { int i = int.Parse(h.Substring(3)); _openSec[i] = !_openSec[i]; }
            Invalidate();
        }

        private void CopyPath()
        {
            try { Clipboard.SetText(_path); _copiedAt = DateTime.Now; } catch (Exception ex) { Log.Write("info copy: " + ex.Message); }
        }

        private void ToggleAttr(FileAttributes a, ref bool state)
        {
            try
            {
                FileAttributes cur = File.GetAttributes(_path);
                cur = state ? cur & ~a : cur | a;
                File.SetAttributes(_path, cur);
                state = !state;
            }
            catch (Exception ex) { MessageBox.Show(this, "Tidak bisa mengubah atribut: " + ex.Message, "Info"); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode; bool ctrl = (keyData & Keys.Control) != 0;
            if (k == Keys.Escape || (ctrl && k == Keys.W)) { Close(); return true; }
            if (ctrl && k == Keys.C) { CopyPath(); Invalidate(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // promo/uji: simpan tampilan ke PNG
        public static void SnapshotTo(string path, string file)
        {
            FinderInfoForm f = new FinderInfoForm(path);
            f.ShowInTaskbar = false; f.Location = new Point(-5000, 100); f.Show();
            for (int i = 0; i < 40; i++) { Application.DoEvents(); Thread.Sleep(25); }
            using (Bitmap b = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(CBg); f.Compose(g, true);
                }
                b.Save(file);
            }
            f.Close();
        }
    }
}
