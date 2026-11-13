using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace LiquidMac
{
    // Membuka aplikasi Foto LiquidMac (proses terpisah: LiquidMac.exe --photos [berkas]).
    internal static class PhotosLauncher
    {
        private static readonly string[] Exts = { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".dng", ".cr2", ".nef", ".arw",
            ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".mpg", ".mpeg", ".3gp", ".ts", ".m2ts", ".flv" };

        public static bool IsMedia(string path)
        {
            string e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Array.IndexOf(Exts, e) >= 0;
        }

        public static void Open(string file)
        {
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(Application.ExecutablePath, "--photos" + (string.IsNullOrEmpty(file) ? "" : " \"" + file + "\""));
                si.UseShellExecute = false;
                foreach (string k in new System.Collections.Generic.List<string>(EnvKeys())) si.EnvironmentVariables.Remove(k);
                Process.Start(si);
            }
            catch (Exception ex) { Log.Write("buka foto: " + ex.Message); }
        }

        private static System.Collections.Generic.IEnumerable<string> EnvKeys()
        {
            foreach (System.Collections.DictionaryEntry d in Environment.GetEnvironmentVariables())
            {
                string k = d.Key as string;
                if (k != null && k.StartsWith("LM_", StringComparison.OrdinalIgnoreCase)) yield return k;
            }
        }
    }
    // Ikon aplikasi Foto (desain sendiri): ubin membulat, langit biru, matahari, dua lapis bukit, bingkai foto putih tipis.
    internal static class PhotosIcon
    {
        public const string OwnProp = "LiquidMac.Own";   // properti jendela: traffic light & animasi LiquidMac melewatinya
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool SetProp(IntPtr h, string name, IntPtr data);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr h, string name);
        public static void MarkOwn(IntPtr h) { SetProp(h, OwnProp, new IntPtr(1)); }
        public static bool IsOwn(IntPtr h) { return GetProp(h, OwnProp) != IntPtr.Zero; }

        public static System.Drawing.Bitmap Render(int size)
        {
            const int K = 2;
            int n = size * K;
            System.Drawing.Bitmap big = new System.Drawing.Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(big))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float m = n * 0.06f, w = n - 2 * m;
                System.Drawing.RectangleF tile = new System.Drawing.RectangleF(m, m, w, w);
                using (System.Drawing.Drawing2D.GraphicsPath tp = Glass.Round(tile, w * 0.225f))
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush sky = new System.Drawing.Drawing2D.LinearGradientBrush(tile, System.Drawing.Color.FromArgb(64, 156, 255), System.Drawing.Color.FromArgb(150, 214, 255), 90f)) g.FillPath(sky, tp);
                    System.Drawing.Region old = g.Clip; g.SetClip(tp, System.Drawing.Drawing2D.CombineMode.Intersect);
                    // matahari dengan cahaya lembut
                    float sx = m + w * 0.70f, sy = m + w * 0.30f, sr = w * 0.12f;
                    using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        gp.AddEllipse(sx - sr * 2.2f, sy - sr * 2.2f, sr * 4.4f, sr * 4.4f);
                        using (System.Drawing.Drawing2D.PathGradientBrush pg = new System.Drawing.Drawing2D.PathGradientBrush(gp)) { pg.CenterColor = System.Drawing.Color.FromArgb(150, 255, 236, 150); pg.SurroundColors = new System.Drawing.Color[] { System.Drawing.Color.FromArgb(0, 255, 236, 150) }; g.FillPath(pg, gp); }
                    }
                    using (System.Drawing.SolidBrush sb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 214, 10))) g.FillEllipse(sb, sx - sr, sy - sr, sr * 2, sr * 2);
                    // bukit belakang & depan
                    using (System.Drawing.Drawing2D.GraphicsPath h1 = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        h1.AddBezier(m - 2, m + w * 0.72f, m + w * 0.25f, m + w * 0.42f, m + w * 0.45f, m + w * 0.50f, m + w * 0.62f, m + w * 0.66f);
                        h1.AddBezier(m + w * 0.62f, m + w * 0.66f, m + w * 0.75f, m + w * 0.56f, m + w * 0.90f, m + w * 0.58f, m + w + 2, m + w * 0.66f);
                        h1.AddLine(m + w + 2, m + w + 2, m - 2, m + w + 2); h1.CloseFigure();
                        using (System.Drawing.Drawing2D.LinearGradientBrush b1 = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.RectangleF(m, m + w * 0.45f, w, w * 0.56f), System.Drawing.Color.FromArgb(40, 170, 120), System.Drawing.Color.FromArgb(20, 110, 90), 90f)) g.FillPath(b1, h1);
                    }
                    using (System.Drawing.Drawing2D.GraphicsPath h2 = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        h2.AddBezier(m - 2, m + w * 0.86f, m + w * 0.30f, m + w * 0.66f, m + w * 0.62f, m + w * 0.74f, m + w + 2, m + w * 0.80f);
                        h2.AddLine(m + w + 2, m + w + 2, m - 2, m + w + 2); h2.CloseFigure();
                        using (System.Drawing.Drawing2D.LinearGradientBrush b2 = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.RectangleF(m, m + w * 0.66f, w, w * 0.35f), System.Drawing.Color.FromArgb(120, 220, 110), System.Drawing.Color.FromArgb(52, 168, 83), 90f)) g.FillPath(b2, h2);
                    }
                    g.Clip = old; old.Dispose();
                    // bingkai foto putih tipis + kilau atas
                    using (System.Drawing.Pen fp = new System.Drawing.Pen(System.Drawing.Color.FromArgb(235, 255, 255, 255), w * 0.045f)) using (System.Drawing.Drawing2D.GraphicsPath ip = Glass.Round(new System.Drawing.RectangleF(m + w * 0.1f, m + w * 0.1f, w * 0.8f, w * 0.8f), w * 0.11f)) g.DrawPath(fp, ip);
                    using (System.Drawing.Pen rim = new System.Drawing.Pen(System.Drawing.Color.FromArgb(70, 255, 255, 255), n / 128f)) g.DrawPath(rim, tp);
                }
            }
            System.Drawing.Bitmap o = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(o)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; g.DrawImage(big, 0, 0, size, size); }
            big.Dispose();
            return o;
        }

        public static System.Drawing.Icon MakeIcon()
        {
            using (System.Drawing.Bitmap b = Render(64)) return System.Drawing.Icon.FromHandle(b.GetHicon());
        }
    }
}
