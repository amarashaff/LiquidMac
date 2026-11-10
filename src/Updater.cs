// Pembaruan otomatis dari GitHub Releases (amarashaff/LiquidMac).
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    internal static class Updater
    {
        private const string Api = "https://api.github.com/repos/amarashaff/LiquidMac/releases/latest";
        public static string LatestTag, LatestUrl, Status = "";
        private static bool _busy;

        private static Version Parse(string tag)
        {
            try { return new Version(tag.Trim().TrimStart('v', 'V')); } catch { return new Version(0, 0); }
        }

        public static bool Newer { get { return LatestTag != null && Parse(LatestTag) > Parse(Program.Version); } }

        // Cek di latar belakang; done dipanggil di thread UI lewat invoker.
        public static void Check(Control invoker, Action done)
        {
            if (_busy) return;
            _busy = true;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "LiquidMac/" + Program.Version;
                        wc.Encoding = Encoding.UTF8;
                        string json = wc.DownloadString(Api);
                        Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                        Match url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+/LiquidMac\\.exe)\"");
                        if (tag.Success) LatestTag = tag.Groups[1].Value;
                        if (url.Success) LatestUrl = url.Groups[1].Value;
                        Status = Newer ? L.T("Versi baru ") + LatestTag + L.T(" tersedia.") : L.T("Sudah versi terbaru (") + Program.Version + ").";
                    }
                }
                catch (Exception ex) { Status = "Gagal memeriksa pembaruan."; Log.Write("update check: " + ex.Message); }
                finally { _busy = false; }
                try { if (done != null && invoker != null && invoker.IsHandleCreated) invoker.BeginInvoke(done); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Unduh exe baru, lalu skrip kecil menukar berkas setelah LiquidMac keluar dan menjalankannya lagi.
        public static void Apply(Control invoker, Action exitApp)
        {
            if (!Newer || LatestUrl == null || _busy) return;
            _busy = true;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    string exe = Application.ExecutablePath;
                    string tmp = exe + ".update";
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "LiquidMac/" + Program.Version;
                        wc.DownloadFile(LatestUrl, tmp);
                    }
                    if (new FileInfo(tmp).Length < 50000) throw new Exception("berkas unduhan terlalu kecil");
                    string ps = Path.Combine(Path.GetTempPath(), "liquidmac-update.ps1");
                    File.WriteAllText(ps,
                        "$p = " + Process.GetCurrentProcess().Id + "\r\n" +
                        "while (Get-Process -Id $p -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }\r\n" +
                        "Move-Item -Force -LiteralPath '" + tmp.Replace("'", "''") + "' -Destination '" + exe.Replace("'", "''") + "'\r\n" +
                        "Unblock-File -LiteralPath '" + exe.Replace("'", "''") + "' -ErrorAction SilentlyContinue\r\n" +
                        "Start-Process -FilePath '" + exe.Replace("'", "''") + "' -ArgumentList '--updated'\r\n");
                    ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps + "\"");
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    Process.Start(psi);
                    Log.Write("update: " + Program.Version + " -> " + LatestTag);
                    if (invoker != null && invoker.IsHandleCreated) invoker.BeginInvoke(exitApp);
                }
                catch (Exception ex) { Status = "Gagal memperbarui: " + ex.Message; Log.Write("update apply: " + ex.Message); _busy = false; }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
