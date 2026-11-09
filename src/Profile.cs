using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiquidMac
{
    // ============================================================
    //  INFO PENGGUNA & SISTEM
    // ============================================================
    internal static class SysInfo
    {
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kb);
        [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);

        // Akun admin dengan UAC berjalan "terbatas" (TokenElevationTypeLimited = 3)
        private static bool IsAdminAccount()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return true;
                int t, r;
                return GetTokenInformation(id.Token, 18, out t, 4, out r) && t == 3;
            }
            catch { return false; }
        }

        public static string UserName = Environment.UserName, FullName, Email, Device = Environment.MachineName;
        public static string Model = "-", Cpu = "-", Gpu = "-", Os = "-", Ram = "-", BatteryHealth;
        public static bool Admin;
        public static Bitmap Avatar;
        private static int _loaded;

        public static string DisplayName { get { return !string.IsNullOrEmpty(FullName) ? FullName : UserName; } }

        public static void LoadAsync(Control ui, MethodInvoker done)
        {
            if (Interlocked.Exchange(ref _loaded, 1) == 1) { if (done != null) done(); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Load(); } catch (Exception ex) { Log.Write("sysinfo: " + ex.Message); }
                if (done != null && ui != null && ui.IsHandleCreated) try { ui.BeginInvoke(done); } catch { }
            });
        }

        public static void LoadNow() { if (Interlocked.Exchange(ref _loaded, 1) == 0) Load(); }

        public static void LoadQuick()
        {
            if (Avatar == null) try { Avatar = LoadAvatar(); ResetAvatarCache(); } catch { }
            if (FullName == null) try { FullName = LoadFullName(); } catch { FullName = ""; }
        }

        private static void Load()
        {
            LoadQuick();
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\IdentityCRL\UserExtendedProperties")) if (k != null && k.SubKeyCount > 0) Email = k.GetSubKeyNames()[0]; } catch { }
            Admin = IsAdminAccount();
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) Cpu = Clean(Convert.ToString(k.GetValue("ProcessorNameString"))).Replace("11th Gen ", "").Replace("12th Gen ", "").Replace("13th Gen ", "") + "\n" + Environment.ProcessorCount + " thread";
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = Convert.ToString(k.GetValue("ProductName")), dv = Convert.ToString(k.GetValue("DisplayVersion")), build = Convert.ToString(k.GetValue("CurrentBuild"));
                    int b; if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    Os = name + (dv.Length > 0 ? " " + dv : "") + "\nbuild " + build + "." + Convert.ToString(k.GetValue("UBR"));
                }
            }
            catch { }
            try { ulong kb; if (GetPhysicallyInstalledSystemMemory(out kb)) Ram = Math.Round(kb / 1048576.0) + " GB"; } catch { }
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get()) { Model = Clean(o["Manufacturer"] + " " + o["Model"]); break; }
            }
            catch { }
            try
            {
                List<string> g = new List<string>();
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get()) { string n = Clean(Convert.ToString(o["Name"])); if (n.Length > 0 && !g.Contains(n)) g.Add(n); }
                if (g.Count > 0) Gpu = string.Join("\n", g.ToArray());
            }
            catch { }
            try
            {
                long design = 0, full = 0;
                ManagementScope sc = new ManagementScope(@"root\wmi");
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(sc, new ObjectQuery("SELECT DesignedCapacity FROM BatteryStaticData")))
                    foreach (ManagementObject o in s.Get()) { design = Convert.ToInt64(o["DesignedCapacity"]); break; }
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(sc, new ObjectQuery("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity")))
                    foreach (ManagementObject o in s.Get()) { full = Convert.ToInt64(o["FullChargedCapacity"]); break; }
                if (design > 0 && full > 0) BatteryHealth = Math.Min(100, (int)Math.Round(full * 100.0 / design)) + "%";
            }
            catch { }
        }

        private static string Clean(string s)
        {
            if (s == null) return "";
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace(" CPU", "").Replace("  ", " ").Trim();
            int at = s.IndexOf(" @ "); if (at > 0) s = s.Substring(0, at);
            return s;
        }

        private static string LoadFullName()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT FullName FROM Win32_UserAccount WHERE LocalAccount=True AND Name='" + UserName.Replace("'", "''") + "'"))
                    foreach (ManagementObject o in s.Get()) { string f = Convert.ToString(o["FullName"]).Trim(); if (f.Length > 0) return f; }
            }
            catch { }
            return "";
        }

        private static Bitmap LoadAvatar()
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid))
                    if (k != null)
                        foreach (string v in new string[] { "Image448", "Image240", "Image208", "Image192", "Image96" })
                        {
                            string p = k.GetValue(v) as string;
                            Bitmap b = p != null && File.Exists(p) ? Square(p) : null;
                            if (b != null) return b;
                        }
            }
            catch { }
            try
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\AccountPictures");
                if (Directory.Exists(d))
                    foreach (string f in Directory.GetFiles(d, "*.accountpicture-ms"))
                    {
                        byte[] all = File.ReadAllBytes(f);
                        int at = -1;
                        for (int i = all.Length - 3; i >= 0; i--) if (all[i] == 0xFF && all[i + 1] == 0xD8 && all[i + 2] == 0xFF) { at = i; break; }
                        if (at < 0) continue;
                        using (MemoryStream ms = new MemoryStream(all, at, all.Length - at))
                        using (Image img = Image.FromStream(ms)) return Square(img);
                    }
            }
            catch { }
            return null;
        }

        private static Bitmap Square(string path) { try { using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (Image i = Image.FromStream(fs)) return Square(i); } catch { return null; } }
        private static Bitmap Square(Image img)
        {
            int s = Math.Min(img.Width, img.Height), o = Math.Min(256, s);
            Bitmap b = new Bitmap(o, o, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(0, 0, o, o), new Rectangle((img.Width - s) / 2, (img.Height - s) / 2, s, s), GraphicsUnit.Pixel);
            }
            return b;
        }

        public static void DrawInitials(Graphics g, RectangleF r)
        {
            using (LinearGradientBrush lb = new LinearGradientBrush(r, Color.FromArgb(160, 165, 180), Color.FromArgb(105, 110, 128), 90f)) g.FillEllipse(lb, r);
            string n = DisplayName.Trim(), ini = "";
            foreach (string part in n.Split(new char[] { ' ', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)) { ini += char.ToUpperInvariant(part[0]); if (ini.Length == 2) break; }
            if (ini.Length == 0) ini = "?";
            using (Font f = new Font(Ui.Family, r.Height * 0.40f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat()) using (SolidBrush b = new SolidBrush(Color.White))
            { sf.Alignment = sf.LineAlignment = StringAlignment.Center; g.DrawString(ini, f, b, r, sf); }
        }

        // Foto bulat dibuat sekali per ukuran: supersampling 4x lalu diperkecil bikubik -> tepi halus, tidak pecah.
        private static readonly Dictionary<int, Bitmap> _av = new Dictionary<int, Bitmap>();
        public static void ResetAvatarCache() { lock (_av) { foreach (Bitmap b in _av.Values) b.Dispose(); _av.Clear(); } }

        public static void DrawAvatar(Graphics g, RectangleF r)
        {
            int s = Math.Max(4, (int)Math.Round(r.Width));
            Bitmap b;
            lock (_av) if (!_av.TryGetValue(s, out b)) { b = MakeAvatar(s); _av[s] = b; }
            PixelOffsetMode po = g.PixelOffsetMode; InterpolationMode im = g.InterpolationMode;
            g.PixelOffsetMode = PixelOffsetMode.Half; g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(b, new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), s, s));
            g.PixelOffsetMode = po; g.InterpolationMode = im;
        }

        private static Bitmap MakeAvatar(int s)
        {
            int big = s * 4;
            using (Bitmap hi = new Bitmap(big, big, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(hi))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    RectangleF rr = new RectangleF(0, 0, big, big);
                    Bitmap src = Avatar;
                    if (src != null)
                        using (Bitmap scaled = new Bitmap(big, big, PixelFormat.Format32bppPArgb))
                        {
                            using (Graphics sg = Graphics.FromImage(scaled))
                            using (ImageAttributes ia = new ImageAttributes())
                            {
                                sg.InterpolationMode = InterpolationMode.HighQualityBicubic; sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                ia.SetWrapMode(WrapMode.TileFlipXY);
                                sg.DrawImage(src, new Rectangle(0, 0, big, big), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                            }
                            using (TextureBrush tb = new TextureBrush(scaled)) g.FillEllipse(tb, rr);
                        }
                    else DrawInitials(g, rr);
                }
                Bitmap o = new Bitmap(s, s, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(o))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(hi, new Rectangle(0, 0, s, s), 0, 0, big, big, GraphicsUnit.Pixel, ia);
                }
                return o;
            }
        }

        public static string Uptime
        {
            get
            {
                TimeSpan t = TimeSpan.FromMilliseconds(GetTickCount64());
                if (t.TotalDays >= 1) return (int)t.TotalDays + " hari " + t.Hours + " jam";
                if (t.TotalHours >= 1) return t.Hours + " jam " + t.Minutes + " mnt";
                return t.Minutes + " menit";
            }
        }

        public static string Disk
        {
            get
            {
                try
                {
                    DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                    return Gb(d.AvailableFreeSpace) + " kosong dari " + Gb(d.TotalSize);
                }
                catch { return "-"; }
            }
        }
        private static string Gb(long b) { double g = b / 1073741824.0; return g >= 100 ? Math.Round(g) + " GB" : g.ToString("0.#") + " GB"; }

        public static string Battery
        {
            get
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0) return "Tanpa baterai";
                string s = (int)Math.Round(ps.BatteryLifePercent * 100) + "%" + (ps.PowerLineStatus == PowerLineStatus.Online ? " (mengisi)" : "");
                if (BatteryHealth != null) s += "\nkesehatan " + BatteryHealth;
                return s;
            }
        }
    }

    // ============================================================
    //  PANEL PROFIL (dari ikon pengguna di bilah atas)
    // ============================================================
    internal sealed class ProfileForm : GlassPopup
    {
        private readonly Font _name = Ui.F(12.5f, FontStyle.Bold), _t = Ui.F(9.5f, FontStyle.Regular), _s = Ui.F(8.5f, FontStyle.Regular), _sb = Ui.F(8.5f, FontStyle.Bold);
        private readonly Font _gl = Glyphs.Font(Ui.Pf(15));
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private int _hover = -1;

        public ProfileForm()
        {
            Text = "Profil";
            Deactivate += delegate { Hide(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
        }

        public void Toggle(int anchorRight, int top)
        {
            if (Visible) { Hide(); return; }
            SysInfo.LoadAsync(this, delegate { if (Visible) Invalidate(); });
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            int w = Ui.P(340);
            int x = Math.Min(scr.Right - w - Ui.P(8), Math.Max(scr.Left + Ui.P(8), anchorRight - w));
            int h;
            using (Graphics g = CreateGraphics()) h = (int)Math.Ceiling(PaintAll(g, w, true));
            ShowAt(new Rectangle(x, top + Ui.P(6), w, h), 0.42f);
            Round(true);
        }

        protected override void OnPaint(PaintEventArgs e) { PaintAll(e.Graphics, Width, false); }

        public void SnapshotTo(string path)
        {
            SysInfo.LoadNow();
            int w = Ui.P(340), h;
            using (Bitmap m = new Bitmap(10, 10)) using (Graphics g = Graphics.FromImage(m)) h = (int)Math.Ceiling(PaintAll(g, w, true));
            using (Bitmap b = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(38, 36, 48)); PaintAll(g, w, false); }
                b.Save(path, ImageFormat.Png);
            }
        }

        private static float R(float v) { return (float)Math.Round(v); }

        private float PaintAll(Graphics g, int W, bool measure)
        {
            Hq(g);
            _hits.Clear();
            float pad = Ui.Pf(14), y = pad;
            using (SolidBrush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(160, 225, 228, 240)))
            using (SolidBrush card = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            using (Pen line = new Pen(Color.FromArgb(40, 255, 255, 255)))
            {
                // kepala: foto + nama
                float av = Ui.Pf(56);
                RectangleF head = new RectangleF(pad, y, W - pad * 2, av + Ui.Pf(20));
                if (!measure)
                {
                    using (GraphicsPath hp = Glass.Round(head, Ui.Pf(12))) { g.FillPath(card, hp); g.DrawPath(line, hp); }
                    SysInfo.DrawAvatar(g, new RectangleF(head.X + Ui.Pf(10), head.Y + Ui.Pf(10), av, av));
                    float tx = head.X + av + Ui.Pf(22), tw = head.Right - tx - Ui.Pf(10);
                    g.DrawString(Fit(g, SysInfo.DisplayName, _name, tw), _name, white, R(tx), R(head.Y + Ui.Pf(14)));
                    string sub = !string.IsNullOrEmpty(SysInfo.Email) ? SysInfo.Email : SysInfo.UserName;
                    g.DrawString(Fit(g, sub, _s, tw), _s, soft, R(tx), R(head.Y + Ui.Pf(38)));
                    g.DrawString(SysInfo.Admin ? "Administrator" : "Pengguna standar", _s, soft, R(tx), R(head.Y + Ui.Pf(53)));
                }
                y = head.Bottom + Ui.Pf(10);

                // rincian sistem
                string[,] rows = {
                    { "Perangkat", SysInfo.Device }, { "Model", SysInfo.Model }, { "Prosesor", SysInfo.Cpu }, { "Grafis", SysInfo.Gpu },
                    { "Memori", SysInfo.Ram }, { "Penyimpanan", SysInfo.Disk }, { "Sistem", SysInfo.Os }, { "Menyala", SysInfo.Uptime }, { "Baterai", SysInfo.Battery } };
                float lw = Ui.Pf(86), rowGap = Ui.Pf(6);
                float top = y;
                float cy = y + Ui.Pf(10);
                for (int i = 0; i < rows.GetLength(0); i++)
                {
                    string[] lines = (rows[i, 1] ?? "-").Split('\n');
                    foreach (string l in lines) cy += _t.GetHeight(g) + Ui.Pf(1);
                    cy += rowGap;
                }
                RectangleF info = new RectangleF(pad, top, W - pad * 2, cy - top + Ui.Pf(4));
                if (!measure) using (GraphicsPath ip = Glass.Round(info, Ui.Pf(12))) { g.FillPath(card, ip); g.DrawPath(line, ip); }
                if (!measure)
                {
                    // gambar ulang teks di atas kartu (kartu diisi setelah ukuran diketahui)
                    float yy = top + Ui.Pf(10);
                    for (int i = 0; i < rows.GetLength(0); i++)
                    {
                        string[] lines = (rows[i, 1] ?? "-").Split('\n');
                        float vx = pad + Ui.Pf(12) + lw, vw = W - pad - Ui.Pf(12) - vx;
                        g.DrawString(rows[i, 0], _s, soft, R(pad + Ui.Pf(12)), R(yy + Ui.Pf(1)));
                        foreach (string l in lines) { g.DrawString(Fit(g, l, _t, vw), _t, white, R(vx), R(yy)); yy += _t.GetHeight(g) + Ui.Pf(1); }
                        yy += rowGap;
                    }
                }
                y = info.Bottom + Ui.Pf(10);

                // tombol
                string[] labels = { "Akun", "Tentang PC", "Kunci", "Keluar" };
                string[] glyphs = { "", "", Glyphs.Lock, "" };
                Action[] acts = {
                    delegate { Hide(); Shell.Run("ms-settings:accounts", null); },
                    delegate { Hide(); Shell.Run("ms-settings:about", null); },
                    delegate { Hide(); MacLock.LockNow(); },
                    delegate { Hide(); if (MessageBox.Show("Keluar dari akun Windows sekarang?", "LiquidMac", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Shell.Run("shutdown.exe", "/l"); } };
                float bw = (W - pad * 2 - Ui.Pf(8) * 3) / 4f, bh = Ui.Pf(54);
                for (int i = 0; i < 4; i++)
                {
                    RectangleF br = new RectangleF(pad + i * (bw + Ui.Pf(8)), y, bw, bh);
                    _hits.Add(new KeyValuePair<RectangleF, Action>(br, acts[i]));
                    if (measure) continue;
                    using (GraphicsPath bp = Glass.Round(br, Ui.Pf(12)))
                    using (SolidBrush bb = new SolidBrush(Color.FromArgb(i == _hover ? 58 : 30, 255, 255, 255))) { g.FillPath(bb, bp); g.DrawPath(line, bp); }
                    Glyphs.Draw(g, glyphs[i], _gl, white, new RectangleF(br.X, br.Y + Ui.Pf(6), br.Width, Ui.Pf(24)));
                    using (StringFormat sf = new StringFormat()) { sf.Alignment = StringAlignment.Center; g.DrawString(labels[i], _s, white, new RectangleF(br.X, br.Y + Ui.Pf(32), br.Width, Ui.Pf(18)), sf); }
                }
                y += bh + pad;
            }
            return y;
        }

        private static string Fit(Graphics g, string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            if (g.MeasureString(s, f).Width <= maxW) return s;
            while (s.Length > 1 && g.MeasureString(s + "…", f).Width > maxW) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < _hits.Count; i++) if (_hits[i].Key.Contains(e.Location)) h = i;
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != -1) { _hover = -1; Invalidate(); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            foreach (KeyValuePair<RectangleF, Action> kv in _hits) if (kv.Key.Contains(e.Location)) { kv.Value(); return; }
        }
    }
}
