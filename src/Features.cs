// LiquidMac - fitur tambahan: ikon shell, daftar aplikasi, notifikasi, Launchpad, Pusat Notifikasi, hook tombol Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace LiquidMac
{
    // ============================================================
    //  GAMBAR IKON DARI SHELL (exe, lnk, folder, aplikasi Store)
    // ============================================================
    internal static class ShellImage
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory { [PreserveSig] int GetImage(Native.SIZE size, int flags, out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bm);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);

        public static Bitmap Get(string parsingName, int size) { return Get(parsingName, size, 0x4 /* ICONONLY */); }

        // flags 0 = thumbnail bila ada (gambar, video, PDF), selain itu ikon
        public static Bitmap Get(string parsingName, int size, int flags)
        {
            Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out f) != 0 || f == null) return null;
                if (f.GetImage(new Native.SIZE(size, size), flags, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                BITMAP bm;
                GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm);
                int w = bm.bmWidth, h = bm.bmHeight;
                if (w <= 0 || h <= 0) return null;
                BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
                bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)); bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
                byte[] bits = new byte[w * h * 4];
                IntPtr dc = Native.GetDC(IntPtr.Zero);
                GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0);
                Native.ReleaseDC(IntPtr.Zero, dc);
                bool anyAlpha = false;
                for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
                Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(bits, y * w * 4, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), w * 4);
                b.UnlockBits(d);
                Bitmap o = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(o)) g.DrawImage(b, 0, 0, w, h);
                b.Dispose();
                return o;
            }
            catch (Exception ex) { Log.Write("shellimage " + parsingName + ": " + ex.Message); return null; }
            finally
            {
                if (hbm != IntPtr.Zero) Native.DeleteObject(hbm);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }
    }

    // ============================================================
    //  IKON GAYA macOS: kotak kaca (squircle) Liquid Glass
    // ============================================================
    internal static class Tiles
    {
        public static bool Enabled = true;

        public static Bitmap Make(Bitmap icon, int size)
        {
            Bitmap o = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                RectangleF r = new RectangleF(size * 0.04f, size * 0.04f, size * 0.92f, size * 0.92f);
                float rad = r.Width * 0.225f;
                if (icon == null) return o;
                if (!Enabled) { g.DrawImage(icon, new Rectangle(0, 0, size, size)); return o; }
                using (GraphicsPath p = Glass.Round(r, rad))
                {
                    if (FullBleed(icon))
                    {
                        g.SetClip(p);
                        g.DrawImage(icon, r);
                        g.ResetClip();
                    }
                    else
                    {
                        Color tint = Dominant(icon);
                        Color top = Mix(Color.FromArgb(255, 255, 255), tint, 0.10f), bot = Mix(Color.FromArgb(226, 229, 236), tint, 0.22f);
                        using (LinearGradientBrush bg = new LinearGradientBrush(r, top, bot, 90f))
                            g.FillPath(bg, p);
                        float s = r.Width * 0.72f;
                        RectangleF ir = new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s);
                        using (ImageAttributes sh = new ImageAttributes())
                        {
                            ColorMatrix cm = new ColorMatrix(new float[][] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0.18f, 0 }, new float[] { 0, 0, 0, 0, 1 } });
                            sh.SetColorMatrix(cm);
                            g.DrawImage(icon, Rectangle.Round(new RectangleF(ir.X, ir.Y + size * 0.02f, ir.Width, ir.Height)), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, sh);
                        }
                        g.DrawImage(icon, ir);
                    }
                    using (LinearGradientBrush rim = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(170, 255, 255, 255), Color.FromArgb(60, 0, 0, 0), 90f))
                    using (Pen pen = new Pen(rim, Math.Max(1f, size / 96f)))
                        g.DrawPath(pen, p);
                }
            }
            return o;
        }

        private static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Warna dominan yang paling "hidup" dari ikon (untuk rona latar kotak).
        private static Color Dominant(Bitmap b)
        {
            double sx = 0, sy = 0, wsum = 0, vsum = 0, ssum = 0;
            int step = Math.Max(1, b.Width / 32);
            for (int y = 0; y < b.Height; y += step)
                for (int x = 0; x < b.Width; x += step)
                {
                    Color c = b.GetPixel(x, y);
                    if (c.A < 160) continue;
                    float sat = c.GetSaturation(), bri = c.GetBrightness();
                    double w = sat * (1 - Math.Abs(bri - 0.5) * 1.4);
                    if (w <= 0.05) continue;
                    double h = c.GetHue() * Math.PI / 180;
                    sx += Math.Cos(h) * w; sy += Math.Sin(h) * w; wsum += w; ssum += sat * w; vsum += bri * w;
                }
            if (wsum < 1) return Color.FromArgb(200, 205, 215);
            double hue = Math.Atan2(sy, sx) * 180 / Math.PI; if (hue < 0) hue += 360;
            return FromHsl(hue, Math.Min(0.9, ssum / wsum), 0.55);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = l - c / 2;
            double r = 0, g = 0, bl = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; bl = x; }
            else if (h < 240) { g = x; bl = c; } else if (h < 300) { r = x; bl = c; } else { r = c; bl = x; }
            return Color.FromArgb(255, (int)((r + m) * 255), (int)((g + m) * 255), (int)((bl + m) * 255));
        }

        private static bool FullBleed(Bitmap b)
        {
            try
            {
                float[][] pts = { new float[] { 0.14f, 0.14f }, new float[] { 0.86f, 0.14f }, new float[] { 0.14f, 0.86f }, new float[] { 0.86f, 0.86f }, new float[] { 0.5f, 0.03f }, new float[] { 0.03f, 0.5f } };
                foreach (float[] p in pts)
                    if (b.GetPixel((int)(p[0] * (b.Width - 1)), (int)(p[1] * (b.Height - 1))).A < 200) return false;
                return true;
            }
            catch { return false; }
        }

        public static Bitmap LaunchpadGlyph(int size)
        {
            Bitmap o = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(size * 0.04f, size * 0.04f, size * 0.92f, size * 0.92f);
                using (GraphicsPath p = Glass.Round(r, r.Width * 0.225f))
                {
                    using (LinearGradientBrush bg = new LinearGradientBrush(r, Color.FromArgb(255, 120, 90, 255), Color.FromArgb(255, 255, 95, 160), 45f)) g.FillPath(bg, p);
                    using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, size / 96f))) g.DrawPath(pen, p);
                }
                float cell = r.Width / 4.2f, gap = cell * 0.25f;
                float start = r.X + (r.Width - (cell * 3 + gap * 2)) / 2f;
                Color[] cs = { Color.FromArgb(255, 255, 214, 10), Color.FromArgb(255, 48, 209, 88), Color.FromArgb(255, 10, 132, 255), Color.FromArgb(255, 255, 69, 58), Color.White, Color.FromArgb(255, 191, 90, 242), Color.FromArgb(255, 100, 210, 255), Color.FromArgb(255, 255, 159, 10), Color.FromArgb(255, 255, 55, 95) };
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        using (GraphicsPath cp = Glass.Round(new RectangleF(start + x * (cell + gap), start + y * (cell + gap), cell, cell), cell * 0.28f))
                        using (SolidBrush b = new SolidBrush(cs[y * 3 + x])) g.FillPath(b, cp);
            }
            return o;
        }
    }

    // ============================================================
    //  DAFTAR SEMUA APLIKASI (shell:AppsFolder)
    // ============================================================
    internal sealed class AppEntry
    {
        public string Name, Id;
        public Bitmap Tile;
        public bool Shrunk;
    }

    internal static class Apps
    {
        private static List<AppEntry> _all;
        private static DateTime _at;
        private static readonly Dictionary<string, string> _byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static object Call(object o, string name, BindingFlags kind, params object[] args)
        {
            return o.GetType().InvokeMember(name, kind, null, o, args.Length == 0 ? null : args);
        }

        public static List<AppEntry> All()
        {
            if (_all != null && (DateTime.Now - _at).TotalMinutes < 10) return _all;
            List<AppEntry> list = new List<AppEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                object sh = Activator.CreateInstance(t);
                object folder = Call(sh, "NameSpace", BindingFlags.InvokeMethod, "shell:AppsFolder");
                object items = Call(folder, "Items", BindingFlags.InvokeMethod);
                int count = Convert.ToInt32(Call(items, "Count", BindingFlags.GetProperty));
                for (int i = 0; i < count; i++)
                {
                    object it = Call(items, "Item", BindingFlags.InvokeMethod, i);
                    if (it == null) continue;
                    string name = Call(it, "Name", BindingFlags.GetProperty) as string;
                    string id = Call(it, "Path", BindingFlags.GetProperty) as string;
                    Marshal.ReleaseComObject(it);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                    string ln = name.ToLowerInvariant();
                    if (ln.Contains("uninstall") || ln.Contains("readme") || ln.Contains("hapus instal") || ln.EndsWith(" help") || ln.Contains("release notes")) continue;
                    if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".chm", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(name)) continue;
                    AppEntry e = new AppEntry(); e.Name = name; e.Id = id;
                    list.Add(e);
                    _byId[id] = name;
                }
                Marshal.ReleaseComObject(items); Marshal.ReleaseComObject(folder); Marshal.ReleaseComObject(sh);
            }
            catch (Exception ex) { Log.Write("apps: " + ex.Message); }
            list.Sort(delegate (AppEntry a, AppEntry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            if (_all != null) foreach (AppEntry old in _all) if (old.Tile != null) foreach (AppEntry n in list) if (n.Id == old.Id) { n.Tile = old.Tile; break; }
            _all = list; _at = DateTime.Now;
            return list;
        }

        public static string NameFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_all == null) All();
            string n;
            return _byId.TryGetValue(id, out n) ? n : null;
        }

        public static Bitmap TileFor(AppEntry e)
        {
            if (e.Tile == null && !e.Shrunk)
            {
                Bitmap raw = ShellImage.Get("shell:AppsFolder\\" + e.Id, 128);
                e.Tile = Tiles.Make(raw, 128);
                if (raw != null) raw.Dispose();
            }
            return e.Tile;
        }

        public static void Launch(string id) { Shell.Run("explorer.exe", "shell:AppsFolder\\" + id); }

        // ---- cache ikon di disk: dibuat oleh proses pembantu agar memori shell tidak menumpuk di LiquidMac ----
        public static readonly string CacheDir = Path.Combine(Log.Dir, "tilecache");

        public static string CachePath(string id)
        {
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes((Tiles.Enabled ? "t1|" : "t0|") + id));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
                return Path.Combine(CacheDir, sb.ToString() + ".png");
            }
        }

        public static bool Cached(AppEntry e) { return File.Exists(CachePath(e.Id)); }

        // Muat tile dari cache (pemanggil wajib Dispose).
        public static Bitmap LoadTile(AppEntry e)
        {
            string p = CachePath(e.Id);
            if (!File.Exists(p)) return null;
            try
            {
                byte[] data = File.ReadAllBytes(p);
                using (MemoryStream ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        // Dijalankan di proses pembantu: LiquidMac.exe --build-tiles
        public static void BuildCache()
        {
            Directory.CreateDirectory(CacheDir);
            foreach (AppEntry e in All())
            {
                string p = CachePath(e.Id);
                if (File.Exists(p)) continue;
                try
                {
                    Bitmap raw = ShellImage.Get("shell:AppsFolder\\" + e.Id, 128);
                    using (Bitmap t = Tiles.Make(raw, 128)) t.Save(p, ImageFormat.Png);
                    if (raw != null) raw.Dispose();
                }
                catch (Exception ex) { Log.Write("tile " + e.Name + ": " + ex.Message); }
            }
        }

        public static void ClearDiskCache() { try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true); } catch { } }

        public static void ResetTiles() { if (_all != null) foreach (AppEntry e in _all) { if (e.Tile != null) e.Tile.Dispose(); e.Tile = null; e.Shrunk = false; } ClearDiskCache(); LaunchpadForm.ClearCache(); }
    }

    // ============================================================
    //  DATABASE NOTIFIKASI WINDOWS (read-only, via winsqlite3.dll bawaan Windows)
    // ============================================================
    internal sealed class Notif
    {
        public long Id, Arrival;
        public string AppId, App, Title, Body, Launch;
        public DateTime Time { get { try { return DateTime.FromFileTimeUtc(Arrival).ToLocalTime(); } catch { return DateTime.Now; } } }
    }

    internal static class NotifDb
    {
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2")] private static extern int Open(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close_v2")] private static extern int Close(IntPtr db);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_busy_timeout")] private static extern int BusyTimeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2")] private static extern int Prepare(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step")] private static extern int Step(IntPtr stmt);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize")] private static extern int Finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64")] private static extern long ColInt64(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_blob")] private static extern IntPtr ColBlob(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_bytes")] private static extern int ColBytes(IntPtr stmt, int i);

        private static readonly string DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications\wpndatabase.db");
        private static bool _broken;

        private static byte[] Z(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }

        private static string Text(IntPtr st, int i)
        {
            int n = ColBytes(st, i);
            IntPtr p = ColBlob(st, i);
            if (n <= 0 || p == IntPtr.Zero) return "";
            byte[] b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public static List<Notif> Read(int limit)
        {
            List<Notif> list = new List<Notif>();
            if (_broken || !File.Exists(DbPath)) return list;
            IntPtr db = IntPtr.Zero, st = IntPtr.Zero;
            try
            {
                if (Open(Z(DbPath), out db, 0x1 /* READONLY */, IntPtr.Zero) != 0) return list;
                BusyTimeout(db, 400);
                string sql = "SELECT n.Id, n.ArrivalTime, h.PrimaryId, n.Payload FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId WHERE n.Type = 'toast' ORDER BY n.ArrivalTime DESC LIMIT " + limit;
                if (Prepare(db, Z(sql), -1, out st, IntPtr.Zero) != 0) return list;
                while (Step(st) == 100)
                {
                    Notif n = new Notif();
                    n.Id = ColInt64(st, 0);
                    n.Arrival = ColInt64(st, 1);
                    n.AppId = Text(st, 2);
                    ParsePayload(n, Text(st, 3));
                    if (string.IsNullOrEmpty(n.Title) && string.IsNullOrEmpty(n.Body)) continue;
                    n.App = AppName(n.AppId);
                    list.Add(n);
                }
            }
            catch (DllNotFoundException) { _broken = true; }
            catch (EntryPointNotFoundException) { _broken = true; }
            catch (Exception ex) { Log.Write("notifdb: " + ex.Message); }
            finally
            {
                if (st != IntPtr.Zero) Finalize(st);
                if (db != IntPtr.Zero) Close(db);
            }
            return list;
        }

        private static void ParsePayload(Notif n, string xml)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                XmlNodeList texts = doc.SelectNodes("//binding/text");
                List<string> parts = new List<string>();
                foreach (XmlNode t in texts) { string s = t.InnerText.Trim(); if (s.Length > 0) parts.Add(s); }
                if (parts.Count > 0) n.Title = parts[0];
                if (parts.Count > 1) n.Body = string.Join("\n", parts.GetRange(1, parts.Count - 1).ToArray());
                XmlElement toast = doc.DocumentElement;
                if (toast != null && toast.GetAttribute("activationType") == "protocol") n.Launch = toast.GetAttribute("launch");
            }
            catch { }
        }

        private static readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Windows";
            string n;
            if (_names.TryGetValue(id, out n)) return n;
            n = Apps.NameFor(id);
            if (n == null)
            {
                if (id.StartsWith("Windows.SystemToast", StringComparison.OrdinalIgnoreCase)) n = "Windows";
                else if (id.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0) n = L.T("Keamanan Windows");
                else
                {
                    string s = id;
                    int bang = s.IndexOf('!'); if (bang > 0) s = s.Substring(0, bang);
                    int slash = s.LastIndexOfAny(new char[] { '\\', '/' }); if (slash >= 0) s = s.Substring(slash + 1);
                    if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
                    int dot = s.LastIndexOf('.'); if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
                    s = s.Replace("Win32", "").Replace("_", " ").Trim();
                    n = s.Length > 0 ? s : L.T("Aplikasi");
                }
            }
            _names[id] = n;
            return n;
        }

        // Banner Windows dimatikan per aplikasi (notifikasi tetap masuk Pusat Notifikasi);
        // LiquidMac menampilkan banner sendiri di atas. Semua perubahan dicatat agar bisa dikembalikan.
        private const string NotifKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\";

        public static void SuppressWindowsBanners()
        {
            HashSet<string> done = new HashSet<string>(Settings.GetS("BannerSuppressed", "").Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (string id in Handlers())
            {
                if (done.Contains(id)) continue;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(NotifKey + id))
                    {
                        object cur = k.GetValue("ShowBanner");
                        if (cur != null && Convert.ToInt32(cur) == 0) continue; // memang sudah mati oleh pengguna
                        k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
                        done.Add(id); changed = true;
                    }
                }
                catch (Exception ex) { Log.Write("suppress " + id + ": " + ex.Message); }
            }
            if (changed) Settings.SetS("BannerSuppressed", string.Join("|", new List<string>(done).ToArray()));
        }

        public static void RestoreWindowsBanners()
        {
            foreach (string id in Settings.GetS("BannerSuppressed", "").Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(NotifKey + id, true)) if (k != null) k.DeleteValue("ShowBanner", false); }
                catch { }
            }
            Settings.SetS("BannerSuppressed", "");
        }

        private static List<string> Handlers()
        {
            List<string> ids = new List<string>();
            IntPtr db = IntPtr.Zero, st = IntPtr.Zero;
            try
            {
                if (_broken || Open(Z(DbPath), out db, 0x1, IntPtr.Zero) != 0) return ids;
                BusyTimeout(db, 400);
                if (Prepare(db, Z("SELECT PrimaryId FROM NotificationHandler WHERE PrimaryId IS NOT NULL AND PrimaryId <> ''"), -1, out st, IntPtr.Zero) != 0) return ids;
                while (Step(st) == 100) ids.Add(Text(st, 0));
            }
            catch (Exception ex) { Log.Write("handlers: " + ex.Message); }
            finally { if (st != IntPtr.Zero) Finalize(st); if (db != IntPtr.Zero) Close(db); }
            return ids;
        }

        public static void Open(Notif n)
        {
            if (n == null) return;
            if (!string.IsNullOrEmpty(n.Launch) && n.Launch.IndexOf(':') > 1 && n.Launch.IndexOf(' ') < 0) { Shell.Run(n.Launch, null); return; }
            if (Apps.NameFor(n.AppId) != null) { Apps.Launch(n.AppId); return; }
            Native.Keys(Native.VK_LWIN, (byte)'N');
        }

        // Daftar yang dihapus pengguna (disimpan, dipakai island & Pusat Notifikasi).
        private static HashSet<long> _dismissed;
        private static HashSet<long> Dismissed
        {
            get
            {
                if (_dismissed == null)
                {
                    _dismissed = new HashSet<long>();
                    foreach (string x in Settings.GetS("NotifDismissed", "").Split(',')) { long v; if (long.TryParse(x, out v)) _dismissed.Add(v); }
                }
                return _dismissed;
            }
        }

        public static void Dismiss(long id)
        {
            Dismissed.Add(id);
            List<long> keep = new List<long>(Dismissed);
            if (keep.Count > 200) keep = keep.GetRange(keep.Count - 200, 200);
            Settings.SetS("NotifDismissed", string.Join(",", keep.ConvertAll(delegate (long v) { return v.ToString(); }).ToArray()));
        }

        public static void ClearAll(long newestArrival)
        {
            Settings.SetS("NotifClearedBefore", newestArrival.ToString());
        }

        public static List<Notif> Visible(int limit)
        {
            long cleared; long.TryParse(Settings.GetS("NotifClearedBefore", "0"), out cleared);
            List<Notif> all = Read(limit + 40);
            List<Notif> v = all.FindAll(delegate (Notif n) { return n.Arrival > cleared && !Dismissed.Contains(n.Id); });
            if (v.Count > limit) v = v.GetRange(0, limit);
            return v;
        }

        public static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "sekarang";
            if (d.TotalMinutes < 60) return ((int)d.TotalMinutes) + L.T(" mnt lalu");
            if (d.TotalHours < 24) return ((int)d.TotalHours) + L.T(" jam lalu");
            return t.ToString("d MMM HH.mm", L.Culture);
        }
    }

    // ============================================================
    //  VOLUME (Core Audio) & KECERAHAN (WMI)
    // ============================================================
    internal static class SystemControls
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
            [PreserveSig] int GetChannelCount(out int c);
            [PreserveSig] int SetMasterVolumeLevel(float l, ref Guid g);
            [PreserveSig] int SetMasterVolumeLevelScalar(float l, ref Guid g);
            [PreserveSig] int GetMasterVolumeLevel(out float l);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float l);
            [PreserveSig] int SetChannelVolumeLevel(uint n, float l, ref Guid g);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint n, float l, ref Guid g);
            [PreserveSig] int GetChannelVolumeLevel(uint n, out float l);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint n, out float l);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid g);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
        }

        private static IAudioEndpointVolume Endpoint()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out dev) != 0 || dev == null) return null;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        public static float Volume
        {
            get
            {
                try { IAudioEndpointVolume v = Endpoint(); float l; if (v != null && v.GetMasterVolumeLevelScalar(out l) == 0) return l; }
                catch (Exception ex) { Log.Write("vol get: " + ex.Message); }
                return -1;
            }
            set
            {
                try
                {
                    IAudioEndpointVolume v = Endpoint(); if (v == null) return;
                    Guid g = Guid.Empty;
                    v.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, value)), ref g);
                    bool m; if (v.GetMute(out m) == 0 && m && value > 0) v.SetMute(false, ref g);
                }
                catch (Exception ex) { Log.Write("vol set: " + ex.Message); }
            }
        }

        public static bool Muted
        {
            get
            {
                try { IAudioEndpointVolume v = Endpoint(); bool m; if (v != null && v.GetMute(out m) == 0) return m; }
                catch { }
                return false;
            }
            set
            {
                try { IAudioEndpointVolume v = Endpoint(); if (v == null) return; Guid g = Guid.Empty; v.SetMute(value, ref g); }
                catch (Exception ex) { Log.Write("mute: " + ex.Message); }
            }
        }

        private static bool _noBrightness;

        public static int Brightness
        {
            get
            {
                if (_noBrightness) return -1;
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                        foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
                }
                catch { }
                _noBrightness = true;
                return -1;
            }
            set
            {
                if (_noBrightness) return;
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                        foreach (ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Max(0, Math.Min(100, value)) }); break; }
                }
                catch (Exception ex) { Log.Write("brightness: " + ex.Message); }
            }
        }
    }

    // ============================================================
    //  BLUR LATAR (tangkap layar lalu Gaussian-like blur)
    // ============================================================
    internal static class Backdrop
    {
        public static Bitmap Capture(Rectangle area, float darken)
        {
            Bitmap shot = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            try { using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(area.Location, Point.Empty, area.Size); }
            catch { }
            Bitmap blurred = Blur(shot, 14);
            shot.Dispose();
            Bitmap fast = new Bitmap(blurred.Width, blurred.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(fast))
            {
                g.DrawImageUnscaled(blurred, 0, 0);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb((int)(darken * 255), 10, 10, 18)))
                    g.FillRectangle(dim, 0, 0, blurred.Width, blurred.Height);
            }
            blurred.Dispose();
            return fast;
        }

        // Turunkan resolusi lalu naikkan lagi dua kali: hasilnya mendekati Gaussian blur, cepat.
        public static Bitmap Blur(Bitmap src, int factor)
        {
            Bitmap cur = src;
            for (int pass = 0; pass < 2; pass++)
            {
                int w = Math.Max(1, src.Width / factor), h = Math.Max(1, src.Height / factor);
                Bitmap small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(cur, new Rectangle(0, 0, w, h));
                }
                Bitmap big = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(big))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(0, 0, src.Width, src.Height), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                }
                small.Dispose();
                if (cur != src) cur.Dispose();
                cur = big;
                factor = Math.Max(2, factor / 2);
            }
            return cur;
        }
    }

    // Jendela popup dasar: tanpa bingkai, latar blur, sudut membulat (Windows 11).
    internal abstract class GlassPopup : Form
    {
        protected Bitmap Back;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        protected GlassPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Color.FromArgb(20, 20, 26);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; }
        }

        protected void Round(bool on)
        {
            int v = on ? 2 : 1;
            try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
        }

        public void ShowAt(Rectangle bounds, float darken)
        {
            if (Back != null) Back.Dispose();
            Back = Backdrop.Capture(bounds, darken);
            Bounds = bounds;
            OnBeforeShow();
            Show();
            Activate();
            Invalidate();
        }

        protected virtual void OnBeforeShow() { }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && Back != null) { Back.Dispose(); Back = null; MemoryTrim.Soon(); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Back != null) e.Graphics.DrawImageUnscaled(Back, 0, 0);
            else e.Graphics.Clear(BackColor);
        }

        protected static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }

    // ============================================================
    //  LAUNCHPAD (pengganti Start menu)
    // ============================================================
    // Statistik peluncuran dari Launchpad (untuk kategori "Sering dipakai" & "Terakhir dibuka")
    internal static class LaunchStats
    {
        private static readonly string FilePath = Path.Combine(Log.Dir, "launchstats.txt");
        private static Dictionary<string, long[]> _d;
        private static void Load()
        {
            if (_d != null) return;
            _d = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
            try { foreach (string l in File.ReadAllLines(FilePath)) { string[] p = l.Split('\t'); long c, t; if (p.Length == 3 && long.TryParse(p[1], out c) && long.TryParse(p[2], out t)) _d[p[0]] = new long[] { c, t }; } } catch { }
        }
        public static void Record(string id)
        {
            Load(); long[] v; if (!_d.TryGetValue(id, out v)) { v = new long[2]; _d[id] = v; }
            v[0]++; v[1] = DateTime.Now.Ticks;
            try { List<string> lines = new List<string>(); foreach (KeyValuePair<string, long[]> kv in _d) lines.Add(kv.Key + "\t" + kv.Value[0] + "\t" + kv.Value[1]); File.WriteAllLines(FilePath, lines.ToArray()); } catch { }
        }
        public static long Count(string id) { Load(); long[] v; return _d.TryGetValue(id, out v) ? v[0] : 0; }
        public static long Last(string id) { Load(); long[] v; return _d.TryGetValue(id, out v) ? v[1] : 0; }
    }

    internal sealed class LaunchpadForm : GlassPopup
    {
        // --- interaksi tambahan ---
        private DateTime _waveStart = DateTime.MinValue; private double _waveMs = 560;   // ikon bermunculan bergelombang
        private bool Waving { get { return (DateTime.Now - _waveStart).TotalMilliseconds < _waveMs + 40; } }
        private PointF _par, _parT;              // parallax mengikuti kursor
        private float _hv;                        // kecepatan pegas sorot
        private int _sel = -1;                    // pilihan keyboard
        private string _cat = "all";
        private readonly RectangleF[] _chips = new RectangleF[3];
        private static readonly string[] CatIds = { "all", "freq", "recent" }, CatNames = { "Semua", "Sering dipakai", "Terakhir dibuka" };
        private int _launchIdx = -1; private DateTime _launchStart;
        private readonly Font _chip = Ui.F(9.5f, FontStyle.Regular), _chipB = Ui.F(9.5f, FontStyle.Bold);
        private readonly Font _glyph = Glyphs.Font(Ui.Pf(14));
        private List<AppEntry> _apps = new List<AppEntry>();
        private List<AppEntry> _view = new List<AppEntry>();
        private string _query = "";
        private int _page, _cols = 6, _rows = 4, _hover = -1, _press = -1;
        private Rectangle _grid;
        private int _cellW, _cellH, _icon;
        private readonly List<KeyValuePair<int, RectangleF>> _hits = new List<KeyValuePair<int, RectangleF>>();
        private readonly List<RectangleF> _dots = new List<RectangleF>();
        private readonly Font _label = Ui.F(10f, FontStyle.Regular);
        private readonly Font _search = Ui.F(11.5f, FontStyle.Regular);
        private readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer();
        private readonly Dictionary<AppEntry, Bitmap> _small = new Dictionary<AppEntry, Bitmap>();
        private readonly Dictionary<int, Bitmap> _pageCache = new Dictionary<int, Bitmap>();
        private int _cacheLoaded = -1;
        private static System.Threading.Thread _loader;
        private static volatile int _loaded;
        public Action<string> AddToDock;

        // --- gerak halaman: offset dalam piksel, animasi berbasis waktu ---
        private float _offset;                 // >0: halaman bergeser ke kanan (menuju halaman sebelumnya)
        private bool _dragging;
        private DateTime _lastInput = DateTime.MinValue, _ignoreUntil = DateTime.MinValue;
        private float _animFrom, _animTo; private DateTime _animStart; private double _animMs; private bool _animating;
        private readonly List<KeyValuePair<DateTime, float>> _recent = new List<KeyValuePair<DateTime, float>>();
        private float _hoverT, _pressT;        // progres animasi sorot & tekan
        private double _openT = 1;             // 0..1 animasi membuka
        private DateTime _openStart;
        private bool _closing;

        private static LaunchpadForm _instance;
        public static void ClearCache() { if (_instance != null) { foreach (Bitmap b in _instance._small.Values) b.Dispose(); _instance._small.Clear(); _instance.DropPages(); } _loaded = 0; }

        public LaunchpadForm()
        {
            Text = "Launchpad";
            _instance = this;
            Deactivate += delegate { if (!_closing) CloseAnimated(); };
            KeyPress += OnKeyPressed;
            KeyDown += OnKeyDowned;
            _anim.Interval = 10;
            _anim.Tick += delegate { Tick(); };
        }

        // Muat semua ikon di thread STA terpisah agar Launchpad tidak tersendat.
        public static void Preload()
        {
            if (_loader != null && _loader.IsAlive) return;
            List<AppEntry> all = Apps.All();
            _loader = new System.Threading.Thread(delegate ()
            {
                int missing = all.FindAll(delegate (AppEntry e) { return !Apps.Cached(e); }).Count;
                if (missing > 0)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--build-tiles");
                        psi.UseShellExecute = false; psi.CreateNoWindow = true;
                        using (Process p = Process.Start(psi))
                        {
                            p.PriorityClass = ProcessPriorityClass.BelowNormal;
                            while (!p.WaitForExit(400)) _loaded = all.FindAll(Apps.Cached).Count;
                        }
                    }
                    catch (Exception ex) { Log.Write("build-tiles: " + ex.Message); }
                }
                _loaded = all.Count;
            });
            _loader.IsBackground = true;
            _loader.Start();
        }

        public void Toggle()
        {
            if (Visible) { CloseAnimated(); return; }
            _apps = Apps.All();
            Preload();
            _query = ""; _page = 0; _offset = 0; _hover = -1; _press = -1; _animating = false; _dragging = false; _closing = false;
            _sel = -1; _launchIdx = -1; _par = _parT = PointF.Empty; _hoverT = 0; _hv = 0;
            Filter();
            _waveStart = DateTime.Now; _waveMs = 560;
            Rectangle scr = Screen.FromPoint(Cursor.Position).Bounds;
            _openT = 0; _openStart = DateTime.Now;
            ShowAt(scr, 0.30f);
            _anim.Start();
        }

        private void CloseAnimated()
        {
            if (!Visible || _closing) return;
            _closing = true; _openStart = DateTime.Now;
            _anim.Start();
        }

        private void Filter()
        {
            List<AppEntry> src = _apps;
            if (_cat == "freq")
            {
                src = _apps.FindAll(delegate (AppEntry a) { return LaunchStats.Count(a.Id) > 0; });
                src.Sort(delegate (AppEntry a, AppEntry b) { return LaunchStats.Count(b.Id).CompareTo(LaunchStats.Count(a.Id)); });
            }
            else if (_cat == "recent")
            {
                src = _apps.FindAll(delegate (AppEntry a) { return LaunchStats.Last(a.Id) > 0; });
                src.Sort(delegate (AppEntry a, AppEntry b) { return LaunchStats.Last(b.Id).CompareTo(LaunchStats.Last(a.Id)); });
            }
            if (_query.Length == 0) _view = src;
            else _view = src.FindAll(delegate (AppEntry a) { return a.Name.IndexOf(_query, StringComparison.CurrentCultureIgnoreCase) >= 0; });
            _page = 0; _offset = 0; _animating = false;
            _sel = _query.Length > 0 && _view.Count > 0 ? 0 : -1;
            DropPages();
            if (Visible && _openT >= 1) { _waveStart = DateTime.Now; _waveMs = 300; _anim.Start(); }
        }

        private void SetCat(int i) { if (CatIds[i] == _cat) return; _cat = CatIds[i]; Filter(); Invalidate(); }

        private void DropPages() { foreach (Bitmap b in _pageCache.Values) b.Dispose(); _pageCache.Clear(); }

        private int PerPage { get { return Math.Max(1, _cols * _rows); } }
        private int Pages { get { return Math.Max(1, (_view.Count + PerPage - 1) / PerPage); } }

        private static double EaseOut(double t) { return 1 - Math.Pow(1 - t, 3); }

        private void AnimateTo(float target, double ms)
        {
            _animFrom = _offset; _animTo = target; _animStart = DateTime.Now; _animMs = ms; _animating = true;
            _anim.Start();
        }

        // Pindah halaman dengan animasi; offset disesuaikan agar gerakan menyambung dari posisi sekarang.
        private void GoPage(int p)
        {
            p = Math.Max(0, Math.Min(Pages - 1, p));
            if (p != _page)
            {
                _offset += (p - _page) * ClientSize.Width;   // halaman baru mulai dari posisi tampilan saat ini
                _page = p; _hover = -1;
            }
            AnimateTo(0, 340);
        }

        // ---------- input scroll / touchpad ----------
        private void ScrollBy(float pixels, bool discrete)
        {
            DateTime now = DateTime.Now;
            if (now < _ignoreUntil) { _ignoreUntil = now.AddMilliseconds(200); return; }   // sisa inersia
            if (discrete)
            {
                if (_animating && Math.Abs(_offset) > ClientSize.Width * 0.3f) return;
                GoPage(_page + (pixels < 0 ? 1 : -1));
                _ignoreUntil = now.AddMilliseconds(260);
                return;
            }
            _animating = false; _dragging = true; _lastInput = now;
            bool edge = (_page == 0 && _offset + pixels > 0) || (_page == Pages - 1 && _offset + pixels < 0);
            _offset += edge ? pixels * 0.3f : pixels;                 // efek karet di tepi
            float lim = ClientSize.Width * 0.95f;
            _offset = Math.Max(-lim, Math.Min(lim, _offset));
            _recent.Add(new KeyValuePair<DateTime, float>(now, pixels));
            _recent.RemoveAll(delegate (KeyValuePair<DateTime, float> k) { return (now - k.Key).TotalMilliseconds > 90; });
            _hover = -1;
            // langsung pindah halaman begitu geseran cukup jauh atau cepat (tidak menunggu inersia touchpad)
            float W0 = ClientSize.Width;
            float speed = 0; foreach (KeyValuePair<DateTime, float> k in _recent) speed += k.Value;
            bool canNext = _page < Pages - 1, canPrev = _page > 0;
            if ((_offset < -W0 * 0.12f || speed < -W0 * 0.07f) && canNext) { Commit(_page + 1); return; }
            if ((_offset > W0 * 0.12f || speed > W0 * 0.07f) && canPrev) { Commit(_page - 1); return; }
            _anim.Start();
            Invalidate();
        }

        private void Commit(int page)
        {
            _dragging = false; _recent.Clear();
            _ignoreUntil = DateTime.Now.AddMilliseconds(260);   // abaikan sisa gestur/inersia
            GoPage(page);
        }

        private void Settle()
        {
            _dragging = false;
            float v = 0; foreach (KeyValuePair<DateTime, float> k in _recent) v += k.Value;
            _recent.Clear();
            int W = ClientSize.Width;
            int target = _page;
            if (_offset < -W * 0.22f || v < -W * 0.10f) target = _page + 1;
            else if (_offset > W * 0.22f || v > W * 0.10f) target = _page - 1;
            _ignoreUntil = DateTime.Now.AddMilliseconds(280);
            GoPage(target);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            bool discrete = Math.Abs(e.Delta) >= 120 && (DateTime.Now - _lastInput).TotalMilliseconds > 120 && !_dragging;
            ScrollBy(e.Delta * 0.9f, discrete);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020E) // WM_MOUSEHWHEEL (geser dua jari kiri/kanan)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                ScrollBy(-delta * 0.9f, false);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        private void Tick()
        {
            bool busy = false;
            DateTime now = DateTime.Now;
            if (_dragging)
            {
                busy = true;
                if ((now - _lastInput).TotalMilliseconds > 70) Settle();
            }
            if (_animating)
            {
                double t = Math.Min(1, (now - _animStart).TotalMilliseconds / _animMs);
                _offset = (float)(_animFrom + (_animTo - _animFrom) * EaseOut(t));
                if (t >= 1) { _offset = _animTo; _animating = false; }
                busy = true;
            }
            // buka / tutup
            if (_closing)
            {
                double t = Math.Min(1, (now - _openStart).TotalMilliseconds / 130);
                _openT = 1 - t;
                if (t >= 1) { _closing = false; Hide(); _anim.Stop(); return; }
                busy = true;
            }
            else if (_openT < 1)
            {
                double t = Math.Min(1, (now - _openStart).TotalMilliseconds / 220);
                _openT = EaseOut(t);
                busy = true;
            }
            // sorot (pegas, sedikit memantul) & tekan
            float ht = _hover >= 0 ? 1f : 0f;
            _hv = (_hv + (ht - _hoverT) * 0.22f) * 0.70f; _hoverT += _hv;
            if (Math.Abs(_hoverT - ht) < 0.005f && Math.Abs(_hv) < 0.005f) { _hoverT = ht; _hv = 0; } else busy = true;
            // parallax
            if (Math.Abs(_par.X - _parT.X) > 0.15f || Math.Abs(_par.Y - _parT.Y) > 0.15f) { _par = new PointF(_par.X + (_parT.X - _par.X) * 0.12f, _par.Y + (_parT.Y - _par.Y) * 0.12f); busy = true; }
            if (Waving) busy = true;
            if (_launchIdx >= 0)
            {
                busy = true;
                if ((now - _launchStart).TotalMilliseconds > 260) { _launchIdx = -1; _closing = false; Hide(); _anim.Stop(); return; }
            }
            float pt = _press >= 0 ? 1f : 0f;
            if (Math.Abs(_pressT - pt) > 0.02f) { _pressT += (pt - _pressT) * 0.4f; busy = true; } else _pressT = pt;
            if (Visible && _loaded != _cacheLoaded)
            {
                if (_loaded - Math.Max(0, _cacheLoaded) >= 6 || _loaded >= _apps.Count) { _cacheLoaded = _loaded; DropPages(); }
                if (_loaded < _apps.Count) busy = true;
            }
            Invalidate();
            if (!busy) _anim.Stop();
        }

        private void OnKeyDowned(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (_query.Length > 0) { _query = ""; Filter(); Invalidate(); } else CloseAnimated(); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) GoPage(_page + 1);
            else if (e.KeyCode == Keys.PageUp) GoPage(_page - 1);
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Left || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (_view.Count == 0) return;
                int d = e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Down ? _cols : -_cols;
                int cur = _sel < 0 ? _page * PerPage : _sel;
                int n = Math.Max(0, Math.Min(_view.Count - 1, _sel < 0 ? cur : cur + d));
                _sel = n; _hover = -1;
                if (n / PerPage != _page) GoPage(n / PerPage);
                _anim.Start(); Invalidate(); e.Handled = true;
            }
            else if (e.KeyCode == Keys.Tab) { int ci = Array.IndexOf(CatIds, _cat); SetCat((ci + (e.Shift ? 2 : 1)) % 3); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && _view.Count > 0) Launch(_view[_sel >= 0 ? _sel : Math.Min(_view.Count - 1, _page * PerPage)]);
            else if (e.KeyCode == Keys.Back && _query.Length > 0) { _query = _query.Substring(0, _query.Length - 1); Filter(); Invalidate(); }
        }

        private void OnKeyPressed(object s, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            _query += e.KeyChar; Filter(); Invalidate();
        }

        private void Launch(AppEntry a)
        {
            LaunchStats.Record(a.Id);
            Apps.Launch(a.Id);
            int idx = _view.IndexOf(a);
            if (idx >= 0 && idx / PerPage == _page && Visible) { _launchIdx = idx; _launchStart = DateTime.Now; _closing = true; _openStart = DateTime.Now; _anim.Start(); }
            else CloseAnimated();
        }

        public void SnapshotTo(string dir)
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Size = scr.Size; _apps = Apps.All(); _openT = 1; _query = ""; _cat = "all"; Filter();
            Bitmap bg = new Bitmap(scr.Width, scr.Height);
            using (Graphics g = Graphics.FromImage(bg))
            {
                string wc = Environment.GetEnvironmentVariable("LM_WALL") ?? Path.Combine(Log.Dir, "wallpaper-cache.png");
                if (File.Exists(wc)) using (Bitmap w = new Bitmap(wc)) using (Bitmap bl = Backdrop.Blur(w, 14)) g.DrawImage(bl, 0, 0, scr.Width, scr.Height);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(77, 10, 10, 18))) g.FillRectangle(dim, 0, 0, scr.Width, scr.Height);
            }
            Action<string> shot = delegate (string name)
            {
                using (Bitmap b = new Bitmap(scr.Width, scr.Height))
                {
                    using (Graphics g = Graphics.FromImage(b)) { g.DrawImage(bg, 0, 0); OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, scr.Width, scr.Height))); }
                    b.Save(Path.Combine(dir, name), ImageFormat.Png);
                }
            };
            foreach (int ms in new int[] { 150, 300, 450 }) { _waveStart = DateTime.Now.AddMilliseconds(-ms); _waveMs = 560; shot("lp-wave" + ms + ".png"); }
            _waveStart = DateTime.MinValue;
            shot("lp-all.png");
            _hover = 2; _hoverT = 1; _par = new PointF(Ui.Pf(-6), Ui.Pf(-3)); shot("lp-hover.png");
            // uji: nilai sorot di luar 0..1 (pegas memantul) harus tetap tergambar tanpa pengecualian
            using (Bitmap tb = new Bitmap(scr.Width, scr.Height))
            using (Graphics tg = Graphics.FromImage(tb))
            {
                StringBuilder res = new StringBuilder();
                _par = PointF.Empty; _hover = 2; _waveStart = DateTime.MinValue; _openT = 1;
                foreach (float v in new float[] { -0.3f, -0.08f, -0.01f, 0f, 0.5f, 1f, 1.2f })
                {
                    _hoverT = v;
                    try { PaintLaunchpad(new PaintEventArgs(tg, new Rectangle(0, 0, scr.Width, scr.Height))); res.AppendLine("hoverT " + v + " ok"); }
                    catch (Exception ex) { res.AppendLine("hoverT " + v + " GAGAL " + ex.Message); }
                }
                File.WriteAllText(Path.Combine(dir, "lp-test.txt"), res.ToString());
            }
            _hover = -1; _hoverT = 0; _par = PointF.Empty; _query = "chro"; Filter(); _waveStart = DateTime.MinValue; shot("lp-search.png");
            _query = ""; _cat = "freq"; Filter(); _waveStart = DateTime.MinValue; shot("lp-freq.png");
            bg.Dispose();
        }

        protected override bool ProcessDialogKey(Keys keyData) { if ((keyData & Keys.KeyCode) == Keys.Tab) { OnKeyDowned(this, new KeyEventArgs(keyData)); return true; } return base.ProcessDialogKey(keyData); }

        // Ikon siap pakai pada ukuran layar (tanpa skala ulang tiap frame).
        private Bitmap Small(AppEntry a)
        {
            Bitmap b;
            if (_small.TryGetValue(a, out b)) return b;
            using (Bitmap tile = Apps.LoadTile(a))
            {
                if (tile == null) return null;
                b = new Bitmap(_icon, _icon, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(tile, new Rectangle(0, 0, _icon, _icon));
                }
            }
            _small[a] = b;
            return b;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) return;
            DropPages();
            foreach (Bitmap b in _small.Values) b.Dispose();
            _small.Clear();
            MemoryTrim.Soon();
        }

        private void DoGrid(int W, int H)
        {
            int cellW = Ui.P(140), cellH = Ui.P(132);
            int marginX = Math.Max(Ui.P(90), W / 10);
            int cols = Math.Max(3, Math.Min(8, (W - marginX * 2) / cellW));
            int rows = Math.Max(2, Math.Min(5, (H - Ui.P(250)) / cellH));
            int icon = Ui.P(84);
            if (cols != _cols || rows != _rows || icon != _icon)
            {
                _cols = cols; _rows = rows; DropPages();
                if (icon != _icon) { foreach (Bitmap b in _small.Values) b.Dispose(); _small.Clear(); _icon = icon; }
            }
            _cellW = cellW; _cellH = cellH;
            int gridW = _cols * cellW, gridH = _rows * cellH;
            _grid = new Rectangle((W - gridW) / 2, Ui.P(150) + Math.Max(0, (H - Ui.P(240) - gridH) / 2), gridW, gridH);
        }

        private RectangleF Cell(int i) { return new RectangleF(_grid.X + _par.X + (i % _cols) * _cellW, _grid.Y + _par.Y + (i / _cols) * _cellH, _cellW, _cellH); }

        // Satu halaman digambar sekali ke bitmap; animasi tinggal menggeser bitmap ini.
        private Bitmap PageBitmap(int page)
        {
            Bitmap pb;
            if (_pageCache.TryGetValue(page, out pb)) return pb;
            pb = new Bitmap(_grid.Width, _grid.Height + Ui.P(4), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(pb))
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            using (StringFormat sf = new StringFormat())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                sf.Alignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.NoWrap;
                int start = page * PerPage;
                for (int i = 0; i < PerPage && start + i < _view.Count; i++)
                {
                    AppEntry a = _view[start + i];
                    float cx = (i % _cols) * _cellW, cy = (i / _cols) * _cellH;
                    int ix = (int)Math.Round(cx + (_cellW - _icon) / 2f), iy = (int)Math.Round(cy + Ui.Pf(8));
                    Bitmap t = Small(a);
                    if (t != null) g.DrawImageUnscaled(t, ix, iy);
                    else using (GraphicsPath p = Glass.Round(new RectangleF(ix + _icon * 0.04f, iy + _icon * 0.04f, _icon * 0.92f, _icon * 0.92f), _icon * 0.2f))
                        using (SolidBrush ph = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillPath(ph, p);
                    RectangleF lr = new RectangleF((float)Math.Round(cx + Ui.Pf(4)), (float)Math.Round(cy + Ui.Pf(14) + _icon), _cellW - Ui.Pf(8), _label.GetHeight(g) + 4);
                    g.DrawString(a.Name, _label, sh, new RectangleF(lr.X, lr.Y + 1, lr.Width, lr.Height), sf);
                    g.DrawString(a.Name, _label, lb, lr, sf);
                }
            }
            _pageCache[page] = pb;
            return pb;
        }

        // Pengecualian saat menggambar membuat WinForms mengunci jendela menjadi kotak silang merah sampai aplikasi dimulai ulang.
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintLaunchpad(e); }
            catch (Exception ex) { Log.Write("launchpad paint: " + ex); }
        }

        private void PaintLaunchpad(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Hq(g);
            g.CompositingQuality = CompositingQuality.HighSpeed;
            _hits.Clear(); _dots.Clear();
            int W = ClientSize.Width, H = ClientSize.Height;
            DoGrid(W, H);
            if (_page >= Pages) _page = Pages - 1;

            // kolom pencarian (bercahaya saat mengetik) + jumlah hasil
            RectangleF sb = new RectangleF((float)Math.Round(W / 2f - Ui.P(170)), Ui.P(46), Ui.P(340), Ui.P(38));
            using (GraphicsPath p = Glass.Round(sb, sb.Height / 2))
            {
                if (_query.Length > 0)
                    using (Pen glow = new Pen(Color.FromArgb(60, 140, 190, 255), Ui.Pf(6))) g.DrawPath(glow, p);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(_query.Length > 0 ? 95 : 70, 255, 255, 255))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(_query.Length > 0 ? 170 : 110, 255, 255, 255))) g.DrawPath(pen, p);
            }
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(190, 255, 255, 255))) Glyphs.Draw(g, Glyphs.Search, _glyph, gb, new RectangleF(sb.X + Ui.Pf(10), sb.Y, Ui.Pf(22), sb.Height));
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(_query.Length > 0 ? 250 : 170, 255, 255, 255)))
            {
                bool blink = (DateTime.Now.Millisecond / 500) == 0;
                string txt = _query.Length > 0 ? _query + (blink ? "|" : " ") : L.T("Cari aplikasi");
                SizeF ts = g.MeasureString(txt, _search);
                g.DrawString(txt, _search, tb, (float)Math.Round(sb.X + (sb.Width - ts.Width) / 2), (float)Math.Round(sb.Y + (sb.Height - ts.Height) / 2));
                if (_query.Length > 0)
                {
                    string cnt = _view.Count + L.T(" aplikasi");
                    SizeF cs = g.MeasureString(cnt, _chip);
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(170, 255, 255, 255))) g.DrawString(cnt, _chip, cb, sb.Right - cs.Width - Ui.Pf(14), (float)Math.Round(sb.Y + (sb.Height - cs.Height) / 2));
                }
            }
            // kategori
            {
                float cy = sb.Bottom + Ui.Pf(14), chH = Ui.Pf(28), gap = Ui.Pf(8);
                float[] wd = new float[3]; float tot = 0;
                for (int k = 0; k < 3; k++) { wd[k] = g.MeasureString(CatNames[k], _chipB).Width + Ui.Pf(26); tot += wd[k] + (k > 0 ? gap : 0); }
                float x = W / 2f - tot / 2f;
                for (int k = 0; k < 3; k++)
                {
                    RectangleF r = new RectangleF((float)Math.Round(x), (float)Math.Round(cy), wd[k], chH);
                    _chips[k] = r;
                    bool on = CatIds[k] == _cat;
                    using (GraphicsPath cp = Glass.Round(r, chH / 2))
                    {
                        using (SolidBrush b = new SolidBrush(on ? Color.FromArgb(225, 255, 255, 255) : Color.FromArgb(45, 255, 255, 255))) g.FillPath(b, cp);
                        if (!on) using (Pen pn = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawPath(pn, cp);
                    }
                    using (StringFormat cf = new StringFormat()) using (SolidBrush tb2 = new SolidBrush(on ? Color.FromArgb(255, 25, 25, 32) : Color.FromArgb(235, 255, 255, 255)))
                    { cf.Alignment = cf.LineAlignment = StringAlignment.Center; g.DrawString(CatNames[k], on ? _chipB : _chip, tb2, r, cf); }
                    x += wd[k] + gap;
                }
            }

            float scale = (float)(1.06 - 0.06 * _openT);
            int off = (int)Math.Round(_offset);
            int parX = (int)Math.Round(_par.X), parY = (int)Math.Round(_par.Y);
            bool wave = Waving || _launchIdx >= 0;
            if (wave)
            {
                // ikon digambar satu per satu: muncul bergelombang dari tengah (memantul), atau animasi meluncurkan aplikasi
                double el = (DateTime.Now - _waveStart).TotalMilliseconds;
                double le = _launchIdx >= 0 ? Math.Min(1, (DateTime.Now - _launchStart).TotalMilliseconds / 240.0) : 0;
                float gcx = _grid.X + _grid.Width / 2f, gcy = _grid.Y + _grid.Height / 2f;
                float maxD = (float)Math.Sqrt(_grid.Width * _grid.Width / 4f + _grid.Height * _grid.Height / 4f);
                int start = _page * PerPage;
                using (SolidBrush lb = new SolidBrush(Color.White)) using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.NoWrap;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    for (int i = 0; i < PerPage && start + i < _view.Count; i++)
                    {
                        RectangleF c = Cell(i);
                        float icx = c.X + _cellW / 2f, icy = c.Y + Ui.Pf(8) + _icon / 2f;
                        double t = 1;
                        if (_launchIdx < 0)
                        {
                            float dist = (float)Math.Sqrt((icx - gcx) * (icx - gcx) + (icy - gcy) * (icy - gcy)) / Math.Max(1, maxD);
                            double delay = dist * _waveMs * 0.45;
                            t = Math.Max(0, Math.Min(1, (el - delay) / (_waveMs * 0.55)));
                        }
                        double eb = t <= 0 ? 0 : 1 + 2.4 * Math.Pow(t - 1, 3) + 1.4 * Math.Pow(t - 1, 2);   // easeOutBack
                        float sc = (float)(0.35 + 0.65 * eb), alpha = (float)Math.Min(1, t * 1.4);
                        if (_launchIdx >= 0)
                        {
                            bool me = start + i == _launchIdx;
                            sc = me ? (float)(1 + 1.3 * le) : (float)(1 - 0.12 * le);
                            alpha = me ? (float)(1 - le) : (float)(1 - le * 1.2);
                        }
                        if (alpha <= 0.01f) continue;
                        float s2 = _icon * sc;
                        Bitmap bm = Small(_view[start + i]);
                        RectangleF dst = new RectangleF(icx - s2 / 2f, icy - s2 / 2f + (float)((1 - eb) * Ui.Pf(18)), s2, s2);
                        if (bm != null)
                            using (ImageAttributes ia = new ImageAttributes())
                            {
                                ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = Math.Max(0, Math.Min(1, alpha)); ia.SetColorMatrix(cm);
                                g.DrawImage(bm, new PointF[] { dst.Location, new PointF(dst.Right, dst.Top), new PointF(dst.Left, dst.Bottom) }, new RectangleF(0, 0, bm.Width, bm.Height), GraphicsUnit.Pixel, ia);
                            }
                        if (_launchIdx < 0 && t > 0.6)
                            using (SolidBrush lb2 = new SolidBrush(Color.FromArgb((int)(245 * Math.Min(1, (t - 0.6) / 0.4)), 255, 255, 255)))
                                g.DrawString(_view[start + i].Name, _label, lb2, new RectangleF(c.X + Ui.Pf(4), c.Y + Ui.Pf(14) + _icon, _cellW - Ui.Pf(8), _label.GetHeight(g) + 4), sf);
                    }
                }
            }
            else
            {
                // halaman: bitmap cache digeser (ringan); halaman tetangga sedikit memudar saat digeser
                System.Drawing.Drawing2D.GraphicsState gs = g.Save();
                if (Math.Abs(scale - 1f) > 0.001f)
                {
                    g.TranslateTransform(W / 2f, _grid.Y + _grid.Height / 2f);
                    g.ScaleTransform(scale, scale);
                    g.TranslateTransform(-W / 2f, -(_grid.Y + _grid.Height / 2f));
                    g.InterpolationMode = InterpolationMode.Bilinear;
                }
                for (int pg = Math.Max(0, _page - 1); pg <= Math.Min(Pages - 1, _page + 1); pg++)
                {
                    int px = _grid.X + off + (pg - _page) * W + parX;
                    if (px + _grid.Width < 0 || px > W) continue;
                    Bitmap bmp = PageBitmap(pg);
                    float fade = 1f - 0.55f * Math.Min(1f, Math.Abs(px - _grid.X - parX) / (float)W);
                    float a = (float)Math.Max(0, Math.Min(1, _openT * 1.3)) * fade;
                    if (a < 0.999f)
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = a; ia.SetColorMatrix(cm);
                            g.DrawImage(bmp, new Rectangle(px, _grid.Y + parY, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                        }
                    else if (Math.Abs(scale - 1f) > 0.001f) g.DrawImage(bmp, px, _grid.Y + parY, bmp.Width, bmp.Height);
                    else g.DrawImageUnscaled(bmp, px, _grid.Y + parY);
                }
                g.Restore(gs);
            }

            // ikon yang disorot / ditekan digambar ulang di atas (terangkat atau mengecil)
            bool still = !_animating && !_dragging && Math.Abs(_offset) < 0.5f && _openT >= 1 && !wave;
            if (still)
            {
                int start = _page * PerPage;
                for (int i = 0; i < PerPage && start + i < _view.Count; i++) _hits.Add(new KeyValuePair<int, RectangleF>(start + i, Cell(i)));
                int focus = _press >= 0 ? _press : _hover;
                if (focus >= start && focus < start + PerPage && focus < _view.Count)
                {
                    RectangleF c = Cell(focus - start);
                    Bitmap t = Small(_view[focus]);
                    if (t != null)
                    {
                        float grow = _press >= 0 ? -_icon * 0.08f * _pressT : _icon * 0.08f * _hoverT;
                        float ix = c.X + (_cellW - _icon) / 2f - grow / 2f, iy = c.Y + Ui.Pf(8) - grow / 2f - Ui.Pf(3) * _hoverT;
                        RectangleF clear = new RectangleF(c.X + (_cellW - _icon) / 2f - 2, c.Y + Ui.Pf(8) - 2, _icon + 4, _icon + 4);
                        if (Back != null) g.DrawImage(Back, clear, clear, GraphicsUnit.Pixel);   // hapus ikon lama dari latar
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, (int)(40 * _hoverT))), 255, 255, 255)))   // animasi pegas bisa sempat di bawah nol
                        using (GraphicsPath gp = Glass.Round(new RectangleF(ix - 6, iy - 6, _icon + grow + 12, _icon + grow + 12), (_icon + grow) * 0.26f))
                            g.FillPath(glow, gp);
                        g.DrawImage(t, ix, iy, _icon + grow, _icon + grow);
                        if (_press < 0 && _hoverT > 0.05f)
                        {
                            // label disorot: pil lembut di belakang nama
                            string nm = _view[focus].Name;
                            SizeF ns = g.MeasureString(nm, _label);
                            float lw = Math.Min(_cellW - Ui.Pf(6), ns.Width + Ui.Pf(16));
                            RectangleF lr = new RectangleF(c.X + (_cellW - lw) / 2f, c.Y + Ui.Pf(12) + _icon, lw, ns.Height + Ui.Pf(4));
                            using (GraphicsPath lp = Glass.Round(lr, lr.Height / 2)) using (SolidBrush lb = new SolidBrush(Color.FromArgb((int)(70 * Math.Min(1, _hoverT)), 255, 255, 255))) g.FillPath(lb, lp);
                        }
                    }
                }
                // cincin pilihan keyboard
                if (_sel >= start && _sel < start + PerPage && _sel < _view.Count)
                {
                    RectangleF c = Cell(_sel - start);
                    RectangleF rr = new RectangleF(c.X + (_cellW - _icon) / 2f - Ui.Pf(6), c.Y + Ui.Pf(2), _icon + Ui.Pf(12), _icon + Ui.Pf(12));
                    using (GraphicsPath rp = Glass.Round(rr, rr.Width * 0.27f)) using (Pen pn = new Pen(Color.FromArgb(200, 255, 255, 255), Ui.Pf(2.5))) g.DrawPath(pn, rp);
                }
            }

            if (_view.Count == 0)
                using (SolidBrush nb = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                {
                    string msg = _query.Length > 0 ? L.T("Tidak ada aplikasi yang cocok") : _cat == "freq" ? L.T("Buka aplikasi dari Launchpad, yang paling sering dipakai muncul di sini") : _cat == "recent" ? L.T("Belum ada aplikasi yang dibuka dari Launchpad") : L.T("Tidak ada aplikasi");
                    SizeF ms = g.MeasureString(msg, _search);
                    g.DrawString(msg, _search, nb, (float)Math.Round((W - ms.Width) / 2), H / 2f);
                }

            // titik halaman: posisi aktif mengikuti geseran jari
            int pages = Pages;
            if (pages > 1)
            {
                float d = Ui.Pf(8), gap = Ui.Pf(12), wide = Ui.Pf(22);
                float live = _page - _offset / Math.Max(1, W);
                float total = (pages - 1) * (d + gap) + wide;
                float x = W / 2f - total / 2f, y0 = _grid.Bottom + Ui.Pf(26);
                for (int i = 0; i < pages; i++)
                {
                    float k = Math.Max(0f, 1f - Math.Abs(live - i));
                    float dw = d + (wide - d) * k;
                    RectangleF r = new RectangleF(x, y0, dw, d);
                    using (GraphicsPath dp = Glass.Round(r, d / 2))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(95 + 155 * k), 255, 255, 255))) g.FillPath(b, dp);
                    RectangleF hit = r; hit.Inflate(Ui.Pf(5), Ui.Pf(8));
                    _dots.Add(hit);
                    x += dw + gap;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            float W2 = Math.Max(1, ClientSize.Width / 2f), H2 = Math.Max(1, ClientSize.Height / 2f);
            _parT = new PointF(-(e.X - W2) / W2 * Ui.Pf(9), -(e.Y - H2) / H2 * Ui.Pf(6));
            _anim.Start();
            int h = -1;
            foreach (KeyValuePair<int, RectangleF> kv in _hits) if (kv.Value.Contains(e.Location)) { h = kv.Key; break; }
            if (h != _hover) { _hover = h; if (h >= 0) _sel = -1; _anim.Start(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (KeyValuePair<int, RectangleF> kv in _hits) if (kv.Value.Contains(e.Location)) { _press = kv.Key; _anim.Start(); break; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int pressed = _press; _press = -1; _anim.Start();
            for (int i = 0; i < _dots.Count; i++) if (_dots[i].Contains(e.Location)) { GoPage(i); return; }
            for (int k = 0; k < 3; k++) if (_chips[k].Contains(e.Location)) { SetCat(k); return; }
            int idx = -1;
            foreach (KeyValuePair<int, RectangleF> kv in _hits) if (kv.Value.Contains(e.Location)) { idx = kv.Key; break; }
            if (idx < 0) { if (e.Button == MouseButtons.Left && !_dragging) CloseAnimated(); return; }
            AppEntry a = _view[idx];
            if (e.Button == MouseButtons.Left) { if (pressed == idx) Launch(a); return; }
            if (e.Button == MouseButtons.Right)
            {
                ContextMenuStrip m = Menus.Create();
                Menus.Add(m, a.Name, null);
                Menus.Sep(m);
                Menus.Add(m, L.T("Buka"), delegate { Launch(a); });
                Menus.Add(m, L.T("Tambahkan ke Dock"), delegate { if (AddToDock != null) AddToDock("app:" + a.Id); });
                m.Show(this, e.Location);
            }
        }
    }

    // ============================================================
    //  PUSAT NOTIFIKASI + KONTROL (gaya macOS)
    // ============================================================
    internal sealed class NotificationCenterForm : GlassPopup
    {
        private List<Notif> _items = new List<Notif>();
        private readonly HashSet<long> _dismissed = new HashSet<long>();
        private long _clearedBefore;
        private int _scroll, _contentH;
        private float _vol = -1; private int _bri = -1;
        private string _drag;
        private DateTime _lastBri = DateTime.MinValue;
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private RectangleF _volBar, _briBar;
        private readonly System.Windows.Forms.Timer _refresh = new System.Windows.Forms.Timer();
        private readonly Font _big = Ui.F(20f, FontStyle.Bold), _h = Ui.F(10f, FontStyle.Bold), _t = Ui.F(9.25f, FontStyle.Regular), _s = Ui.F(8.25f, FontStyle.Regular), _sb = Ui.F(8.25f, FontStyle.Bold);
        private readonly CultureInfo _id = L.Culture;

        public NotificationCenterForm()
        {
            Text = L.T("Pusat Notifikasi");
            Deactivate += delegate { if (_drag == null) Hide(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
            MouseWheel += delegate (object s, MouseEventArgs e)
            {
                _scroll = Math.Max(0, Math.Min(Math.Max(0, _contentH - ClientSize.Height), _scroll - e.Delta / 2));
                Invalidate();
            };
            long.TryParse(Settings.GetS("NotifClearedBefore", "0"), out _clearedBefore);
            _refresh.Interval = 3000;
            _refresh.Tick += delegate { if (Visible) { Reload(); Invalidate(); } };
        }

        public void Toggle(int topOffset)
        {
            if (Visible) { Hide(); return; }
            Reload();
            _vol = SystemControls.Volume;
            _bri = SystemControls.Brightness;
            _scroll = 0;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            int w = Ui.P(380);
            int top = Math.Max(wa.Top, scr.Top + topOffset) + Ui.P(8);
            Rectangle r = new Rectangle(scr.Right - w - Ui.P(10), top, w, wa.Bottom - top - Ui.P(10));
            ShowAt(r, 0.45f);
            Round(true);
            _refresh.Start();
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) _refresh.Stop(); }

        private void Reload()
        {
            _items = NotifDb.Visible(40);
        }

        private void Card(Graphics g, RectangleF r)
        {
            using (GraphicsPath p = Glass.Round(r, Ui.Pf(14)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(55, 255, 255, 255))) g.DrawPath(pen, p);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Hq(g);
            _hits.Clear();
            float W = ClientSize.Width, pad = Ui.Pf(14), y = pad - _scroll, x = pad, cw = W - pad * 2;
            using (Pen border = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            SolidBrush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            SolidBrush soft = new SolidBrush(Color.FromArgb(175, 235, 235, 245));
            try
            {
                // jam & tanggal
                g.DrawString(DateTime.Now.ToString("HH.mm", _id), _big, white, x, y);
                g.DrawString(DateTime.Now.ToString("dddd, d MMMM yyyy", _id), _t, soft, x + Ui.Pf(2), y + Ui.Pf(36));
                y += Ui.Pf(64);

                // kalender
                float calH = Ui.Pf(200);
                Card(g, new RectangleF(x, y, cw, calH));
                DrawCalendar(g, new RectangleF(x + Ui.Pf(12), y + Ui.Pf(10), cw - Ui.Pf(24), calH - Ui.Pf(20)), white, soft);
                y += calH + Ui.Pf(10);

                // kontrol
                float ctlH = Ui.Pf(_bri >= 0 ? 150 : 108);
                Card(g, new RectangleF(x, y, cw, ctlH));
                float cy = y + Ui.Pf(12);
                _volBar = Slider(g, "Volume", _vol, x + Ui.Pf(12), cy, cw - Ui.Pf(24), white, soft);
                cy += Ui.Pf(42);
                if (_bri >= 0) { _briBar = Slider(g, L.T("Kecerahan"), _bri / 100f, x + Ui.Pf(12), cy, cw - Ui.Pf(24), white, soft); cy += Ui.Pf(42); }
                else _briBar = RectangleF.Empty;
                string[] labels = { "Wi-Fi", "Bluetooth", L.T("Fokus"), L.T("Pengaturan") };
                Action[] acts = {
                    delegate { Hide(); Shell.Run("ms-settings:network-wifi", null); },
                    delegate { Hide(); Shell.Run("ms-settings:bluetooth", null); },
                    delegate { Hide(); Shell.Run("ms-settings:quiethours", null); },
                    delegate { Hide(); Native.Keys(Native.VK_LWIN, (byte)'A'); } };
                float bw = (cw - Ui.Pf(24) - Ui.Pf(8) * 3) / 4f;
                for (int i = 0; i < 4; i++)
                {
                    RectangleF br = new RectangleF(x + Ui.Pf(12) + i * (bw + Ui.Pf(8)), cy, bw, Ui.Pf(30));
                    Glass.Pill(g, br, 170);
                    SizeF ts = g.MeasureString(labels[i], _s);
                    g.DrawString(labels[i], _s, white, br.X + (br.Width - ts.Width) / 2, br.Y + (br.Height - ts.Height) / 2);
                    _hits.Add(new KeyValuePair<RectangleF, Action>(br, acts[i]));
                }
                y += ctlH + Ui.Pf(16);

                // notifikasi
                g.DrawString(L.T("Pemberitahuan"), _h, white, x + Ui.Pf(2), y);
                if (_items.Count > 0)
                {
                    string clr = L.T("Bersihkan");
                    SizeF cs = g.MeasureString(clr, _sb);
                    RectangleF cr = new RectangleF(x + cw - cs.Width - Ui.Pf(4), y, cs.Width + Ui.Pf(4), cs.Height + Ui.Pf(2));
                    using (SolidBrush ab = new SolidBrush(Color.FromArgb(255, 100, 170, 255))) g.DrawString(clr, _sb, ab, cr.X, cr.Y + Ui.Pf(2));
                    _hits.Add(new KeyValuePair<RectangleF, Action>(cr, delegate
                    {
                        _clearedBefore = _items[0].Arrival;
                        NotifDb.ClearAll(_clearedBefore);
                        Reload(); Invalidate();
                    }));
                }
                y += Ui.Pf(26);

                if (_items.Count == 0)
                {
                    g.DrawString(L.T("Tidak ada pemberitahuan baru"), _t, soft, x + Ui.Pf(2), y);
                    y += Ui.Pf(30);
                }
                using (StringFormat sf = new StringFormat())
                {
                    sf.Trimming = StringTrimming.EllipsisWord;
                    foreach (Notif n in _items)
                    {
                        float tw = cw - Ui.Pf(24);
                        SizeF bodySz = string.IsNullOrEmpty(n.Body) ? SizeF.Empty : g.MeasureString(n.Body, _t, (int)tw);
                        float bodyH = Math.Min(bodySz.Height, _t.GetHeight(g) * 3);
                        float h = Ui.Pf(52) + bodyH;
                        RectangleF r = new RectangleF(x, y, cw, h);
                        Card(g, r);
                        g.DrawString(n.App.ToUpperInvariant(), _s, soft, x + Ui.Pf(12), y + Ui.Pf(9));
                        string ago = NotifDb.Ago(n.Time);
                        SizeF az = g.MeasureString(ago, _s);
                        g.DrawString(ago, _s, soft, x + cw - az.Width - Ui.Pf(30), y + Ui.Pf(9));
                        RectangleF xr = new RectangleF(x + cw - Ui.Pf(24), y + Ui.Pf(8), Ui.Pf(16), Ui.Pf(16));
                        using (Pen xp = new Pen(Color.FromArgb(170, 255, 255, 255), Ui.Pf(1.4)))
                        {
                            g.DrawLine(xp, xr.X + Ui.Pf(4), xr.Y + Ui.Pf(4), xr.Right - Ui.Pf(4), xr.Bottom - Ui.Pf(4));
                            g.DrawLine(xp, xr.Right - Ui.Pf(4), xr.Y + Ui.Pf(4), xr.X + Ui.Pf(4), xr.Bottom - Ui.Pf(4));
                        }
                        long nid = n.Id;
                        _hits.Add(new KeyValuePair<RectangleF, Action>(xr, delegate { NotifDb.Dismiss(nid); Reload(); Invalidate(); }));
                        g.DrawString(n.Title ?? "", _h, white, new RectangleF(x + Ui.Pf(12), y + Ui.Pf(26), tw, _h.GetHeight(g) + 2), sf);
                        if (bodyH > 0) g.DrawString(n.Body, _t, soft, new RectangleF(x + Ui.Pf(12), y + Ui.Pf(46), tw, bodyH + 2), sf);
                        Notif nn = n;
                        _hits.Add(new KeyValuePair<RectangleF, Action>(r, delegate { Hide(); NotifDb.Open(nn); }));
                        y += h + Ui.Pf(8);
                    }
                }
                RectangleF all = new RectangleF(x, y, cw, Ui.Pf(30));
                Glass.Pill(g, all, 150);
                string more = L.T("Buka Pusat Notifikasi Windows");
                SizeF ms = g.MeasureString(more, _s);
                g.DrawString(more, _s, white, all.X + (all.Width - ms.Width) / 2, all.Y + (all.Height - ms.Height) / 2);
                _hits.Add(new KeyValuePair<RectangleF, Action>(all, delegate { Hide(); Native.Keys(Native.VK_LWIN, (byte)'N'); }));
                y += Ui.Pf(30) + pad;
                _contentH = (int)(y + _scroll);
            }
            finally { white.Dispose(); soft.Dispose(); }
        }

        private void DrawCalendar(Graphics g, RectangleF r, Brush white, Brush soft)
        {
            DateTime now = DateTime.Now;
            g.DrawString(now.ToString("MMMM yyyy", _id), _h, white, r.X, r.Y);
            string[] days = { L.T("Sen"), L.T("Sel"), L.T("Rab"), L.T("Kam"), L.T("Jum"), L.T("Sab"), L.T("Min") };
            float cw = r.Width / 7f, top = r.Y + Ui.Pf(26), rh = (r.Bottom - top) / 7f;
            for (int i = 0; i < 7; i++)
            {
                SizeF s = g.MeasureString(days[i], _s);
                g.DrawString(days[i], _s, soft, r.X + i * cw + (cw - s.Width) / 2, top);
            }
            DateTime first = new DateTime(now.Year, now.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            int dim = DateTime.DaysInMonth(now.Year, now.Month);
            for (int d = 1; d <= dim; d++)
            {
                int cell = offset + d - 1;
                float cx = r.X + (cell % 7) * cw, cy = top + rh * (1 + cell / 7);
                string txt = d.ToString();
                SizeF s = g.MeasureString(txt, _t);
                if (d == now.Day)
                {
                    float dd = Math.Min(cw, rh) * 0.95f;
                    using (SolidBrush red = new SolidBrush(Color.FromArgb(255, 255, 69, 58)))
                        g.FillEllipse(red, cx + (cw - dd) / 2, cy + (rh - dd) / 2, dd, dd);
                }
                g.DrawString(txt, _t, (cell % 7 == 6 && d != now.Day) ? soft : white, cx + (cw - s.Width) / 2, cy + (rh - s.Height) / 2);
            }
        }

        private RectangleF Slider(Graphics g, string label, float v, float x, float y, float w, Brush white, Brush soft)
        {
            g.DrawString(label, _sb, white, x, y);
            string pct = v < 0 ? "-" : ((int)Math.Round(v * 100)) + "%";
            SizeF ps = g.MeasureString(pct, _s);
            g.DrawString(pct, _s, soft, x + w - ps.Width, y);
            RectangleF bar = new RectangleF(x, y + Ui.Pf(20), w, Ui.Pf(14));
            using (GraphicsPath p = Glass.Round(bar, bar.Height / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, p);
            if (v >= 0)
            {
                float fw = Math.Max(bar.Height, bar.Width * v);
                using (GraphicsPath fp = Glass.Round(new RectangleF(bar.X, bar.Y, fw, bar.Height), bar.Height / 2))
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(245, 255, 255, 255))) g.FillPath(fb, fp);
            }
            return new RectangleF(bar.X, bar.Y - Ui.Pf(6), bar.Width, bar.Height + Ui.Pf(12));
        }

        private void SetFromSlider(string which, int mx)
        {
            RectangleF bar = which == "vol" ? _volBar : _briBar;
            float v = Math.Max(0f, Math.Min(1f, (mx - bar.X) / bar.Width));
            if (which == "vol") { _vol = v; SystemControls.Volume = v; }
            else
            {
                _bri = (int)Math.Round(v * 100);
                if ((DateTime.Now - _lastBri).TotalMilliseconds > 120) { SystemControls.Brightness = _bri; _lastBri = DateTime.Now; }
            }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_volBar.Contains(e.Location) && _vol >= 0) { _drag = "vol"; SetFromSlider(_drag, e.X); return; }
            if (!_briBar.IsEmpty && _briBar.Contains(e.Location)) { _drag = "bri"; SetFromSlider(_drag, e.X); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != null) SetFromSlider(_drag, e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag != null)
            {
                if (_drag == "bri") SystemControls.Brightness = _bri;
                else SoundKit.Feedback();
                _drag = null;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            foreach (KeyValuePair<RectangleF, Action> kv in _hits)
                if (kv.Key.Contains(e.Location)) { kv.Value(); return; }
        }
    }

    // ============================================================
    //  BANNER NOTIFIKASI gaya macOS (kanan atas)
    // ============================================================
    internal sealed class BannerForm : OverlayForm
    {
        private readonly Notif _n;
        private readonly int W = Ui.P(360), H = Ui.P(82);
        private double _slide = 1;   // 1 = di luar layar, 0 = terlihat
        private int _life;
        private bool _closing, _hover;
        public int Slot;
        public int TopOffset;
        public event EventHandler Done;
        private readonly Font _app = Ui.F(8f, FontStyle.Regular), _title = Ui.F(9.5f, FontStyle.Bold), _body = Ui.F(9f, FontStyle.Regular);

        public BannerForm(Notif n)
        {
            _n = n;
            Configure(W + Ui.P(24), H + Ui.P(16));
        }

        public override void Reposition()
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            int x = s.Right - Bw - Ui.P(4) + (int)(_slide * (Bw + Ui.P(10)));
            int y = s.Top + TopOffset + Ui.P(6) + Slot * (H + Ui.P(10));
            Location = new Point(x, y);
        }

        protected override bool OnFrame()
        {
            Point p = LocalCursor();
            _hover = p.X >= 0 && p.Y >= 0 && p.X < Bw && p.Y < Bh;
            if (!_hover && !_closing) _life++;
            if (_life > 60 * 6) _closing = true;
            double target = _closing ? 1 : 0;
            if (Math.Abs(_slide - target) > 0.002) { _slide += (target - _slide) * 0.2; Reposition(); }
            else if (_closing)
            {
                Hide();
                if (Done != null) Done(this, EventArgs.Empty);
                BeginInvoke((Action)delegate { Close(); });
            }
            else Reposition();
            return false;
        }

        public void Dismiss() { _closing = true; }

        protected override void Render()
        {
            G.Clear(Color.Transparent);
            RectangleF r = new RectangleF(Ui.P(12), Ui.P(6), W, H);
            Glass.Shadow(G, r, Ui.Pf(18), 70);
            Glass.GlassPanel(G, r, Ui.Pf(18), 245);
            float x = r.X + Ui.Pf(14), y = r.Y + Ui.Pf(10), tw = r.Width - Ui.Pf(28);
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(180, 230, 230, 240)))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(250, 255, 255, 255)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Trimming = StringTrimming.EllipsisWord;
                sf.FormatFlags = StringFormatFlags.LineLimit;
                G.DrawString(_n.App.ToUpperInvariant(), _app, soft, x, y);
                string now = "sekarang";
                SizeF ns = G.MeasureString(now, _app);
                G.DrawString(now, _app, soft, r.Right - Ui.Pf(14) - ns.Width, y);
                G.DrawString(_n.Title ?? "", _title, white, new RectangleF(x, y + Ui.Pf(17), tw, _title.GetHeight(G) + 2), sf);
                if (!string.IsNullOrEmpty(_n.Body))
                    G.DrawString(_n.Body.Replace("\n", " "), _body, soft, new RectangleF(x, y + Ui.Pf(36), tw, _body.GetHeight(G) * 2 + 2), sf);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) NotifDb.Open(_n);
            Dismiss();
        }
    }

    // ============================================================
    //  TOMBOL WINDOWS -> LAUNCHPAD (hook keyboard tingkat rendah)
    // ============================================================
    internal static class WinKeyHook
    {
        private delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] private struct KB { public int vk, scan, flags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Proc fn, IntPtr mod, uint tid);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

        private static IntPtr _hook = IntPtr.Zero;
        private static Proc _proc;
        private static bool _winDown, _other;
        private static Control _invoker;
        private static Action _onWin;
        private static Action<int> _onVol;
        private static bool _winOn, _volOn;

        public static bool Active { get { return _hook != IntPtr.Zero; } }

        private static void Ensure(Control invoker)
        {
            if (invoker != null) _invoker = invoker;
            if (_hook != IntPtr.Zero) return;
            _proc = Hook;
            _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Write("hook gagal: " + Marshal.GetLastWin32Error());
        }

        private static void Release() { if (!_winOn && !_volOn && _hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } }

        // Tombol Win -> Launchpad
        public static void Start(Control invoker, Action onWin) { _onWin = onWin; _winOn = true; Ensure(invoker); }
        public static void Stop() { _winOn = false; _winDown = false; Release(); }

        // Tombol volume (Fn+Vol) ditangani sendiri: pop-up volume Windows tidak muncul. dir: +1 naik, -1 turun, 0 bisu.
        public static void StartVolume(Control invoker, Action<int> onVol) { _onVol = onVol; _volOn = true; Ensure(invoker); }
        public static void StopVolume() { _volOn = false; Release(); }

        public static void StopAll() { _winOn = _volOn = false; Release(); }

        private static IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                KB k = (KB)Marshal.PtrToStructure(l, typeof(KB));
                bool injected = (k.flags & 0x10) != 0;
                int msg = w.ToInt32();
                bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                if (_volOn && (k.vk == 0xAF || k.vk == 0xAE || k.vk == 0xAD))
                {
                    // dari keyboard maupun utilitas hotkey laptop (sering berupa input tersuntik)
                    if (down)
                    {
                        int dir = k.vk == 0xAF ? 1 : (k.vk == 0xAE ? -1 : 0);
                        try { if (_invoker != null && _invoker.IsHandleCreated) _invoker.BeginInvoke(_onVol, dir); } catch { }
                    }
                    return new IntPtr(1);   // telan: Windows tidak menampilkan pop-up volume
                }
                if (!injected && _winOn)
                {
                    if (k.vk == 0x5B || k.vk == 0x5C)
                    {
                        if (down && !_winDown) { _winDown = true; _other = false; }
                        else if (up && _winDown)
                        {
                            _winDown = false;
                            if (!_other)
                            {
                                // tekan tombol netral agar Windows tidak membuka Start, lalu buka Launchpad
                                Native.keybd_event(0xE8, 0, 0, IntPtr.Zero);
                                Native.keybd_event(0xE8, 0, Native.KEYEVENTF_KEYUP, IntPtr.Zero);
                                try { if (_invoker != null && _invoker.IsHandleCreated) _invoker.BeginInvoke(_onWin); } catch { }
                            }
                        }
                    }
                    else if (down && _winDown) _other = true;
                }
            }
            return CallNextHookEx(_hook, code, w, l);
        }
    }

    // ============================================================
    //  PENYIMPANAN ISI DOCK (bisa ditambah / dikurangi)
    // ============================================================
    internal static class DockStore
    {
        private static readonly string FilePath = Path.Combine(Log.Dir, "dock.txt");
        public const string LaunchpadEntry = "liquidmac:launchpad";

        public static bool Exists { get { return File.Exists(FilePath); } }

        public static List<string> Load()
        {
            List<string> l = new List<string>();
            try { foreach (string s in File.ReadAllLines(FilePath)) if (s.Trim().Length > 0) l.Add(s.Trim()); }
            catch { }
            return l;
        }

        public static void Save(List<string> entries)
        {
            try { Directory.CreateDirectory(Log.Dir); File.WriteAllLines(FilePath, entries.ToArray()); }
            catch (Exception ex) { Log.Write("dock save: " + ex.Message); }
        }

        public static bool Contains(string e)
        {
            foreach (string s in Load()) if (string.Equals(s, e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void Add(string entry, int index)
        {
            List<string> l = Load();
            l.RemoveAll(delegate (string s) { return string.Equals(s, entry, StringComparison.OrdinalIgnoreCase); });
            if (index < 0 || index > l.Count) index = l.Count;
            l.Insert(index, entry);
            Save(l);
        }

        public static void Remove(string entry)
        {
            List<string> l = Load();
            l.RemoveAll(delegate (string s) { return string.Equals(s, entry, StringComparison.OrdinalIgnoreCase); });
            Save(l);
        }

        public static void Move(string entry, int delta)
        {
            List<string> l = Load();
            int i = l.FindIndex(delegate (string s) { return string.Equals(s, entry, StringComparison.OrdinalIgnoreCase); });
            if (i < 0) return;
            int j = Math.Max(0, Math.Min(l.Count - 1, i + delta));
            if (i == j) return;
            string v = l[i]; l.RemoveAt(i); l.Insert(j, v);
            Save(l);
        }

        public static void Reset() { try { File.Delete(FilePath); } catch { } }
    }

    // ============================================================
    //  IKON DESKTOP (sembunyikan / tampilkan, tanpa menghapus berkas)
    // ============================================================
    internal static class DesktopIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        private static IntPtr DefView()
        {
            IntPtr prog = Native.FindWindow("Progman", null);
            IntPtr dv = FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            {
                dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero) return dv;
            }
            return IntPtr.Zero;
        }

        public static bool Visible
        {
            get
            {
                IntPtr dv = DefView();
                IntPtr lv = dv == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                return lv == IntPtr.Zero || Native.IsWindowVisible(lv);
            }
            set
            {
                try
                {
                    if (Visible == value) return;
                    IntPtr dv = DefView();
                    if (dv != IntPtr.Zero) SendMessage(dv, 0x0111 /* WM_COMMAND */, (IntPtr)0x7402 /* toggle ikon desktop */, IntPtr.Zero);
                }
                catch (Exception ex) { Log.Write("desktop icons: " + ex.Message); }
            }
        }
    }

    // ============================================================
    //  HEMAT RAM: kumpulkan sampah & kembalikan halaman memori ke Windows
    // ============================================================
    internal static class MemoryTrim
    {
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr h);
        private static System.Windows.Forms.Timer _soon;

        public static void Run()
        {
            try
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        public static void Soon()
        {
            if (_soon == null) { _soon = new System.Windows.Forms.Timer(); _soon.Interval = 1500; _soon.Tick += delegate { _soon.Stop(); Run(); }; }
            _soon.Stop(); _soon.Start();
        }
    }

    // Penyaring gestur scroll/touchpad: ambang lebih tinggi, kunci setelah berpindah,
    // dan sisa inersia diabaikan sampai gerakan berhenti -> satu gestur = satu langkah.
    internal sealed class SwipeGate
    {
        private int _acc;
        private DateTime _last = DateTime.MinValue, _lockUntil = DateTime.MinValue;
        private readonly int _threshold;
        public SwipeGate(int threshold) { _threshold = threshold; }

        // Mengembalikan +1 / -1 bila satu langkah terjadi, 0 bila belum.
        public int Feed(int delta)
        {
            DateTime now = DateTime.Now;
            bool gap = (now - _last).TotalMilliseconds > 220;
            _last = now;
            if (now < _lockUntil) { _lockUntil = now.AddMilliseconds(260); return 0; }   // inersia: perpanjang kunci
            if (gap || Math.Sign(delta) != Math.Sign(_acc)) _acc = 0;
            _acc += delta;
            if (Math.Abs(_acc) < _threshold) return 0;
            int dir = _acc > 0 ? 1 : -1;
            _acc = 0;
            _lockUntil = now.AddMilliseconds(480);
            return dir;
        }
    }
}
