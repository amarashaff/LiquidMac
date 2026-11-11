using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace LiquidMac
{
    // Pratinjau isi dokumen memakai preview handler Windows (mesin panel pratinjau Explorer).
    // Handler selalu berjalan di proses lain dan dipanggil dari thread sendiri,
    // jadi dokumen berat atau handler yang macet tidak menahan Finder, Dock, atau notch.
    internal sealed class DocPreviewHost
    {
        public static bool Enabled { get { return Settings.Get("FinderDocPreview", false); } }

        private static readonly string[] DocExt = { ".doc", ".docx", ".docm", ".rtf", ".odt", ".xls", ".xlsx", ".xlsm", ".xlsb", ".ods", ".ppt", ".pptx", ".pptm", ".odp", ".pdf", ".vsd", ".vsdx" };
        private static readonly Dictionary<string, string> Handlers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool IsDoc(string path) { return HandlerFor(path) != null; }

        private static string HandlerFor(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) || Array.IndexOf(DocExt, ext.ToLowerInvariant()) < 0) return null;
            string v;
            lock (Handlers) if (Handlers.TryGetValue(ext, out v)) return v;
            uint n = 64; StringBuilder sb = new StringBuilder(64);
            int hr = AssocQueryString(0, 16 /* ASSOCSTR_SHELLEXTENSION */, ext, "{8895b1c6-b41f-4c1c-a562-0d564250836f}", sb, ref n);
            v = hr == 0 && sb.Length > 0 ? sb.ToString() : null;
            lock (Handlers) Handlers[ext] = v;
            return v;
        }

        // Word membiarkan proses pratinjaunya hidup (~250 MB) setelah dokumen ditutup. Proses yang dibuka oleh
        // pratinjau ini saja, tanpa jendela, dan bertanda -Embedding, diakhiri saat pratinjau sudah menganggur.
        private const string WordHandler = "{84F66100-FF7C-4fb4-B0C0-02CD7FB668FE}";
        private static readonly HashSet<int> Spawned = new HashSet<int>();
        private static int _active;
        private static Timer _reap;

        private static HashSet<int> WordPids()
        {
            HashSet<int> r = new HashSet<int>();
            foreach (Process p in Process.GetProcessesByName("WINWORD")) { r.Add(p.Id); p.Dispose(); }
            return r;
        }

        private static void NoteSpawned(HashSet<int> before)
        {
            using (ManagementObjectSearcher q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='WINWORD.EXE'"))
            using (ManagementObjectCollection all = q.Get())
                foreach (ManagementBaseObject o in all)
                    using (o)
                    {
                        int pid = (int)(uint)o["ProcessId"];
                        string cmd = o["CommandLine"] as string;
                        if (before.Contains(pid) || cmd == null || cmd.IndexOf("-Embedding", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        lock (Spawned) Spawned.Add(pid);
                    }
        }

        private static void ReapSoon()
        {
            lock (Spawned)
            {
                if (Spawned.Count == 0) return;
                if (_reap == null) _reap = new Timer(delegate { Reap(); });
                _reap.Change(30000, Timeout.Infinite);
            }
        }

        private static void Reap()
        {
            lock (Spawned)
            {
                if (Thread.VolatileRead(ref _active) > 0) return;
                foreach (int pid in Spawned)
                {
                    try
                    {
                        using (Process p = Process.GetProcessById(pid))
                            if (string.Equals(p.ProcessName, "WINWORD", StringComparison.OrdinalIgnoreCase) && p.MainWindowHandle == IntPtr.Zero) p.Kill();
                    }
                    catch { }
                }
                Spawned.Clear();
            }
        }

        private readonly IntPtr _hwnd;
        private readonly string _path;
        private readonly Action<bool> _ready;
        private readonly ManualResetEvent _quit = new ManualResetEvent(false);
        private readonly AutoResetEvent _resize = new AutoResetEvent(false);
        private readonly object _lock = new object();
        private int _w, _h;
        public readonly ManualResetEvent Done = new ManualResetEvent(false);

        // ready dipanggil dari thread pratinjau: true bila dokumen sudah tampil.
        public DocPreviewHost(IntPtr parent, string path, int w, int h, Action<bool> ready)
        {
            _hwnd = parent; _path = path; _w = w; _h = h; _ready = ready;
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public void Resize(int w, int h) { lock (_lock) { _w = w; _h = h; } _resize.Set(); }
        public void Stop() { _quit.Set(); }

        private RECT Rect() { lock (_lock) { RECT r = new RECT(); r.Right = _w; r.Bottom = _h; return r; } }
        private bool Quit { get { return _quit.WaitOne(0); } }

        private void Run()
        {
            bool word = string.Equals(HandlerFor(_path), WordHandler, StringComparison.OrdinalIgnoreCase);
            HashSet<int> before = null;
            Interlocked.Increment(ref _active);
            try
            {
                try { if (word) before = WordPids(); } catch { }
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!Attempt(attempt == 0)) break;
                    Thread.Sleep(700);
                    if (Quit) break;
                }
            }
            finally
            {
                if (before != null) try { NoteSpawned(before); } catch (Exception ex) { Log.Write("pratinjau word: " + ex.Message); }
                Interlocked.Decrement(ref _active);
                ReapSoon();
                Done.Set();
            }
        }

        // Server COM yang baru saja menutup bisa menolak sambungan; kegagalan seperti ini layak dicoba sekali lagi.
        private static bool Transient(Exception ex)
        {
            COMException c = ex as COMException;
            if (c == null) return false;
            uint code = (uint)c.ErrorCode;
            return code == 0x800706BA || code == 0x800706BE || code == 0x800706BF || code == 0x80080005 || code == 0x80010108 || code == 0x80010100;
        }

        // true = perlu dicoba lagi
        private bool Attempt(bool canRetry)
        {
            object com = null, stream = null, item = null; IPreviewHandler h = null; bool shown = false, retry = false; string stage = "create";
            try
            {
                Guid clsid = new Guid(HandlerFor(_path)), unk = new Guid("00000000-0000-0000-C000-000000000046");
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 4 /* CLSCTX_LOCAL_SERVER */, ref unk, out com);
                if (hr != 0) throw new COMException("CoCreateInstance", hr);
                if (Quit) return false;
                stage = "init";
                if (com is IInitializeWithFile) ((IInitializeWithFile)com).Initialize(_path, 0);
                else if (com is IInitializeWithItem)
                {
                    Guid iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                    hr = SHCreateItemFromParsingName(_path, IntPtr.Zero, ref iid, out item);
                    if (hr != 0) throw new COMException("SHCreateItem", hr);
                    ((IInitializeWithItem)com).Initialize(item, 0);
                }
                else if (com is IInitializeWithStream)
                {
                    hr = SHCreateStreamOnFileEx(_path, 0x40 /* STGM_READ | SHARE_DENY_NONE */, 0, false, IntPtr.Zero, out stream);
                    if (hr != 0) throw new COMException("SHCreateStream", hr);
                    ((IInitializeWithStream)com).Initialize(stream, 0);
                }
                else throw new InvalidCastException("no initializer");
                if (Quit) return false;
                h = (IPreviewHandler)com;
                RECT r = Rect();
                stage = "setwindow";
                h.SetWindow(_hwnd, ref r);
                stage = "dopreview";
                h.DoPreview();
                shown = true;
                _ready(true);
                WaitHandle[] waits = { _quit, _resize };
                while (WaitHandle.WaitAny(waits) == 1) { r = Rect(); h.SetRect(ref r); }
            }
            catch (Exception ex)
            {
                Log.Write("pratinjau dokumen: " + Path.GetExtension(_path) + " [" + stage + "] " + ex.Message);
                if (!shown && canRetry && Transient(ex)) retry = true;
                else if (!shown) try { _ready(false); } catch { }
            }
            finally
            {
                try { if (h != null) h.Unload(); } catch { }
                foreach (object o in new object[] { com, item, stream })
                    if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { }
            }
            return retry;
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref RECT rect);
            void SetRect(ref RECT rect);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr phwnd);
            [PreserveSig] int TranslateAccelerator(ref MSG msg);
        }

        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }

        [ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithItem { void Initialize([MarshalAs(UnmanagedType.IUnknown)] object item, uint mode); }

        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithStream { void Initialize([MarshalAs(UnmanagedType.IUnknown)] object stream, uint mode); }

        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr ctx, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateStreamOnFileEx(string path, uint mode, uint attrs, bool create, IntPtr template, [MarshalAs(UnmanagedType.IUnknown)] out object stream);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, int str, string assoc, string extra, StringBuilder outBuf, ref uint outLen);
    }
    // Teks dokumen Office dibaca langsung dari XML-nya, tanpa menjalankan Office atau makro.
    // Dipakai saat handler menolak berkas, misalnya berkas bertanda "dari internet".
    internal static class DocText
    {
        private const int MaxChars = 20000, MaxRows = 200, MaxSheets = 3, MaxSlides = 60;

        public static string Extract(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool doc = ext == ".docx" || ext == ".docm", xls = ext == ".xlsx" || ext == ".xlsm", ppt = ext == ".pptx" || ext == ".pptm";
            if (!doc && !xls && !ppt) return null;
            StringBuilder sb = new StringBuilder();
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                if (doc) { ZipArchiveEntry e = z.GetEntry("word/document.xml"); if (e != null) Paragraphs(e, sb); }
                else if (ppt)
                {
                    int n = 0;
                    foreach (ZipArchiveEntry e in Numbered(z, "ppt/slides/slide"))
                    {
                        if (++n > MaxSlides || sb.Length > MaxChars) break;
                        sb.Append("Slide ").Append(n).Append("\r\n");
                        Paragraphs(e, sb);
                        sb.Append("\r\n");
                    }
                }
                else Sheets(z, sb);
            }
            string t = sb.ToString().Trim();
            if (t.Length > MaxChars) t = t.Substring(0, MaxChars) + "...";
            return t.Length == 0 ? null : t;
        }

        private static XmlReader Open(Stream s)
        {
            XmlReaderSettings o = new XmlReaderSettings();
            o.DtdProcessing = DtdProcessing.Prohibit; o.XmlResolver = null; o.MaxCharactersInDocument = 64000000;
            return XmlReader.Create(s, o);
        }

        private static List<ZipArchiveEntry> Numbered(ZipArchive z, string prefix)
        {
            List<KeyValuePair<int, ZipArchiveEntry>> l = new List<KeyValuePair<int, ZipArchiveEntry>>();
            foreach (ZipArchiveEntry e in z.Entries)
            {
                if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                int n;
                if (int.TryParse(e.FullName.Substring(prefix.Length, e.FullName.Length - prefix.Length - 4), out n)) l.Add(new KeyValuePair<int, ZipArchiveEntry>(n, e));
            }
            l.Sort(delegate (KeyValuePair<int, ZipArchiveEntry> a, KeyValuePair<int, ZipArchiveEntry> b) { return a.Key.CompareTo(b.Key); });
            List<ZipArchiveEntry> r = new List<ZipArchiveEntry>();
            foreach (KeyValuePair<int, ZipArchiveEntry> kv in l) r.Add(kv.Value);
            return r;
        }

        private static void Paragraphs(ZipArchiveEntry e, StringBuilder sb)
        {
            using (Stream s = e.Open())
            using (XmlReader r = Open(s))
                while (!r.EOF && sb.Length <= MaxChars)
                {
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "t") { sb.Append(r.ReadElementContentAsString()); continue; }
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "tab") sb.Append('\t');
                    else if (r.NodeType == XmlNodeType.Element && r.LocalName == "br") sb.Append("\r\n");
                    else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "p") sb.Append("\r\n");
                    else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "tc") sb.Append('\t');
                    r.Read();
                }
        }

        private static void Sheets(ZipArchive z, StringBuilder sb)
        {
            List<string> shared = new List<string>();
            ZipArchiveEntry ss = z.GetEntry("xl/sharedStrings.xml");
            if (ss != null)
                using (Stream s = ss.Open())
                using (XmlReader r = Open(s))
                {
                    StringBuilder cur = null;
                    while (!r.EOF)
                    {
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "si") { cur = new StringBuilder(); if (r.IsEmptyElement) { shared.Add(""); cur = null; } }
                        else if (r.NodeType == XmlNodeType.Element && r.LocalName == "t" && cur != null) { cur.Append(r.ReadElementContentAsString()); continue; }
                        else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "si" && cur != null) { shared.Add(cur.ToString()); cur = null; }
                        r.Read();
                    }
                }
            int sheets = 0;
            foreach (ZipArchiveEntry e in Numbered(z, "xl/worksheets/sheet"))
            {
                if (++sheets > MaxSheets || sb.Length > MaxChars) break;
                if (sheets > 1) sb.Append("\r\n");
                using (Stream s = e.Open())
                using (XmlReader r = Open(s))
                {
                    int rows = 0; string type = null; bool first = true;
                    while (!r.EOF && rows <= MaxRows && sb.Length <= MaxChars)
                    {
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "row") { rows++; first = true; }
                        else if (r.NodeType == XmlNodeType.Element && r.LocalName == "c") { type = r.GetAttribute("t"); if (!first) sb.Append('\t'); first = false; }
                        else if (r.NodeType == XmlNodeType.Element && (r.LocalName == "v" || r.LocalName == "t"))
                        {
                            string v = r.ReadElementContentAsString(); int i;
                            if (type == "s" && int.TryParse(v, out i) && i >= 0 && i < shared.Count) v = shared[i];
                            sb.Append(v);
                            continue;
                        }
                        else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row") sb.Append("\r\n");
                        r.Read();
                    }
                }
            }
        }
    }
}
