using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
            object com = null, stream = null, item = null; IPreviewHandler h = null; bool shown = false;
            bool word = string.Equals(HandlerFor(_path), WordHandler, StringComparison.OrdinalIgnoreCase);
            Interlocked.Increment(ref _active);
            try
            {
                HashSet<int> before = word ? WordPids() : null;
                Guid clsid = new Guid(HandlerFor(_path)), unk = new Guid("00000000-0000-0000-C000-000000000046");
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 4 /* CLSCTX_LOCAL_SERVER */, ref unk, out com);
                if (hr != 0) throw new COMException("CoCreateInstance", hr);
                if (Quit) return;
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
                if (Quit) return;
                h = (IPreviewHandler)com;
                RECT r = Rect();
                h.SetWindow(_hwnd, ref r);
                h.DoPreview();
                shown = true;
                _ready(true);
                if (word) try { NoteSpawned(before); } catch (Exception ex) { Log.Write("pratinjau word: " + ex.Message); }
                WaitHandle[] waits = { _quit, _resize };
                while (WaitHandle.WaitAny(waits) == 1) { r = Rect(); h.SetRect(ref r); }
            }
            catch (Exception ex)
            {
                Log.Write("pratinjau dokumen: " + Path.GetExtension(_path) + " " + ex.Message);
                if (!shown) try { _ready(false); } catch { }
            }
            finally
            {
                try { if (h != null) h.Unload(); } catch { }
                foreach (object o in new object[] { com, item, stream })
                    if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { }
                Interlocked.Decrement(ref _active);
                ReapSoon();
                Done.Set();
            }
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
}
