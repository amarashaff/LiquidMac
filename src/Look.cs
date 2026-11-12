using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiquidMac
{
    // Berkas yang ditanam di exe (font Inter, kursor macOS)
    internal static class Res
    {
        public static List<string> Names(string prefix)
        {
            List<string> r = new List<string>();
            foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames()) if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) r.Add(n);
            return r;
        }

        public static bool Extract(string name, string path)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return false;
                if (File.Exists(path) && new FileInfo(path).Length == s.Length) return true;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream f = File.Create(path)) s.CopyTo(f);
                return true;
            }
        }
    }

    // ============================================================
    //  FONT GAYA macOS (Inter, lisensi OFL) — dipasang per pengguna, tanpa admin
    // ============================================================
    internal static class MacFont
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResource(string f);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr res);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SPI(int a, int p, byte[] v, int f);
        private const int NCM = 504, LF = 92, FACE = 28;
        private static readonly int[] NcmFonts = { 24, 124, 224, 316, 408 };   // caption, smcaption, menu, status, message
        public const string Family = "Inter", Semi = "Inter Semi Bold";

        private static string FontDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts"); } }

        public static bool Present
        {
            get
            {
                try { using (InstalledFontCollection fc = new InstalledFontCollection()) foreach (FontFamily f in fc.Families) if (f.Name == Family) return true; }
                catch { }
                return false;
            }
        }

        // Pasang Inter untuk pengguna ini (aman, tidak mengubah tampilan apa pun)
        public static void Install()
        {
            bool any = false;
            foreach (string n in Res.Names("font."))
            {
                string file = n.Substring(5), path = Path.Combine(FontDir, file);
                try
                {
                    bool existed = File.Exists(path);
                    if (!Res.Extract(n, path)) continue;
                    string style = Path.GetFileNameWithoutExtension(file).Replace("Inter-", "");
                    if (style == "SemiBold") style = "Semi Bold";
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts"))
                        if (!path.Equals(k.GetValue("Inter " + style + " (TrueType)") as string, StringComparison.OrdinalIgnoreCase)) { k.SetValue("Inter " + style + " (TrueType)", path); any = true; }
                    if (!existed) any = true;
                    AddFontResource(path);
                }
                catch (Exception ex) { Log.Write("font install " + file + ": " + ex.Message); }
            }
            if (any) Broadcast(0x001D);
        }

        private static void Broadcast(int msg) { IntPtr r; SendMessageTimeout((IntPtr)0xFFFF, msg, IntPtr.Zero, IntPtr.Zero, 2, 1000, out r); }

        private static string Face(byte[] b, int off) { return Encoding.Unicode.GetString(b, off + FACE, 64).Split('\0')[0]; }
        private static void SetFace(byte[] b, int off, string face)
        {
            byte[] z = new byte[64]; byte[] f = Encoding.Unicode.GetBytes(face);
            Array.Copy(f, z, Math.Min(62, f.Length));
            Array.Copy(z, 0, b, off + FACE, 64);
        }

        public static bool Applied { get { return Settings.GetS("FontBackupNcm", "").Length > 0; } }

        // Judul jendela, menu klasik, kotak pesan, status bar, dan label ikon desktop pakai Inter.
        public static void Apply()
        {
            Install();
            byte[] ncm = new byte[NCM]; BitConverter.GetBytes(NCM).CopyTo(ncm, 0);
            byte[] icon = new byte[LF];
            if (!SPI(0x29, NCM, ncm, 0) || !SPI(0x1F, LF, icon, 0)) { Log.Write("font: gagal membaca metrik"); return; }
            if (!Applied && !Face(ncm, 24).StartsWith("Inter"))
            {
                Settings.SetS("FontBackupNcm", Convert.ToBase64String(ncm));
                Settings.SetS("FontBackupIcon", Convert.ToBase64String(icon));
            }
            for (int i = 0; i < NcmFonts.Length; i++) SetFace(ncm, NcmFonts[i], i == 0 ? Semi : Family);
            SetFace(icon, 0, Family);
            SPI(0x2A, NCM, ncm, 3);
            SPI(0x22, LF, icon, 3);
            Log.Write("font macOS diterapkan");
        }

        public static void Restore()
        {
            try
            {
                string a = Settings.GetS("FontBackupNcm", ""), b = Settings.GetS("FontBackupIcon", "");
                if (a.Length > 0) { byte[] ncm = Convert.FromBase64String(a); SPI(0x2A, NCM, ncm, 3); }
                if (b.Length > 0) { byte[] icon = Convert.FromBase64String(b); SPI(0x22, LF, icon, 3); }
                Settings.SetS("FontBackupNcm", ""); Settings.SetS("FontBackupIcon", "");
                Log.Write("font Windows dikembalikan");
            }
            catch (Exception ex) { Log.Write("font restore: " + ex.Message); }
        }
    }

    // ============================================================
    //  KURSOR macOS (apple_cursor oleh ful1e5, GPL-3.0)
    // ============================================================
    internal static class MacCursor
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SPI(int a, int p, IntPtr v, int f);
        private const string Key = @"Control Panel\Cursors";
        private static readonly string[,] Map = {
            { "Arrow", "Pointer.cur" }, { "Help", "Help.cur" }, { "AppStarting", "Work.ani" }, { "Wait", "Busy.ani" },
            { "Crosshair", "Cross.cur" }, { "IBeam", "Text.cur" }, { "NWPen", "Handwriting.cur" }, { "No", "Unavailiable.cur" },
            { "SizeNS", "Vert.cur" }, { "SizeWE", "Horz.cur" }, { "SizeNWSE", "Dng1.cur" }, { "SizeNESW", "Dng2.cur" },
            { "SizeAll", "Move.cur" }, { "UpArrow", "Alternate.cur" }, { "Hand", "Link.cur" }, { "Pin", "Pin.cur" }, { "Person", "Person.cur" } };

        public static bool Applied { get { return Settings.GetS("CursorBackup", "").Length > 0; } }

        public static void Apply(bool large)
        {
            string size = large ? "large" : "regular";
            string dir = Path.Combine(Log.Dir, Path.Combine("cursors", size));
            foreach (string n in Res.Names("cur." + size + "."))
                try { Res.Extract(n, Path.Combine(dir, n.Substring(("cur." + size + ".").Length))); } catch (Exception ex) { Log.Write("cursor extract: " + ex.Message); }
            if (!File.Exists(Path.Combine(dir, "Pointer.cur"))) { Log.Write("cursor: berkas tidak ada"); return; }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (!Applied)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string v in k.GetValueNames())
                    {
                        object o = k.GetValue(v, "", RegistryValueOptions.DoNotExpandEnvironmentNames);
                        sb.Append(v).Append('=').Append(Convert.ToString(o)).Append('|');
                    }
                    Settings.SetS("CursorBackup", sb.Length > 0 ? sb.ToString() : "=|");
                }
                for (int i = 0; i < Map.GetLength(0); i++) k.SetValue(Map[i, 0], Path.Combine(dir, Map[i, 1]), RegistryValueKind.ExpandString);
                k.SetValue("", "macOS (LiquidMac)");
                k.SetValue("Scheme Source", 1, RegistryValueKind.DWord);
            }
            SPI(0x57, 0, IntPtr.Zero, 3);   // SPI_SETCURSORS
            Log.Write("kursor macOS diterapkan (" + size + ")");
        }

        public static void Restore()
        {
            string b = Settings.GetS("CursorBackup", "");
            if (b.Length == 0) return;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    Dictionary<string, string> old = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string part in b.Split('|')) { int e = part.IndexOf('='); if (e >= 0) old[part.Substring(0, e)] = part.Substring(e + 1); }
                    for (int i = 0; i < Map.GetLength(0); i++)
                    {
                        string v; old.TryGetValue(Map[i, 0], out v);
                        k.SetValue(Map[i, 0], v ?? "", RegistryValueKind.ExpandString);
                    }
                    string def; k.SetValue("", old.TryGetValue("", out def) ? def : "Windows Default");
                    string src; int si; k.SetValue("Scheme Source", old.TryGetValue("Scheme Source", out src) && int.TryParse(src, out si) ? si : 2, RegistryValueKind.DWord);
                }
                SPI(0x57, 0, IntPtr.Zero, 3);
                Settings.SetS("CursorBackup", "");
                Log.Write("kursor Windows dikembalikan");
            }
            catch (Exception ex) { Log.Write("cursor restore: " + ex.Message); }
        }
    }

    // ============================================================
    //  LAYAR KUNCI GAYA macOS
    //  Layar kunci asli Windows berjalan di desktop aman (tidak bisa ditimpa aplikasi),
    //  jadi gambarnya yang dibuat ala macOS: wallpaper + foto & nama pengguna + kolom sandi di bawah.
    // ============================================================
    internal static class MacLock
    {
        [DllImport("user32.dll")] private static extern bool LockWorkStation();
        private static string Img { get { return Path.Combine(Log.Dir, "lockscreen.jpg"); } }
        private static string Backup { get { return Path.Combine(Log.Dir, "lockscreen-asli.img"); } }
        private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private static readonly string[] CdmVals = { "RotatingLockScreenEnabled", "RotatingLockScreenOverlayEnabled", "SubscribedContent-338387Enabled" };
        private static int _busy;

        public static void LockNow() { if (!LockWorkStation()) Shell.Run("rundll32.exe", "user32.dll,LockWorkStation"); }

        public static void ApplyAsync()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Apply(); } catch (Exception ex) { Log.Write("lock apply: " + ex.Message); }
                finally { _busy = 0; }
            });
        }

        private static void Apply()
        {
            Render(Img);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Cdm))
            {
                if (Settings.GetS("LockCdmBackup", "").Length == 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string v in CdmVals) sb.Append(v).Append('=').Append(Convert.ToString(k.GetValue(v, ""))).Append('|');
                    Settings.SetS("LockCdmBackup", sb.ToString());
                }
                foreach (string v in CdmVals) k.SetValue(v, 0, RegistryValueKind.DWord);   // tanpa Spotlight & "fakta menarik"
            }
            string r = RunPs(Img, true);
            Log.Write("layar kunci: " + r);
        }

        public static void RestoreAsync() { ThreadPool.QueueUserWorkItem(delegate { Restore(); }); }

        public static void Restore()
        {
                try
                {
                    string b = Settings.GetS("LockCdmBackup", "");
                    if (b.Length > 0)
                        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Cdm))
                            foreach (string part in b.Split('|'))
                            {
                                int e = part.IndexOf('='); if (e <= 0) continue;
                                string n = part.Substring(0, e), v = part.Substring(e + 1); int iv;
                                if (v.Length == 0) k.DeleteValue(n, false); else if (int.TryParse(v, out iv)) k.SetValue(n, iv, RegistryValueKind.DWord);
                            }
                    Settings.SetS("LockCdmBackup", "");
                    if (File.Exists(Backup)) Log.Write("layar kunci dikembalikan: " + RunPs(Backup, false));
                }
                catch (Exception ex) { Log.Write("lock restore: " + ex.Message); }
        }

        private static string RunPs(string image, bool backup)
        {
            string ps = Path.Combine(Log.Dir, "lockscreen.ps1");
            string script =
@"$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$ms = [System.WindowsRuntimeSystemExtensions].GetMethods()
$op = $ms | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | select -First 1
$ac = $ms | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | select -First 1
[Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
[Windows.System.UserProfile.LockScreen, Windows.System.UserProfile, ContentType = WindowsRuntime] | Out-Null
$bk = '" + Backup.Replace("'", "''") + @"'
if (" + (backup ? "$true" : "$false") + @" -and -not (Test-Path $bk)) {
  try {
    $s = [Windows.System.UserProfile.LockScreen]::GetImageStream()
    $ns = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($s)
    $fs = [IO.File]::Create($bk); $ns.CopyTo($fs); $fs.Close()
  } catch { }
}
$t = $op.MakeGenericMethod([Windows.Storage.StorageFile]).Invoke($null, @([Windows.Storage.StorageFile]::GetFileFromPathAsync('" + image.Replace("'", "''") + @"')))
$t.Wait(-1) | Out-Null
$a = $ac.Invoke($null, @([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($t.Result)))
$a.Wait(-1) | Out-Null
'ok'
";
            File.WriteAllText(ps, script, new UTF8Encoding(true));
            ProcessStartInfo si = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps + "\"");
            si.CreateNoWindow = true; si.UseShellExecute = false; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                return o.Trim().Replace("\r", " ").Replace("\n", " ");
            }
        }

        private static string WallpaperPath()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string w = k == null ? null : k.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(w) && File.Exists(w)) return w;
                }
            }
            catch { }
            string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return File.Exists(t) ? t : null;
        }

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, int f);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Native.RECT r);
        [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);

        private static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(64); GetClassName(h, sb, 64); return sb.ToString(); }

        // Wallpaper hidup (Wallpaper Engine, Lively, dll.) digambar di jendela WorkerW di balik ikon desktop: ambil frame-nya.
        public static Bitmap LiveWallpaper()
        {
            List<IntPtr> cand = new List<IntPtr>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                string c = Cls(h);
                if (c == "Progman") EnumChildWindows(h, delegate (IntPtr ch, IntPtr l2) { if (Cls(ch) == "WorkerW") cand.Add(ch); return true; }, IntPtr.Zero);
                else if (c == "WorkerW" && FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) cand.Add(h);
                return true;
            }, IntPtr.Zero);
            foreach (IntPtr h in cand)
            {
                Native.RECT r; if (!GetWindowRect(h, out r)) continue;
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                if (w < 320 || hh < 240) continue;
                Bitmap b = new Bitmap(w, hh, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
                long sum = 0; int n = 0;
                for (int y = 0; y < hh; y += 53) for (int x = 0; x < w; x += 53) { Color c = b.GetPixel(x, y); sum += c.R + c.G + c.B; n++; }
                if (n > 0 && sum / n > 24)
                {
                    try { b.Save(Path.Combine(Log.Dir, "wallpaper-cache.png"), ImageFormat.Png); } catch { }
                    return b;
                }
                b.Dispose();
            }
            // wallpaper hidup sedang jeda (mis. ada jendela dimaksimalkan): pakai tangkapan terakhir
            string cache = Path.Combine(Log.Dir, "wallpaper-cache.png");
            if (File.Exists(cache)) try { using (FileStream fs = new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return new Bitmap(Image.FromStream(fs)); } catch { }
            return null;
        }

        private static Image WallpaperImage()
        {
            string wp = WallpaperPath();
            if (wp != null && !wp.EndsWith("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase))
                try { using (FileStream fs = new FileStream(wp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return new Bitmap(Image.FromStream(fs)); } catch { }
            try { Bitmap live = LiveWallpaper(); if (live != null) return live; } catch (Exception ex) { Log.Write("live wallpaper: " + ex.Message); }
            if (wp != null)
                try { using (FileStream fs = new FileStream(wp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return new Bitmap(Image.FromStream(fs)); } catch { }
            return null;
        }

        public static void Render(string path)
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            int w = Math.Max(1280, s.Width), h = Math.Max(720, s.Height);
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(20, 18, 40));
                using (Image img = WallpaperImage())
                {
                    if (img != null)
                    {
                        float sc = Math.Max((float)w / img.Width, (float)h / img.Height);
                        float dw = img.Width * sc, dh = img.Height * sc;
                        g.DrawImage(img, (w - dw) / 2f, (h - dh) / 2f, dw, dh);
                    }
                    else
                        using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(40, 52, 120), Color.FromArgb(120, 40, 110), 60f)) g.FillRectangle(bg, 0, 0, w, h);
                }
                // gelap lembut di bawah agar foto & nama terbaca
                using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(0, h * 0.55f - 2, w, h * 0.45f + 4), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(110, 0, 0, 0), 90f))
                    g.FillRectangle(lg, 0, h * 0.55f, w, h * 0.45f + 1);   // kuas dimulai 2 px lebih atas: tanpa garis tegas di tepi atas

                // posisi mengikuti layar masuk bawaan Windows 11 (foto di tengah, sedikit di atas garis tengah); ornamen tetap macOS
                float av = h * 0.115f, cx = w / 2f, ay = h * 0.355f - av / 2f;
                using (GraphicsPath gp = new GraphicsPath())
                {
                    // bayangan lembut di sekitar profil agar nama & kolom sandi tetap terbaca di wallpaper terang
                    gp.AddEllipse(cx - h * 0.42f, ay - h * 0.14f, h * 0.84f, h * 0.56f);
                    using (PathGradientBrush pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb(70, 0, 0, 0); pg.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) }; g.FillPath(pg, gp); }
                }
                RectangleF ar = new RectangleF(cx - av / 2f, ay, av, av);
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, ar.X - 2, ar.Y + 3, ar.Width + 4, ar.Height + 4);
                SysInfo.DrawAvatar(g, ar);
                using (Pen rim = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1.5f, h / 700f))) g.DrawEllipse(rim, ar);
                string fam = MacFont.Present ? MacFont.Family : "Segoe UI";
                using (Font nf = new Font(MacFont.Present ? MacFont.Semi : "Segoe UI Semibold", h * 0.026f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font hf = new Font(fam, h * 0.0135f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    using (SolidBrush wb = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                        g.DrawString(SysInfo.DisplayName, nf, wb, new RectangleF(0, ar.Bottom + h * 0.018f, w, h * 0.05f), sf);
                    // kolom sandi kaca
                    float pw = h * 0.20f, ph = h * 0.032f, py = ar.Bottom + h * 0.072f;
                    RectangleF pr = new RectangleF(cx - pw / 2f, py, pw, ph);
                    using (GraphicsPath pp = Glass.Round(pr, ph / 2f))
                    {
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(pb, pp);
                        using (Pen pe = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) g.DrawPath(pe, pp);
                    }
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
                        g.DrawString(L.T("Masukkan Kata Sandi"), hf, tb, pr, sf);
                }
                using (EncoderParameters ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 94L);
                    ImageCodecInfo jpg = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") jpg = c;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    bmp.Save(path, jpg, ep);
                }
            }
        }
    }
}
