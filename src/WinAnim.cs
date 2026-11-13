// Animasi jendela LiquidMac (minimize ke Dock, buka dari Dock, zoom) pengganti animasi Windows,
// supaya traffic light ikut beranimasi dan tombol bawaan tidak sempat terlihat.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    internal static class WinAnim
    {
        [StructLayout(LayoutKind.Sequential)] private struct ANIMATIONINFO { public int cbSize, iMinAnimate; }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int act, int p, ref ANIMATIONINFO ai, int win);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT r, int size);

        public static bool Enabled { get { return Settings.Get("MacWindowAnim", true); } }

        // Diisi Dock: kotak ikon (koordinat layar) untuk jendela tertentu.
        public static Func<IntPtr, Rectangle> DockTarget;
        public static IntPtr DockHwnd;
        public static IntPtr[] KeepAbove = new IntPtr[0];   // notch & bilah atas tetap di atas jendela animasi
        public static string ForceStyle;   // untuk render promo
        // "genie" (bawaan) atau "pocket" (Kantong Ajaib: kantong terbuka, jendela berputar masuk/keluar)
        // Kantong Ajaib dinonaktifkan (v1.7.9): selalu genie, kecuali render promo yang memaksa lewat ForceStyle.
        public static bool PocketStyle { get { return ForceStyle == "pocket"; } }   // jendela animasi ditaruh tepat di bawah Dock: tersedot ke BELAKANG ikon
        // Diisi TrafficLights: gambar traffic light di atas tangkapan jendela.
        public static Action<IntPtr, Graphics, Rectangle, float> PaintOverlay;

        private sealed class Snap { public Bitmap Img; public Rectangle Frame; }
        private static readonly Dictionary<IntPtr, Snap> _snaps = new Dictionary<IntPtr, Snap>();
        private static readonly List<IntPtr> _order = new List<IntPtr>();

        // ---------- animasi sistem ----------
        private static int GetSys()
        {
            ANIMATIONINFO ai = new ANIMATIONINFO(); ai.cbSize = Marshal.SizeOf(typeof(ANIMATIONINFO));
            return SystemParametersInfo(0x0048, ai.cbSize, ref ai, 0) ? ai.iMinAnimate : 1;
        }

        private static void SetSys(int v)
        {
            ANIMATIONINFO ai = new ANIMATIONINFO(); ai.cbSize = Marshal.SizeOf(typeof(ANIMATIONINFO)); ai.iMinAnimate = v;
            SystemParametersInfo(0x0049 /* SPI_SETANIMATION */, ai.cbSize, ref ai, 0x3 /* UPDATEINIFILE|SENDCHANGE */);
        }

        // Saat aktif: animasi minimize/maximize Windows dimatikan (nilai asli disimpan & dikembalikan saat keluar).
        public static void SyncSystem()
        {
            try
            {
                if (Enabled)
                {
                    if (Settings.GetS("OrigMinAnimate", "") == "") Settings.SetS("OrigMinAnimate", GetSys().ToString());
                    if (GetSys() != 0) SetSys(0);
                }
                else RestoreSystem();
            }
            catch (Exception ex) { Log.Write("winanim sys: " + ex.Message); }
        }

        public static void RestoreSystem()
        {
            ReleaseTaskbar();
            try
            {
                string o = Settings.GetS("OrigMinAnimate", "");
                if (o == "") return;
                int v; if (int.TryParse(o, out v) && GetSys() != v) SetSys(v);
                Settings.SetS("OrigMinAnimate", "");
            }
            catch { }
        }

        // ---------- bantuan ----------
        public static Rectangle Frame(IntPtr h)
        {
            Native.RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0) Native.GetWindowRect(h, out r);
            Rectangle f = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            Rectangle wa = Screen.FromHandle(h).Bounds;
            return Rectangle.Intersect(f, wa);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr p);

        private static Bitmap Capture(IntPtr h, Rectangle f, float scale)
        {
            if (f.Width < 20 || f.Height < 20) return null;
            try
            {
                int w = Math.Max(1, (int)(f.Width * scale)), hh = Math.Max(1, (int)(f.Height * scale));
                Bitmap small = new Bitmap(w, hh, PixelFormat.Format32bppRgb);
                // Ambil gambar langsung dari jendelanya (PrintWindow), BUKAN tangkapan layar: tangkapan layar memaksa DWM
                // menyusun ulang tanpa jendela WDA_EXCLUDEFROMCAPTURE (notch) dan membuat notch berkedip.
                Bitmap direct = PrintCapture(h, f);
                using (Graphics g = Graphics.FromImage(small))
                {
                    if (direct != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(direct, new Rectangle(0, 0, w, hh));
                        direct.Dispose();
                    }
                    else
                    {
                        IntPtr dst = g.GetHdc(), scr = GetDC(IntPtr.Zero);
                        try
                        {
                            SetStretchBltMode(dst, 4 /* HALFTONE */); SetBrushOrgEx(dst, 0, 0, IntPtr.Zero);
                            StretchBlt(dst, 0, 0, w, hh, scr, f.Left, f.Top, f.Width, f.Height, 0x00CC0020 /* SRCCOPY */);
                        }
                        finally { ReleaseDC(IntPtr.Zero, scr); g.ReleaseHdc(dst); }
                    }
                    // traffic light (tidak ikut tertangkap layar) digambar di atasnya
                    Action<IntPtr, Graphics, Rectangle, float> po = PaintOverlay;
                    if (po != null) { g.ScaleTransform(scale, scale); g.InterpolationMode = InterpolationMode.HighQualityBilinear; po(h, g, f, scale); }
                }
                return small;
            }
            catch (Exception ex) { Log.Write("winanim capture: " + ex.Message); return null; }
        }

        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // uji: LM_ANIMFPS=1, FPS animasi di luar layar dengan timer lama dan pacu layar (anim-fps.txt)
        public static void TestFps(string dir)
        {
            Settings.SetTemp("WindowAnimSound", "0");
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            foreach (bool vs in new bool[] { false, true, false, true })
            {
                AnimForm.UseVsync = vs;
                bool done = false;
                AnimForm a = new AnimForm(new Bitmap(1040, 640, PixelFormat.Format32bppRgb), new Rectangle(-5000, 100, 1040, 640), new Rectangle(-4600, 900, 48, 48), 430, AnimForm.Mode.Minimize, delegate { done = true; });
                Stopwatch sw = Stopwatch.StartNew();
                a.Start();
                while (!done && sw.ElapsedMilliseconds < 4000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                long ms = sw.ElapsedMilliseconds;
                log.AppendLine((vs ? "pacu layar" : "timer lama") + ": " + a.Frames + " frame dalam " + ms + " ms = " + (a.Frames * 1000 / Math.Max(1, ms)) + " fps, selesai=" + done);
                for (int i = 0; i < 40; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            }
            AnimForm.UseVsync = true;
            // lapisan animasi saat Dock tenggelam di bawah jendela aplikasi
            foreach (bool raise in new bool[] { false, true })
            {
                AnimForm.RaiseDock = raise;
                SetWindowPos(DockHwnd, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                bool dn = false;
                AnimForm a = new AnimForm(new Bitmap(1040, 640, PixelFormat.Format32bppRgb), new Rectangle(-5000, 100, 1040, 640), new Rectangle(-4600, 900, 48, 48), 300, AnimForm.Mode.Minimize, delegate { dn = true; });
                a.Start();
                int covering = 0;
                for (IntPtr w = GetWindow(a.Handle, 3 /* GW_HWNDPREV */); w != IntPtr.Zero; w = GetWindow(w, 3))
                    if (Native.IsWindowVisible(w) && (Native.GetWindowLong(w, Native.GWL_EXSTYLE) & 8) == 0) covering++;
                log.AppendLine((raise ? "dengan perbaikan" : "tanpa perbaikan") + ": jendela biasa di atas animasi = " + covering);
                Stopwatch s4 = Stopwatch.StartNew();
                while (!dn && s4.ElapsedMilliseconds < 2000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
            }
            AnimForm.RaiseDock = true;
            SetWindowPos(DockHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            // alih tampilan saat dibuka: selesai animasi -> ditahan -> memudar -> hilang
            for (int run = 0; run < 2; run++)
            {
                long doneAt = -1;
                Stopwatch sw2 = Stopwatch.StartNew();
                AnimForm r = new AnimForm(new Bitmap(1040, 640, PixelFormat.Format32bppRgb), new Rectangle(-4600, 900, 48, 48), new Rectangle(-5000, 100, 1040, 640), 440, AnimForm.Mode.Restore, delegate { doneAt = sw2.ElapsedMilliseconds; });
                r.Start();
                while (!r.IsDisposed && sw2.ElapsedMilliseconds < 4000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                log.AppendLine("buka: animasi selesai " + doneAt + " ms, jendela animasi hilang " + sw2.ElapsedMilliseconds + " ms, alih " + (sw2.ElapsedMilliseconds - doneAt) + " ms, dibuang=" + r.IsDisposed);
            }
            // buka dengan jendela asli (jendela uji "lm-tltest", tidak diaktifkan): jendela tak pernah di depan -> batas 700 ms;
            // posisi jendela beda dari gambar -> langsung hilang tanpa memudar
            using (Form tf = new Form())
            {
                tf.Text = "lm-tltest"; tf.ShowInTaskbar = false; tf.FormBorderStyle = FormBorderStyle.None; tf.StartPosition = FormStartPosition.Manual;
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                tf.Bounds = new Rectangle(wa.Right - 220, wa.Bottom - 170, 200, 150);
                Native.ShowWindow(tf.Handle, 4 /* SW_SHOWNOACTIVATE */);
                foreach (bool same in new bool[] { true, false })
                {
                    Rectangle to = same ? Frame(tf.Handle) : new Rectangle(wa.Right - 260, wa.Bottom - 200, 180, 130);
                    long doneAt = -1;
                    Stopwatch sw3 = Stopwatch.StartNew();
                    AnimForm r = new AnimForm(new Bitmap(to.Width, to.Height, PixelFormat.Format32bppRgb), new Rectangle(wa.Right - 120, wa.Bottom - 40, 40, 40), to, 300, AnimForm.Mode.Restore, delegate { doneAt = sw3.ElapsedMilliseconds; });
                    r.Target = tf.Handle;
                    r.Start();
                    while (!r.IsDisposed && sw3.ElapsedMilliseconds < 4000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                    log.AppendLine("buka jendela uji (" + (same ? "posisi sama" : "posisi beda") + "): selesai " + doneAt + " ms, hilang " + sw3.ElapsedMilliseconds + " ms, alih " + (sw3.ElapsedMilliseconds - doneAt) + " ms");
                }
            }
            // animasi baru saat yang lama masih memudar
            {
                bool d1 = false; Stopwatch sw3 = Stopwatch.StartNew();
                AnimForm r1 = new AnimForm(new Bitmap(1040, 640, PixelFormat.Format32bppRgb), new Rectangle(-4600, 900, 48, 48), new Rectangle(-5000, 100, 1040, 640), 120, AnimForm.Mode.Restore, delegate { d1 = true; });
                r1.Start();
                while (!d1 && sw3.ElapsedMilliseconds < 2000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                bool done2 = false;
                AnimForm m2 = new AnimForm(new Bitmap(1040, 640, PixelFormat.Format32bppRgb), new Rectangle(-5000, 100, 1040, 640), new Rectangle(-4600, 900, 48, 48), 120, AnimForm.Mode.Minimize, delegate { done2 = true; });
                m2.Start();
                log.AppendLine("animasi lama dihentikan seketika=" + r1.IsDisposed);
                while (!done2 && sw3.ElapsedMilliseconds < 3000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                log.AppendLine("animasi baru selesai=" + done2);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "anim-fps.txt"), log.ToString());
        }

        // uji: LM_ANIMBIG=1, genie minimize & restore dengan gambar seukuran layar (di luar layar), hasil di log
        public static void TestBig(string dir)
        {
            foreach (AnimForm.Mode m in new AnimForm.Mode[] { AnimForm.Mode.Minimize, AnimForm.Mode.Restore })
            {
                Bitmap img = new Bitmap(1904, 965, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(img)) { g.Clear(Color.FromArgb(30, 30, 34)); for (int i = 0; i < 1904; i += 80) g.FillRectangle(Brushes.OrangeRed, i, 0, 40, 965); }
                int ox = Environment.GetEnvironmentVariable("LM_ANIMBIG") == "2" ? 0 : -5000;   // 2 = tampil di layar (untuk direkam)
                Rectangle win = new Rectangle(ox + 7, 28, 1904, 965), icon = new Rectangle(ox + 900, 1010, 48, 48);
                bool done = false;
                AnimForm a = m == AnimForm.Mode.Minimize ? new AnimForm(img, win, icon, 480, m, delegate { done = true; }) : new AnimForm(img, icon, win, 440, m, delegate { done = true; });
                a.Start();
                Stopwatch sw = Stopwatch.StartNew();
                while (!a.IsDisposed && sw.ElapsedMilliseconds < 2500) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(1); }
            }
        }

        // uji: LM_SHADOW=1, frame genie minimize dengan bayangan (shadow_XXXX.png)
        public static void TestShadow(string dir)
        {
            Bitmap img = new Bitmap(800, 500, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(img)) using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, 800, 500), Color.FromArgb(240, 240, 245), Color.FromArgb(200, 210, 230), 90f)) g.FillRectangle(lg, 0, 0, 800, 500);
            AnimForm a = new AnimForm(img, new Rectangle(200, 100, 800, 500), new Rectangle(560, 900, 48, 48), 480, AnimForm.Mode.Minimize, null);
            a.PromoRender(dir, "shadow", 20);
            using (Bitmap pi = PhotosIcon.Render(256)) pi.Save(System.IO.Path.Combine(dir, "photos-icon.png"));
        }

        public static void TestCapture(string dir)
        {
            foreach (KeyValuePair<string, List<IntPtr>> kv in Windows.AppWindows())
            {
                IntPtr h = kv.Value[0]; Rectangle f = Frame(h);
                Stopwatch sw = Stopwatch.StartNew(); Bitmap b = PrintCapture(h, f); long ms = sw.ElapsedMilliseconds;
                Log.Write("captest " + System.IO.Path.GetFileName(kv.Key.Split('|')[0]) + " print=" + (b != null) + " " + ms + "ms frame=" + f);
                if (b != null) { b.Save(System.IO.Path.Combine(dir, "cap-" + System.IO.Path.GetFileNameWithoutExtension(kv.Key.Split('|')[0]) + ".png")); b.Dispose(); }
            }
        }

        // Render jendela ke bitmap (PW_RENDERFULLCONTENT), dipotong ke bingkai terlihat f. null bila gagal / hitam total.
        private static Bitmap PrintCapture(IntPtr h, Rectangle f)
        {
            try
            {
                Native.RECT wr;
                if (!Native.GetWindowRect(h, out wr)) return null;
                int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
                if (ww < 20 || wh < 20 || ww > 8000 || wh > 8000) return null;
                Bitmap full = new Bitmap(ww, wh, PixelFormat.Format32bppRgb);
                bool ok;
                using (Graphics g = Graphics.FromImage(full)) { IntPtr dc = g.GetHdc(); try { ok = PrintWindow(h, dc, 2); } finally { g.ReleaseHdc(dc); } }
                if (!ok) { full.Dispose(); return null; }
                Rectangle src = new Rectangle(f.Left - wr.Left, f.Top - wr.Top, f.Width, f.Height);
                src.Intersect(new Rectangle(0, 0, ww, wh));
                if (src.Width < 20 || src.Height < 20) { full.Dispose(); return null; }
                // hitam total = aplikasi tidak mendukung PrintWindow (mis. game): pakai cara lama
                bool any = false;
                for (int y = 0; y < 8 && !any; y++) for (int x = 0; x < 8 && !any; x++)
                { Color c = full.GetPixel(src.X + src.Width * (2 * x + 1) / 16, src.Y + src.Height * (2 * y + 1) / 16); if (c.R + c.G + c.B > 0) any = true; }
                if (!any) { full.Dispose(); return null; }
                Bitmap cut = full.Clone(src, PixelFormat.Format32bppRgb);
                full.Dispose();
                return cut;
            }
            catch { return null; }
        }

        private static Rectangle TargetFor(IntPtr h, Rectangle frame)
        {
            Func<IntPtr, Rectangle> dt = DockTarget;
            Rectangle r = dt != null ? dt(h) : Rectangle.Empty;
            if (r.IsEmpty)
            {
                Rectangle s = Screen.FromHandle(h).Bounds;
                r = new Rectangle(s.Left + s.Width / 2 - 24, s.Bottom - 60, 48, 48);
            }
            return r;
        }

        private static void Keep(IntPtr h, Snap s)
        {
            Forget(h);
            _snaps[h] = s; _order.Add(h);
            while (_order.Count > 10) Forget(_order[0]);   // banyak jendela diminimalkan: yang tanpa tangkapan terbuka tanpa animasi
        }

        // Buang tangkapan milik jendela yang sudah ditutup.
        public static void Prune()
        {
            for (int i = _order.Count - 1; i >= 0; i--) if (!Native.IsWindow(_order[i])) Forget(_order[i]);
        }

        public static void Forget(IntPtr h)
        {
            Snap s;
            if (_snaps.TryGetValue(h, out s)) { if (s.Img != null) s.Img.Dispose(); _snaps.Remove(h); }
            _order.Remove(h);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

        // Minimize tanpa membiarkan Windows memilih jendela aktif berikutnya (sering taskbar,
        // yang membuat taskbar sembunyi-otomatis ikut muncul). Kita pilih sendiri jendela aplikasi berikutnya.
        public static void MinimizeQuiet(IntPtr h)
        {
            IntPtr next = NextAppWindow(h);
            HoldTaskbar();
            ShowWindowAsync(h, 7 /* SW_SHOWMINNOACTIVE */);
            SetForegroundWindow(next != IntPtr.Zero ? next : GetShellWindow());
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        private static System.Windows.Forms.Timer _tbTimer;
        private static readonly List<IntPtr> _tbHidden = new List<IntPtr>();

        // Windows memunculkan taskbar sembunyi-otomatis setiap kali jendela di-minimize.
        // Bila taskbar memang sembunyi otomatis, sembunyikan sebentar lalu kembalikan.
        private static void HoldTaskbar()
        {
            try
            {
                if (!AppBar.TaskbarAutoHide) return;
                foreach (string cls in new string[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                    for (IntPtr w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, cls, null); w != IntPtr.Zero; w = FindWindowEx(IntPtr.Zero, w, cls, null))
                        if (Native.IsWindowVisible(w)) { Native.ShowWindow(w, 0 /* SW_HIDE */); _tbHidden.Add(w); }
                if (_tbTimer == null)
                {
                    _tbTimer = new System.Windows.Forms.Timer();
                    _tbTimer.Interval = 700;
                    _tbTimer.Tick += delegate { _tbTimer.Stop(); ReleaseTaskbar(); };
                }
                _tbTimer.Stop(); _tbTimer.Start();
            }
            catch (Exception ex) { Log.Write("taskbar hold: " + ex.Message); }
        }

        public static void ReleaseTaskbar()
        {
            foreach (IntPtr w in _tbHidden) if (Native.IsWindow(w)) Native.ShowWindow(w, 4 /* SW_SHOWNOACTIVATE */);
            _tbHidden.Clear();
        }

        private static IntPtr NextAppWindow(IntPtr h)
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            int guard = 0;
            for (IntPtr w = GetWindow(h, 2 /* GW_HWNDNEXT */); w != IntPtr.Zero && guard < 500; w = GetWindow(w, 2), guard++)
            {
                if (!Native.IsWindowVisible(w) || Native.IsIconic(w)) continue;
                if (Native.GetWindow(w, Native.GW_OWNER) != IntPtr.Zero) continue;
                int ex = Native.GetWindowLong(w, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) continue;
                int cl; if (Native.DwmGetWindowAttribute(w, 14, out cl, 4) == 0 && cl != 0) continue;
                uint pid; Native.GetWindowThreadProcessId(w, out pid);
                if (pid == me) continue;
                string c = Native.ClassOf(w);
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Progman" || c == "WorkerW") continue;
                Native.RECT r; if (!Native.GetWindowRect(w, out r) || r.Right - r.Left < 50 || r.Bottom - r.Top < 50) continue;
                return w;
            }
            return IntPtr.Zero;
        }

        // ---------- aksi ----------
        public static void Minimize(IntPtr h)
        {
            if (!Enabled || !Native.IsWindow(h)) { MinimizeQuiet(h); return; }
            Rectangle f = Frame(h);
            Stopwatch dsw = Stopwatch.StartNew();
            Bitmap img = Capture(h, f, 1f);
            if (Environment.GetEnvironmentVariable("LIQUIDMAC_DEBUG") == "1" || System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug"))) Log.Write("anim min frame=" + f + " img=" + (img != null) + " enabled=" + Enabled + " capture_ms=" + dsw.ElapsedMilliseconds);
            if (img == null) { MinimizeQuiet(h); return; }
            Rectangle to = TargetFor(h, f);
            Keep(h, new Snap { Img = img, Frame = f });
            AnimForm a = new AnimForm(img, f, to, PocketStyle ? 660 : 480, AnimForm.Mode.Minimize, null);
            a.Start();
            MinimizeQuiet(h);
        }

        public static void Restore(IntPtr h, Rectangle from)
        {
            Snap s;
            if (!Enabled || !Native.IsIconic(h) || !_snaps.TryGetValue(h, out s)) { Shell.Activate(h); return; }
            _snaps.Remove(h); _order.Remove(h);
            if (from.IsEmpty) from = TargetFor(h, s.Frame);
            Bitmap img = s.Img;
            AnimForm a = new AnimForm(img, from, s.Frame, PocketStyle ? 580 : 440, AnimForm.Mode.Restore, delegate
            {
                Shell.Activate(h);
            });
            a.Target = h;
            a.Start();
        }

        public static void Zoom(IntPtr h)
        {
            bool toMax = !Native.IsZoomed(h);
            if (!Enabled) { ShowWindowAsync(h, toMax ? 3 : 9); return; }
            Rectangle f = Frame(h);
            Bitmap img = Capture(h, f, 1f);
            if (img == null) { ShowWindowAsync(h, toMax ? 3 : 9); return; }
            // Ukuran akhir dihitung dulu. Jendela asli baru diubah setelah animasi selesai membesar/mengecil:
            // kalau diubah di awal, jendela besar langsung tampak di balik animasi lalu gambar lama memudar
            // di atas isi baru (teks dobel), terlihat seperti "refresh".
            Rectangle to = ZoomTarget(h, f, toMax);
            if (to.IsEmpty)
            {
                AnimForm old = new AnimForm(img, f, Rectangle.Empty, 280, AnimForm.Mode.Zoom, null);
                old.Follow = h;
                old.Start();
                ShowWindowAsync(h, toMax ? 3 : 9);
                return;
            }
            AnimForm a = new AnimForm(img, f, to, 280, AnimForm.Mode.Zoom, delegate { ShowWindowAsync(h, toMax ? 3 : 9); });
            a.Target = h;
            a.Start();
        }

        [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT { public int length, flags, showCmd; public Native.POINT min, max; public Native.RECT normal; }
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);

        // Bingkai terlihat jendela setelah maximize / restore (Empty bila tidak bisa diperkirakan).
        public static Rectangle ZoomTarget(IntPtr h, Rectangle frame, bool toMax)
        {
            try
            {
                Screen scr = Screen.FromHandle(h);
                Rectangle wa = scr.WorkingArea;
                if (toMax) return Rectangle.Intersect(wa, scr.Bounds);
                WINDOWPLACEMENT wp = new WINDOWPLACEMENT(); wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                if (!GetWindowPlacement(h, ref wp)) return Rectangle.Empty;
                Native.RECT wr; if (!Native.GetWindowRect(h, out wr)) return Rectangle.Empty;
                // posisi normal memakai koordinat area kerja; border tak terlihat diukur dari jendela yang sedang maximize
                int bl = Math.Max(0, frame.Left - wr.Left), br = Math.Max(0, wr.Right - frame.Right), bb = Math.Max(0, wr.Bottom - frame.Bottom);
                Rectangle n = Rectangle.FromLTRB(wp.normal.Left + wa.Left - scr.Bounds.Left + scr.Bounds.Left, wp.normal.Top + wa.Top - scr.Bounds.Top + scr.Bounds.Top, wp.normal.Right + wa.Left, wp.normal.Bottom + wa.Top);
                n = Rectangle.FromLTRB(n.Left + bl, n.Top, n.Right - br, n.Bottom - bb);
                n.Intersect(scr.Bounds);
                return n.Width > 100 && n.Height > 60 ? n : Rectangle.Empty;
            }
            catch { return Rectangle.Empty; }
        }
    }

    // Jendela berlapis untuk animasi. Digambar per baris ke DIB 32-bit dengan alfa per piksel,
    // tepi kiri/kanan corong diberi alfa pecahan (anti-alias) agar lengkungannya halus.
    internal sealed class AnimForm : Form
    {
        public enum Mode { Minimize, Restore, Zoom }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        private readonly Bitmap _img;
        private Rectangle _from, _to;
        private readonly int _ms;
        private readonly Mode _mode;
        private readonly Action _done;
        private readonly Stopwatch _sw = new Stopwatch();
        private readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer();
        private bool _finished; private int _frames;
        public IntPtr Follow;
        public IntPtr Target;   // Restore: jendela asli yang dibuka
        private long _readyAt = -1;
        private long _pfLast; private double _pfSum, _pfMax, _pfGap;
        public static readonly bool PerfLog = System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "perf"));
        private bool _mismatch;

        private Rectangle _win, _icon, _union;
        private IntPtr _srcDc, _srcBmp, _srcOld, _workDc, _workBmp, _workOld, _bits;
        private int _stride, _imgW, _imgH;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, int usage, out IntPtr bits, IntPtr sec, int off);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("ntdll.dll")] private static extern IntPtr memset(IntPtr dst, int c, UIntPtr n);

        private bool Genie { get { return _mode != Mode.Zoom; } }
        private readonly bool _pocket;
        private Bitmap _small, _gbmp;
        private Graphics _g;
        private string _promoDir, _promoPrefix;
        private int _promoFrame;
        private System.Text.StringBuilder _promoMeta;
        private IntPtr _prev;            // frame sebelumnya untuk motion blur
        private readonly bool _blur = false;   // motion blur dimatikan: memperlambat frame & membuat traffic light glitch

        // Motion blur: frame baru ditumpuk di atas jejak frame sebelumnya yang memudar 50%.
        private unsafe void MotionBlur()
        {
            if (!_blur) return;
            int n = _union.Width * _union.Height;
            if (_prev == IntPtr.Zero) { _prev = Marshal.AllocHGlobal(n * 4); memset(_prev, 0, (UIntPtr)(uint)(n * 4)); }
            uint* c = (uint*)_bits, p = (uint*)_prev;
            for (int i = 0; i < n; i++)
            {
                uint a = c[i], b = p[i];
                if (b != 0)
                {
                    uint inv = 255 - (a >> 24);
                    if (inv != 0)
                    {
                        uint f = inv * 118;   // ~0.46 x sisa transparansi
                        uint A = (a >> 24) + (((b >> 24) * f) >> 16), R = ((a >> 16) & 0xFF) + ((((b >> 16) & 0xFF) * f) >> 16);
                        uint G = ((a >> 8) & 0xFF) + ((((b >> 8) & 0xFF) * f) >> 16), B = (a & 0xFF) + (((b & 0xFF) * f) >> 16);
                        a = (Math.Min(255u, A) << 24) | (Math.Min(255u, R) << 16) | (Math.Min(255u, G) << 8) | Math.Min(255u, B);
                        c[i] = a;
                    }
                }
                p[i] = a;
            }
        }

        public AnimForm(Bitmap img, Rectangle from, Rectangle to, int ms, Mode mode, Action done)
        {
            _img = img; _from = from; _to = to; _ms = ms; _mode = mode; _done = done;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Rectangle scr = Screen.FromRectangle(from).Bounds;
            if (mode == Mode.Minimize) { _win = from; _icon = to; }
            else if (mode == Mode.Restore) { _win = to; _icon = from; }
            _pocket = Genie && WinAnim.PocketStyle;
            _union = Genie ? Rectangle.Union(_win, _icon) : scr;
            if (Genie && !_pocket) { Rectangle sw = _win; sw.Inflate(ShadowR, ShadowR); sw.Offset(0, ShadowDy); _union = Rectangle.Union(_union, sw); }
            _union.Inflate(2, 2);
            if (_pocket)
            {
                _union = scr;   // jendela berputar bebas di seluruh layar
            }
            _t.Interval = UseVsync ? 40 : 10;   // dengan pacu layar, timer hanya penjaga bila DWM tidak memberi sinyal
            _t.Tick += delegate { Step(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | 0x20 /* TRANSPARENT */;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private const double HoldMs = 90, FadeMs = 140;
        private long _doneAt;
        private static AnimForm _holding;   // animasi buka yang sedang memudar; dihentikan bila animasi baru mulai

        public static bool RaiseDock = true;

        // Saat laptop sibuk (mis. render video), thread UI biasa kalah giliran dan frame genie tersendat.
        // Selama animasi berjalan prioritasnya dinaikkan, lalu dikembalikan.
        private static ThreadPriority _prioOld; private static bool _boosted;
        private static void Boost(bool on)
        {
            if (NoBoost) return;
            try
            {
                if (on && !_boosted) { _prioOld = Thread.CurrentThread.Priority; Thread.CurrentThread.Priority = ThreadPriority.Highest; _boosted = true; }
                else if (!on && _boosted) { Thread.CurrentThread.Priority = _prioOld; _boosted = false; }
            }
            catch { }
        }
        // Selama animasi berjalan, tangkapan layar di area jendela berisi gambar animasi, bukan jendela aslinya.
        private static int _active;
        private bool _counted;
        public static bool TestBusy;
        public static bool Busy { get { return _active > 0 || TestBusy; } }

        public void Start()
        {
            if (_holding != null && _holding != this) { AnimForm old = _holding; _holding = null; if (!old.IsDisposed) old.Finish(); }
            _active++; _counted = true;
            Boost(true);
            Setup();
            Bounds = _union;
            if (Settings.Get("WindowAnimSound", true)) AnimSound.Play(_mode == Mode.Minimize ? AnimSound.Suck : _mode == Mode.Restore ? AnimSound.Pop : null);
            // Urutan lapisan diatur SEBELUM jendela tampil: tidak ada satu frame pun jendela animasi menutupi notch/bilah atas.
            // genie: tepat di bawah Dock (tersedot ke belakang ikon); lainnya: puncak topmost, lalu notch & bilah dinaikkan di atasnya.
            // 1) tampil dulu dalam keadaan KOSONG (jendela berlapis tanpa isi = tak terlihat)
            ShowWindow(Handle, 8 /* SW_SHOWNA */);
            // 2) atur lapisan: genie tepat di bawah Dock (tersedot ke balik ikon); lainnya di bawah bilah & notch
            IntPtr after = (Genie && !_pocket && WinAnim.DockHwnd != IntPtr.Zero) ? WinAnim.DockHwnd : new IntPtr(-1);
            // Dock bisa tenggelam di bawah jendela aplikasi; naikkan dulu supaya animasi yang menempel di bawahnya tidak tertutup aplikasi.
            if (after == WinAnim.DockHwnd && RaiseDock) SetWindowPos(WinAnim.DockHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            SetWindowPos(Handle, after, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            // bilah dulu, notch TERAKHIR: notch selalu di lapisan paling atas
            foreach (IntPtr k in WinAnim.KeepAbove) if (k != IntPtr.Zero) SetWindowPos(k, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            // 3) baru gambar frame pertama
            _sw.Start();
            Step();
            _t.Start();
            if (UseVsync) StartPacer();
        }

        // Pacu frame mengikuti refresh layar (DwmFlush di thread sendiri), jadi tidak tertahan resolusi timer Windows.
        // Animasi berbasis waktu, jadi durasinya tetap sama berapa pun FPS-nya.
        public static bool UseVsync = true;
        public int Frames { get { return _frames; } }
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        private volatile bool _pacing;
        private int _queued;

        private void StartPacer()
        {
            _pacing = true;
            Thread th = new Thread(delegate ()
            {
                Stopwatch s = new Stopwatch();
                while (_pacing)
                {
                    s.Restart();
                    int hr = DwmFlush();
                    if (!_pacing) break;
                    if (hr != 0 || s.ElapsedMilliseconds < 2) Thread.Sleep(4);   // DWM sedang tidak menyusun frame
                    if (Interlocked.CompareExchange(ref _queued, 1, 0) != 0) continue;   // frame sebelumnya belum selesai
                    try { BeginInvoke((MethodInvoker)delegate { _queued = 0; Step(); }); }
                    catch { break; }
                }
            });
            th.IsBackground = true; th.Priority = ThreadPriority.AboveNormal;
            th.Start();
        }

        // Render offline untuk video promo: frame disimpan sebagai PNG (alfa premultiplied).
        public void PromoRender(string dir, string prefix, int fps)
        {
            _promoDir = dir; _promoPrefix = prefix; _promoMeta = new System.Text.StringBuilder();
            _promoMeta.AppendLine("union " + _union.X + " " + _union.Y + " " + _union.Width + " " + _union.Height);
            Setup();
            int n = Math.Max(2, (int)Math.Ceiling(_ms / 1000.0 * fps));
            for (int i = 0; i <= n; i++) Draw(Math.Min(1.0, i / (double)n));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, prefix + ".txt"), _promoMeta.ToString());
            Finish();
        }

        private void Setup()
        {
            if (Genie && !_pocket && _shadow == null) BuildShadow();   // sebelum jam animasi berjalan: frame pertama tidak tertahan
            IntPtr scr = GetDC(IntPtr.Zero);
            _srcDc = CreateCompatibleDC(scr);
            _srcBmp = _img.GetHbitmap(Color.Black);
            _imgW = _img.Width; _imgH = _img.Height;   // setelah ini gambar asli tidak dipakai lagi (aman bila dibuang)
            _srcOld = SelectObject(_srcDc, _srcBmp);
            BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)); bi.biWidth = _union.Width; bi.biHeight = -_union.Height;   // top-down
            bi.biPlanes = 1; bi.biBitCount = 32;
            _workDc = CreateCompatibleDC(scr);
            _workBmp = CreateDIBSection(scr, ref bi, 0, out _bits, IntPtr.Zero, 0);
            _workOld = SelectObject(_workDc, _workBmp);
            _stride = _union.Width * 4;
            SetStretchBltMode(_workDc, 3 /* COLORONCOLOR */);
            ReleaseDC(IntPtr.Zero, scr);
            if (_pocket || _promoDir != null)
            {
                _gbmp = new Bitmap(_union.Width, _union.Height, _stride, PixelFormat.Format32bppPArgb, _bits);
                _g = Graphics.FromImage(_gbmp);
            }
            if (_pocket)
            {
                double sc = Math.Min(1.0, 1100.0 / Math.Max(_imgW, _imgH));
                _small = new Bitmap(Math.Max(1, (int)(_imgW * sc)), Math.Max(1, (int)(_imgH * sc)), PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(_small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(_img, 0, 0, _small.Width, _small.Height); }
            }
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        private static double Smooth(double v) { v = Clamp01(v); return v * v * (3 - 2 * v); }
        private static double Smoother(double v) { v = Clamp01(v); return v * v * v * (v * (v * 6 - 15) + 10); }
        private static double Lerp(double a, double b, double k) { return a + (b - a) * k; }

        private void Step()
        {
            if (IsDisposed) return;
            if (_finished)
            {
                // Jendela asli sudah muncul di bawah: tahan gambar terakhir sampai aplikasi selesai menggambar, lalu pudarkan.
                long now = _sw.ElapsedMilliseconds;
                if (_mode == Mode.Restore && Target != IntPtr.Zero && _readyAt < 0)
                {
                    // Tunggu jendela asli benar-benar terbuka dan di depan. Memudar lebih awal memperlihatkan jendela lain
                    // di baliknya sehingga aplikasi tampak "refresh".
                    bool up = !Native.IsWindow(Target) || (!Native.IsIconic(Target) && Native.GetForegroundWindow() == Target);
                    if (!up && now - _doneAt < 700) return;
                    _readyAt = now;
                    Rectangle rf = Native.IsWindow(Target) ? WinAnim.Frame(Target) : _to;
                    _mismatch = Math.Abs(rf.Left - _to.Left) > 3 || Math.Abs(rf.Top - _to.Top) > 3 || Math.Abs(rf.Width - _to.Width) > 3 || Math.Abs(rf.Height - _to.Height) > 3;
                    if (System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug"))) Log.Write("anim restore siap: " + (now - _doneAt) + " ms, up=" + up + ", gambar=" + _to + ", jendela=" + rf);
                }
                // maximize/restore: tahan gambar akhir sampai jendela asli mencapai ukuran barunya dan selesai menggambar,
                // lalu lepas sekaligus (tanpa pudar)
                if (_mode == Mode.Zoom && Target != IntPtr.Zero)
                {
                    if (_readyAt < 0)
                    {
                        Rectangle rz = Native.IsWindow(Target) ? WinAnim.Frame(Target) : _to;
                        bool there = Math.Abs(rz.Left - _to.Left) <= 8 && Math.Abs(rz.Top - _to.Top) <= 8 && Math.Abs(rz.Right - _to.Right) <= 8 && Math.Abs(rz.Bottom - _to.Bottom) <= 8;
                        if (!there && now - _doneAt < 450) return;
                        _readyAt = now;
                        if (System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug")) || PerfLog) Log.Write("anim zoom siap: " + (now - _doneAt) + " ms, cocok=" + there + ", gambar=" + _to + ", jendela=" + rz);
                    }
                    if (now - _readyAt < HoldMs) return;
                    Finish(); return;
                }
                // jendela kini berbeda posisi/ukuran dari gambar lama: langsung hilangkan, jangan dipudarkan (bayangan ganda)
                if (_mismatch) { Finish(); return; }
                double e = now - (_readyAt >= 0 ? _readyAt : _doneAt);
                if (e < HoldMs) return;
                // Buka jendela asli: gambar lama dilepas seketika, TIDAK dipudarkan. Isi jendela hampir selalu sedikit beda
                // (bergeser beberapa px / halaman sudah berubah), sehingga crossfade tampak teks dobel = efek "refresh".
                if (_mode == Mode.Restore && Target != IntPtr.Zero) { Finish(); return; }
                double fade = (e - HoldMs) / FadeMs;
                if (fade >= 1) { Finish(); return; }
                Present(1 - Smooth(fade));
                return;
            }
            if (_mode == Mode.Zoom && Follow != IntPtr.Zero)
            {
                Rectangle f = WinAnim.Frame(Follow);
                if (f != _from && f.Width > 0) _to = f;
            }
            _frames++;
            double t = Math.Min(1.0, _sw.ElapsedMilliseconds / (double)_ms);
            long pf0 = _sw.ElapsedTicks;
            if (_pfLast > 0) _pfGap = Math.Max(_pfGap, (pf0 - _pfLast) * 1000.0 / Stopwatch.Frequency);
            _pfLast = pf0;
            try { Draw(t); } catch (Exception ex) { Log.Write("anim draw: " + ex.Message); t = 1; }
            double pfMs = (_sw.ElapsedTicks - pf0) * 1000.0 / Stopwatch.Frequency; _pfSum += pfMs; _pfMax = Math.Max(_pfMax, pfMs);
            if (t >= 1)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(Log.Dir, "debug")) || PerfLog) Log.Write("anim " + _mode + " selesai: " + _frames + " frame, draw rata " + (_pfSum / Math.Max(1, _frames)).ToString("0.0") + " ms, maks " + _pfMax.ToString("0.0") + " ms, jeda maks " + _pfGap.ToString("0") + " ms, union " + _union.Width + "x" + _union.Height);
                _finished = true; _doneAt = _sw.ElapsedMilliseconds;
                bool zoomHold = _mode == Mode.Zoom && Target != IntPtr.Zero;
                if (_mode == Mode.Restore || zoomHold) _holding = this;
                if (_done != null) _done();
                if (_mode != Mode.Restore && !zoomHold) Finish();
            }
        }

        // Baris yang digambar frame ini (alfa diisi setelah semua baris disalin, cukup satu GdiFlush).
        private int _nRows;
        private int[] _ry = new int[4096];
        private double[] _rl = new double[4096], _rr = new double[4096];

        // Satu baris tujuan: salin baris sumber ke [l, r).
        private void Row(int y, double l, double r, int sy)
        {
            int yy = y - _union.Top;
            if (yy < 0 || yy >= _union.Height || r - l < 0.5 || _nRows >= _ry.Length) return;
            int x0 = (int)Math.Floor(l), x1 = (int)Math.Ceiling(r);
            if (Math.Min(_union.Width, x1 - _union.Left) <= Math.Max(0, x0 - _union.Left)) return;
            StretchBlt(_workDc, x0 - _union.Left, yy, x1 - x0, 1, _srcDc, 0, sy, _imgW, 1, 0x00CC0020);
            _ry[_nRows] = yy; _rl[_nRows] = l; _rr[_nRows] = r; _nRows++;
            Grow(Math.Max(0, x0 - _union.Left), yy, Math.Min(_union.Width, x1 - _union.Left), yy + 1);
        }

        // Isi alfa semua baris; tepi diberi alfa pecahan (anti-alias).
        private unsafe void FillAlpha()
        {
            GdiFlush();
            for (int i = 0; i < _nRows; i++) AlphaRow(_ry[i], _rl[i], _rr[i]);
            _nRows = 0;
        }

        private unsafe void AlphaRow(int yy, double l, double r)
        {
            int x0 = (int)Math.Floor(l), x1 = (int)Math.Ceiling(r);
            int dx0 = Math.Max(0, x0 - _union.Left), dx1 = Math.Min(_union.Width, x1 - _union.Left);
            uint* p = (uint*)((byte*)_bits + yy * _stride);
            for (int x = dx0; x < dx1; x++) p[x] |= 0xFF000000;
            double covL = 1 - (l - x0), covR = 1 - (x1 - r);
            int ix0 = x0 - _union.Left, ix1 = x1 - 1 - _union.Left;
            if (ix0 >= 0 && ix0 < _union.Width) Scale(p + ix0, ix0 == ix1 ? covL * covR : covL);
            if (ix1 != ix0 && ix1 >= 0 && ix1 < _union.Width) Scale(p + ix1, covR);
        }

        private static unsafe void Scale(uint* px, double a)
        {
            if (a >= 0.999) return;
            uint c = *px; double k = Math.Max(0, a);
            uint A = (uint)((c >> 24) * k), R = (uint)(((c >> 16) & 0xFF) * k), G = (uint)(((c >> 8) & 0xFF) * k), B = (uint)((c & 0xFF) * k);
            *px = (A << 24) | (R << 16) | (G << 8) | B;
        }

        // Hanya area yang digambar frame sebelumnya yang dibersihkan, dan hanya area yang berubah yang dikirim ke DWM
        // (dulu seluruh kanvas seukuran layar, ~8 MB per frame, membuat frame tersendat).
        private Rectangle _box, _prevBox;   // koordinat lokal kanvas
        private bool _ulwInit, _fullDirty = true;
        private static readonly bool NoDirty = Environment.GetEnvironmentVariable("LM_NODIRTY") != null, NoBoost = Environment.GetEnvironmentVariable("LM_NOBOOST") != null;
        private void Grow(int l, int t, int r, int bt)
        {
            Rectangle a = Rectangle.FromLTRB(l, t, r, bt);
            _box = _box.IsEmpty ? a : Rectangle.Union(_box, a);
        }
        private void Clear()
        {
            if (_fullDirty || _pocket || _promoDir != null || NoDirty) { memset(_bits, 0, (UIntPtr)(uint)(_stride * _union.Height)); _prevBox = new Rectangle(0, 0, _union.Width, _union.Height); }
            else
            {
                Rectangle c = Rectangle.Intersect(_box, new Rectangle(0, 0, _union.Width, _union.Height));
                for (int y = c.Top; y < c.Bottom; y++) memset((IntPtr)((long)_bits + (long)y * _stride + c.Left * 4), 0, (UIntPtr)(uint)(c.Width * 4));
                _prevBox = c;
            }
            _fullDirty = false;
            _box = Rectangle.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ULWINFO { public int cbSize; public IntPtr hdcDst; public IntPtr pptDst; public IntPtr psize; public IntPtr hdcSrc; public IntPtr pptSrc; public int crKey; public IntPtr pblend; public int dwFlags; public IntPtr prcDirty; }
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindowIndirect(IntPtr h, ref ULWINFO info);

        // ---------- bayangan jendela ----------
        private static int ShadowR { get { return Ui.P(22); } }
        private static int ShadowDy { get { return Ui.P(5); } }
        private byte[] _shadow; private int _shW, _shH;

        // Alfa bayangan hitam di luar persegi jendela: jatuh lembut dari tepi, sedikit turun ke bawah. 0 di dalam jendela.
        private void BuildShadow()
        {
            int R = ShadowR, dy = ShadowDy;
            _shW = _win.Width + 2 * R; _shH = _win.Height + 2 * R + dy;
            _shadow = new byte[_shW * _shH];
            double sig = R * 0.45;
            for (int y = 0; y < _shH; y++)
            {
                double py = y - R + 0.5;                                  // relatif ke tepi atas jendela
                double qy = Math.Max(Math.Max(dy - py, py - (_win.Height + dy)), 0);   // jarak ke persegi bayangan (bergeser dy)
                bool inRows = py >= 0 && py < _win.Height;
                for (int x = 0; x < _shW; x++)
                {
                    double px = x - R + 0.5;
                    if (inRows && px >= 0 && px < _win.Width) { x = R + _win.Width - 1; continue; }   // di dalam jendela: tertutup gambar
                    double qx = Math.Max(Math.Max(-px, px - _win.Width), 0);
                    double d = Math.Sqrt(qx * qx + qy * qy);
                    if (d > R) continue;
                    double a = 0.30 * Math.Exp(-(d * d) / (2 * sig * sig)) + 0.10 * Math.Exp(-d / 2.0);
                    _shadow[y * _shW + x] = (byte)Math.Min(255, a * 255);
                }
            }
        }

        private unsafe void DrawShadow(double k)
        {
            if (_shadow == null) BuildShadow();
            int ox = _win.Left - ShadowR - _union.Left, oy = _win.Top - ShadowR - _union.Top;
            Grow(Math.Max(0, ox), Math.Max(0, oy), Math.Min(_union.Width, ox + _shW), Math.Min(_union.Height, oy + _shH));
            int ka = (int)(k * 256);
            for (int y = 0; y < _shH; y++)
            {
                int yy = oy + y;
                if (yy < 0 || yy >= _union.Height) continue;
                uint* row = (uint*)((byte*)_bits + yy * _stride);
                int baseI = y * _shW;
                for (int x = 0; x < _shW; x++)
                {
                    int a = _shadow[baseI + x];
                    if (a == 0) continue;
                    int xx = ox + x;
                    if (xx < 0 || xx >= _union.Width) continue;
                    row[xx] = (uint)((a * ka) >> 8) << 24;   // hitam premultiplied
                }
            }
        }

        // ---------- Kantong Ajaib ----------
        private void DrawPocket(double t)
        {
            double p = _mode == Mode.Minimize ? t : 1 - t;
            Rectangle S = _win, T = _icon;
            float ox = _union.Left, oy = _union.Top;
            float ix = T.X + T.Width / 2f - ox, iy = T.Y + T.Height * 0.30f - oy;
            double open = p < 0.16 ? Smooth(p / 0.16) : p > 0.88 ? 1 - Smooth((p - 0.88) / 0.12) : 1;
            float pop = (float)(open < 1 ? open * (1 + 0.25 * Math.Sin(open * Math.PI)) : 1);   // muncul sedikit memantul
            float pw = T.Width * 1.18f * (0.75f + 0.25f * pop), mh = T.Height * 0.34f * pop, bh = T.Height * 0.66f * (0.75f + 0.25f * pop);   // kecil, menempel di ikon
            Graphics g = _g;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.Bilinear; g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            g.ResetTransform(); g.ResetClip();
            int pa = (int)(255 * Math.Min(1, open * 1.4));
            RectangleF mouth = new RectangleF(ix - pw * 0.43f, iy - mh / 2f, pw * 0.86f, Math.Max(1, mh));
            if (open > 0.01)
            {
                // bayangan & mulut kantong (bagian dalam gelap)
                using (GraphicsPath mp = new GraphicsPath())
                {
                    mp.AddEllipse(mouth);
                    using (PathGradientBrush mb = new PathGradientBrush(mp)) { mb.CenterColor = Color.FromArgb(pa, 6, 4, 14); mb.SurroundColors = new Color[] { Color.FromArgb(pa, 46, 38, 70) }; g.FillPath(mb, mp); }
                }
            }
            // jendela berputar spiral masuk ke mulut kantong
            double q = Smoother(Clamp01((p - 0.06) / 0.84));
            float sc = (float)Lerp(1, 0.03, Math.Pow(q, 0.55));
            float sx = S.X + S.Width / 2f - ox, sy = S.Y + S.Height / 2f - oy;
            double vx = sx - ix, vy = sy - iy, ang = q * Math.PI * 0.85, rad = Math.Pow(1 - q, 1.5);
            float px = ix + (float)((vx * Math.Cos(ang) - vy * Math.Sin(ang)) * rad);
            float py = iy + (float)((vx * Math.Sin(ang) + vy * Math.Cos(ang)) * rad);
            py -= (float)(Math.Sin(q * Math.PI) * T.Height * 1.2);   // melengkung naik sebelum jatuh ke kantong
            float rot = (float)(q * q * 540);
            float wa = q > 0.9 ? (float)(1 - (q - 0.9) / 0.1) : 1f;
            if (sc > 0.002 && wa > 0.01)
            {
                if (sc < 0.3f)
                {
                    using (Region clip = new Region(new RectangleF(0, 0, _union.Width, _union.Height)))
                    {
                        clip.Exclude(new RectangleF(ix - pw * 0.45f, iy, pw * 0.9f, _union.Height));   // masuk ke dalam kantong: bagian di bawah bibir tersembunyi
                        g.Clip = clip;
                    }
                }
                g.TranslateTransform(px, py); g.RotateTransform(rot); g.ScaleTransform(sc, sc);
                RectangleF dst = new RectangleF(-S.Width / 2f, -S.Height / 2f, S.Width, S.Height);
                if (wa >= 0.99f) g.DrawImage(_small, dst);
                else using (ImageAttributes ia = new ImageAttributes())
                    {
                        ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = wa; ia.SetColorMatrix(cm);
                        g.DrawImage(_small, new PointF[] { new PointF(dst.Left, dst.Top), new PointF(dst.Right, dst.Top), new PointF(dst.Left, dst.Bottom) }, new RectangleF(0, 0, _small.Width, _small.Height), GraphicsUnit.Pixel, ia);
                    }
                g.ResetTransform(); g.ResetClip();
            }
            if (open > 0.01)
            {
                // badan kantong putih di depan (setengah elips), bibir = setengah bawah mulut
                using (GraphicsPath body = new GraphicsPath())
                {
                    body.AddArc(mouth, 180, -180);
                    body.AddArc(new RectangleF(ix - pw / 2f, iy - bh, pw, bh * 2), 0, 180);
                    body.CloseFigure();
                    using (LinearGradientBrush bb = new LinearGradientBrush(new RectangleF(ix - pw / 2f, iy - 1, pw, bh + 2), Color.FromArgb(pa * 235 / 255, 255, 255, 255), Color.FromArgb(pa * 215 / 255, 220, 224, 236), 90f)) g.FillPath(bb, body);
                    using (Pen bp = new Pen(Color.FromArgb(pa * 70 / 255, 30, 30, 50), 1f)) g.DrawPath(bp, body);
                    // jahitan kantong
                    using (Pen st = new Pen(Color.FromArgb(pa * 110 / 255, 120, 125, 145), Math.Max(1f, T.Width / 45f)))
                    {
                        st.DashPattern = new float[] { 3f, 2.2f };
                        RectangleF inner = new RectangleF(ix - pw * 0.40f, iy - bh * 0.80f, pw * 0.80f, bh * 1.60f);
                        g.DrawArc(st, inner, 20, 140);
                    }
                }
                using (Pen lip = new Pen(Color.FromArgb(pa * 120 / 255, 255, 255, 255), Math.Max(1f, T.Width / 50f))) g.DrawArc(lip, mouth, 200, 140);
            }
            g.Flush();
        }

        private void Draw(double t)
        {
            Clear();
            double alpha = 1;
            if (_pocket) { DrawPocket(t); GdiFlush(); MotionBlur(); Present(1); return; }
            if (Genie)
            {
                double p = _mode == Mode.Minimize ? t : 1 - t;
                p = Smoother(p) * 0.7 + p * 0.3;
                // bayangan jendela (seperti bayangan DWM): penuh saat genie = jendela utuh, hilang di 25% awal.
                // Tanpa ini bayangan asli Windows hilang/muncul seketika di awal minimize / akhir buka (tampak berkedip).
                double sk = 1 - Smooth(p / 0.18);
                if (sk > 0.01) DrawShadow(sk);
                Rectangle S = _win, T = _icon;
                double A = Smooth(p / 0.55);                       // corong terbentuk
                double B = Smooth((p - 0.28) / 0.72);              // jendela meluncur masuk
                double top = Lerp(S.Top, T.Top, B);
                double bottom = Lerp(S.Bottom, T.Bottom, Smooth(p / 0.62));
                if (bottom - top >= 1)
                {
                    double cs = Lerp(S.Bottom, S.Top - S.Height * 0.25, A);
                    double ce = Math.Max(cs + 1, T.Top);
                    double taper = Math.Max(2, S.Height * 0.32 * A + T.Height * 0.6);   // panjang ujung yang meruncing
                    int y0 = (int)Math.Floor(top), y1 = (int)Math.Ceiling(bottom);
                    double span = bottom - top;
                    for (int y = y0; y < y1; y++)
                    {
                        double ym = y + 0.5;
                        double k = ym <= cs ? 0 : Smooth((ym - cs) / (ce - cs));
                        double l = Lerp(S.Left, T.Left, k), r = Lerp(S.Right, T.Right, k);
                        double c = (l + r) / 2, hw = (r - l) / 2;
                        hw *= 1 - 0.9 * Smooth((ym - T.Top) / Math.Max(1.0, T.Height));                 // makin sempit di dalam ikon
                        double lead = Smooth((bottom - ym) / taper);
                        hw *= Lerp(1, 0.05 + 0.95 * Math.Sqrt(lead), Math.Min(1, A * 1.6));               // ujung depan meruncing (bukan datar)
                        int sy = (int)Math.Min(_imgH - 1, Math.Max(0, (ym - top) / span * _imgH));
                        Row(y, c - hw, c + hw, sy);
                    }
                }
                alpha = p > 0.72 ? 1 - 0.92 * Smooth((p - 0.72) / 0.28) : 1;
            }
            else
            {
                Rectangle to = _to.IsEmpty ? _from : _to;
                double k = 1 - Math.Pow(1 - t, 3);
                alpha = Target != IntPtr.Zero ? 1 : 1 - Smooth((t - 0.45) / 0.55);   // cara baru: tetap utuh, dilepas setelah jendela siap
                double x = Lerp(_from.X, to.X, k), y = Lerp(_from.Y, to.Y, k), w = Lerp(_from.Width, to.Width, k), h = Lerp(_from.Height, to.Height, k);
                int ya = (int)Math.Floor(y), yb = (int)Math.Ceiling(y + h);
                for (int yy = ya; yy < yb; yy++)
                {
                    int sy = (int)Math.Min(_imgH - 1, Math.Max(0, (yy + 0.5 - y) / h * _imgH));
                    Row(yy, x, x + w, sy);
                }
            }
            FillAlpha();
            if (Genie) MotionBlur();
            Present(alpha);
        }

        private void Present(double alpha)
        {
            if (_promoDir != null)
            {
                GdiFlush();
                string fn = _promoPrefix + "_" + _promoFrame.ToString("0000") + ".png";
                _gbmp.Save(System.IO.Path.Combine(_promoDir, fn), ImageFormat.Png);
                _promoMeta.AppendLine("f " + _promoFrame + " " + alpha.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                _promoFrame++;
                return;
            }
            IntPtr scr = GetDC(IntPtr.Zero);
            try
            {
                Native.SIZE size = new Native.SIZE(_union.Width, _union.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.POINT dst = new Native.POINT(_union.Left, _union.Top);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)(alpha * 255)));
                bf.AlphaFormat = 1;   // AC_SRC_ALPHA: alfa per piksel (premultiplied)
                Rectangle dirty = _prevBox.IsEmpty ? _box : _box.IsEmpty ? _prevBox : Rectangle.Union(_prevBox, _box);
                if (!_ulwInit || _pocket || dirty.IsEmpty || NoDirty)
                {
                    Native.UpdateLayeredWindow(Handle, scr, ref dst, ref size, _workDc, ref src, 0, ref bf, Native.ULW_ALPHA);
                    _ulwInit = true;
                }
                else
                {
                    unsafe
                    {
                        Native.RECT rc = new Native.RECT(); rc.Left = dirty.Left; rc.Top = dirty.Top; rc.Right = dirty.Right; rc.Bottom = dirty.Bottom;
                        ULWINFO u = new ULWINFO();
                        u.cbSize = Marshal.SizeOf(typeof(ULWINFO)); u.hdcDst = scr; u.pptDst = (IntPtr)(&dst); u.psize = (IntPtr)(&size);
                        u.hdcSrc = _workDc; u.pptSrc = (IntPtr)(&src); u.pblend = (IntPtr)(&bf); u.dwFlags = Native.ULW_ALPHA; u.prcDirty = (IntPtr)(&rc);
                        if (!UpdateLayeredWindowIndirect(Handle, ref u)) Native.UpdateLayeredWindow(Handle, scr, ref dst, ref size, _workDc, ref src, 0, ref bf, Native.ULW_ALPHA);
                    }
                }
            }
            finally { ReleaseDC(IntPtr.Zero, scr); }
        }

        private void Finish()
        {
            if (IsDisposed) return;
            if (_holding == this) _holding = null;
            if (_counted) { _counted = false; _active--; }
            if (_active == 0) Boost(false);
            _pacing = false;
            _t.Stop();
            if (IsHandleCreated) ShowWindow(Handle, 0 /* SW_HIDE */);
            if (_g != null) { _g.Dispose(); _g = null; }
            if (_gbmp != null) { _gbmp.Dispose(); _gbmp = null; }
            if (_prev != IntPtr.Zero) { Marshal.FreeHGlobal(_prev); _prev = IntPtr.Zero; }
            if (_workDc != IntPtr.Zero) { SelectObject(_workDc, _workOld); DeleteObject(_workBmp); DeleteDC(_workDc); _workDc = IntPtr.Zero; }
            if (_srcDc != IntPtr.Zero) { SelectObject(_srcDc, _srcOld); DeleteObject(_srcBmp); DeleteDC(_srcDc); _srcDc = IntPtr.Zero; }
            if (_mode != Mode.Minimize && _img != null) _img.Dispose();
            if (_g != null) { _g.Dispose(); _g = null; }
            if (_gbmp != null) { _gbmp.Dispose(); _gbmp = null; }
            if (_small != null) { _small.Dispose(); _small = null; }
            Close(); Dispose();
            MemoryTrim.Soon();
        }
    }

    // Suara "tersedot" (minimize) dan "keluar" (buka dari Dock): disintesis, diputar dari memori.
    internal static class AnimSound
    {
        [DllImport("winmm.dll")] private static extern bool PlaySound(byte[] data, IntPtr mod, uint flags);
        private static byte[] _suck, _pop;
        public static byte[] Suck { get { if (_suck == null) _suck = Make(true); return _suck; } }
        public static byte[] Pop { get { if (_pop == null) _pop = Make(false); return _pop; } }

        public static void Play(byte[] wav)
        {
            if (wav == null) return;
            try { PlaySound(wav, IntPtr.Zero, 0x1 | 0x2 | 0x4 /* ASYNC|NODEFAULT|MEMORY */); } catch { }
        }

        // Bunyi "gelembung" lembut seperti di Dynamic Island: masuk = nada meluncur turun, keluar = naik.
        private static byte[] Make(bool suck)
        {
            const int rate = 44100;
            double dur = 0.24;
            int n = (int)(rate * dur);
            short[] smp = new short[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double g = 1 - Math.Exp(-t / 0.045);
                double f = suck ? 560 - 260 * g : 300 + 260 * g;   // lebih rendah dari bunyi island
                ph += 2 * Math.PI * f / rate;
                double env = Math.Min(1, t / 0.012) * Math.Exp(-t / 0.065);
                double v = Math.Sin(ph) * 0.92 + Math.Sin(ph * 1.5) * 0.06;
                smp[i] = (short)(v * env * 0.055 * 32767);
            }
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(ms))
            {
                int bytes = n * 2;
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
                foreach (short v in smp) bw.Write(v);
                bw.Flush();
                return ms.ToArray();
            }
        }

        private static double L(double a, double b, double k) { return a + (b - a) * Math.Max(0, Math.Min(1, k)); }
    }
}
