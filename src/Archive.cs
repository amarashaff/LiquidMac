using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace LiquidMac
{
    // Isi arsip (zip, rar, tar, tgz) yang ditampilkan Finder sebagai folder baca-saja.
    // zip dan tar/tgz dibaca langsung; rar lewat 7-Zip bila terpasang, kalau tidak lewat tar.exe bawaan Windows.
    internal sealed class ArcEntry
    {
        public string Raw, Inner;
        public long Size;
        public DateTime Modified;
        public bool IsDir;
    }

    internal sealed class ArcListing
    {
        public string File, Engine, Temp;
        public DateTime Stamp;
        public readonly Dictionary<string, ArcEntry> All = new Dictionary<string, ArcEntry>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<ArcEntry>> Kids = new Dictionary<string, List<ArcEntry>>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasDir(string inner)
        {
            if (inner.Length == 0) return true;
            ArcEntry e;
            return All.TryGetValue(inner, out e) && e.IsDir;
        }

        public List<ArcEntry> Children(string inner)
        {
            List<ArcEntry> l;
            return Kids.TryGetValue(inner, out l) ? l : new List<ArcEntry>();
        }

        public void Add(string raw, long size, DateTime modified, bool isDir)
        {
            string inner = ArchiveFs.Clean(raw);
            if (inner == null) return;
            EnsureParents(inner);
            ArcEntry e;
            if (All.TryGetValue(inner, out e)) { e.Raw = raw; e.Size = size; e.Modified = modified; return; }
            Put(inner, raw, size, modified, isDir);
        }

        private void EnsureParents(string inner)
        {
            int i = inner.LastIndexOf('\\');
            if (i <= 0) return;
            string dir = inner.Substring(0, i);
            if (All.ContainsKey(dir)) return;
            EnsureParents(dir);
            Put(dir, dir.Replace('\\', '/'), 0, Stamp, true);
        }

        private void Put(string inner, string raw, long size, DateTime modified, bool isDir)
        {
            ArcEntry e = new ArcEntry();
            e.Inner = inner; e.Raw = raw; e.Size = size; e.Modified = modified; e.IsDir = isDir;
            All[inner] = e;
            int i = inner.LastIndexOf('\\');
            string parent = i > 0 ? inner.Substring(0, i) : "";
            List<ArcEntry> l;
            if (!Kids.TryGetValue(parent, out l)) { l = new List<ArcEntry>(); Kids[parent] = l; }
            l.Add(e);
            if (isDir && !Kids.ContainsKey(inner)) Kids[inner] = new List<ArcEntry>();
        }
    }

    internal static class ArchiveFs
    {
        private static readonly string[] Exts = { ".zip", ".rar", ".tar", ".tgz", ".tar.gz" };
        private static readonly Dictionary<string, ArcListing> Cache = new Dictionary<string, ArcListing>(StringComparer.OrdinalIgnoreCase);
        private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "LiquidMac-arc");
        private static bool _swept;

        public static bool IsArchive(string path)
        {
            foreach (string e in Exts) if (path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Memisahkan "C:\a\b.zip\sub\c.txt" menjadi berkas arsip dan jalur di dalamnya.
        public static bool Locate(string path, out string file, out string inner)
        {
            file = null; inner = "";
            string cur = path.TrimEnd('\\');
            while (!string.IsNullOrEmpty(cur))
            {
                if (System.IO.File.Exists(cur))
                {
                    if (!IsArchive(cur)) return false;
                    file = cur; inner = path.Substring(cur.Length).Trim('\\');
                    return true;
                }
                if (Directory.Exists(cur)) return false;
                string up = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(up) || up == cur) return false;
                cur = up;
            }
            return false;
        }

        // Nama aman untuk Windows; null bila entri tidak boleh dipakai (mis. berisi "..").
        public static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            StringBuilder sb = new StringBuilder();
            foreach (string part0 in raw.Replace('\\', '/').Split('/'))
            {
                string part = part0;
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") return null;
                foreach (char c in Path.GetInvalidFileNameChars()) part = part.Replace(c, '_');
                part = part.TrimEnd(' ', '.');
                if (part.Length == 0) part = "_";
                if (Regex.IsMatch(part, @"^(con|prn|aux|nul|com\d|lpt\d)(\.|$)", RegexOptions.IgnoreCase)) part = "_" + part;
                if (sb.Length > 0) sb.Append('\\');
                sb.Append(part);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        public static ArcListing Open(string file)
        {
            DateTime stamp = System.IO.File.GetLastWriteTimeUtc(file);
            lock (Cache)
            {
                ArcListing c;
                if (Cache.TryGetValue(file, out c) && c.Stamp == stamp) return c;
            }
            Sweep();
            ArcListing a = new ArcListing();
            a.File = file; a.Stamp = stamp;
            a.Temp = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            a.Engine = EngineFor(file);
            if (a.Engine == "zip") ListZip(a);
            else if (a.Engine == "tar") TarWalk(file, delegate (string n, long s, DateTime m, bool d, Stream data) { a.Add(n, s, m, d); });
            else if (a.Engine == "7z") List7z(a);
            else ListBsdTar(a);
            if (a.All.Count == 0) throw new InvalidDataException("empty or unreadable archive");
            lock (Cache) { if (Cache.Count >= 8) Cache.Clear(); Cache[file] = a; }
            return a;
        }

        private static string EngineFor(string file)
        {
            string f = file.ToLowerInvariant();
            if (f.EndsWith(".zip")) return "zip";
            if (!f.EndsWith(".rar")) return "tar";
            return SevenZip() != null ? "7z" : "bsdtar";
        }

        private static string SevenZip()
        {
            if (Environment.GetEnvironmentVariable("LM_NO7Z") != null) return null;
            foreach (string root in new string[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                if (string.IsNullOrEmpty(root)) continue;
                string p = Path.Combine(root, "7-Zip", "7z.exe");
                if (System.IO.File.Exists(p)) return p;
            }
            return null;
        }

        private static string BsdTar() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"); }

        // ---------- zip ----------
        private static void ListZip(ArcListing a)
        {
            using (FileStream fs = new FileStream(a.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    bool dir = e.FullName.EndsWith("/") || e.FullName.EndsWith("\\");
                    a.Add(e.FullName, dir ? 0 : e.Length, e.LastWriteTime.LocalDateTime, dir);
                }
        }

        private static void ExtractZip(ArcListing a, Func<string, bool> want, string dest)
        {
            using (FileStream fs = new FileStream(a.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    string inner = Clean(e.FullName);
                    if (inner == null || !want(inner)) continue;
                    string target = Path.Combine(dest, inner);
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream src = e.Open()) using (FileStream o = System.IO.File.Create(target)) src.CopyTo(o);
                    try { System.IO.File.SetLastWriteTime(target, e.LastWriteTime.LocalDateTime); } catch { }
                }
        }

        // ---------- tar / tgz ----------
        private delegate void TarVisit(string name, long size, DateTime modified, bool isDir, Stream data);

        private static void TarWalk(string file, TarVisit visit)
        {
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                string f = file.ToLowerInvariant();
                Stream s = f.EndsWith(".tar") ? (Stream)fs : new GZipStream(fs, CompressionMode.Decompress);
                try { TarRead(s, visit); }
                finally { if (s != fs) s.Dispose(); }
            }
        }

        private static void TarRead(Stream s, TarVisit visit)
        {
            byte[] h = new byte[512];
            string longName = null, paxName = null;
            while (ReadFull(s, h, 512) == 512)
            {
                bool zero = true;
                for (int i = 0; i < 512; i++) if (h[i] != 0) { zero = false; break; }
                if (zero) break;
                long size = TarNum(h, 124, 12);
                long mtime = TarNum(h, 136, 12);
                char type = (char)h[156];
                string name = TarStr(h, 0, 100);
                if (TarStr(h, 257, 5) == "ustar") { string pre = TarStr(h, 345, 155); if (pre.Length > 0 && h[263] == (byte)'0') name = pre + "/" + name; }
                long padded = (size + 511) / 512 * 512;
                if (type == 'L' || type == 'x' || type == 'g' || type == 'K')
                {
                    byte[] b = new byte[size];
                    if (ReadFull(s, b, (int)size) != size) break;
                    Skip(s, padded - size);
                    if (type == 'L') longName = Encoding.UTF8.GetString(b).TrimEnd('\0');
                    else if (type == 'x') paxName = PaxPath(b);
                    continue;
                }
                if (longName != null) name = longName;
                if (paxName != null) name = paxName;
                longName = paxName = null;
                DateTime when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(mtime).ToLocalTime();
                bool dir = type == '5' || name.EndsWith("/");
                bool file = !dir && (type == '0' || type == '\0' || type == '7');
                if (dir || file)
                {
                    using (Bounded body = new Bounded(s, file ? size : 0))
                    {
                        visit(name, file ? size : 0, when, dir, body);
                        body.Drain();
                    }
                    if (file) Skip(s, padded - size);
                }
                else Skip(s, padded);
            }
        }

        private static string PaxPath(byte[] b)
        {
            int i = 0;
            while (i < b.Length)
            {
                int sp = Array.IndexOf(b, (byte)' ', i);
                if (sp < 0) break;
                int len;
                if (!int.TryParse(Encoding.ASCII.GetString(b, i, sp - i), out len) || len <= 0) break;
                string kv = Encoding.UTF8.GetString(b, sp + 1, i + len - sp - 2);
                if (kv.StartsWith("path=")) return kv.Substring(5);
                i += len;
            }
            return null;
        }

        private static string TarStr(byte[] h, int off, int len)
        {
            int n = 0;
            while (n < len && h[off + n] != 0) n++;
            return Encoding.UTF8.GetString(h, off, n);
        }

        private static long TarNum(byte[] h, int off, int len)
        {
            if ((h[off] & 0x80) != 0)
            {
                long v = 0;
                for (int i = 1; i < len; i++) v = (v << 8) | h[off + i];
                return v;
            }
            long r = 0;
            for (int i = 0; i < len; i++)
            {
                byte c = h[off + i];
                if (c == 0) break;
                if (c >= (byte)'0' && c <= (byte)'7') r = r * 8 + (c - (byte)'0');
            }
            return r;
        }

        private static int ReadFull(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n) { int k = s.Read(b, got, n - got); if (k <= 0) break; got += k; }
            return got;
        }

        private static void Skip(Stream s, long n)
        {
            byte[] b = new byte[8192];
            while (n > 0) { int k = s.Read(b, 0, (int)Math.Min(n, b.Length)); if (k <= 0) break; n -= k; }
        }

        // Membatasi pembacaan pada isi satu entri tar.
        private sealed class Bounded : Stream
        {
            private readonly Stream _s; private long _left;
            public Bounded(Stream s, long n) { _s = s; _left = n; }
            public void Drain() { Skip(_s, _left); _left = 0; }
            public override int Read(byte[] b, int o, int c) { if (_left <= 0) return 0; int k = _s.Read(b, o, (int)Math.Min(c, _left)); _left -= k; return k; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin w) { throw new NotSupportedException(); }
            public override void SetLength(long v) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
        }

        private static void ExtractTar(ArcListing a, Func<string, bool> want, string dest)
        {
            TarWalk(a.File, delegate (string n, long s, DateTime m, bool d, Stream data)
            {
                string inner = Clean(n);
                if (inner == null || !want(inner)) return;
                string target = Path.Combine(dest, inner);
                if (d) { Directory.CreateDirectory(target); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (FileStream o = System.IO.File.Create(target)) data.CopyTo(o);
                try { System.IO.File.SetLastWriteTime(target, m); } catch { }
            });
        }

        // ---------- rar ----------
        private static int RunTool(string exe, string args, Encoding enc, out string stdout)
        {
            ProcessStartInfo si = new ProcessStartInfo(exe, args);
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true; si.RedirectStandardInput = true;
            si.StandardOutputEncoding = enc;
            using (Process p = Process.Start(si))
            {
                p.StandardInput.Close();
                p.ErrorDataReceived += delegate { };
                p.BeginErrorReadLine();
                stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        private static void List7z(ArcListing a)
        {
            string o;
            int code = RunTool(SevenZip(), "l -slt -sccUTF-8 -ba -p- \"" + a.File + "\"", Encoding.UTF8, out o);
            if (code > 1) throw new InvalidDataException("7-Zip exit " + code);
            string path = null, folder = null, attrs = null; long size = 0; DateTime when = a.Stamp.ToLocalTime();
            foreach (string line0 in (o + "\n\n").Split('\n'))
            {
                string line = line0.TrimEnd('\r');
                if (line.Length == 0)
                {
                    if (path != null) { bool dir = folder == "+" || (attrs != null && attrs.Contains("D")); a.Add(path, dir ? 0 : size, when, dir); }
                    path = null; folder = null; attrs = null; size = 0; when = a.Stamp.ToLocalTime();
                    continue;
                }
                int eq = line.IndexOf(" = ");
                string key = eq > 0 ? line.Substring(0, eq) : line.TrimEnd(' ', '=');
                string val = eq > 0 ? line.Substring(eq + 3) : "";
                if (key == "Path") path = val;
                else if (key == "Folder") folder = val;
                else if (key == "Attributes") attrs = val;
                else if (key == "Size") long.TryParse(val, out size);
                else if (key == "Modified" && val.Length >= 19) DateTime.TryParse(val.Substring(0, 19), CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
            }
        }

        private static void Extract7z(ArcListing a, List<string> inners, string dest)
        {
            string args = "x -y -spd -p- -scsUTF-8 \"-o" + dest + "\" \"" + a.File + "\"";
            string list = null;
            if (inners != null)
            {
                Directory.CreateDirectory(dest);
                list = Path.Combine(dest, ".lm-list-" + Guid.NewGuid().ToString("N") + ".txt");
                List<string> raws = new List<string>();
                foreach (string i in inners) raws.Add(a.All[i].Raw.TrimEnd('/', '\\').Replace('/', '\\'));
                System.IO.File.WriteAllLines(list, raws.ToArray(), new UTF8Encoding(false));
                args += " \"@" + list + "\"";
            }
            try
            {
                string o;
                int code = RunTool(SevenZip(), args, Encoding.UTF8, out o);
                if (code > 1) throw new InvalidDataException("7-Zip exit " + code);
            }
            finally { if (list != null) try { System.IO.File.Delete(list); } catch { } }
        }

        // Cadangan tanpa 7-Zip: nama berkas di luar kode halaman sistem dilewati oleh tar.exe.
        private static void ListBsdTar(ArcListing a)
        {
            string o;
            Encoding oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            RunTool(BsdTar(), "-tvf \"" + a.File + "\"", oem, out o);
            Regex rx = new Regex(@"^(\S)\S+\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\w{3})\s+(\d+)\s+(\d+:\d+|\d{4})\s(.+)$");
            foreach (string line0 in o.Split('\n'))
            {
                Match m = rx.Match(line0.TrimEnd('\r'));
                if (!m.Success) continue;
                DateTime when;
                string stamp = m.Groups[3].Value + " " + m.Groups[4].Value + " " + (m.Groups[5].Value.Contains(":") ? DateTime.Now.Year + " " + m.Groups[5].Value : m.Groups[5].Value + " 00:00");
                if (!DateTime.TryParseExact(stamp, "MMM d yyyy H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) when = a.Stamp.ToLocalTime();
                bool dir = m.Groups[1].Value == "d";
                long size; long.TryParse(m.Groups[2].Value, out size);
                a.Add(m.Groups[6].Value, dir ? 0 : size, when, dir);
            }
        }

        private static void ExtractBsdTar(ArcListing a, string dest)
        {
            Directory.CreateDirectory(dest);
            string o;
            RunTool(BsdTar(), "-xf \"" + a.File + "\" -C \"" + dest + "\"", Encoding.Default, out o);
        }

        // ---------- ekstrak ----------
        // inners null = seluruh arsip. Isi ditulis ke dest dengan jalur aslinya.
        public static void Extract(ArcListing a, List<string> inners, string dest)
        {
            Func<string, bool> want = delegate (string inner)
            {
                if (inners == null) return true;
                foreach (string i in inners)
                    if (string.Equals(inner, i, StringComparison.OrdinalIgnoreCase) || inner.StartsWith(i + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            };
            if (a.Engine == "zip") ExtractZip(a, want, dest);
            else if (a.Engine == "tar") ExtractTar(a, want, dest);
            else if (a.Engine == "7z") Extract7z(a, inners, dest);
            else ExtractBsdTar(a, dest);
        }

        // Mengekstrak ke folder sementara dan mengembalikan jalur nyatanya.
        public static List<string> ExtractToTemp(ArcListing a, List<string> inners)
        {
            List<string> todo = new List<string>();
            foreach (string i in inners) if (!a.Done.Contains(i)) todo.Add(i);
            if (todo.Count > 0) { Extract(a, todo, a.Temp); foreach (string i in todo) a.Done.Add(i); }
            List<string> r = new List<string>();
            foreach (string i in inners)
            {
                string p = Path.Combine(a.Temp, i);
                if (System.IO.File.Exists(p) || Directory.Exists(p)) r.Add(p);
            }
            return r;
        }

        // Folder tujuan di samping arsip, nama tidak bentrok.
        public static string UniqueDest(string file)
        {
            string name = Path.GetFileName(file);
            foreach (string e in new string[] { ".tar.gz", ".tgz", ".tar", ".zip", ".rar" })
                if (name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) { name = name.Substring(0, name.Length - e.Length); break; }
            string dir = Path.GetDirectoryName(file), p = Path.Combine(dir, name);
            int n = 2;
            while (Directory.Exists(p) || System.IO.File.Exists(p)) p = Path.Combine(dir, name + " " + n++);
            return p;
        }

        // Berkas kosong berekstensi sama, hanya agar shell memberi ikon jenis berkas.
        public static string IconProxy(string path)
        {
            string ext = Path.GetExtension(path);
            string dir = Path.Combine(TempRoot, "icons");
            string p = Path.Combine(dir, "x" + ext);
            if (!System.IO.File.Exists(p)) { Directory.CreateDirectory(dir); using (System.IO.File.Create(p)) { } }
            return p;
        }

        public static string Summary(string file)
        {
            ArcListing a = Open(file);
            List<ArcEntry> all = new List<ArcEntry>(a.All.Values);
            all.Sort(delegate (ArcEntry x, ArcEntry y) { return string.Compare(x.Inner, y.Inner, StringComparison.OrdinalIgnoreCase); });
            StringBuilder sb = new StringBuilder();
            sb.Append(all.Count).Append(L.T(" item")).Append("\r\n\r\n");
            int shown = 0;
            foreach (ArcEntry e in all)
            {
                if (shown++ >= 300) { sb.Append("...\r\n"); break; }
                sb.Append(e.Inner);
                if (e.IsDir) sb.Append('\\');
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        // Membersihkan sisa ekstrak sementara yang sudah lebih dari 2 hari.
        private static void Sweep()
        {
            if (_swept) return;
            _swept = true;
            try
            {
                if (!Directory.Exists(TempRoot)) return;
                foreach (string d in Directory.GetDirectories(TempRoot))
                {
                    if (string.Equals(Path.GetFileName(d), "icons", StringComparison.OrdinalIgnoreCase)) continue;
                    try { if ((DateTime.Now - Directory.GetLastWriteTime(d)).TotalDays > 2) Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
        }
    }
}
