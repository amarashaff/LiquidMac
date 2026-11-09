using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiquidMac
{
    // ============================================================
    //  MEDIA FOUNDATION SINK WRITER (MP4: H.264 + AAC) lewat panggilan vtable mentah
    // ============================================================
    internal sealed class MfWriter : IDisposable
    {
        [DllImport("mfplat.dll")] private static extern int MFStartup(int ver, int flags);
        [DllImport("mfplat.dll")] private static extern int MFShutdown();
        [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr a, int n);
        [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr t);
        [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IntPtr s);
        [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(int max, out IntPtr b);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSinkWriterFromURL(string url, IntPtr bs, IntPtr attr, out IntPtr w);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void Copy(IntPtr dst, IntPtr src, IntPtr len);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnV(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnGU32(IntPtr self, ref Guid k, int v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnGU64(IntPtr self, ref Guid k, long v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnGG(IntPtr self, ref Guid k, ref Guid v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnAdd(IntPtr self, IntPtr type, out int idx);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnSetIn(IntPtr self, int idx, IntPtr type, IntPtr enc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnIdxPtr(IntPtr self, int idx, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnPtr(IntPtr self, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnLong(IntPtr self, long v);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnLock(IntPtr self, out IntPtr data, out int max, out int cur);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FnInt(IntPtr self, int v);

        private static readonly Dictionary<long, Delegate> _cache = new Dictionary<long, Delegate>();
        private static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr f = Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);
            long key = f.ToInt64();
            Delegate d;
            lock (_cache)
                if (!_cache.TryGetValue(key, out d)) { d = Marshal.GetDelegateForFunctionPointer(f, typeof(T)); _cache[key] = d; }
            return d as T;
        }
        private static void Release(IntPtr p) { if (p != IntPtr.Zero) Fn<FnV>(p, 2)(p); }
        private static void Check(int hr, string what) { if (hr < 0) throw new Exception(what + " gagal (0x" + hr.ToString("X8") + ")"); }

        // slot vtable IMFAttributes (setelah IUnknown)
        private static void U32(IntPtr a, Guid k, int v) { Check(Fn<FnGU32>(a, 21)(a, ref k, v), "SetUINT32"); }
        private static void U64(IntPtr a, Guid k, long v) { Check(Fn<FnGU64>(a, 22)(a, ref k, v), "SetUINT64"); }
        private static void Gd(IntPtr a, Guid k, Guid v) { Check(Fn<FnGG>(a, 24)(a, ref k, ref v), "SetGUID"); }

        private static Guid G(string s) { return new Guid(s); }
        private static readonly Guid MT_MAJOR = G("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), MT_SUB = G("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid VIDEO = G("73646976-0000-0010-8000-00AA00389B71"), AUDIO = G("73647561-0000-0010-8000-00AA00389B71");
        private static readonly Guid H264 = G("34363248-0000-0010-8000-00AA00389B71"), RGB32 = G("00000016-0000-0010-8000-00AA00389B71");
        private static readonly Guid PCM = G("00000001-0000-0010-8000-00AA00389B71"), AAC = G("00001610-0000-0010-8000-00AA00389B71");
        private static readonly Guid BITRATE = G("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), INTERLACE = G("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        private static readonly Guid FSIZE = G("1652c33d-d6b2-4012-b834-72030849a37d"), FRATE = G("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static readonly Guid PAR = G("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), STRIDE = G("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static readonly Guid A_CH = G("37e48bf5-645e-4c5b-89de-ada9e29b696a"), A_SPS = G("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static readonly Guid A_BPS = G("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), A_AVG = G("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        private static readonly Guid A_ALIGN = G("322de230-9eeb-43bd-ab7a-ff412251541d"), AAC_PAYLOAD = G("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
        private static readonly Guid HW = G("a634a91c-822b-41b9-a494-4de4643612b0");

        private IntPtr _w;
        private int _vIdx = -1, _aIdx = -1, _vw, _vh, _fps;
        private readonly object _lock = new object();
        private bool _started, _closed;

        private MfWriter() { }

        // width/height genap; audioRate 0 = tanpa audio; video=false = audio saja (m4a)
        public static MfWriter Create(string path, bool video, int w, int h, int fps, int audioRate, bool hw)
        {
            Check(MFStartup(0x20070, 0), "MFStartup");
            MfWriter m = new MfWriter();
            IntPtr attr = IntPtr.Zero;
            try
            {
                Check(MFCreateAttributes(out attr, 2), "MFCreateAttributes");
                if (hw) U32(attr, HW, 1);
                if (File.Exists(path)) File.Delete(path);
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attr, out m._w), "SinkWriter");
                if (video) m.AddVideo(w, h, fps);
                if (audioRate > 0) m.AddAudio(audioRate);
                Check(Fn<FnV>(m._w, 5)(m._w), "BeginWriting");
                m._started = true;
                return m;
            }
            catch { m.Abort(); throw; }
            finally { Release(attr); }
        }

        private void AddVideo(int w, int h, int fps)
        {
            _vw = w; _vh = h; _fps = fps;
            IntPtr o = IntPtr.Zero, i = IntPtr.Zero;
            try
            {
                Check(MFCreateMediaType(out o), "MediaType");
                Gd(o, MT_MAJOR, VIDEO); Gd(o, MT_SUB, H264);
                U32(o, BITRATE, (int)Math.Min(24000000L, Math.Max(4000000L, (long)w * h * fps / 8 * 3 / 10 * 8 / 10)));
                U32(o, INTERLACE, 2);
                U64(o, FSIZE, ((long)w << 32) | (uint)h); U64(o, FRATE, ((long)fps << 32) | 1); U64(o, PAR, (1L << 32) | 1);
                Check(Fn<FnAdd>(_w, 3)(_w, o, out _vIdx), "AddStream video");
                Check(MFCreateMediaType(out i), "MediaType");
                Gd(i, MT_MAJOR, VIDEO); Gd(i, MT_SUB, RGB32);
                U32(i, INTERLACE, 2);
                U64(i, FSIZE, ((long)w << 32) | (uint)h); U64(i, FRATE, ((long)fps << 32) | 1); U64(i, PAR, (1L << 32) | 1);
                U32(i, STRIDE, w * 4);   // top-down
                Check(Fn<FnSetIn>(_w, 4)(_w, _vIdx, i, IntPtr.Zero), "SetInputMediaType video");
            }
            finally { Release(o); Release(i); }
        }

        private void AddAudio(int rate)
        {
            IntPtr o = IntPtr.Zero, i = IntPtr.Zero;
            try
            {
                Check(MFCreateMediaType(out o), "MediaType");
                Gd(o, MT_MAJOR, AUDIO); Gd(o, MT_SUB, AAC);
                U32(o, A_BPS, 16); U32(o, A_SPS, rate); U32(o, A_CH, 1); U32(o, A_AVG, 16000); U32(o, AAC_PAYLOAD, 0);
                Check(Fn<FnAdd>(_w, 3)(_w, o, out _aIdx), "AddStream audio");
                Check(MFCreateMediaType(out i), "MediaType");
                Gd(i, MT_MAJOR, AUDIO); Gd(i, MT_SUB, PCM);
                U32(i, A_BPS, 16); U32(i, A_SPS, rate); U32(i, A_CH, 1); U32(i, A_ALIGN, 2); U32(i, A_AVG, rate * 2);
                Check(Fn<FnSetIn>(_w, 4)(_w, _aIdx, i, IntPtr.Zero), "SetInputMediaType audio");
            }
            finally { Release(o); Release(i); }
        }

        private void Write(int idx, IntPtr src, int len, long time, long dur)
        {
            IntPtr buf = IntPtr.Zero, smp = IntPtr.Zero;
            try
            {
                Check(MFCreateMemoryBuffer(len, out buf), "Buffer");
                IntPtr data; int max, cur;
                Check(Fn<FnLock>(buf, 3)(buf, out data, out max, out cur), "Lock");
                Copy(data, src, (IntPtr)len);
                Fn<FnV>(buf, 4)(buf);
                Fn<FnInt>(buf, 6)(buf, len);
                Check(MFCreateSample(out smp), "Sample");
                Check(Fn<FnPtr>(smp, 42)(smp, buf), "AddBuffer");
                Fn<FnLong>(smp, 36)(smp, time);
                Fn<FnLong>(smp, 38)(smp, dur);
                lock (_lock) { if (_closed) return; Check(Fn<FnIdxPtr>(_w, 6)(_w, idx, smp), "WriteSample"); }
            }
            finally { Release(smp); Release(buf); }
        }

        public void WriteVideo(IntPtr bits, long time) { if (_vIdx >= 0) Write(_vIdx, bits, _vw * _vh * 4, time, 10000000L / _fps); }

        public void WriteAudio(byte[] pcm, int len, long time, int rate)
        {
            if (_aIdx < 0 || len <= 0) return;
            GCHandle h = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            try { Write(_aIdx, h.AddrOfPinnedObject(), len, time, (long)len / 2 * 10000000L / rate); }
            finally { h.Free(); }
        }

        public void Finish()
        {
            lock (_lock)
            {
                if (_closed) return;
                _closed = true;
                try { if (_started) Fn<FnV>(_w, 11)(_w); } catch (Exception ex) { Log.Write("mf finalize: " + ex.Message); }
                Release(_w); _w = IntPtr.Zero;
            }
            try { MFShutdown(); } catch { }
        }

        private void Abort() { lock (_lock) { _closed = true; Release(_w); _w = IntPtr.Zero; } try { MFShutdown(); } catch { } }
        public void Dispose() { Finish(); }
    }

    // ============================================================
    //  MIKROFON (waveIn, 16-bit mono) — buffer diambil dengan polling
    // ============================================================
    internal sealed class MicCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr h);

        public const int Rate = 44100;
        private IntPtr _h;
        private readonly IntPtr[] _hdr = new IntPtr[8];
        private Thread _t;
        private volatile bool _run;
        public Action<byte[], int> Data;     // dipanggil di thread perekam
        public volatile float Level;

        public void Start()
        {
            WAVEFORMATEX f = new WAVEFORMATEX();
            f.wFormatTag = 1; f.nChannels = 1; f.nSamplesPerSec = Rate; f.wBitsPerSample = 16; f.nBlockAlign = 2; f.nAvgBytesPerSec = Rate * 2;
            int r = waveInOpen(out _h, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) throw new Exception("Mikrofon tidak tersedia (kode " + r + ")");
            int size = Marshal.SizeOf(typeof(WAVEHDR)), bytes = Rate / 20 * 2;   // 50 ms
            for (int i = 0; i < _hdr.Length; i++)
            {
                WAVEHDR w = new WAVEHDR(); w.lpData = Marshal.AllocHGlobal(bytes); w.dwBufferLength = bytes;
                _hdr[i] = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(w, _hdr[i], false);
                waveInPrepareHeader(_h, _hdr[i], size);
                waveInAddBuffer(_h, _hdr[i], size);
            }
            waveInStart(_h);
            _run = true;
            _t = new Thread(Loop); _t.IsBackground = true; _t.Name = "mic"; _t.Start();
        }

        private void Loop()
        {
            int size = Marshal.SizeOf(typeof(WAVEHDR));
            int offFlags = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags"), offRec = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded");
            byte[] tmp = new byte[Rate / 20 * 2];
            int next = 0;
            while (_run)
            {
                IntPtr hp = _hdr[next];
                if ((Marshal.ReadInt32(hp, offFlags) & 1) == 0) { Thread.Sleep(8); continue; }
                int n = Math.Min(tmp.Length, Marshal.ReadInt32(hp, offRec));
                Marshal.Copy(Marshal.ReadIntPtr(hp), tmp, 0, n);
                int peak = 0;
                for (int i = 0; i + 1 < n; i += 2) { int v = Math.Abs((int)(short)(tmp[i] | (tmp[i + 1] << 8))); if (v > peak) peak = v; }
                Level = Math.Min(1f, (float)Math.Sqrt(peak / 32768f) * 1.15f);
                Action<byte[], int> d = Data;
                if (d != null && n > 0) try { d(tmp, n); } catch (Exception ex) { Log.Write("mic data: " + ex.Message); }
                if (_run) waveInAddBuffer(_h, hp, size);
                next = (next + 1) % _hdr.Length;
            }
        }

        public void Stop()
        {
            _run = false;
            if (_t != null) _t.Join(500);
            if (_h == IntPtr.Zero) return;
            waveInReset(_h);
            int size = Marshal.SizeOf(typeof(WAVEHDR));
            foreach (IntPtr hp in _hdr)
            {
                if (hp == IntPtr.Zero) continue;
                waveInUnprepareHeader(_h, hp, size);
                Marshal.FreeHGlobal(Marshal.ReadIntPtr(hp));
                Marshal.FreeHGlobal(hp);
            }
            waveInClose(_h); _h = IntPtr.Zero;
        }
    }

    // ============================================================
    //  PEREKAM (layar / suara) — dikendalikan dari Dynamic Island
    // ============================================================
    internal static class Rec
    {
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, int usage, out IntPtr bits, IntPtr sec, int off);
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

        public static string Kind;                 // null, "screen", "audio"
        public static bool Active { get { return Kind != null; } }
        public static bool Paused;
        public static bool WithMic;
        private static readonly Stopwatch _sw = new Stopwatch();
        private static long _pausedTicks, _pauseStart;
        private static MfWriter _mf;
        private static MicCapture _mic;
        private static Thread _vt;
        private static volatile bool _run;
        private static string _path;
        private static long _audioSamples;
        public static readonly float[] Levels = new float[48];
        private static int _levelPos;
        public static Action<string, Bitmap, string> Saved;     // path, thumbnail, kind (thread UI)
        public static Action<string> Failed;
        private static Control _ui;

        public static TimeSpan? FakeElapsed;   // untuk render promo
        public static TimeSpan Elapsed { get { if (FakeElapsed.HasValue) return FakeElapsed.Value; if (!Active) return TimeSpan.Zero; long t = _sw.Elapsed.Ticks - _pausedTicks - (Paused ? _sw.Elapsed.Ticks - _pauseStart : 0); return TimeSpan.FromTicks(Math.Max(0, t)); } }
        public static float Level { get { return _mic != null ? _mic.Level : 0f; } }
        public static bool HasMic { get { return _mic != null; } }

        public static void Init(Control ui) { _ui = ui; }

        private static void PushLevel(float v) { lock (Levels) { Levels[_levelPos] = v; _levelPos = (_levelPos + 1) % Levels.Length; } }
        // n level terbaru (lama -> baru)
        public static float[] Recent(int n)
        {
            float[] r = new float[n];
            lock (Levels) for (int i = 0; i < n; i++) r[i] = Levels[(_levelPos - n + i + Levels.Length * 4) % Levels.Length];
            return r;
        }

        private static string Stamp() { return DateTime.Now.ToString("yyyy-MM-dd 'pukul' HH.mm.ss"); }

        public static string VideoDir { get { return Writable(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Rekaman Layar"); } }
        public static string AudioDir { get { return Writable(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Rekaman Suara"); } }

        // Folder pilihan; bila diblokir (mis. Controlled Folder Access Defender) pindah ke Unduhan.
        private static readonly Dictionary<string, string> _dirs = new Dictionary<string, string>();
        private static string Writable(string root, string name)
        {
            string c;
            if (_dirs.TryGetValue(name, out c) && Directory.Exists(c)) return c;
            c = Probe(root, name); _dirs[name] = c; return c;
        }

        private static string Probe(string root, string name)
        {
            string[] tries = { Path.Combine(root, name), Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), name), Path.Combine(Log.Dir, name) };
            foreach (string d in tries)
            {
                try
                {
                    Directory.CreateDirectory(d);
                    string t = Path.Combine(d, ".lm-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(t, ""); File.Delete(t);
                    return d;
                }
                catch { }
            }
            return Log.Dir;
        }

        public static void StartScreen(bool mic)
        {
            if (Active) return;
            Rectangle b = Screen.PrimaryScreen.Bounds;
            int w = b.Width & ~1, h = b.Height & ~1;
            _path = Path.Combine(VideoDir, "Rekaman Layar " + Stamp() + ".mp4");
            Array.Clear(Levels, 0, Levels.Length);
            try
            {
                try { _mf = MfWriter.Create(_path, true, w, h, 30, mic ? MicCapture.Rate : 0, true); }
                catch (Exception ex) { Log.Write("rec hw: " + ex.Message + " -> coba tanpa akselerasi"); _mf = MfWriter.Create(_path, true, w, h, 30, mic ? MicCapture.Rate : 0, false); }
                if (mic) StartMic();
            }
            catch (Exception ex) { Cleanup(); Fail("Tidak bisa mulai merekam layar: " + ex.Message); return; }
            Kind = "screen"; WithMic = mic; Paused = false; _pausedTicks = 0;
            _sw.Restart();
            _run = true;
            _vt = new Thread(delegate () { VideoLoop(b.Left, b.Top, w, h); });
            _vt.IsBackground = true; _vt.Name = "rec-video"; _vt.Priority = ThreadPriority.AboveNormal; _vt.Start();
        }

        public static void StartAudio()
        {
            if (Active) return;
            _path = Path.Combine(AudioDir, "Rekaman " + Stamp() + ".m4a");
            Array.Clear(Levels, 0, Levels.Length);
            try { _mf = MfWriter.Create(_path, false, 0, 0, 0, MicCapture.Rate, false); StartMic(); }
            catch (Exception ex) { Cleanup(); Fail("Tidak bisa mulai merekam suara: " + ex.Message); return; }
            Kind = "audio"; WithMic = true; Paused = false; _pausedTicks = 0;
            _sw.Restart();
            _run = true;
            Thread lt = new Thread(delegate () { while (_run) { PushLevel(Paused ? 0.02f : _mic != null ? _mic.Level : 0); Thread.Sleep(60); } });
            lt.IsBackground = true; lt.Start();
        }

        private static void StartMic()
        {
            _audioSamples = 0;
            _mic = new MicCapture();
            _mic.Data = delegate (byte[] buf, int n)
            {
                if (!_run || Paused || _mf == null) return;
                long t = _audioSamples * 10000000L / MicCapture.Rate;
                _mf.WriteAudio(buf, n, t, MicCapture.Rate);
                _audioSamples += n / 2;
            };
            _mic.Start();
        }

        public static void TogglePause()
        {
            if (!Active) return;
            if (Paused) { _pausedTicks += _sw.Elapsed.Ticks - _pauseStart; Paused = false; }
            else { _pauseStart = _sw.Elapsed.Ticks; Paused = true; }
        }

        private static Bitmap _lastThumb;

        private static void VideoLoop(int sx, int sy, int w, int h)
        {
            IntPtr scr = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(scr), bits, bmp, old;
            BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
            bmp = CreateDIBSection(scr, ref bi, 0, out bits, IntPtr.Zero, 0);
            old = SelectObject(mem, bmp);
            Dictionary<IntPtr, Point> hot = new Dictionary<IntPtr, Point>();
            long frame = 0, lastTs = -1;
            try
            {
                while (_run)
                {
                    if (Paused) { Thread.Sleep(30); continue; }
                    BitBlt(mem, 0, 0, w, h, scr, sx, sy, 0x00CC0020);
                    CURSORINFO ci = new CURSORINFO(); ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
                    if (GetCursorInfo(ref ci) && (ci.flags & 1) != 0 && ci.hCursor != IntPtr.Zero)
                    {
                        Point hp;
                        if (!hot.TryGetValue(ci.hCursor, out hp))
                        {
                            ICONINFO ii;
                            if (GetIconInfo(ci.hCursor, out ii)) { hp = new Point(ii.xHotspot, ii.yHotspot); if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask); if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor); }
                            hot[ci.hCursor] = hp;
                        }
                        DrawIconEx(mem, ci.pt.X - sx - hp.X, ci.pt.Y - sy - hp.Y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3);
                    }
                    GdiFlush();
                    long ts = Elapsed.Ticks;
                    if (ts <= lastTs) ts = lastTs + 1;
                    lastTs = ts;
                    _mf.WriteVideo(bits, ts);
                    if (!HasMic) PushLevel(0);
                    else PushLevel(_mic.Level);
                    frame++;
                    if (frame % 30 == 1) MakeThumb(bmp, w, h);
                    long next = (long)((Elapsed.TotalMilliseconds / 33.333) + 1) * 33333L * 10;   // jadwal frame berikutnya
                    int wait = (int)((next - Elapsed.Ticks) / 10000);
                    if (wait > 0) Thread.Sleep(Math.Min(wait, 34));
                }
            }
            catch (Exception ex) { Log.Write("rec video: " + ex); _run = false; Post(delegate { Fail("Perekaman berhenti: " + ex.Message); Stop(); }); }
            finally
            {
                SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, scr);
            }
        }

        private static void MakeThumb(IntPtr hbmp, int w, int h)
        {
            try
            {
                using (Bitmap full = Image.FromHbitmap(hbmp))
                {
                    int tw = 240, th = Math.Max(1, h * tw / w);
                    Bitmap t = new Bitmap(tw, th, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(t)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(full, 0, 0, tw, th); }
                    Bitmap o = Interlocked.Exchange(ref _lastThumb, t);
                    if (o != null) o.Dispose();
                }
            }
            catch { }
        }

        public static void Stop()
        {
            if (!Active) return;
            string kind = Kind, path = _path;
            _run = false;
            if (_vt != null && _vt != Thread.CurrentThread) _vt.Join(2000);
            _vt = null;
            Cleanup();
            Kind = null; Paused = false;
            Bitmap thumb = Interlocked.Exchange(ref _lastThumb, null);
            bool ok = File.Exists(path) && new FileInfo(path).Length > 0;
            if (!ok) { if (thumb != null) thumb.Dispose(); Fail("Rekaman gagal disimpan."); return; }
            Log.Write("rekaman disimpan: " + path);
            if (Saved != null) Saved(path, thumb, kind);
        }

        private static void Cleanup()
        {
            if (_mic != null) { try { _mic.Stop(); } catch { } _mic = null; }
            if (_mf != null) { try { _mf.Finish(); } catch (Exception ex) { Log.Write("rec finish: " + ex.Message); } _mf = null; }
        }

        private static void Fail(string msg) { Log.Write("rec: " + msg); if (Failed != null) Failed(msg); }
        private static void Post(MethodInvoker m) { if (_ui != null && _ui.IsHandleCreated) _ui.BeginInvoke(m); else m(); }

        // ---------- tangkapan layar ----------
        public static string ShotDir
        {
            get
            {
                string d = null;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                    {
                        string v = k == null ? null : k.GetValue("{B7BEDE81-DF94-4682-A7D8-57A52620B86F}") as string;
                        if (!string.IsNullOrEmpty(v)) d = Environment.ExpandEnvironmentVariables(v);
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        // Pakai tangkapan layar bawaan Windows (Win+PrtScn): tersimpan di folder Screenshots, lalu island menampilkan pratinjau.
        public static void Screenshot() { Native.Keys(Native.VK_LWIN, 0x2C); }
    }

    // ============================================================
    //  PANTAU TANGKAPAN LAYAR BARU (Win+PrtScn, Snipping Tool, island)
    // ============================================================
    internal static class ShotWatch
    {
        private static FileSystemWatcher _fw;
        private static Control _ui;
        public static Action<string, Bitmap> Shot;    // thread UI
        private static string _last;

        public static void Start(Control ui)
        {
            _ui = ui;
            Stop();
            try
            {
                _fw = new FileSystemWatcher(Rec.ShotDir);
                _fw.IncludeSubdirectories = false;
                _fw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
                _fw.Created += OnNew;
                _fw.Renamed += delegate (object s, RenamedEventArgs e) { OnNew(s, e); };
                _fw.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("shotwatch: " + ex.Message); }
        }

        public static void Stop() { if (_fw != null) { try { _fw.EnableRaisingEvents = false; _fw.Dispose(); } catch { } _fw = null; } }

        private static void OnNew(object s, FileSystemEventArgs e)
        {
            string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return;
            if (e.FullPath == _last) return;
            _last = e.FullPath;
            string path = e.FullPath;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap thumb = null;
                for (int i = 0; i < 20 && thumb == null; i++)
                {
                    Thread.Sleep(150);
                    try
                    {
                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (Image img = Image.FromStream(fs))
                        {
                            int tw = 240, th = Math.Max(1, img.Height * tw / Math.Max(1, img.Width));
                            if (th > 240) { th = 240; tw = Math.Max(1, img.Width * th / Math.Max(1, img.Height)); }
                            Bitmap t = new Bitmap(tw, th, PixelFormat.Format32bppPArgb);
                            using (Graphics g = Graphics.FromImage(t)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, tw, th); }
                            thumb = t;
                        }
                    }
                    catch { }
                }
                if (thumb == null || _ui == null || !_ui.IsHandleCreated) return;
                _ui.BeginInvoke((MethodInvoker)delegate { if (Shot != null) Shot(path, thumb); else thumb.Dispose(); });
            });
        }
    }

    // ============================================================
    //  KAMERA / MIKROFON SEDANG DIPAKAI (CapabilityAccessManager)
    // ============================================================
    internal static class PrivacyWatch
    {
        public static List<string> Cam = new List<string>(), Mic = new List<string>();
        public static Action<List<string>, List<string>> Changed;   // thread UI
        private static System.Threading.Timer _t;
        private static Control _ui;
        private static string _sig = "";
        private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        public static void Start(Control ui)
        {
            _ui = ui;
            if (_t == null) _t = new System.Threading.Timer(delegate { Poll(); }, null, 1000, 1500);
        }

        public static void Stop() { if (_t != null) { _t.Dispose(); _t = null; } Cam = new List<string>(); Mic = new List<string>(); _sig = ""; }

        private static void Poll()
        {
            try
            {
                List<string> cam = InUse("webcam"), mic = InUse("microphone");
                string sig = string.Join("|", cam.ToArray()) + "#" + string.Join("|", mic.ToArray());
                if (sig == _sig) return;
                _sig = sig;
                if (_ui == null || !_ui.IsHandleCreated) return;
                _ui.BeginInvoke((MethodInvoker)delegate { Cam = cam; Mic = mic; if (Changed != null) Changed(cam, mic); });
            }
            catch (Exception ex) { Log.Write("privacy: " + ex.Message); }
        }

        private static List<string> InUse(string cap)
        {
            List<string> r = new List<string>();
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Root + cap))
            {
                if (k == null) return r;
                Scan(k, r, false);
                using (RegistryKey np = k.OpenSubKey("NonPackaged")) if (np != null) Scan(np, r, true);
            }
            return r;
        }

        private static void Scan(RegistryKey k, List<string> r, bool desktop)
        {
            foreach (string n in k.GetSubKeyNames())
            {
                if (n == "NonPackaged") continue;
                try
                {
                    using (RegistryKey a = k.OpenSubKey(n))
                    {
                        if (a == null) continue;
                        object start = a.GetValue("LastUsedTimeStart"), stop = a.GetValue("LastUsedTimeStop");
                        if (start == null || stop == null) continue;
                        if (Convert.ToInt64(stop) != 0 || Convert.ToInt64(start) == 0) continue;
                        string name = Pretty(n, desktop);
                        if (name.Equals("LiquidMac", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!r.Contains(name)) r.Add(name);
                    }
                }
                catch { }
            }
        }

        private static string Pretty(string key, bool desktop)
        {
            if (desktop)
            {
                string f = key.Substring(key.LastIndexOf('#') + 1);
                string path = key.Replace('#', '\\');
                try
                {
                    if (File.Exists(path))
                    {
                        string d = FileVersionInfo.GetVersionInfo(path).FileDescription;
                        if (!string.IsNullOrEmpty(d) && d.Length < 40) return d.Trim();
                    }
                }
                catch { }
                return Path.GetFileNameWithoutExtension(f);
            }
            string s = key;
            int u = s.IndexOf('_'); if (u > 0) s = s.Substring(0, u);
            int dot = s.LastIndexOf('.'); if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            if (s == "WindowsCamera") s = "Kamera";
            return s;
        }
    }
}
