// LiquidMac - ikon Explorer gaya macOS, tombol jendela "traffic light", dan suara sistem gaya macOS.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiquidMac
{
    // ============================================================
    //  PABRIK IKON (digambar sendiri, disimpan sebagai .ico multi-ukuran)
    // ============================================================
    internal static class IconFactory
    {
        public static readonly string Dir = Path.Combine(Log.Dir, "icons");
        private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

        private static Graphics Hq(Bitmap b)
        {
            Graphics g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        private static GraphicsPath RR(float x, float y, float w, float h, float r) { return Glass.Round(new RectangleF(x, y, w, h), r); }

        public static Bitmap Folder(int s)
        {
            Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Hq(b))
            {
                float r = Math.Max(1f, s * 0.07f);
                // bayangan
                using (GraphicsPath sh = RR(s * 0.07f, s * 0.22f, s * 0.86f, s * 0.68f, r))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(50, 0, 40, 90))) { g.TranslateTransform(0, s * 0.02f); g.FillPath(sb, sh); g.ResetTransform(); }
                // tab + badan belakang
                using (GraphicsPath tab = RR(s * 0.06f, s * 0.13f, s * 0.38f, s * 0.16f, r))
                using (GraphicsPath back = RR(s * 0.06f, s * 0.19f, s * 0.88f, s * 0.68f, r))
                using (LinearGradientBrush bb = new LinearGradientBrush(new RectangleF(0, s * 0.12f, s, s * 0.76f), Color.FromArgb(255, 78, 168, 236), Color.FromArgb(255, 40, 128, 214), 90f))
                { g.FillPath(bb, tab); g.FillPath(bb, back); }
                // kertas
                using (GraphicsPath paper = RR(s * 0.12f, s * 0.24f, s * 0.76f, s * 0.30f, r * 0.6f))
                using (SolidBrush pb = new SolidBrush(Color.FromArgb(240, 250, 252, 255))) g.FillPath(pb, paper);
                // lipatan depan
                using (GraphicsPath front = RR(s * 0.06f, s * 0.32f, s * 0.88f, s * 0.56f, r))
                {
                    using (LinearGradientBrush fb = new LinearGradientBrush(new RectangleF(0, s * 0.31f, s, s * 0.58f), Color.FromArgb(255, 132, 206, 250), Color.FromArgb(255, 82, 176, 240), 90f))
                        g.FillPath(fb, front);
                    if (s >= 24)
                        using (Pen hl = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, s / 64f))) g.DrawLine(hl, s * 0.10f, s * 0.335f, s * 0.90f, s * 0.335f);
                }
            }
            return b;
        }

        public static Bitmap Drive(int s)
        {
            Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Hq(b))
            {
                float r = s * 0.08f;
                RectangleF body = new RectangleF(s * 0.08f, s * 0.30f, s * 0.84f, s * 0.42f);
                using (GraphicsPath sh = Glass.Round(new RectangleF(body.X, body.Y + s * 0.03f, body.Width, body.Height), r)) using (SolidBrush sb = new SolidBrush(Color.FromArgb(55, 0, 0, 0))) g.FillPath(sb, sh);
                using (GraphicsPath p = Glass.Round(body, r))
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(body, Color.FromArgb(255, 238, 239, 243), Color.FromArgb(255, 170, 173, 182), 90f)) g.FillPath(lb, p);
                    using (Pen pen = new Pen(Color.FromArgb(120, 90, 92, 100), Math.Max(1f, s / 96f))) g.DrawPath(pen, p);
                }
                using (SolidBrush strip = new SolidBrush(Color.FromArgb(70, 60, 62, 70))) g.FillRectangle(strip, body.X + r, body.Bottom - body.Height * 0.32f, body.Width - r * 2, Math.Max(1f, s * 0.02f));
                float d = Math.Max(2f, s * 0.06f);
                using (SolidBrush led = new SolidBrush(Color.FromArgb(255, 52, 210, 90))) g.FillEllipse(led, body.Right - d * 2.4f, body.Bottom - body.Height * 0.62f, d, d);
            }
            return b;
        }

        public static Bitmap Computer(int s)
        {
            Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Hq(b))
            {
                RectangleF mon = new RectangleF(s * 0.06f, s * 0.12f, s * 0.88f, s * 0.58f);
                using (GraphicsPath p = Glass.Round(mon, s * 0.06f)) using (SolidBrush bz = new SolidBrush(Color.FromArgb(255, 40, 40, 46))) g.FillPath(bz, p);
                RectangleF scr = new RectangleF(mon.X + s * 0.035f, mon.Y + s * 0.035f, mon.Width - s * 0.07f, mon.Height - s * 0.07f);
                using (GraphicsPath sp = Glass.Round(scr, s * 0.03f))
                using (LinearGradientBrush wb = new LinearGradientBrush(scr, Color.FromArgb(255, 110, 90, 240), Color.FromArgb(255, 250, 100, 150), 35f)) g.FillPath(wb, sp);
                PointF[] stand = { new PointF(s * 0.42f, mon.Bottom), new PointF(s * 0.58f, mon.Bottom), new PointF(s * 0.62f, s * 0.84f), new PointF(s * 0.38f, s * 0.84f) };
                using (LinearGradientBrush st = new LinearGradientBrush(new RectangleF(0, mon.Bottom, s, s * 0.16f), Color.FromArgb(255, 200, 202, 210), Color.FromArgb(255, 150, 152, 160), 90f)) g.FillPolygon(st, stand);
                using (GraphicsPath bp = Glass.Round(new RectangleF(s * 0.30f, s * 0.83f, s * 0.40f, s * 0.05f), s * 0.02f)) using (SolidBrush bs = new SolidBrush(Color.FromArgb(255, 175, 177, 185))) g.FillPath(bs, bp);
            }
            return b;
        }

        public static Bitmap Trash(int s, bool full)
        {
            Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Hq(b))
            {
                RectangleF r = new RectangleF(s * 0.20f, s * 0.08f, s * 0.60f, s * 0.86f);
                float lidH = r.Height * 0.11f;
                RectangleF body = new RectangleF(r.X + r.Width * 0.05f, r.Y + lidH, r.Width * 0.90f, r.Height - lidH);
                using (GraphicsPath bp = Glass.Round(body, r.Width * 0.12f))
                {
                    using (LinearGradientBrush gb = new LinearGradientBrush(body, Color.FromArgb(235, 236, 238, 244), Color.FromArgb(220, 178, 182, 196), 0f)) g.FillPath(gb, bp);
                    g.SetClip(bp);
                    using (Pen mesh = new Pen(Color.FromArgb(80, 70, 72, 84), Math.Max(1f, s / 48f)))
                        for (float lx = body.X + body.Width / 7f; lx < body.Right; lx += body.Width / 7f) g.DrawLine(mesh, lx, body.Y, lx, body.Bottom);
                    if (full)
                        using (SolidBrush paper = new SolidBrush(Color.FromArgb(250, 252, 252, 255)))
                        using (Pen fold = new Pen(Color.FromArgb(90, 120, 120, 140), Math.Max(1f, s / 96f)))
                        {
                            RectangleF p1 = new RectangleF(body.X + body.Width * 0.06f, body.Y - body.Height * 0.12f, body.Width * 0.55f, body.Height * 0.36f);
                            RectangleF p2 = new RectangleF(body.X + body.Width * 0.40f, body.Y - body.Height * 0.08f, body.Width * 0.52f, body.Height * 0.30f);
                            g.FillEllipse(paper, p1); g.DrawEllipse(fold, p1); g.FillEllipse(paper, p2); g.DrawEllipse(fold, p2);
                        }
                    g.ResetClip();
                    using (Pen edge = new Pen(Color.FromArgb(150, 120, 122, 134), Math.Max(1f, s / 64f))) g.DrawPath(edge, bp);
                }
                using (GraphicsPath lp = Glass.Round(new RectangleF(r.X, r.Y, r.Width, lidH), lidH / 2))
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, lidH + 1), Color.FromArgb(255, 240, 241, 246), Color.FromArgb(255, 190, 193, 205), 90f)) g.FillPath(lb, lp);
            }
            return b;
        }

        public static void SaveIco(string path, Func<int, Bitmap> draw)
        {
            List<byte[]> pngs = new List<byte[]>();
            foreach (int sz in Sizes)
                using (Bitmap bm = draw(sz))
                using (MemoryStream ms = new MemoryStream()) { bm.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (FileStream fs = File.Create(path))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);
                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int sz = Sizes[i];
                    w.Write((byte)(sz >= 256 ? 0 : sz)); w.Write((byte)(sz >= 256 ? 0 : sz));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }

        public static void Generate()
        {
            Directory.CreateDirectory(Dir);
            SaveIco(Path.Combine(Dir, "folder.ico"), Folder);
            SaveIco(Path.Combine(Dir, "drive.ico"), Drive);
            SaveIco(Path.Combine(Dir, "computer.ico"), Computer);
            SaveIco(Path.Combine(Dir, "trash-empty.ico"), delegate (int s) { return Trash(s, false); });
            SaveIco(Path.Combine(Dir, "trash-full.ico"), delegate (int s) { return Trash(s, true); });
        }
    }

    // ============================================================
    //  PASANG / KEMBALIKAN IKON EXPLORER
    // ============================================================
    internal static class ExplorerIcons
    {
        private const string ThisPc = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\DefaultIcon";
        private const string Bin = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon";

        public static bool Applied { get { return Settings.Get("MacExplorerIcons", false); } }

        private static void RunElevated(string script)
        {
            string ps = Path.Combine(Path.GetTempPath(), "liquidmac-icons.ps1");
            File.WriteAllText(ps, script);
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\"");
            psi.Verb = "runas"; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process p = Process.Start(psi)) p.WaitForExit(60000);
        }

        private static void RefreshShell()
        {
            try { using (Process p = Process.Start(new ProcessStartInfo("ie4uinit.exe", "-show") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(10000); } catch { }
            foreach (Process e in Process.GetProcessesByName("explorer")) { try { e.Kill(); } catch { } }
            System.Threading.Thread.Sleep(1500);
            if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
        }

        public static void Apply()
        {
            IconFactory.Generate();
            string d = IconFactory.Dir;
            // bagian per pengguna (tanpa admin)
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ThisPc)) k.SetValue("", Path.Combine(d, "computer.ico") + ",0");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Bin))
            {
                if (k.GetValue("LiquidMacBackupEmpty") == null) { k.SetValue("LiquidMacBackupEmpty", k.GetValue("empty") ?? ""); k.SetValue("LiquidMacBackupFull", k.GetValue("full") ?? ""); k.SetValue("LiquidMacBackupDefault", k.GetValue("") ?? ""); }
                k.SetValue("", Path.Combine(d, "trash-full.ico") + ",0");
                k.SetValue("full", Path.Combine(d, "trash-full.ico") + ",0");
                k.SetValue("empty", Path.Combine(d, "trash-empty.ico") + ",0");
            }
            // bagian sistem: ikon folder & drive (butuh admin)
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$si='HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons'; New-Item -Force $si | Out-Null");
            sb.AppendLine("Set-ItemProperty $si -Name '3' -Value '" + Path.Combine(d, "folder.ico") + ",0'");
            sb.AppendLine("Set-ItemProperty $si -Name '4' -Value '" + Path.Combine(d, "folder.ico") + ",0'");
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                if (di.DriveType != DriveType.Fixed) continue;
                string letter = di.Name.Substring(0, 1);
                sb.AppendLine("$k='HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\DriveIcons\\" + letter + "\\DefaultIcon'; New-Item -Force $k | Out-Null; Set-ItemProperty $k -Name '(default)' -Value '" + Path.Combine(d, "drive.ico") + ",0'");
            }
            RunElevated(sb.ToString());
            Settings.Set("MacExplorerIcons", true);
            RefreshShell();
        }

        public static void Restore()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(ThisPc.Substring(0, ThisPc.LastIndexOf('\\')), false); } catch { }
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Bin, true))
            {
                if (k != null)
                {
                    object e = k.GetValue("LiquidMacBackupEmpty"), f = k.GetValue("LiquidMacBackupFull"), df = k.GetValue("LiquidMacBackupDefault");
                    if (e != null && (string)e != "") k.SetValue("empty", e); else k.DeleteValue("empty", false);
                    if (f != null && (string)f != "") k.SetValue("full", f); else k.DeleteValue("full", false);
                    if (df != null && (string)df != "") k.SetValue("", df); else k.DeleteValue("", false);
                    k.DeleteValue("LiquidMacBackupEmpty", false); k.DeleteValue("LiquidMacBackupFull", false); k.DeleteValue("LiquidMacBackupDefault", false);
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$si='HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons'; Remove-ItemProperty $si -Name '3','4' -ErrorAction SilentlyContinue");
            sb.AppendLine("Get-ChildItem 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\DriveIcons' -ErrorAction SilentlyContinue | ForEach-Object { $d=Join-Path $_.PSPath 'DefaultIcon'; if ((Get-ItemProperty $d -ErrorAction SilentlyContinue).'(default)' -like '*LiquidMac*') { Remove-Item $_.PSPath -Recurse -Force } }");
            RunElevated(sb.ToString());
            Settings.Set("MacExplorerIcons", false);
            RefreshShell();
        }
    }

    // ============================================================
    //  TOMBOL JENDELA "TRAFFIC LIGHT" (menutupi tombol min/max/close bawaan)
    // ============================================================
    internal sealed class TrafficLightForm : OverlayForm
    {
        public readonly IntPtr Target;
        private Native.RECT _frame;           // bingkai jendela yang terlihat (tanpa border tak kasatmata)
        private int _capH, _patchW;          // tinggi baris judul & lebar tombol asli (diukur dari piksel)
        private Color _bg = Color.FromArgb(32, 32, 32);
        private int _hover = -1, _tick, _measure, _measureFails, _settle, _recol;   // _measure: hitung mundur ukur ulang; _settle: tunggu jendela berhenti berubah
        private bool _active, _gone, _zoomed, _custom, _measuredOk;
        private int _goodCap, _goodOne; private bool _goodZoom;
        private int _inset;   // strip di tepi kanan yang bukan baris judul (mis. Chrome maximized)

        // Warna terbanyak di satu baris (latar baris judul), tahan terhadap strip/tepi berbeda warna.
        private static Color Mode(Bitmap b, int y, int x0, int x1)
        {
            Dictionary<int, int> cnt = new Dictionary<int, int>(); int best = 0, bestK = 0;
            for (int x = Math.Max(0, x0); x < Math.Min(b.Width, x1); x++)
            {
                Color c = b.GetPixel(x, y); int k = ((c.R >> 3) << 10) | ((c.G >> 3) << 5) | (c.B >> 3);
                int n; cnt.TryGetValue(k, out n); cnt[k] = ++n;
                if (n > best) { best = n; bestK = k; }
            }
            for (int x = Math.Max(0, x0); x < Math.Min(b.Width, x1); x++)
            { Color c = b.GetPixel(x, y); if ((((c.R >> 3) << 10) | ((c.G >> 3) << 5) | (c.B >> 3)) == bestK) return c; }
            return b.GetPixel(b.Width - 3, y);
        }   // pengukuran terakhir yang berhasil (cegah lompat saat ukur ulang gagal)
        private bool Unready { get { return !_measuredOk && (_custom || _measureFails < 8); } }
        private DateTime _holdUntil = DateTime.MinValue;
        private int _enhLvl;
        private bool _affNone;      // true: tidak dikecualikan dari tangkapan (Layar Jernih aktif -> ikut filter warna Windows)
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();

        // Ambil piksel layar di bawah patch. Saat patch ikut terfilter (Layar Jernih), pengecualian dipasang sesaat.
        private void Grab(Graphics g, int x, int y, Size sz)
        {
            bool tmp = _affNone && IsHandleCreated;
            if (tmp) { SetWindowDisplayAffinity(Handle, 0x11); DwmFlush(); DwmFlush(); }
            try { g.CopyFromScreen(x, y, 0, 0, sz); }
            finally { if (tmp) SetWindowDisplayAffinity(Handle, 0); }
        }    // selama animasi minimize/maximize: jangan gambar patch

        [StructLayout(LayoutKind.Sequential)] private struct ANIMATIONINFO { public int cbSize, iMinAnimate; }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int act, int p, ref ANIMATIONINFO ai, int win);
        private static int _animOn = -1;
        private static bool Animations
        {
            get
            {
                // dibaca ulang tiap kali: animasi Windows bisa dimatikan LiquidMac (Animasi jendela LiquidMac)
                ANIMATIONINFO ai = new ANIMATIONINFO(); ai.cbSize = Marshal.SizeOf(typeof(ANIMATIONINFO));
                _animOn = SystemParametersInfo(0x0048 /* SPI_GETANIMATION */, ai.cbSize, ref ai, 0) && ai.iMinAnimate == 0 ? 0 : 1;
                return _animOn == 1;
            }
        }

        // Jendela sedang beranimasi (gambar DWM bergerak, posisi akhir sudah dilaporkan): patch disembunyikan sebentar.
        public void Hold(int ms)
        {
            if (!Animations) return;
            DateTime until = DateTime.Now.AddMilliseconds(ms);
            if (until > _holdUntil) _holdUntil = until;
            Dirty = true;
        }

        private bool Holding { get { return DateTime.Now < _holdUntil; } }
        private readonly Font _gl = Glyphs.Font(Ui.Pf(9));

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT r, int size);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint aff);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);

        protected override void OnHandleCreated(EventArgs e)
        {
            // Tidak ikut tertangkap layar: tombol asli bisa diukur & diambil warnanya tanpa menyembunyikan patch.
            SetWindowDisplayAffinity(Handle, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
            base.OnHandleCreated(e);
        }

        // Apakah titik ini benar-benar milik jendela target (tidak tertutup menu/jendela lain)?
        private static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        private bool Owns(int x, int y)
        {
            int guard = 0;
            for (IntPtr w = GetWindow(Target, 3 /* GW_HWNDPREV */); w != IntPtr.Zero && guard < 400; w = GetWindow(w, 3), guard++)
            {
                if (w == Handle || !Native.IsWindowVisible(w)) continue;
                uint wp; Native.GetWindowThreadProcessId(w, out wp);
                if (wp == OwnPid) continue;   // overlay LiquidMac sendiri (notch, bilah, traffic light lain) tidak dihitung
                int cl; if (Native.DwmGetWindowAttribute(w, 14, out cl, 4) == 0 && cl != 0) continue;
                Native.RECT r; if (!Native.GetWindowRect(w, out r)) continue;
                if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return false;
            }
            return true;
        }

        protected override bool Topmost { get { return false; } }
        public bool Gone { get { return _gone; } }

        public TrafficLightForm(IntPtr target) { Target = target; }

        // Gambar traffic light ke tangkapan jendela (untuk animasi LiquidMac).
        public void PaintInto(Graphics g, Rectangle frame)
        {
            if (!Visible || Bmp == null || Holding || Unready) return;
            g.DrawImage(Bmp, Left - frame.Left, Top - frame.Top);
        }

        // Bingkai terlihat + area tombol caption dari DWM (untuk memastikan jendela punya tombol standar).
        public static bool Geometry(IntPtr h, out Native.RECT frame, out Native.RECT btn)
        {
            bool custom; return Geometry(h, out frame, out btn, out custom);
        }

        // custom = aplikasi menggambar tombolnya sendiri (Electron, Spotify, Discord, VS Code, dll.):
        // DWM tidak tahu letaknya, jadi diasumsikan di pojok kanan atas dan WAJIB lolos deteksi simbol X.
        public static bool Geometry(IntPtr h, out Native.RECT frame, out Native.RECT btn, out bool custom)
        {
            custom = false;
            frame = new Native.RECT(); btn = new Native.RECT();
            Native.RECT wr, b;
            if (!Native.GetWindowRect(h, out wr)) return false;
            if (DwmGetWindowAttribute(h, 9 /* EXTENDED_FRAME_BOUNDS */, out frame, Marshal.SizeOf(typeof(Native.RECT))) != 0) frame = wr;
            if (Native.IsZoomed(h))
            {
                // jendela maximize menjorok ke luar layar; pakai bagian yang benar-benar terlihat
                Rectangle wa = Screen.FromHandle(h).WorkingArea;
                frame.Left = Math.Max(frame.Left, wa.Left); frame.Top = Math.Max(frame.Top, wa.Top);
                frame.Right = Math.Min(frame.Right, wa.Right); frame.Bottom = Math.Min(frame.Bottom, wa.Bottom);
            }
            int w = 0, hh = 0;
            if (DwmGetWindowAttribute(h, 5 /* CAPTION_BUTTON_BOUNDS */, out b, Marshal.SizeOf(typeof(Native.RECT))) == 0) { w = b.Right - b.Left; hh = b.Bottom - b.Top; }
            if (w < Ui.P(60) || w > Ui.P(600) || hh < Ui.P(16) || hh > Ui.P(64))
            {
                int st = Native.GetWindowLong(h, -16);
                if ((st & 0x00030000) == 0) return false;          // tanpa tombol minimize/maximize
                custom = true;
                btn.Left = frame.Right - Ui.P(138); btn.Top = frame.Top; btn.Right = frame.Right; btn.Bottom = frame.Top + Ui.P(32);
            }
            else { btn.Left = wr.Left + b.Left; btn.Top = wr.Top + b.Top; btn.Right = wr.Left + b.Right; btn.Bottom = wr.Top + b.Bottom; }
            return frame.Right - frame.Left > Ui.P(200);
        }

        private static Color Px(IntPtr dc, int x, int y)
        {
            uint c = GetPixel(dc, x, y);
            if (c == 0xFFFFFFFF) return Color.Empty;
            return Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
        }

        private static int Diff(Color a, Color b) { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); }

        // Ukur tombol asli dari tangkapan layar: cari simbol "X" tombol close.
        // Pusat X -> tinggi baris judul (2 x jarak ke atas) dan lebar satu tombol (2 x jarak ke kanan).
        private string _why = "";
        private void MeasureCaption(Native.RECT btn)
        {
            int dwmH = Math.Max(Ui.P(24), Math.Min(Ui.P(48), btn.Bottom - Math.Max(btn.Top, _frame.Top)));
            int dwmW = btn.Right - btn.Left;
            // area tombol tertutup jendela lain: tunda (tidak dihitung gagal, ukuran lama tetap dipakai)
            if (!Owns(_frame.Right - Ui.P(24), _frame.Top + Ui.P(10)) || !Owns(_frame.Right - Ui.P(60), _frame.Top + Ui.P(10)))
            {
                _measure = 30;
                return;
            }
            int W = Ui.P(90), H = Ui.P(56);   // cukup untuk baris judul; ikon toolbar di bawahnya tidak ikut
            int capH = -1, oneW = -1;
            try
            {
                using (Bitmap shot = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(shot)) Grab(g, _frame.Right - W, _frame.Top, new Size(W, H));
                    Color bg = Mode(shot, Ui.P(6), W - Ui.P(70), W);
                    int edge = W - 1;
                    while (edge > W - Ui.P(16) && Diff(shot.GetPixel(edge, Ui.P(6)), bg) > 30 && Diff(shot.GetPixel(edge, Ui.P(14)), bg) > 30) edge--;
                    _inset = W - 1 - edge;
                    foreach (int thr in new int[] { 90, 40 })   // kontras tinggi dulu, lalu X pudar (jendela tidak aktif)
                    {
                    if (capH > 0) break;
                    int x0 = edge - Ui.P(40), x1 = edge - 1, band = x1 - x0;
                    int top = -1, bottom = -1, minX = W, maxX = -1;
                    for (int y = Ui.P(4); y < H; y++)
                    {
                        int cnt = 0, rmin = W, rmax = -1;
                        for (int x = x0; x < x1; x++)
                            if (Diff(shot.GetPixel(x, y), bg) > thr) { cnt++; if (x < rmin) rmin = x; if (x > rmax) rmax = x; }
                        bool glyphRow = cnt > 0 && cnt < band * 0.5;      // garis penuh (border/toolbar) bukan glyph
                        if (cnt >= band * 0.5) { if (top >= 0) { bottom = y - 1; break; } continue; }
                        if (glyphRow) { if (top < 0) top = y; bottom = y; if (rmin < minX) minX = rmin; if (rmax > maxX) maxX = rmax; }
                        else if (top >= 0 && y - bottom > Ui.P(3)) break;   // jeda > 3 px: glyph selesai
                    }
                    int gw = maxX - minX + 1, gh = bottom - top + 1;
                    bool square = Math.Abs(gw - gh) <= Math.Max(Ui.P(3), gh * 0.4f) && gh >= Ui.P(6);
                    if (top >= 0 && top < Ui.P(36) && bottom > top && bottom - top <= Ui.P(16) && gw <= Ui.P(18) && maxX > minX && square)
                    {
                        float cy = (top + bottom) / 2f, cx = (minX + maxX) / 2f;
                        capH = (int)Math.Round(cy * 2 + 1);
                        oneW = (int)Math.Round((edge + 1 - cx) * 2);
                    }
                    }
                }
            }
            catch (Exception ex) { Log.Write("measure: " + ex.Message); }
            bool ok = capH >= Ui.P(20) && capH <= Ui.P(64) && oneW >= Ui.P(30) && oneW <= Ui.P(70);
            bool zoomNow = Native.IsZoomed(Target);
            if (ok) { _goodCap = capH; _goodOne = oneW; _goodZoom = zoomNow; }
            else if (_goodCap > 0 && _goodZoom == zoomNow) { capH = _goodCap; oneW = _goodOne; ok = true; }   // pakai ukuran terakhir yang benar
            _measureFails = ok ? 0 : _measureFails + 1;
            _measuredOk = ok;
            if (!ok && _measureFails < 8) _measure = _measureFails < 3 ? 6 : 30;   // coba lagi sebentar lagi (mis. jendela belum selesai digambar)
            _capH = capH >= Ui.P(20) && capH <= Ui.P(64) ? capH : dwmH;
            int w3 = oneW >= Ui.P(30) && oneW <= Ui.P(70) ? oneW * 3 : Math.Max(dwmW, Ui.P(138));
            _patchW = Math.Min(Ui.P(220), w3 + Ui.P(2));
            if (_custom && ok) TrimLeft(oneW);
            if (System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug")))
                Log.Write("tl measure " + _why + " " + Target + " " + Native.ClassOf(Target) + " frame=" + _frame.Left + "," + _frame.Top + "," + _frame.Right + "," + _frame.Bottom +
                    " btn=" + btn.Left + "," + btn.Top + "," + btn.Right + "," + btn.Bottom + " custom=" + _custom + " capH=" + capH + " oneW=" + oneW +
                    " -> _capH=" + _capH + " patchW=" + _patchW + " ok=" + ok + " inset=" + _inset + " zoom=" + Native.IsZoomed(Target));
        }

        // Tombol buatan aplikasi sering berdempetan dengan tombol lain (mis. unduhan di Comfy/VS Code):
        // geser tepi kiri patch ke kanan sampai kolomnya polos (sewarna latar baris judul).
        private void TrimLeft(int oneW)
        {
            try
            {
                using (Bitmap strip = new Bitmap(_patchW, _capH, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(strip)) Grab(g, _frame.Right - _inset - _patchW, _frame.Top, new Size(_patchW, _capH));
                    Color bg = strip.GetPixel(_patchW - oneW + Ui.P(4), _capH / 2);
                    int cut = 0;
                    for (int x = 0; x < oneW / 2; x++)
                    {
                        bool plain = true;
                        for (int y = 1; y < _capH - 1 && plain; y++) if (Diff(strip.GetPixel(x, y), bg) > 24) plain = false;
                        if (plain) { cut = x; break; }
                        cut = x + 1;
                    }
                    _patchW -= cut;
                }
            }
            catch { }
        }

        // Latar per status (0 = tidak aktif, 1 = aktif): kolom piksel dari DALAM tombol minimize asli
        // (bagian kosong di tepi kirinya), diambil saat patch disembunyikan. Jadi warnanya persis sama.
        private readonly Bitmap[] _cols = new Bitmap[2];

        private void CaptureColumn(bool active)
        {
            if (_capH <= 0 || _patchW <= 0) return;
            int cx = _frame.Right - _inset - Math.Max(Ui.P(30), (_patchW - Ui.P(2)) / 3) + Ui.P(4);
            if (!Owns(cx, _frame.Top + 1) || !Owns(cx, _frame.Top + _capH - 2)) return;
            try
            {
                Bitmap col = new Bitmap(1, _capH, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(col)) Grab(g, cx, _frame.Top, new Size(1, _capH));   // tepi kiri tombol close (tidak ada glyph, tidak bersinggungan dengan tombol aplikasi)
                int i = active ? 1 : 0;
                if (_cols[i] != null) _cols[i].Dispose();
                _cols[i] = col;
                _bg = col.GetPixel(0, _capH / 2);
                Dirty = true;
            }
            catch { }
        }

        private void ClearColumns() { for (int i = 0; i < 2; i++) { if (_cols[i] != null) _cols[i].Dispose(); _cols[i] = null; } }

        private void SampleColor() { }

        public override void Reposition()
        {
            Native.RECT f, b;
            if (!Geometry(Target, out f, out b, out _custom)) return;
            _frame = f;
            if (_capH == 0)
            {
                _capH = Math.Max(Ui.P(24), Math.Min(Ui.P(48), b.Bottom - Math.Max(b.Top, f.Top)));
                _patchW = Math.Min(Ui.P(220), Math.Max(Ui.P(120), b.Right - b.Left) + Ui.P(2));
                _zoomed = Native.IsZoomed(Target);
                _why = "init"; MeasureCaption(b);                 // langsung, karena patch tidak ikut tertangkap layar
                CaptureColumn(Native.GetForegroundWindow() == Target);
            }
            Place();
        }

        private void Place()
        {
            Configure(Math.Max(Ui.P(60), _patchW), _capH);
            Location = new Point(_frame.Right - _inset - Math.Max(Ui.P(60), _patchW), _frame.Top);
        }

        protected override bool OnFrame()
        {
            _tick++;
            if (!Native.IsWindow(Target)) { _gone = true; Hide(); return false; }
            bool vis = Native.IsWindowVisible(Target) && !Native.IsIconic(Target) && !Windows.IsFullscreen(Target);
            Native.RECT f, b;
            if (!vis || !Geometry(Target, out f, out b, out _custom)) { if (Visible) Hide(); return false; }

            bool zoomed = Native.IsZoomed(Target);
            if (f.Left != _frame.Left || f.Top != _frame.Top || f.Right != _frame.Right || f.Bottom != _frame.Bottom || zoomed != _zoomed)
            {
                bool sizeChanged = (f.Right - f.Left) != (_frame.Right - _frame.Left) || (f.Bottom - f.Top) != (_frame.Bottom - _frame.Top);
                _frame = f;
                if (zoomed != _zoomed) { _zoomed = zoomed; Hold(240); _settle = 1; }   // maximize/restore: tunggu animasi selesai, lalu ukur
                else if (sizeChanged) _settle = 6;   // patch tetap tampil; ukur ulang setelah jendela berhenti berubah
                Place();
                Dirty = true;
            }
            if (Holding) { if (_settle == 0) _settle = 1; return false; }
            if (_holdUntil != DateTime.MinValue) { _holdUntil = DateTime.MinValue; Dirty = true; }
            if (_settle > 0 && --_settle == 0) { _why = "settle"; MeasureCaption(b); Place(); CaptureColumn(_active); Dirty = true; }
            if (_measure > 0 && --_measure == 0) { _why = "retry" + _measureFails; MeasureCaption(b); Place(); CaptureColumn(_active); Dirty = true; }


            // urutan-Z: tepat di atas jendela target (tanpa ikatan owner lintas proses)
            IntPtr above = Native.GetWindow(Target, 3 /* GW_HWNDPREV */);
            if (above != Handle)
                SetWindowPos(Handle, above == IntPtr.Zero ? IntPtr.Zero : above, 0, 0, 0, 0, 0x0013 /* NOSIZE|NOMOVE|NOACTIVATE */);

            bool act = Native.GetForegroundWindow() == Target;
            bool dirty = false;
            if (act != _active) { _active = act; dirty = true; if (_cols[act ? 1 : 0] == null) CaptureColumn(act); _recol = 16; }
            if (_recol > 0) { _recol--; if (_recol == 12 || _recol == 0) { CaptureColumn(_active); dirty = true; } }   // ikuti animasi warna aktif/tidak aktif
            else if (_tick % (_affNone ? 360 : 90) == 0 && _settle == 0) { CaptureColumn(_active); dirty = true; }                    // ikuti perubahan tema/warna profil
            Point p = LocalCursor();
            int hv = -1;
            for (int i = 0; i < 3; i++) if (Circle(i).Contains(p.X, p.Y)) hv = i;
            RectangleF grp = Group();
            int code = grp.Contains(p.X, p.Y) ? (hv >= 0 ? hv : 9) : -1;
            if (code != _hover) { _hover = code; dirty = true; }
            return dirty;
        }

        private RectangleF Group()
        {
            RectangleF a = Circle(0), c = Circle(2);
            return RectangleF.FromLTRB(a.X - Ui.Pf(6), 0, c.Right + Ui.Pf(6), Bh);
        }

        private RectangleF Circle(int i)
        {
            float d = Ui.Pf(13.65), gap = Ui.Pf(10.5);   // 5% lebih besar
            float x0 = (Bw - (d * 3 + gap * 2)) / 2f;
            return new RectangleF((float)Math.Round(x0 + i * (d + gap)), (float)Math.Round((Bh - d) / 2f + Ui.Pf(1)), d, d);
        }

        protected override void Render()
        {
            G.Clear(Color.Transparent);
            // tutup tombol asli di kanan (kecuali sedang mengukur)
            if (Unready) return;                         // belum terukur: jangan tampil dengan ukuran tebakan (tombol asli tetap terlihat)
            if (Holding) return;                         // jendela sedang beranimasi
            Bitmap _column = _cols[_active ? 1 : 0] ?? _cols[_active ? 0 : 1];
            if (_column != null)
            {
                InterpolationMode im = G.InterpolationMode; PixelOffsetMode pm = G.PixelOffsetMode;
                G.InterpolationMode = InterpolationMode.NearestNeighbor; G.PixelOffsetMode = PixelOffsetMode.Half;
                G.DrawImage(_column, new Rectangle(0, 0, Bw, Bh), 0, 0, 1, _column.Height, GraphicsUnit.Pixel);
                G.InterpolationMode = im; G.PixelOffsetMode = pm;
            }
            else using (SolidBrush pb = new SolidBrush(_bg)) G.FillRectangle(pb, 0, 0, Bw, Bh);

            Color[] on = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            Color[] rim = { Color.FromArgb(224, 68, 62), Color.FromArgb(222, 161, 35), Color.FromArgb(29, 173, 44) };
            string[] glyph = { "", "", _zoomed ? "" : "" };
            bool lit = _active || _hover >= 0;
            for (int i = 0; i < 3; i++)
            {
                RectangleF c = Circle(i);
                using (SolidBrush b = new SolidBrush(lit ? on[i] : Color.FromArgb(255, 110, 110, 116))) G.FillEllipse(b, c);
                if (lit) using (Pen p = new Pen(Color.FromArgb(170, rim[i]), 1f)) G.DrawEllipse(p, c.X + 0.5f, c.Y + 0.5f, c.Width - 1, c.Height - 1);
                if (_hover >= 0)
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(190, 70, 20, 10)))
                        Glyphs.Draw(G, glyph[i], _gl, gb, c);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Unready || Holding) return;
            for (int i = 0; i < 3; i++)
            {
                RectangleF c = Circle(i);
                c.Inflate(Ui.Pf(3), Ui.Pf(5));
                if (!c.Contains(e.Location)) continue;
                if (i == 0) Native.PostMessage(Target, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                else if (i == 1) WinAnim.Minimize(Target);      // animasi LiquidMac (atau minimize biasa bila dimatikan)
                else WinAnim.Zoom(Target);
                return;
            }
        }
    }

    internal sealed class TrafficLights
    {
        private readonly Dictionary<IntPtr, TrafficLightForm> _forms = new Dictionary<IntPtr, TrafficLightForm>();
        private readonly System.Windows.Forms.Timer _scan = new System.Windows.Forms.Timer();
        private static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;

        private readonly System.Windows.Forms.Timer _quick = new System.Windows.Forms.Timer();
        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
        private readonly WinEventProc _proc;
        private readonly List<IntPtr> _hooks = new List<IntPtr>();

        public TrafficLights()
        {
            _scan.Interval = 1500;   // cadangan; jalur utama lewat event sistem
            _scan.Tick += delegate { try { Scan(); } catch (Exception ex) { Log.Write("lights: " + ex.Message); } };
            _quick.Interval = 25;
            _quick.Tick += delegate { _quick.Stop(); try { Scan(); } catch (Exception ex) { Log.Write("lights: " + ex.Message); } };
            _proc = OnEvent;
            WinAnim.PaintOverlay = delegate (IntPtr h, Graphics g, Rectangle f, float sc)
            {
                TrafficLightForm tf;
                if (_forms.TryGetValue(h, out tf)) tf.PaintInto(g, f);
            };
        }

        public bool Running { get { return _scan.Enabled; } }

        private bool _started;
        public void Poke() { if (!_quick.Enabled) _quick.Start(); }

        public void Start()
        {
            _scan.Start(); Scan(); _started = true;
            uint[][] ranges = { new uint[] { 0x0003, 0x0003 }, new uint[] { 0x0016, 0x0017 }, new uint[] { 0x8002, 0x8003 }, new uint[] { 0x800B, 0x800B }, new uint[] { 0x8017, 0x8018 } };
            foreach (uint[] r in ranges)
            {
                IntPtr h = SetWinEventHook(r[0], r[1], IntPtr.Zero, _proc, 0, 0, 0x0002 /* OUTOFCONTEXT | SKIPOWNPROCESS */);
                if (h != IntPtr.Zero) _hooks.Add(h);
            }
        }

        // Event sistem: tampilkan/ikutkan patch seketika, tanpa menunggu pemindaian berkala.
        private void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                if (ev == 0x0017) WinAnim.Forget(hwnd);   // dipulihkan dari luar Dock: tangkapan lama tidak dipakai
                TrafficLightForm f;
                bool known = _forms.TryGetValue(hwnd, out f);
                switch (ev)
                {
                    case 0x0016: case 0x8003: case 0x8017:          // minimize mulai / hide / cloaked
                        if (known && f.Visible) f.Hide();
                        break;
                    case 0x800B:                                    // pindah / ubah ukuran
                        if (known) f.Pump();
                        break;
                    default:                                        // foreground / minimize selesai / show / uncloaked
                        if (known && ev == 0x0017) f.Hold(260);     // animasi keluar dari minimize
                        if (known) { if (!f.Visible && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)) f.Show(); f.Pump(); }
                        else if (!_quick.Enabled) _quick.Start();
                        break;
                }
            }
            catch (Exception ex) { Log.Write("lights ev: " + ex.Message); }
        }

        public void Stop()
        {
            foreach (IntPtr h in _hooks) UnhookWinEvent(h);
            _hooks.Clear();
            _quick.Stop();
            _scan.Stop(); _started = false;
            foreach (TrafficLightForm f in _forms.Values) { try { f.Close(); f.Dispose(); } catch { } }
            _forms.Clear();
        }

        private void Scan()
        {
            HashSet<IntPtr> seen = new HashSet<IntPtr>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
                int st = Native.GetWindowLong(h, -16);
                // jendela milik jendela lain (dialog) dilewati, kecuali pemiliknya tersembunyi dan ia punya tombol minimize
                IntPtr owner = Native.GetWindow(h, Native.GW_OWNER);
                if (owner != IntPtr.Zero && (Native.IsWindowVisible(owner) || (st & 0x00020000) == 0)) return true;
                bool framed = (st & 0x00C00000) == 0x00C00000 || (st & 0x00080000) != 0;
                bool resizable = (st & 0x00040000) != 0 && (st & 0x00030000) != 0;   // frameless tapi bisa di-resize & minimize (Electron)
                if (!framed && !resizable) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == MyPid && !(Control.FromHandle(h) is SettingsForm)) return true;   // milik sendiri: hanya jendela Pengaturan
                string cls = Native.ClassOf(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Windows.UI.Core.CoreWindow") return true;
                Native.RECT r, r2;
                if (!TrafficLightForm.Geometry(h, out r, out r2)) { if (Environment.GetEnvironmentVariable("LIQUIDMAC_DEBUG") == "1") Log.Write("lights skip rect " + cls + " " + h); return true; }
                seen.Add(h);
                return true;
            }, IntPtr.Zero);
            if (Environment.GetEnvironmentVariable("LIQUIDMAC_DEBUG") == "1") Log.Write("lights scan: seen=" + seen.Count + " forms=" + _forms.Count);

            List<IntPtr> dead = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, TrafficLightForm> kv in _forms) if (kv.Value.Gone || !Native.IsWindow(kv.Key)) dead.Add(kv.Key);
            foreach (IntPtr h in dead) { try { _forms[h].Close(); _forms[h].Dispose(); } catch { } _forms.Remove(h); WinAnim.Forget(h); }
            WinAnim.Prune();

            foreach (IntPtr h in seen)
            {
                TrafficLightForm f;
                if (_forms.TryGetValue(h, out f)) { if (!f.Visible) { f.Show(); f.Pump(); } continue; }
                f = new TrafficLightForm(h);
                _forms[h] = f;
                if (_started) f.Hold(180);                  // jendela baru: tunggu animasi membuka
                f.Show(); f.Pump();
            }
        }
    }

    // ============================================================
    //  SUARA SISTEM gaya macOS (disintesis sendiri, bukan file Apple)
    // ============================================================
    internal static class SoundKit
    {
        public static readonly string Dir = Path.Combine(Log.Dir, "sounds");
        public static readonly string[] Names = { "Kaca", "Gelembung", "Ketuk", "Lonceng", "Sonar", "Petik", "Tetes", "Denting" };
        private const int Rate = 44100;

        public static string PathOf(string name) { return Path.Combine(Dir, name.ToLowerInvariant() + ".wav"); }

        private static void Wav(string path, float[] s)
        {
            using (FileStream fs = File.Create(path))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                int bytes = s.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                float peak = 0.0001f; foreach (float v in s) peak = Math.Max(peak, Math.Abs(v));
                float gain = 0.72f / peak;
                foreach (float v in s) w.Write((short)Math.Max(-32767, Math.Min(32767, v * gain * 32767)));
            }
        }

        private static float[] Buf(double sec) { return new float[(int)(sec * Rate)]; }

        // nada dengan beberapa parsial, attack singkat, peluruhan eksponensial
        private static void Tone(float[] b, double start, double[] freqs, double[] amps, double decay, double attack)
        {
            int s0 = (int)(start * Rate);
            for (int i = s0; i < b.Length; i++)
            {
                double t = (i - s0) / (double)Rate;
                double env = Math.Min(1, t / attack) * Math.Exp(-t / decay);
                double v = 0;
                for (int k = 0; k < freqs.Length; k++) v += amps[k] * Math.Sin(2 * Math.PI * freqs[k] * t) * Math.Exp(-t * k * 0.8 / decay);
                b[i] += (float)(v * env);
            }
        }

        private static float[] Make(string name)
        {
            float[] b;
            switch (name)
            {
                case "Kaca":
                    b = Buf(0.9); Tone(b, 0, new double[] { 1567, 3140, 4705 }, new double[] { 1, 0.45, 0.18 }, 0.22, 0.002); break;
                case "Gelembung":
                    b = Buf(0.35);
                    for (int i = 0; i < b.Length; i++) { double t = i / (double)Rate; double f = 380 + 1100 * Math.Pow(t / 0.35, 0.6); b[i] = (float)(Math.Sin(2 * Math.PI * f * t + 2 * Math.Sin(2 * Math.PI * 28 * t)) * Math.Min(1, t / 0.01) * Math.Exp(-t / 0.09)); }
                    break;
                case "Ketuk":
                    b = Buf(0.25); Tone(b, 0, new double[] { 2960, 5920 }, new double[] { 1, 0.25 }, 0.045, 0.001); break;
                case "Lonceng":
                    b = Buf(1.6); Tone(b, 0, new double[] { 880, 1771, 2652, 3545 }, new double[] { 1, 0.6, 0.35, 0.2 }, 0.45, 0.003); break;
                case "Sonar":
                    b = Buf(1.3); Tone(b, 0, new double[] { 1180, 2360 }, new double[] { 1, 0.12 }, 0.28, 0.004); Tone(b, 0.32, new double[] { 1180 }, new double[] { 0.35 }, 0.25, 0.004); break;
                case "Petik":
                    {
                        b = Buf(0.8);
                        int n = Rate / 523; double[] ring = new double[n]; Random r = new Random(7);
                        for (int i = 0; i < n; i++) ring[i] = r.NextDouble() * 2 - 1;
                        for (int i = 0; i < b.Length; i++) { int j = i % n; double nx = 0.5 * (ring[j] + ring[(j + 1) % n]) * 0.996; b[i] = (float)ring[j]; ring[j] = nx; }
                        break;
                    }
                case "Tetes":
                    b = Buf(0.3);
                    for (int i = 0; i < b.Length; i++) { double t = i / (double)Rate; double f = 1400 * Math.Exp(-t / 0.05) + 520; b[i] = (float)(Math.Sin(2 * Math.PI * f * t) * Math.Min(1, t / 0.002) * Math.Exp(-t / 0.07)); }
                    break;
                default: // Denting: dua nada naik
                    b = Buf(0.7); Tone(b, 0, new double[] { 1318, 2636 }, new double[] { 1, 0.2 }, 0.12, 0.002); Tone(b, 0.11, new double[] { 1760, 3520 }, new double[] { 1, 0.2 }, 0.18, 0.002); break;
            }
            return b;
        }

        private static float[] Sweep(double f0, double f1)
        {
            float[] b = Buf(0.45);
            Tone(b, 0, new double[] { f0, f0 * 2 }, new double[] { 1, 0.2 }, 0.1, 0.002);
            Tone(b, 0.12, new double[] { f1, f1 * 2 }, new double[] { 1, 0.2 }, 0.14, 0.002);
            return b;
        }

        public static void Generate()
        {
            Directory.CreateDirectory(Dir);
            foreach (string n in Names) if (!File.Exists(PathOf(n))) Wav(PathOf(n), Make(n));
            if (!File.Exists(Path.Combine(Dir, "sambung.wav"))) Wav(Path.Combine(Dir, "sambung.wav"), Sweep(988, 1480));
            if (!File.Exists(Path.Combine(Dir, "lepas.wav"))) Wav(Path.Combine(Dir, "lepas.wav"), Sweep(1480, 988));
        }

        public static void Preview(string name)
        {
            try { Generate(); using (SoundPlayer p = new SoundPlayer(PathOf(name))) p.Play(); } catch (Exception ex) { Log.Write("sound: " + ex.Message); }
        }

        public static void Feedback()
        {
            if (!Settings.Get("VolumeFeedback", true)) return;
            Preview("Ketuk");
        }

        private const string Root = @"AppEvents\Schemes\Apps\.Default\";
        private static readonly string[] AlertEvents = { ".Default", "SystemAsterisk", "SystemExclamation", "SystemHand", "SystemQuestion", "SystemNotification" };
        private static readonly string[] NotifEvents = { "Notification.Default", "Notification.IM", "Notification.Reminder", "Notification.Mail", "MailBeep" };

        public static bool Applied { get { return Settings.Get("MacSounds", false); } }

        private static void SetEvent(string ev, string file, Dictionary<string, string> backup)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Root + ev + @"\.Current"))
            {
                if (backup != null && !backup.ContainsKey(ev)) backup[ev] = (k.GetValue("") as string) ?? "";
                k.SetValue("", file);
            }
        }

        public static void Apply()
        {
            Generate();
            Dictionary<string, string> backup = LoadBackup();
            bool hadBackup = backup.Count > 0;
            string alert = PathOf(Settings.GetS("AlertSound", "Kaca"));
            string notif = PathOf(Settings.GetS("NotifSound", "Denting"));
            foreach (string ev in AlertEvents) SetEvent(ev, alert, hadBackup ? null : backup);
            foreach (string ev in NotifEvents) SetEvent(ev, notif, hadBackup ? null : backup);
            SetEvent("DeviceConnect", Path.Combine(Dir, "sambung.wav"), hadBackup ? null : backup);
            SetEvent("DeviceDisconnect", Path.Combine(Dir, "lepas.wav"), hadBackup ? null : backup);
            if (!hadBackup) SaveBackup(backup);
            Settings.Set("MacSounds", true);
        }

        public static void Restore()
        {
            foreach (KeyValuePair<string, string> kv in LoadBackup())
                try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Root + kv.Key + @"\.Current")) k.SetValue("", kv.Value); } catch { }
            try { File.Delete(Path.Combine(Dir, "backup.txt")); } catch { }
            Settings.Set("MacSounds", false);
        }

        private static Dictionary<string, string> LoadBackup()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            string f = Path.Combine(Dir, "backup.txt");
            if (File.Exists(f)) foreach (string line in File.ReadAllLines(f)) { int i = line.IndexOf('\t'); if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1); }
            return d;
        }

        private static void SaveBackup(Dictionary<string, string> d)
        {
            Directory.CreateDirectory(Dir);
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in d) lines.Add(kv.Key + "\t" + kv.Value);
            File.WriteAllLines(Path.Combine(Dir, "backup.txt"), lines.ToArray());
        }
    }

    // ============================================================
    //  AppUserModelID jendela & pintasan (memisahkan profil Chrome/Edge)
    // ============================================================
    internal static class Aumid
    {
        [StructLayout(LayoutKind.Sequential)] private struct PK { public Guid f; public int p; }
        [StructLayout(LayoutKind.Explicit)] private struct PV { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(16)] public IntPtr x; }
        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPS { [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetAt(uint i, out PK k); [PreserveSig] int GetValue(ref PK k, out PV v); }
        [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, out IPS ps);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetPropertyStoreFromParsingName(string p, IntPtr b, int f, ref Guid iid, out IPS ps);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PV v);

        private static string Read(IPS ps)
        {
            if (ps == null) return null;
            try
            {
                PK k = new PK(); k.f = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); k.p = 5;
                PV v;
                if (ps.GetValue(ref k, out v) != 0) return null;
                string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
                PropVariantClear(ref v);
                return string.IsNullOrEmpty(s) ? null : s;
            }
            finally { Marshal.ReleaseComObject(ps); }
        }

        public static string OfWindow(IntPtr h)
        {
            try { Guid g = typeof(IPS).GUID; IPS ps; return SHGetPropertyStoreForWindow(h, ref g, out ps) == 0 ? Read(ps) : null; }
            catch { return null; }
        }

        public static string OfFile(string path)
        {
            try { Guid g = typeof(IPS).GUID; IPS ps; return SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref g, out ps) == 0 ? Read(ps) : null; }
            catch { return null; }
        }
    }
}

