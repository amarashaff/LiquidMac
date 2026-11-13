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
}
