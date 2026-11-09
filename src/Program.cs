// LiquidMac v2 - menu bar, notch, dan Dock gaya macOS (Liquid Glass) untuk Windows 11.
// Dikompilasi dengan csc .NET Framework 4 (C# 5) lewat build.bat - tanpa dependensi luar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiquidMac
{
    // ============================================================
    //  NATIVE / P-INVOKE
    // ============================================================
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; public SIZE(int w, int h) { cx = w; cy = h; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const int WCA_ACCENT_POLICY = 19;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        public const int ULW_ALPHA = 2;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int GWL_EXSTYLE = -20;
        public const int GW_OWNER = 4;
        public const int SW_RESTORE = 9, SW_MINIMIZE = 6, SW_MAXIMIZE = 3;
        public const int WM_CLOSE = 0x0010;
        public const int SPI_SETDESKWALLPAPER = 0x0014;
        public const int SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;
        public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        public const uint ABE_TOP = 1, ABE_BOTTOM = 3;
        public const int ABN_POSCHANGED = 1;
        public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
        public const uint SHGFI_SYSICONINDEX = 0x4000;
        public const int SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;
        public const int ILD_TRANSPARENT = 1;
        public const int KEYEVENTF_KEYUP = 2;
        public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
        [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO sfi, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "#727")] public static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        public static void EnableAcrylic(IntPtr hwnd, int tint)
        {
            AccentPolicy accent = new AccentPolicy();
            accent.AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND;
            accent.AccentFlags = 2;
            accent.GradientColor = tint; // ABGR
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                WindowCompositionAttributeData data = new WindowCompositionAttributeData();
                data.Attribute = WCA_ACCENT_POLICY; data.Data = ptr; data.SizeOfData = size;
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ProcessPath(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static void Keys(params byte[] vks)
        {
            for (int i = 0; i < vks.Length; i++) keybd_event(vks[i], 0, 0, IntPtr.Zero);
            for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        }

        public const byte VK_LWIN = 0x5B, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_TAB = 0x09, VK_F11 = 0x7A;
        public const byte VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY = 0xB3;
    }

    // ============================================================
    //  UI SCALE, FONT, LOG, SETTINGS
    // ============================================================
    internal static class Ui
    {
        public static float S = 1f;
        public static string Family = "Segoe UI";

        public static void Init()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f; } catch { S = 1f; }
            if (S < 1f) S = 1f;
            try
            {
                using (InstalledFontCollection fc = new InstalledFontCollection())
                {
                    string[] prefer = Settings.Get("MacFont", false) ? new string[] { "SF Pro Text", "SF Pro Display", "Inter", "Segoe UI" } : new string[] { "SF Pro Text", "SF Pro Display", "Segoe UI" };
                    foreach (string p in prefer)
                        foreach (FontFamily f in fc.Families)
                            if (string.Equals(f.Name, p, StringComparison.OrdinalIgnoreCase)) { Family = f.Name; return; }
                }
            }
            catch { }
        }

        public static int P(double v) { return (int)Math.Round(v * S); }
        public static float Pf(double v) { return (float)(v * S); }
        // Ukuran dalam piksel bulat + Segoe UI Semibold untuk tebal: teks lebih tajam (hinting) daripada ukuran point pecahan.
        public static Font F(float pt, FontStyle st)
        {
            float px = (float)Math.Round(pt * 96f / 72f * S);
            if ((st & FontStyle.Bold) != 0 && (Family == "Segoe UI" || Family == "Inter"))
            {
                try { return new Font(Family == "Inter" ? "Inter Semi Bold" : "Segoe UI Semibold", px, st & ~FontStyle.Bold, GraphicsUnit.Pixel); } catch { }
            }
            return new Font(Family, px, st, GraphicsUnit.Pixel);
        }
        public static float R(double v) { return (float)Math.Round(v); }
    }

    internal static class Log
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiquidMac");
        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class Settings
    {
        private static readonly string FilePath = Path.Combine(Log.Dir, "settings.ini");
        private static readonly Dictionary<string, string> _v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) _v[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Write("settings load: " + ex.Message); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in _v) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception ex) { Log.Write("settings save: " + ex.Message); }
        }

        public static bool Get(string k, bool def)
        {
            string s;
            if (_v.TryGetValue(k, out s)) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            return def;
        }
        public static string GetS(string k, string def) { string s; return _v.TryGetValue(k, out s) ? s : def; }
        public static void Set(string k, bool v) { _v[k] = v ? "1" : "0"; Save(); }
        public static void SetS(string k, string v) { _v[k] = v ?? ""; Save(); }
    }

    // ============================================================
    //  PAINT HELPERS
    // ============================================================
    internal static class Glass
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width < 0.5f || r.Height < 0.5f) return p;
            if (radius <= 0) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath Round(Rectangle r, int radius) { return Round((RectangleF)r, (float)radius); }

        // Bentuk notch: atas rata, sudut bawah membulat, plus "telinga" cekung di kiri-kanan atas.
        public static GraphicsPath NotchPath(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(r * 2, Math.Min(w, h));
            float e = Math.Min(Ui.Pf(8), h / 2);
            p.AddArc(x - e * 2, y, e * 2, e * 2, 270, 90);              // telinga kiri (cekung)
            p.AddLine(x, y + e, x, y + h - d / 2);
            p.AddArc(x, y + h - d, d, d, 180, -90);
            p.AddLine(x + d / 2, y + h, x + w - d / 2, y + h);
            p.AddArc(x + w - d, y + h - d, d, d, 90, -90);
            p.AddLine(x + w, y + h - d / 2, x + w, y + e);
            p.AddArc(x + w, y, e * 2, e * 2, 180, 90);                  // telinga kanan (cekung)
            p.CloseFigure();
            return p;
        }

        // Panel kaca: isian translusen + kilau atas + tepi terang (rim light) ala Liquid Glass.
        public static void GlassPanel(Graphics g, RectangleF r, float radius, int baseAlpha)
        {
            using (GraphicsPath path = Round(r, radius))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                    Color.FromArgb(baseAlpha, 40, 40, 52), Color.FromArgb(baseAlpha, 14, 14, 22), 90f))
                    g.FillPath(b, path);

                Region old = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (LinearGradientBrush sheen = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height / 2 + 2),
                    Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(sheen, r.X, r.Y, r.Width, r.Height / 2);
                g.Clip = old;
                old.Dispose();

                using (LinearGradientBrush rim = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                    Color.FromArgb(120, 255, 255, 255), Color.FromArgb(30, 255, 255, 255), 90f))
                using (Pen pen = new Pen(rim, Math.Max(1f, Ui.Pf(1))))
                    g.DrawPath(pen, path);
            }
        }

        public static void Shadow(Graphics g, RectangleF r, float radius, int strength)
        {
            for (int i = 6; i >= 1; i--)
            {
                float grow = Ui.Pf(i * 2);
                RectangleF sr = new RectangleF(r.X - grow / 2, r.Y - grow / 4 + Ui.Pf(3), r.Width + grow, r.Height + grow / 2);
                using (GraphicsPath sp = Round(sr, radius + grow / 2))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(Math.Max(0, strength / (i + 2)), 0, 0, 0)))
                    g.FillPath(sb, sp);
            }
        }

        public static void Pill(Graphics g, RectangleF r, int alpha)
        {
            using (GraphicsPath p = Round(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 28, 28, 36))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(alpha / 3, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
        }
    }

    // Tema menu gelap untuk ContextMenuStrip (menu bar, Dock, tray).
    internal sealed class DarkColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(34, 34, 42), Hi = Color.FromArgb(10, 120, 255), Line = Color.FromArgb(70, 70, 82);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Hi; } }
        public override Color MenuItemSelected { get { return Hi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Bg; } }
        public override Color CheckBackground { get { return Color.FromArgb(60, 60, 72); } }
        public override Color CheckSelectedBackground { get { return Hi; } }
        public override Color CheckPressedBackground { get { return Hi; } }
    }

    internal static class Menus
    {
        public static ContextMenuStrip Create()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            m.ForeColor = Color.FromArgb(240, 240, 245);
            m.BackColor = Color.FromArgb(34, 34, 42);
            m.Font = Ui.F(9f, FontStyle.Regular);
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            AutoClose(m);
            return m;
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

        // Menu dibuka dari jendela tanpa fokus (bilah atas, Dock) tidak menutup sendiri saat klik di luar.
        // Pantau tombol mouse & Esc selama menu terbuka; klik di luar semua menu/submenu -> tutup.
        private static void AutoClose(ContextMenuStrip m)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 30;
            bool armed = false;
            t.Tick += delegate
            {
                if (!m.Visible) { t.Stop(); return; }
                bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;
                if (!armed) { if (!down) armed = true; return; }       // abaikan klik yang membuka menu
                if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { t.Stop(); m.Close(ToolStripDropDownCloseReason.Keyboard); return; }
                if (down && !Inside(m, Cursor.Position)) { t.Stop(); m.Close(ToolStripDropDownCloseReason.AppClicked); }
            };
            m.Opened += delegate { armed = false; t.Start(); };
            m.Closed += delegate { t.Stop(); };
        }

        private static bool Inside(ToolStripDropDown d, Point p)
        {
            if (d == null || !d.Visible) return false;
            if (d.Bounds.Contains(p)) return true;
            foreach (ToolStripItem it in d.Items)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems && mi.DropDown.Visible && Inside(mi.DropDown, p)) return true;
            }
            return false;
        }

        public static ToolStripMenuItem Add(ContextMenuStrip m, string text, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.ForeColor = Color.FromArgb(240, 240, 245);
            if (click != null) it.Click += click; else it.Enabled = false;
            m.Items.Add(it);
            return it;
        }

        public static void Sep(ContextMenuStrip m) { m.Items.Add(new ToolStripSeparator()); }
    }

    internal static class Shell
    {
        public static void Run(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file);
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("run " + file + ": " + ex.Message); }
        }

        public static void Explore(string target) { Run("explorer.exe", target); }

        public static void Activate(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return;
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetForegroundWindow(h);
        }
    }

    // ============================================================
    //  APP BAR (menyisihkan ruang layar seperti menu bar / Dock macOS)
    // ============================================================
    internal sealed class AppBar
    {
        private readonly Form _f;
        private readonly uint _edge;
        private bool _registered;
        public readonly uint CallbackMsg;

        public AppBar(Form f, uint edge)
        {
            _f = f; _edge = edge;
            CallbackMsg = Native.RegisterWindowMessage("LiquidMacAppBar" + edge);
        }

        public bool Registered { get { return _registered; } }

        private Native.APPBARDATA Data()
        {
            Native.APPBARDATA a = new Native.APPBARDATA();
            a.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            a.hWnd = _f.Handle;
            a.uEdge = _edge;
            a.uCallbackMessage = CallbackMsg;
            return a;
        }

        // Mengembalikan persegi yang disetujui shell.
        public Rectangle Reserve(Rectangle screen, int thickness)
        {
            Native.APPBARDATA a = Data();
            if (!_registered) { Native.SHAppBarMessage(Native.ABM_NEW, ref a); _registered = true; }
            a.rc.Left = screen.Left; a.rc.Right = screen.Right;
            if (_edge == Native.ABE_TOP) { a.rc.Top = screen.Top; a.rc.Bottom = screen.Top + thickness; }
            else { a.rc.Bottom = screen.Bottom; a.rc.Top = screen.Bottom - thickness; }
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref a);
            if (_edge == Native.ABE_TOP) a.rc.Bottom = a.rc.Top + thickness;
            else a.rc.Top = a.rc.Bottom - thickness;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref a);
            return Rectangle.FromLTRB(a.rc.Left, a.rc.Top, a.rc.Right, a.rc.Bottom);
        }

        public void Remove()
        {
            if (!_registered) return;
            try { Native.APPBARDATA a = Data(); Native.SHAppBarMessage(Native.ABM_REMOVE, ref a); } catch { }
            _registered = false;
        }

        public void Reset() { _registered = false; }

        public static bool TaskbarAutoHide
        {
            get
            {
                Native.APPBARDATA a = new Native.APPBARDATA();
                a.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
                a.hWnd = Native.FindWindow("Shell_TrayWnd", null);
                return (((int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref a).ToUInt32()) & Native.ABS_AUTOHIDE) != 0;
            }
            set
            {
                Native.APPBARDATA a = new Native.APPBARDATA();
                a.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
                a.hWnd = Native.FindWindow("Shell_TrayWnd", null);
                a.lParam = (IntPtr)(value ? Native.ABS_AUTOHIDE : Native.ABS_ALWAYSONTOP);
                Native.SHAppBarMessage(Native.ABM_SETSTATE, ref a);
            }
        }
    }

    // ============================================================
    //  BASE OVERLAY (layered window, digambar hanya saat berubah)
    // ============================================================
    internal abstract class OverlayForm : Form
    {
        protected Bitmap Bmp;
        protected Graphics G;
        protected int Bw, Bh;
        protected bool Dirty = true;
        private System.Windows.Forms.Timer _timer;
        private Point _lastPos;
        private static uint _taskbarCreated;

        protected OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = Topmost;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            AllocBitmap(Ui.P(10), Ui.P(10));
            if (_taskbarCreated == 0) _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        }

        private void AllocBitmap(int w, int h)
        {
            Bw = Math.Max(1, w); Bh = Math.Max(1, h);
            if (G != null) G.Dispose();
            if (Bmp != null) Bmp.Dispose();
            Bmp = new Bitmap(Bw, Bh, PixelFormat.Format32bppPArgb);
            Bmp.SetResolution(96f, 96f);
            G = Graphics.FromImage(Bmp);
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            G.PixelOffsetMode = PixelOffsetMode.None;
            G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            G.CompositingQuality = CompositingQuality.HighQuality;
            Size = new Size(Bw, Bh);
            Dirty = true;
        }

        public void Configure(int w, int h)
        {
            if (w == Bw && h == Bh) return;
            AllocBitmap(w, h);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | (Topmost ? Native.WS_EX_TOPMOST : 0);
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected virtual bool Topmost { get { return true; } }

        // 0 = tanpa acrylic. Acrylic hanya untuk jendela yang seluruh areanya terisi panel.
        protected virtual int AccentTint { get { return 0; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (AccentTint != 0) Native.EnableAcrylic(Handle, AccentTint);
            Reposition();
            SafeRender();
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 16;
            _timer.Tick += delegate { Pump(); };
            _timer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { Dirty = true; }
        }

        private void SafeRender()
        {
            try { Render(); Present(); Dirty = false; _lastPos = Location; }
            catch (Exception ex) { Log.Write(GetType().Name + " render: " + ex); }
        }

        public void Invalidate2() { Dirty = true; }

        // Jalankan satu frame sekarang juga (dipanggil dari event sistem agar tidak menunggu timer).
        public void Pump()
        {
            if (!Visible || !IsHandleCreated) return;
            try { if (OnFrame()) Dirty = true; } catch (Exception ex) { Log.Write(GetType().Name + " frame: " + ex); }
            if (Visible && (Dirty || _lastPos != Location)) SafeRender();
        }

        public void Snapshot(string path) { Render(); Bmp.Save(path, ImageFormat.Png); }

        public abstract void Reposition();
        protected abstract void Render();
        // Kembalikan true jika perlu digambar ulang.
        protected virtual bool OnFrame() { return false; }

        protected virtual void OnTaskbarCreated() { }

        protected override void WndProc(ref Message m)
        {
            if (_taskbarCreated != 0 && m.Msg == (int)_taskbarCreated)
            {
                try { OnTaskbarCreated(); } catch (Exception ex) { Log.Write("taskbarcreated: " + ex.Message); }
            }
            base.WndProc(ref m);
        }

        private void Present()
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = Bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBmp);
            try
            {
                Native.SIZE size = new Native.SIZE(Bw, Bh);
                Native.POINT src = new Native.POINT(0, 0);
                Native.POINT dst = new Native.POINT(Left, Top);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public static Point? FakeCursor;
        public void PromoFrame(string path) { OnFrame(); Render(); Bmp.Save(path, ImageFormat.Png); }

        protected Point LocalCursor()
        {
            Point mp = FakeCursor ?? Cursor.Position;
            return new Point(mp.X - Left, mp.Y - Top);
        }
    }

    // ============================================================
    //  INFO JENDELA AKTIF + DAFTAR APLIKASI BERJALAN
    // ============================================================
    internal static class Windows
    {
        public static IntPtr LastForeign = IntPtr.Zero;   // jendela aktif terakhir milik aplikasi lain
        public static string ActiveName = "Finder";
        private static readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly uint _myPid = (uint)Process.GetCurrentProcess().Id;

        // Dipanggil berkala; true bila nama aplikasi aktif berubah.
        public static bool Poll()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid; Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == _myPid) return false;
            LastForeign = fg;
            string cls = Native.ClassOf(fg);
            string name;
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "CabinetWClass") name = "Finder";
            else name = NameOf(Native.ProcessPath(pid));
            if (name == ActiveName) return false;
            ActiveName = name;
            return true;
        }

        public static string NameOf(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "Aplikasi";
            string n;
            if (_names.TryGetValue(exe, out n)) return n;
            n = Path.GetFileNameWithoutExtension(exe);
            try
            {
                FileVersionInfo vi = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(vi.FileDescription) && vi.FileDescription.Length <= 32) n = vi.FileDescription.Trim();
                else if (!string.IsNullOrEmpty(vi.ProductName) && vi.ProductName.Length <= 32) n = vi.ProductName.Trim();
            }
            catch { }
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            if (n.Equals("Windows Explorer", StringComparison.OrdinalIgnoreCase) || n.Equals("explorer", StringComparison.OrdinalIgnoreCase)) n = "Finder";
            _names[exe] = n;
            return n;
        }

        // exe (lowercase path) -> jendela top-level yang tampil (urutan Z, teratas dulu)
        public static Dictionary<string, List<IntPtr>> AppWindows()
        {
            Dictionary<string, List<IntPtr>> map = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<uint, string> pidPath = new Dictionary<uint, string>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h)) return true;
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                string cls = Native.ClassOf(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == _myPid) return true;
                string path;
                if (!pidPath.TryGetValue(pid, out path)) { path = Native.ProcessPath(pid) ?? ""; pidPath[pid] = path; }
                if (path.Length == 0) return true;
                string key = path + "|" + (Aumid.OfWindow(h) ?? "");
                List<IntPtr> list;
                if (!map.TryGetValue(key, out list)) { list = new List<IntPtr>(); map[key] = list; }
                list.Add(h);
                return true;
            }, IntPtr.Zero);
            return map;
        }

        [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr h, int i);

        public static bool IsFullscreen(IntPtr fg)
        {
            if (fg == IntPtr.Zero) return false;
            string cls = Native.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            uint pid; Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == _myPid) return false;
            // Maximized biasa (punya baris judul) bukan layar penuh. Video/game layar penuh tidak punya baris judul,
            // walau Windows masih menandainya "maximized" (mis. Chrome/YouTube yang sebelumnya dimaksimalkan).
            int style = GetWindowLongW(fg, -16);
            if (Native.IsZoomed(fg) && (style & 0x00C00000) == 0x00C00000) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            Rectangle s = Screen.FromHandle(fg).Bounds;
            return r.Left <= s.Left && r.Top <= s.Top && r.Right >= s.Right && r.Bottom >= s.Bottom;
        }
    }

    // ============================================================
    //  ICON berkualitas tinggi (jumbo 256 px dari shell)
    // ============================================================
    internal static class Icons
    {
        private static Native.IImageList _jumbo, _xl;

        public static Bitmap Get(string path)
        {
            Bitmap b = ShellImage.Get(path, 256);
            if (b != null) return Trim(b);
            try
            {
                Native.SHFILEINFO sfi = new Native.SHFILEINFO();
                IntPtr r = Native.SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf(sfi), Native.SHGFI_SYSICONINDEX);
                if (r != IntPtr.Zero)
                {
                    b = FromList(Native.SHIL_JUMBO, ref _jumbo, sfi.iIcon);
                    // Ikon lama hanya 32/48 px di pojok kanvas 256 -> pakai daftar 48 px.
                    if (b != null && ContentWidth(b) < b.Width / 3)
                    {
                        b.Dispose();
                        b = FromList(Native.SHIL_EXTRALARGE, ref _xl, sfi.iIcon);
                    }
                }
            }
            catch (Exception ex) { Log.Write("icon " + path + ": " + ex.Message); }
            if (b == null)
            {
                try { using (Icon ic = Icon.ExtractAssociatedIcon(path)) b = ic.ToBitmap(); } catch { }
            }
            return b;
        }

        private static Bitmap FromList(int which, ref Native.IImageList list, int index)
        {
            if (list == null)
            {
                Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                if (Native.SHGetImageList(which, ref iid, out list) != 0) { list = null; return null; }
            }
            IntPtr h = IntPtr.Zero;
            if (list.GetIcon(index, Native.ILD_TRANSPARENT, ref h) != 0 || h == IntPtr.Zero) return null;
            try { using (Icon ic = Icon.FromHandle(h)) return Trim(ic.ToBitmap()); }
            finally { Native.DestroyIcon(h); }
        }

        private static Rectangle Bounds(Bitmap b)
        {
            int minX = b.Width, minY = b.Height, maxX = -1, maxY = -1;
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[b.Width];
                for (int y = 0; y < b.Height; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, b.Width);
                    for (int x = 0; x < b.Width; x++)
                    {
                        if (((row[x] >> 24) & 0xFF) > 8)
                        {
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally { b.UnlockBits(d); }
            if (maxX < 0) return Rectangle.Empty;
            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        private static int ContentWidth(Bitmap b) { Rectangle r = Bounds(b); return Math.Max(r.Width, r.Height); }

        // Potong ruang kosong lalu letakkan di tengah kanvas persegi.
        private static Bitmap Trim(Bitmap b)
        {
            Rectangle r = Bounds(b);
            if (r.IsEmpty || (r.Width >= b.Width - 2 && r.Height >= b.Height - 2)) return b;
            int side = Math.Max(r.Width, r.Height);
            Bitmap o = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
                g.DrawImage(b, new Rectangle((side - r.Width) / 2, (side - r.Height) / 2, r.Width, r.Height), r, GraphicsUnit.Pixel);
            b.Dispose();
            return o;
        }
    }

    // ============================================================
    //  TOP MENU BAR
    // ============================================================
    internal sealed class TopBarForm : OverlayForm
    {
        private readonly int BarH = Ui.P(28);
        private readonly AppBar _ab;
        private readonly Font _menuFont = Ui.F(10f, FontStyle.Regular);
        private readonly Font _boldFont = Ui.F(10f, FontStyle.Bold);
        private readonly Font _glyphFont = new Font("Segoe UI Symbol", (float)Math.Round(15 * Ui.S), FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _statusGlyph = Glyphs.Font((float)Math.Round(16 * Ui.S));
        private readonly Font _statusGlyphS = Glyphs.Font((float)Math.Round(14 * Ui.S));
        private float _vol = -1; private bool _muted; private string _wifiGlyph = Glyphs.Wifi3;
        private bool _wifiBusy;
        private readonly List<KeyValuePair<string, RectangleF>> _hits = new List<KeyValuePair<string, RectangleF>>();
        private string _hover, _pressed;
        private string _clock = "";
        private int _tick;
        private bool _online = true;
        public bool Reserve = true;
        public bool AutoHide;
        private double _reveal = 1;
        private int _hideDelay;
        public int VisibleBottom { get { return AutoHide ? (int)Math.Round(_reveal * BarH) : BarH; } }

        protected override int AccentTint { get { return unchecked((int)0x30101018); } }

        public TopBarForm() { _ab = new AppBar(this, Native.ABE_TOP); }

        public override void Reposition()
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            Configure(scr.Width, BarH);
            if (AutoHide)
            {
                _ab.Remove();
                Location = new Point(scr.Left, scr.Top - (int)Math.Round((1 - _reveal) * BarH));
            }
            else if (Reserve && IsHandleCreated)
            {
                Rectangle r = _ab.Reserve(scr, BarH);
                Location = new Point(r.Left, r.Top);
            }
            else
            {
                _ab.Remove();
                Location = new Point(scr.Left, scr.Top);
            }
            Dirty = true;
        }

        public void Release() { _ab.Remove(); }

        protected override void OnTaskbarCreated() { _ab.Reset(); Reposition(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)_ab.CallbackMsg && (int)m.WParam == Native.ABN_POSCHANGED && Reserve && Visible) Reposition();
            base.WndProc(ref m);
        }

        protected override bool OnFrame()
        {
            bool dirty = false;
            if (AutoHide)
            {
                Rectangle scr = Screen.PrimaryScreen.Bounds;
                Point mp = Cursor.Position;
                bool inX = mp.X >= scr.Left && mp.X < scr.Right;
                bool want = _pressed != null || (inX && mp.Y <= scr.Top + 1) || (_reveal > 0.3 && inX && mp.Y < scr.Top + BarH + Ui.P(6));
                if (want) _hideDelay = 24; else if (_hideDelay > 0) _hideDelay--;
                double target = (want || _hideDelay > 0) ? 1 : 0;
                if (Math.Abs(target - _reveal) > 0.01) { _reveal += (target - _reveal) * 0.3; Reposition(); }
                else if (_reveal != target) { _reveal = target; Reposition(); }
                if (_reveal <= 0.01) return false;
            }
            else if (_reveal != 1) { _reveal = 1; }
            if (++_tick % 15 == 0)
            {
                string c = DateTime.Now.ToString("ddd d MMM  HH.mm", new CultureInfo("id-ID"));
                if (c != _clock) { _clock = c; dirty = true; }
                if (Windows.Poll()) dirty = true;
            }
            if (_tick % 60 == 1)
            {
                float v = SystemControls.Volume; bool mu = SystemControls.Muted;
                if (Math.Abs(v - _vol) > 0.004f || mu != _muted) { _vol = v; _muted = mu; dirty = true; }
            }
            if (_tick % 600 == 2 && !_wifiBusy)
            {
                _wifiBusy = true;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { Wifi.RefreshStatus(); } catch { } _wifiBusy = false; });
            }
            if (_tick % 60 == 3)
            {
                bool on = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                string wg = Wifi.HasAdapter ? Glyphs.WifiFor(Wifi.Signal, Wifi.Connected) : (on ? Glyphs.Wifi3 : Glyphs.WifiOff);
                if (on != _online || wg != _wifiGlyph) { _online = on; _wifiGlyph = wg; dirty = true; }
                PowerStatus bps = SystemInformation.PowerStatus;
                string bk = (int)Math.Round(bps.BatteryLifePercent * 100) + "|" + bps.PowerLineStatus;
                if (bk != _batKey) { _batKey = bk; dirty = true; }   // persen & status cas
            }
            Point p = LocalCursor();
            string h = null;
            if (p.Y >= 0 && p.Y < Bh && p.X >= 0 && p.X < Bw)
                foreach (KeyValuePair<string, RectangleF> kv in _hits) if (kv.Value.Contains(p)) { h = kv.Key; break; }
            if (h != _hover) { _hover = h; dirty = true; }
            return dirty;
        }

        protected override void Render()
        {
            G.Clear(Color.Transparent);
            _hits.Clear();
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(110, 18, 18, 26))) G.FillRectangle(bg, 0, 0, Bw, BarH);
            using (Pen line = new Pen(Color.FromArgb(40, 255, 255, 255), 1f)) G.DrawLine(line, 0, BarH - 1, Bw, BarH - 1);

            float ty = Ui.R((BarH - _menuFont.GetHeight(G)) / 2f);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(225, 240, 240, 245)))
            {
                float x = Ui.Pf(10);
                Item("apple", "\u2318", _glyphFont, white, ref x, Ui.R((BarH - _glyphFont.GetHeight(G)) / 2f));
                Item("app", Windows.ActiveName, _boldFont, white, ref x, ty);
                string[] menus = { "File", "Edit", "View", "Go", "Window", "Help" };
                foreach (string m in menus) Item(m, m, _menuFont, dim, ref x, ty);

                // --- status kanan ---
                float right = Bw - Ui.Pf(10);
                SizeF cs = G.MeasureString(_clock, _menuFont);
                right = Status("clock", right, cs.Width, delegate (float sx) { G.DrawString(_clock, _menuFont, white, Ui.R(sx), ty); });
                right = Status("battery", right, BatteryW, delegate (float sx) { DrawBattery(sx + Ui.Pf(1), BarH / 2f); });
                right = Status("wifi", right, Ui.Pf(18), delegate (float sx) { Glyphs.Draw(G, _wifiGlyph, _statusGlyph, white, new RectangleF(sx, 0, Ui.Pf(18), BarH)); });
                right = Status("volume", right, Ui.Pf(18), delegate (float sx) { Glyphs.Draw(G, Glyphs.VolFor(_vol, _muted), _statusGlyph, white, new RectangleF(sx, 0, Ui.Pf(18), BarH)); });
                right = Status("search", right, Ui.Pf(16), delegate (float sx) { Glyphs.Draw(G, Glyphs.Search, _statusGlyphS, white, new RectangleF(sx, 0, Ui.Pf(16), BarH)); });
                right = Status("control", right, Ui.Pf(18), delegate (float sx) { DrawControl(sx + Ui.Pf(2), BarH / 2f); });
                if (Settings.Get("BarProfile", true))
                    Status("profile", right, Ui.Pf(18), delegate (float sx) { SysInfo.DrawAvatar(G, new RectangleF(sx, BarH / 2f - Ui.Pf(9), Ui.Pf(18), Ui.Pf(18))); });
            }
        }

        private void HoverBg(string id, RectangleF r)
        {
            if (id != _hover && id != _pressed) return;
            RectangleF hr = new RectangleF(r.X, Ui.Pf(3), r.Width, BarH - Ui.Pf(6));
            using (GraphicsPath p = Glass.Round(hr, Ui.Pf(5)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(id == _pressed ? 80 : 50, 255, 255, 255)))
                G.FillPath(b, p);
        }

        private void Item(string id, string text, Font f, Brush brush, ref float x, float y)
        {
            SizeF s = G.MeasureString(text, f);
            RectangleF r = new RectangleF(x, 0, s.Width + Ui.Pf(10), BarH);
            HoverBg(id, r);
            G.DrawString(text, f, brush, Ui.R(x + Ui.Pf(5)), Ui.R(y));
            _hits.Add(new KeyValuePair<string, RectangleF>(id, r));
            x += r.Width + Ui.Pf(2);
        }

        private float Status(string id, float right, float w, Action<float> draw)
        {
            RectangleF r = new RectangleF(right - w - Ui.Pf(10), 0, w + Ui.Pf(10), BarH);
            HoverBg(id, r);
            draw(r.X + Ui.Pf(5));
            _hits.Add(new KeyValuePair<string, RectangleF>(id, r));
            return r.X - Ui.Pf(2);
        }

        private void DrawWifi(float cx, float cy)
        {
            int a = _online ? 235 : 90;
            using (Pen pen = new Pen(Color.FromArgb(a, 255, 255, 255), Ui.Pf(1.6)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                for (int k = 0; k < 3; k++)
                {
                    float rad = Ui.Pf(3.2 + k * 3.3);
                    G.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, 225, 90);
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                    G.FillEllipse(b, cx - Ui.Pf(1.5), cy - Ui.Pf(1.8), Ui.Pf(3), Ui.Pf(3));
            }
        }

        private void DrawVolume(float x, float cy)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                PointF[] poly = {
                    new PointF(x, cy - Ui.Pf(2.5)), new PointF(x + Ui.Pf(3), cy - Ui.Pf(2.5)), new PointF(x + Ui.Pf(7), cy - Ui.Pf(6)),
                    new PointF(x + Ui.Pf(7), cy + Ui.Pf(6)), new PointF(x + Ui.Pf(3), cy + Ui.Pf(2.5)), new PointF(x, cy + Ui.Pf(2.5)) };
                G.FillPolygon(b, poly);
            }
            using (Pen pen = new Pen(Color.FromArgb(235, 255, 255, 255), Ui.Pf(1.4)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                G.DrawArc(pen, x + Ui.Pf(5), cy - Ui.Pf(3.5), Ui.Pf(6), Ui.Pf(7), -50, 100);
                G.DrawArc(pen, x + Ui.Pf(5), cy - Ui.Pf(6.5), Ui.Pf(10), Ui.Pf(13), -50, 100);
            }
        }

        private static bool Charging
        {
            get { PowerStatus ps = SystemInformation.PowerStatus; return ps.PowerLineStatus == PowerLineStatus.Online && (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0; }
        }
        private float BatteryW { get { return Charging ? Ui.Pf(44) : Ui.Pf(33); } }
        private string _batKey = "";
        private Font _batFont;

        // Baterai gaya iOS/macOS: persentase di dalam, petir saat dicas. Semua digambar vektor di piksel bulat (tajam).
        private void DrawBattery(float x, float cy)
        {
            PowerStatus ps = SystemInformation.PowerStatus;
            float pct = ps.BatteryLifePercent;
            bool noBattery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
            if (pct < 0f || pct > 1f || noBattery) pct = 1f;
            bool charging = ps.PowerLineStatus == PowerLineStatus.Online && !noBattery;
            int bw = Ui.P(28), bh = Ui.P(14);
            int bx = (int)Math.Round(x), by = (int)Math.Round(cy - bh / 2f);
            SmoothingMode sm = G.SmoothingMode; TextRenderingHint th = G.TextRenderingHint;
            G.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF body = new RectangleF(bx + 0.5f, by + 0.5f, bw - 1, bh - 1);
            float rad = Ui.Pf(4);
            using (GraphicsPath bp = Glass.Round(body, rad))
            using (SolidBrush shell = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) G.FillPath(shell, bp);
            // kutub
            RectangleF nub = new RectangleF(bx + bw + Ui.Pf(1), by + bh / 2f - Ui.Pf(2.5), Ui.Pf(2), Ui.Pf(5));
            using (GraphicsPath np = Glass.Round(nub, Ui.Pf(1))) using (SolidBrush nb = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) G.FillPath(nb, np);
            // isi sesuai level
            Color fc = charging ? Color.FromArgb(255, 52, 199, 89) : pct <= 0.2f ? Color.FromArgb(255, 255, 69, 58) : Color.FromArgb(255, 255, 255, 255);
            float fw = Math.Max(rad * 1.2f, (bw - 1) * pct);
            RectangleF fill = new RectangleF(body.X, body.Y, fw, body.Height);
            using (GraphicsPath bp = Glass.Round(body, rad))
            {
                Region old = G.Clip; G.SetClip(bp, CombineMode.Intersect);
                using (SolidBrush fb = new SolidBrush(fc)) G.FillRectangle(fb, fill);
                G.Clip = old; old.Dispose();
            }
            // persentase di dalam: putih di bagian kosong, gelap di atas isi putih
            if (_batFont == null) _batFont = new Font(Ui.Family == "Inter" ? "Inter Semi Bold" : "Segoe UI Semibold", (float)Math.Round(9.6f * Ui.S), FontStyle.Regular, GraphicsUnit.Pixel);
            string txt = ((int)Math.Round(pct * 100)).ToString();
            G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                RectangleF tr = new RectangleF(bx, by + 0.5f, bw, bh);
                bool lightFill = !charging && pct > 0.2f;
                using (SolidBrush w = new SolidBrush(Color.White)) G.DrawString(txt, _batFont, w, tr, sf);
                if (lightFill)
                {
                    Region old = G.Clip; G.SetClip(fill, CombineMode.Intersect);
                    using (SolidBrush d = new SolidBrush(Color.FromArgb(255, 22, 22, 28))) G.DrawString(txt, _batFont, d, tr, sf);
                    G.Clip = old; old.Dispose();
                }
            }
            // petir vektor saat dicas
            if (charging)
            {
                float lx = bx + bw + Ui.Pf(5), lw = Ui.Pf(8), lh = Ui.Pf(13), ly = cy - lh / 2f;
                PointF[] bolt = {
                    new PointF(lx + lw * 0.62f, ly), new PointF(lx, ly + lh * 0.58f), new PointF(lx + lw * 0.45f, ly + lh * 0.58f),
                    new PointF(lx + lw * 0.34f, ly + lh), new PointF(lx + lw, ly + lh * 0.40f), new PointF(lx + lw * 0.55f, ly + lh * 0.40f) };
                using (SolidBrush b = new SolidBrush(Color.White)) G.FillPolygon(b, bolt);
            }
            G.SmoothingMode = sm; G.TextRenderingHint = th;
        }

        private void DrawSearch(float x, float cy)
        {
            using (Pen pen = new Pen(Color.FromArgb(235, 255, 255, 255), Ui.Pf(1.6)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                G.DrawEllipse(pen, x, cy - Ui.Pf(6), Ui.Pf(8.5), Ui.Pf(8.5));
                G.DrawLine(pen, x + Ui.Pf(7.5), cy + Ui.Pf(1.5), x + Ui.Pf(11), cy + Ui.Pf(5));
            }
        }

        private void DrawControl(float x, float cy)
        {
            // ikon Control Center: dua sakelar
            using (Pen pen = new Pen(Color.FromArgb(235, 255, 255, 255), Ui.Pf(1.3)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                RectangleF a = new RectangleF(x, cy - Ui.Pf(6), Ui.Pf(13), Ui.Pf(5.5));
                RectangleF c = new RectangleF(x, cy + Ui.Pf(0.5), Ui.Pf(13), Ui.Pf(5.5));
                using (GraphicsPath pa = Glass.Round(a, a.Height / 2)) G.DrawPath(pen, pa);
                using (GraphicsPath pc = Glass.Round(c, c.Height / 2)) G.DrawPath(pen, pc);
                G.FillEllipse(b, a.Right - a.Height, a.Y, a.Height, a.Height);
                G.FillEllipse(b, c.X, c.Y, c.Height, c.Height);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string id = null;
            foreach (KeyValuePair<string, RectangleF> kv in _hits) if (kv.Value.Contains(e.Location)) { id = kv.Key; break; }
            if (id == null) return;
            RectangleF r = RectangleF.Empty;
            foreach (KeyValuePair<string, RectangleF> kv in _hits) if (kv.Key == id) r = kv.Value;
            Point at = new Point(Left + (int)r.X, Top + BarH);

            switch (id)
            {
                case "search": Program.Launchpad(); return;
                case "clock": Program.NotificationCenter(); return;
                case "wifi": Program.ControlPanel("wifi", Left + (int)r.Right); return;
                case "volume": Program.ControlPanel("sound", Left + (int)r.Right); return;
                case "battery": Program.ControlPanel("battery", Left + (int)r.Right); return;
                case "control": Program.ControlPanel("control", Left + (int)r.Right); return;
                case "profile": if (Program.ProfileAction != null) Program.ProfileAction(Left + (int)r.Right); return;
            }
            ContextMenuStrip m = BuildMenu(id);
            if (m == null) return;
            _pressed = id; Dirty = true;
            m.Closed += delegate { _pressed = null; Dirty = true; };
            m.Show(at);
        }

        // Kirim pintasan ke jendela aplikasi yang aktif sebelum menu dibuka.
        private static EventHandler Send(params byte[] keys)
        {
            return delegate
            {
                IntPtr h = Windows.LastForeign;
                if (h != IntPtr.Zero) Native.SetForegroundWindow(h);
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 80;
                t.Tick += delegate { t.Stop(); t.Dispose(); Native.Keys(keys); };
                t.Start();
            };
        }

        private static EventHandler Win(int cmd)
        {
            return delegate
            {
                IntPtr h = Windows.LastForeign;
                if (h == IntPtr.Zero || !Native.IsWindow(h)) return;
                if (cmd == Native.SW_MAXIMIZE && Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                else if (cmd == Native.SW_MINIMIZE) WinAnim.MinimizeQuiet(h);   // tanpa memunculkan taskbar
                else Native.ShowWindow(h, cmd);
            };
        }

        private static string Folder(Environment.SpecialFolder f) { return Environment.GetFolderPath(f); }

        private ContextMenuStrip BuildMenu(string id)
        {
            ContextMenuStrip m = Menus.Create();
            byte C = Native.VK_CONTROL;
            switch (id)
            {
                case "apple":
                    Menus.Add(m, "Launchpad", delegate { Program.Launchpad(); });
                    Menus.Add(m, "Pusat Notifikasi", delegate { Program.NotificationCenter(); });
                    Menus.Add(m, "Pengaturan LiquidMac...", delegate { Program.RequestSettings(); });
                    Menus.Sep(m);
                    Menus.Add(m, "Tentang PC Ini", delegate { Shell.Run("ms-settings:about", null); });
                    Menus.Sep(m);
                    Menus.Add(m, "Pengaturan Sistem...", delegate { Shell.Run("ms-settings:", null); });
                    Menus.Add(m, "App Store...", delegate { Shell.Run("ms-windows-store:", null); });
                    Menus.Add(m, "Pengelola Tugas", delegate { Shell.Run("taskmgr.exe", null); });
                    Menus.Sep(m);
                    Menus.Add(m, "Paksa Keluar " + Windows.ActiveName + "...", delegate
                    {
                        IntPtr h = Windows.LastForeign; uint pid; Native.GetWindowThreadProcessId(h, out pid);
                        if (pid == 0) return;
                        if (MessageBox.Show("Paksa tutup " + Windows.ActiveName + "? Pekerjaan yang belum disimpan akan hilang.", "LiquidMac", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                            try { Process.GetProcessById((int)pid).Kill(); } catch (Exception ex) { Log.Write("kill: " + ex.Message); }
                    });
                    Menus.Sep(m);
                    Menus.Add(m, "Tidur", delegate { Application.SetSuspendState(PowerState.Suspend, false, false); });
                    Menus.Add(m, "Mulai Ulang...", delegate { Confirm("Mulai ulang komputer sekarang?", "shutdown.exe", "/r /t 0"); });
                    Menus.Add(m, "Matikan...", delegate { Confirm("Matikan komputer sekarang?", "shutdown.exe", "/s /t 0"); });
                    Menus.Sep(m);
                    Menus.Add(m, "Kunci Layar    Win+L", delegate { MacLock.LockNow(); });
                    Menus.Add(m, "Keluar Akun...", delegate { Confirm("Keluar dari akun Windows sekarang?", "shutdown.exe", "/l"); });
                    break;
                case "app":
                    Menus.Add(m, "Tentang " + Windows.ActiveName, null);
                    Menus.Sep(m);
                    Menus.Add(m, "Sembunyikan " + Windows.ActiveName, Win(Native.SW_MINIMIZE));
                    Menus.Add(m, "Tampilkan Desktop", delegate { Native.Keys(Native.VK_LWIN, (byte)'D'); });
                    Menus.Sep(m);
                    Menus.Add(m, "Tutup " + Windows.ActiveName, delegate { IntPtr h = Windows.LastForeign; if (h != IntPtr.Zero) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); });
                    break;
                case "File":
                    Menus.Add(m, "Baru    Ctrl+N", Send(C, (byte)'N'));
                    Menus.Add(m, "Buka...    Ctrl+O", Send(C, (byte)'O'));
                    Menus.Add(m, "Simpan    Ctrl+S", Send(C, (byte)'S'));
                    Menus.Add(m, "Cetak...    Ctrl+P", Send(C, (byte)'P'));
                    Menus.Sep(m);
                    Menus.Add(m, "Tutup Jendela", delegate { IntPtr h = Windows.LastForeign; if (h != IntPtr.Zero) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); });
                    break;
                case "Edit":
                    Menus.Add(m, "Urungkan    Ctrl+Z", Send(C, (byte)'Z'));
                    Menus.Add(m, "Ulangi    Ctrl+Y", Send(C, (byte)'Y'));
                    Menus.Sep(m);
                    Menus.Add(m, "Potong    Ctrl+X", Send(C, (byte)'X'));
                    Menus.Add(m, "Salin    Ctrl+C", Send(C, (byte)'C'));
                    Menus.Add(m, "Tempel    Ctrl+V", Send(C, (byte)'V'));
                    Menus.Add(m, "Pilih Semua    Ctrl+A", Send(C, (byte)'A'));
                    Menus.Sep(m);
                    Menus.Add(m, "Cari...    Ctrl+F", Send(C, (byte)'F'));
                    Menus.Add(m, "Emoji & Simbol    Win+.", delegate { Native.Keys(Native.VK_LWIN, 0xBE); });
                    break;
                case "View":
                    Menus.Add(m, "Layar Penuh    F11", Send(Native.VK_F11));
                    Menus.Add(m, "Perbesar    Ctrl++", Send(C, 0xBB));
                    Menus.Add(m, "Perkecil    Ctrl+-", Send(C, 0xBD));
                    Menus.Add(m, "Ukuran Asli    Ctrl+0", Send(C, (byte)'0'));
                    break;
                case "Go":
                    Menus.Add(m, "Rumah", delegate { Shell.Explore(Folder(Environment.SpecialFolder.UserProfile)); });
                    Menus.Add(m, "Desktop", delegate { Shell.Explore(Folder(Environment.SpecialFolder.DesktopDirectory)); });
                    Menus.Add(m, "Dokumen", delegate { Shell.Explore(Folder(Environment.SpecialFolder.MyDocuments)); });
                    Menus.Add(m, "Unduhan", delegate { Shell.Explore("shell:Downloads"); });
                    Menus.Add(m, "Gambar", delegate { Shell.Explore(Folder(Environment.SpecialFolder.MyPictures)); });
                    Menus.Sep(m);
                    Menus.Add(m, "Aplikasi (Launchpad)", delegate { Program.Launchpad(); });
                    Menus.Add(m, "Komputer Ini", delegate { Shell.Explore("shell:MyComputerFolder"); });
                    Menus.Add(m, "Jaringan", delegate { Shell.Explore("shell:NetworkPlacesFolder"); });
                    Menus.Add(m, "Tong Sampah", delegate { Shell.Explore("shell:RecycleBinFolder"); });
                    break;
                case "Window":
                    Menus.Add(m, "Minimalkan", Win(Native.SW_MINIMIZE));
                    Menus.Add(m, "Zoom", Win(Native.SW_MAXIMIZE));
                    Menus.Sep(m);
                    Menus.Add(m, "Rapikan ke Kiri    Win+\u2190", Send(Native.VK_LWIN, 0x25));
                    Menus.Add(m, "Rapikan ke Kanan    Win+\u2192", Send(Native.VK_LWIN, 0x27));
                    Menus.Add(m, "Tata Letak Snap    Win+Z", Send(Native.VK_LWIN, (byte)'Z'));
                    Menus.Sep(m);
                    Menus.Add(m, "Mission Control    Win+Tab", delegate { Native.Keys(Native.VK_LWIN, Native.VK_TAB); });
                    Menus.Add(m, "Desktop Baru    Win+Ctrl+D", delegate { Native.Keys(Native.VK_LWIN, C, (byte)'D'); });
                    break;
                case "Help":
                    Menus.Add(m, "Bantuan Windows", delegate { Shell.Run("https://support.microsoft.com/windows", null); });
                    Menus.Add(m, "Pintasan Keyboard", delegate { Shell.Run("https://support.microsoft.com/windows/keyboard-shortcuts-in-windows-dfffa98a-6a4b-4234-a9c1-a0d1f4fa8b05", null); });
                    Menus.Sep(m);
                    Menus.Add(m, "Buka log LiquidMac", delegate { Directory.CreateDirectory(Log.Dir); Shell.Explore(Log.Dir); });
                    break;
                default:
                    m.Dispose();
                    return null;
            }
            return m;
        }

        private static void Confirm(string q, string file, string args)
        {
            if (MessageBox.Show(q, "LiquidMac", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Shell.Run(file, args);
        }
    }

    // ============================================================
    //  NOTCH  (Dynamic Island: jam, statistik sistem, kontrol media)
    // ============================================================
    internal sealed class NotchForm : OverlayForm
    {
        private readonly int MaxW = Ui.P(500), MaxH = Ui.P(186);
        private readonly int ColW = Ui.P(171), ColH = Ui.P(30), MediaColW = Ui.P(238);   // standby 5% lebih ramping
        private readonly int ExpW = Ui.P(440), ExpH = Ui.P(136), MediaExpH = Ui.P(170);
        private double _vw, _vh;              // kecepatan pegas (animasi memantul)
        private float _dotPos;                // posisi titik aktif (beranimasi)
        private long _removing = -1; private float _removeT;   // animasi hapus notifikasi
        private int _rowHover = -1; private float[] _rowGlow = new float[3];
        private float _press;                 // efek tekan tombol
        private string _pressId;
        private readonly int HudW = Ui.P(310), HudH = Ui.P(36);
        private float _drop;                  // HUD turun sedikit dari posisi island biasa
        private float _curVol = -1; private bool _curMute;
        private bool _volDrag; private RectangleF _volTrack;
        public static readonly int Offset = Ui.P(6);   // ~1 mm di bawah tepi atas layar
        private double _w, _h;
        private bool _hover, _pinned;
        private int _tick;
        private float _cpu, _ram, _bat = -1;
        private long _pIdle, _pKern, _pUser;
        private readonly Dictionary<string, RectangleF> _btn = new Dictionary<string, RectangleF>();
        private string _btnHover;
        private int _mediaVer = -1;
        private NowPlaying _np;
        private string _hud; private int _hudLeft; private float _hudVal;
        private float _lastVol = -2; private bool _lastMute; private PowerLineStatus _lastLine = PowerLineStatus.Unknown;
        // kartu yang bisa digeser saat island dibuka
        private string _card = "clock";
        private double _slide;            // offset animasi geser (-1..1), 0 = diam
        private readonly SwipeGate _gate = new SwipeGate(220);
        private bool _wasExpanded;
        private List<Notif> _notifs = new List<Notif>();
        private int _weatherVer = -1;
        private readonly Font _bigFont = Ui.F(24f, FontStyle.Bold);
        private Bitmap _artSrc, _artBlur;
        // tangkapan layar / rekaman / privasi / mode tersembunyi
        private readonly int ShotW = Ui.P(440), ShotH = Ui.P(84);
        private string _wantCard;
        private float _idleT = -1;            // animasi standby: menyusut, bergelombang, pusaran air
        private long _idleNext;
        private static readonly Random _rnd = new Random();
        private const float IdleDur = 3.4f;
        private string _idleKind = "air";
        private int _idleCount;
        private void PickIdle()
        {
            string st = Settings.GetS("IslandIdleStyle", "acak");
            _idleKind = st == "air" || st == "jantung" ? st : (_idleCount++ % 2 == 0 ? "air" : "jantung");
        }
        public void PlayIdle() { PickIdle(); _idleT = 0; Dirty = true; }
        public void PromoIdle(string kind) { _idleKind = kind; _idleT = 0; Dirty = true; }
        private string _shotPath, _shotKind, _hudText;
        private Bitmap _shotThumb;
        private bool _expanded;
        private int _yOff;
        private float _ri;
        private List<string> _prevCam = new List<string>(), _prevMic = new List<string>();
        public static bool ForceHidden;
        private bool Hidden { get { return ForceHidden || Settings.Get("IslandHidden", false); } }
        private static bool PrivActive { get { return PrivacyWatch.Cam.Count > 0 || PrivacyWatch.Mic.Count > 0; } }
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, int a);

        // Seperti notch MacBook: island tidak ikut terekam di tangkapan layar / rekaman / berbagi layar.
        public void SetCaptureHidden(bool on) { if (IsHandleCreated) SetWindowDisplayAffinity(Handle, on ? 0x11 : 0); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Program.SnapshotDir == null && Settings.Get("IslandHideInCapture", true)) SetCaptureHidden(true);
        }

        public void ShowShot(string path, Bitmap thumb, string kind)
        {
            if (_shotThumb != null && _shotThumb != thumb) _shotThumb.Dispose();
            _shotThumb = thumb; _shotPath = path; _shotKind = kind;
            _hud = "shot"; _hudLeft = 330; _pinned = false;
            if (Program.SnapshotDir == null && Settings.Get("IslandHoverSound", true)) IslandTick.Play();
            Dirty = true;
        }

        private void OnPrivacy(List<string> cam, List<string> mic)
        {
            string nc = null, nm = null;
            foreach (string a in cam) if (!_prevCam.Contains(a)) nc = a;
            foreach (string a in mic) if (!_prevMic.Contains(a)) nm = a;
            _prevCam = cam; _prevMic = mic;
            if (nc != null) { _hud = "cam"; _hudText = nc; _hudLeft = 200; }
            else if (nm != null) { _hud = "mic"; _hudText = nm; _hudLeft = 200; }
            Dirty = true;
        }   // sampul buram untuk latar (dibuat sekali per lagu)

        private Bitmap ArtBlur()
        {
            Bitmap art = _np == null ? null : _np.Art;
            if (art == _artSrc) return _artBlur;
            _artSrc = art;
            if (_artBlur != null) { _artBlur.Dispose(); _artBlur = null; }
            if (art == null) return null;
            try
            {
                _artBlur = new Bitmap(12, 6, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(_artBlur))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(art, new Rectangle(0, 0, 12, 6), new Rectangle(0, art.Height / 4, art.Width, art.Height / 2), GraphicsUnit.Pixel);
                }
            }
            catch { _artBlur = null; }
            return _artBlur;
        }

        private void Equalizer(float x, float cy, float h, int alpha, bool playing)
        {
            float bw = Ui.Pf(2.4), gap = Ui.Pf(2.2);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 92, 120)))
                for (int i = 0; i < 4; i++)
                {
                    double ph = _tick * 0.17 + i * 1.9;
                    float hh = playing ? (float)(h * (0.3 + 0.7 * Math.Abs(Math.Sin(ph) * (0.65 + 0.35 * Math.Sin(ph * 0.41 + i))))) : h * 0.25f;
                    RectangleF br = new RectangleF(x + i * (bw + gap), cy - hh / 2f, bw, hh);
                    using (GraphicsPath bp = Glass.Round(br, bw / 2)) G.FillPath(b, bp);
                }
        }

        private readonly Font _timeFont = Ui.F(30f, FontStyle.Bold);
        private readonly Font _dateFont = Ui.F(10.5f, FontStyle.Regular);
        private readonly Font _labelFont = Ui.F(9f, FontStyle.Regular);
        private readonly Font _valFont = Ui.F(9f, FontStyle.Bold);
        private readonly Font _titleFont = Ui.F(11f, FontStyle.Bold);
        private readonly Font _subFont = Ui.F(9.5f, FontStyle.Regular);
        private readonly Font _gl = Glyphs.Font(Ui.Pf(18)), _glS = Glyphs.Font(Ui.Pf(14));

        public NotchForm()
        {
            _w = ColW; _h = ColH;
            Configure(MaxW, MaxH);
            IslandTimer.Finished += delegate { _hud = "timer"; _hudLeft = 300; SoundKit.Preview("Lonceng"); };
            Rec.Init(this);
            Rec.Saved = delegate (string path, Bitmap thumb, string kind) { ShowShot(path, thumb, kind == "audio" ? "audio" : "video"); };
            Rec.Failed = delegate (string msg)
            {
                MethodInvoker m = delegate { _hud = "err"; _hudText = msg; _hudLeft = 300; Dirty = true; };
                if (IsHandleCreated && InvokeRequired) BeginInvoke(m); else m();
            };
            ShotWatch.Shot = delegate (string path, Bitmap thumb) { ShowShot(path, thumb, "shot"); };
            PrivacyWatch.Changed = OnPrivacy;
        }

        private List<string> Cards()
        {
            List<string> c = new List<string>();
            if (HasMedia) c.Add("media");
            c.Add("clock");
            if (Settings.Get("IslandRec", true) || Rec.Active) c.Add("rec");
            c.Add("notif");
            c.Add("timer");
            if (Settings.Get("IslandWeather", true)) c.Add("weather");
            return c;
        }

        private void Go(int dir)
        {
            List<string> c = Cards();
            int i = Math.Max(0, c.IndexOf(_card));
            int j = (i + dir + c.Count) % c.Count;
            if (j == i) return;
            _card = c[j]; _slide = dir > 0 ? 1 : -1;
            if (_card == "notif") _notifs = NotifDb.Visible(3);
            if (_card == "weather") Weather.Refresh(false);
            Dirty = true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Wheel(-e.Delta);
        }

        private void Wheel(int delta)
        {
            if (!(_hover || _pinned)) return;
            int d = _gate.Feed(delta);
            if (d != 0) Go(d);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020E) { Wheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF)); m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        public override void Reposition()
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            Location = new Point(s.Left + s.Width / 2 - MaxW / 2, s.Top + Offset - _yOff);
            Dirty = true;
        }

        private void SampleStats()
        {
            long idle, kern, user;
            if (Native.GetSystemTimes(out idle, out kern, out user))
            {
                long di = idle - _pIdle, dk = kern - _pKern, du = user - _pUser;
                if (_pKern != 0 && dk + du > 0) _cpu = Math.Max(0f, Math.Min(1f, 1f - (float)di / (dk + du)));
                _pIdle = idle; _pKern = kern; _pUser = user;
            }
            Native.MEMORYSTATUSEX m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref m)) _ram = m.dwMemoryLoad / 100f;
            PowerStatus ps = SystemInformation.PowerStatus;
            _bat = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 ? -1 : ps.BatteryLifePercent;
        }

        private bool HasMedia { get { return _np != null && Settings.Get("IslandMedia", true) && !MediaDismissed; } }

        // Media yang dijeda lama / ditutup manual disembunyikan dari island sampai diputar lagi.
        private string _dismissed;
        private DateTime _pausedSince = DateTime.MinValue;
        private static string MediaKey(NowPlaying n) { return n == null ? "" : (n.App ?? "") + "|" + (n.Title ?? ""); }
        private bool MediaDismissed { get { return _np != null && _dismissed != null && _dismissed == MediaKey(_np) && !_np.Playing; } }

        private void TrackPause()
        {
            if (_np == null) { _pausedSince = DateTime.MinValue; return; }
            if (_np.Playing) { _pausedSince = DateTime.MinValue; if (_dismissed == MediaKey(_np)) _dismissed = null; return; }
            if (_pausedSince == DateTime.MinValue) _pausedSince = DateTime.Now;
            int mins = Settings.Get("IslandMediaAutoHide", true) ? 3 : 0;
            if (mins > 0 && _dismissed != MediaKey(_np) && (DateTime.Now - _pausedSince).TotalMinutes >= mins)
            { _dismissed = MediaKey(_np); if (_card == "media") _card = "clock"; Dirty = true; }
        }

        private void DismissMedia()
        {
            if (_np == null) return;
            if (_np.Playing) { Native.Keys(Native.VK_MEDIA_PLAY); _np.Playing = false; }   // hentikan pemutaran
            _dismissed = MediaKey(_np);
            _pausedSince = DateTime.Now;
            if (_card == "media") { _card = "clock"; _slide = 1; }
            SoundKit.Feedback();
            Dirty = true;
        }

        // Dipanggil saat tombol volume ditekan (pop-up Windows ditelan): ubah volume & tampilkan HUD seketika.
        public void VolumeKey(int dir)
        {
            float v = SystemControls.Volume; bool mu = SystemControls.Muted;
            if (v < 0) return;
            if (dir == 0) { mu = !mu; SystemControls.Muted = mu; }
            else
            {
                v = (float)Math.Max(0, Math.Min(1, Math.Round(v * 50 + dir) / 50.0));   // langkah 2% seperti Windows
                SystemControls.Volume = v;
                if (mu) { mu = false; SystemControls.Muted = false; }
            }
            _curVol = _lastVol = v; _curMute = _lastMute = mu;
            _hud = "vol"; _hudVal = mu ? 0 : v; _hudLeft = 100;
            Dirty = true;
        }

        private int _lastBright = -1;

        // Kecerahan berubah (tombol Fn, sensor, slider): HUD island menggantikan pop-up Windows.
        public void BrightnessChanged(int level)
        {
            if (level == _lastBright) return;
            bool first = _lastBright < 0;
            _lastBright = level;
            if (first) return;
            _hud = "bright"; _hudVal = Math.Max(0, Math.Min(100, level)) / 100f; _hudLeft = 100;
            Dirty = true;
        }

        private void SetVolFromX(float px)
        {
            if (_volTrack.Width <= 0) return;
            float v = Math.Max(0f, Math.Min(1f, (px - _volTrack.X) / _volTrack.Width));
            v = (float)Math.Round(v * 100) / 100f;
            SystemControls.Volume = v;
            if (_curMute && v > 0) SystemControls.Muted = false;
            _curVol = _lastVol = v; _curMute = _lastMute = false;
            Dirty = true;
        }

        // Slider volume di dalam island
        private void VolSlider(float x0, float x1, float cy, int alpha)
        {
            float v = _curMute ? 0 : Math.Max(0, _curVol);
            RectangleF ic = new RectangleF(x0, cy - Ui.Pf(10), Ui.Pf(20), Ui.Pf(20));
            using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(alpha * 0.8), 230, 232, 240)))
                Glyphs.Draw(G, Glyphs.VolFor(v, v <= 0), Glyphs.Font(Ui.Pf(12)), soft, ic);
            bool hot = _volDrag || _btnHover == "vol";
            float th = hot ? Ui.Pf(7) : Ui.Pf(5);
            RectangleF track = new RectangleF(ic.Right + Ui.Pf(6), cy - th / 2f, Math.Max(Ui.Pf(30), x1 - ic.Right - Ui.Pf(6)), th);
            _volTrack = track;
            using (GraphicsPath tp = Glass.Round(track, th / 2)) using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(alpha * 0.25), 255, 255, 255))) G.FillPath(tb, tp);
            float fw = Math.Max(th, track.Width * v);
            using (GraphicsPath fp = Glass.Round(new RectangleF(track.X, track.Y, fw, th), th / 2)) using (SolidBrush fb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) G.FillPath(fb, fp);
            if (hot)
            {
                float kx = track.X + fw - th / 2f, ks = Ui.Pf(12);
                using (SolidBrush kb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) G.FillEllipse(kb, kx - ks / 2f, cy - ks / 2f, ks, ks);
            }
            RectangleF hit = new RectangleF(ic.X, cy - Ui.Pf(11), track.Right - ic.X + Ui.Pf(6), Ui.Pf(22));
            _btn["vol"] = hit;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_volDrag) SetVolFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_volDrag) { _volDrag = false; Dirty = true; }
        }

        private void CheckHud()
        {
            if (_tick % 6 == 0 && !_volDrag)
            {
                float v = SystemControls.Volume; bool mu = SystemControls.Muted;
                if (v != _curVol || mu != _curMute) { _curVol = v; _curMute = mu; if (_hover || _pinned) Dirty = true; }
                if (Settings.Get("IslandHud", true) && _lastVol > -2 && v >= 0 && (Math.Abs(v - _lastVol) > 0.004f || mu != _lastMute)) { _hud = "vol"; _hudVal = mu ? 0 : v; _hudLeft = 100; }
                _lastVol = v; _lastMute = mu;
            }
            if (!Settings.Get("IslandHud", true)) return;
            if (_tick % 30 == 0)
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                if (_lastLine == PowerLineStatus.Offline && ps.PowerLineStatus == PowerLineStatus.Online && (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0)
                { _hud = "charge"; _hudVal = ps.BatteryLifePercent; _hudLeft = 150; }
                _lastLine = ps.PowerLineStatus;
            }
        }

        protected override bool OnFrame()
        {
            _tick++;
            bool dirty = false;
            if (Media.Version != _mediaVer) { _mediaVer = Media.Version; _np = Media.Current; dirty = true; TrackPause(); }
            else if (_tick % 60 == 0) TrackPause();
            CheckHud();
            if (_hudLeft > 0) { _hudLeft--; dirty = true; if (_hudLeft == 0) _hud = null; }

            int yOff = Hidden ? Offset : 0;
            if (yOff != _yOff) { _yOff = yOff; Reposition(); }
            Point p = LocalCursor(); p.Y -= _yOff;
            double halfW = Math.Max(_w, Hidden ? ColW : 0) / 2 + Ui.P(30);
            double hoverBottom = Hidden && _h < ColH - 2 ? ColH - _yOff : _h + Ui.P(18);
            _hover = p.X >= MaxW / 2 - halfW && p.X <= MaxW / 2 + halfW && p.Y >= -Ui.P(8) - _yOff && p.Y <= hoverBottom;
            bool shotHud = _hud == "shot";
            if (shotHud && _hover && _hudLeft < 90) _hudLeft = 90;      // tetap tampil selama kursor di atasnya
            bool expand = (_hover && !shotHud) || _pinned;
            _expanded = expand;
            double tw, th;
            if (expand && !_wasExpanded && _hover && !_pinned && Settings.Get("IslandHoverSound", true)) IslandTick.Play();
            if (expand && !_wasExpanded) { _card = _wantCard ?? (Rec.Active ? "rec" : HasMedia ? "media" : "clock"); _wantCard = null; _slide = 0; _notifs = NotifDb.Visible(3); Weather.Refresh(false); }
            if (!Cards().Contains(_card)) _card = "clock";
            _wasExpanded = expand;
            float dropT = (!expand && _hud != null) ? Ui.Pf(6) : 0f;
            if (Math.Abs(_drop - dropT) > 0.2f) { _drop += (dropT - _drop) * 0.22f; dirty = true; } else _drop = dropT;
            if (expand) { tw = ExpW; th = MediaExpH; }
            else if (_hud == "shot") { tw = ShotW; th = ShotH; }
            else if (_hud == "err") { tw = ShotW; th = HudH; }
            else if (_hud != null) { tw = HudW; th = HudH; }
            else if (Rec.Active) { tw = MediaColW; th = ColH; }
            else if (Hidden && !IslandTimer.Running && !PrivActive) { tw = Ui.P(56); th = 0; }   // tersembunyi: masuk ke tepi layar
            else if ((HasMedia && !Hidden) || IslandTimer.Running) { tw = MediaColW; th = ColH; }
            else if (Hidden) { tw = Ui.P(64); th = ColH; }
            else { tw = ColW; th = ColH; }
            if (!expand && _hud == null && PrivActive) tw += Ui.P(16);   // ruang untuk titik kamera/mic
            // pegas sedikit memantul seperti Dynamic Island asli; mode tersembunyi lebih "cair" (tinggi dulu, lalu melebar)
            bool liquid = Hidden;
            double kw = liquid ? 0.105 : 0.16, kh = liquid ? 0.15 : 0.16, damp = liquid ? 0.75 : 0.70;
            _vw = (_vw + (tw - _w) * kw) * damp; _vh = (_vh + (th - _h) * kh) * damp;
            _w += _vw; _h += _vh;
            bool anim = Math.Abs(tw - _w) > 0.3 || Math.Abs(th - _h) > 0.3 || Math.Abs(_vw) > 0.3 || Math.Abs(_vh) > 0.3;
            if (!anim) { _w = tw; _h = th; _vw = _vh = 0; }
            dirty |= anim;

            if (_tick % 60 == 0 || _tick == 1) { SampleStats(); if (expand) dirty = true; }
            bool idle = !expand && _hud == null && !Rec.Active && !HasMedia && !IslandTimer.Running && !Hidden && !PrivActive && !anim;
            if (_idleT >= 0)
            {
                if (!idle) { _idleT = -1; _idleNext = _tick + 600; }
                else { _idleT += 1f / 60f; dirty = true; if (_idleT >= IdleDur) { _idleT = -1; _idleNext = _tick + 60 * _rnd.Next(18, 40); } }
            }
            else if (idle && Settings.Get("IslandIdleAnim", true))
            {
                if (_idleNext == 0) _idleNext = _tick + 60 * 8;
                else if (_tick >= _idleNext) { PickIdle(); _idleT = 0; }
            }
            if (HasMedia && _np.Playing && _tick % 2 == 0) dirty = true;   // equalizer & progress (30 fps)
            if (Rec.Active && _tick % 2 == 0) dirty = true;                 // waveform & waktu rekam
            if (expand && _tick % 2 == 0 && Settings.Get("IslandAura", true)) dirty = true;   // latar gradasi bergerak
            if (PrivActive && _tick % 4 == 0) dirty = true;                 // titik privasi berdenyut
            if (IslandTimer.Running && _tick % 15 == 0) { TimeSpan l = IslandTimer.Left; dirty = true; }
            if (Math.Abs(_slide) > 0.001) { _slide *= 0.80; if (Math.Abs(_slide) < 0.008) _slide = 0; dirty = true; }
            {
                List<string> cl = Cards(); int ci = Math.Max(0, cl.IndexOf(_card));
                if (Math.Abs(_dotPos - ci) > 0.01) { _dotPos += (ci - _dotPos) * 0.25f; dirty = true; } else _dotPos = ci;
            }
            if (_removing >= 0)
            {
                _removeT += 0.09f; dirty = true;
                if (_removeT >= 1) { NotifDb.Dismiss(_removing); _removing = -1; _removeT = 0; _notifs = NotifDb.Visible(3); }
            }
            if (_press > 0) { _press = Math.Max(0, _press - 0.12f); dirty = true; }
            for (int i = 0; i < _rowGlow.Length; i++)
            {
                float tg = i == _rowHover ? 1f : 0f;
                if (Math.Abs(_rowGlow[i] - tg) > 0.02f) { _rowGlow[i] += (tg - _rowGlow[i]) * 0.3f; dirty = true; } else _rowGlow[i] = tg;
            }
            if (_tick % 3600 == 5) Weather.Refresh(false);
            if (Weather.Version != _weatherVer) { _weatherVer = Weather.Version; dirty = true; }

            string bh = null;
            if (expand) foreach (KeyValuePair<string, RectangleF> kv in _btn) if (kv.Value.Contains(p)) { bh = kv.Key; if (!kv.Key.StartsWith("notif")) break; }
            int rh = -1;
            if (expand && _card == "notif") for (int i = 0; i < 3; i++) { RectangleF rr; if (_btn.TryGetValue("notif" + i, out rr) && rr.Contains(p)) rh = i; }
            if (rh != _rowHover) { _rowHover = rh; dirty = true; }
            if (bh != _btnHover) { _btnHover = bh; dirty = true; }
            return dirty;
        }

        protected override void Render()
        {
            G.Clear(Color.Transparent);
            G.TranslateTransform(0, (float)Math.Round(_drop) + _yOff);
            try { RenderIsland(); } finally { G.ResetTransform(); }
        }

        private void RenderIsland()
        {
            G.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // latar pil hitam pekat: ClearType tajam
            _btn.Clear();
            float w = (float)_w, h = (float)_h;
            if (h < 1f || w < 2f) return;
            float x = (float)Math.Round((MaxW - w) / 2f);
            float r = Math.Min(h / 2f, Ui.Pf(30));
            _ri = (PrivActive && !_expanded && _hud == null) ? Ui.Pf(16) : 0f;
            if (_idleT >= 0 && _hud == null && !_expanded) { DrawIdle(x, w, h); return; }
            using (GraphicsPath path = Glass.Round(new RectangleF(x, 0.5f, w, h), r))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 0, 0, 0))) G.FillPath(b, path);
                using (Pen rim = new Pen(Color.FromArgb(55, 255, 255, 255), 1f)) G.DrawPath(rim, path);
            }
            if (_yOff > 0) DrawNeck(x, w, h, r);   // setelah garis tepi: island & tepi layar menyatu seperti cairan

            float expandT = (!_expanded && (_hud == "shot" || _hud == "err")) ? 0f : (float)Clamp((h - HudH - 4) / (MediaExpH - HudH - 4.0));
            if (expandT < 0.05f)
            {
                if (_hud == "shot") { if (h >= ShotH * 0.55f) DrawShot(x, w, h); }
                else if (_hud == "err") { if (h >= HudH - 3) DrawErr(x, w, h); }
                else if (_hud != null && h >= HudH - 3) DrawHud(x, w, h);
                else if (Rec.Active && w > ColW * 0.9f) DrawRecCollapsed(x, w, h);
                else if (((HasMedia && !Hidden) || IslandTimer.Running) && w > ColW + Ui.P(20)) DrawMediaCollapsed(x, w, h);
                if (_ri > 0 && h >= ColH - 4) PrivDot(x, w, h);
                return;
            }
            int alpha = (int)(expandT * 255);
            using (GraphicsPath clip = Glass.Round(new RectangleF(x, 0.5f, w, h), r))
            {
                Region old = G.Clip; G.SetClip(clip, CombineMode.Intersect);
                if (Settings.Get("IslandAura", true)) DrawAura(x, w, h, expandT);
                List<string> cards = Cards();
                int ci = Math.Max(0, cards.IndexOf(_card));
                double t = Math.Abs(_slide);
                float off = (float)(_slide * w * 0.55);   // geser lebih pendek + pudar = terasa halus
                DrawCard(_card, x + off, w, (int)(alpha * (1 - t * 0.9)), true);
                if (off != 0)
                {
                    int ni = (ci + (off > 0 ? -1 : 1) + cards.Count) % cards.Count;
                    DrawCard(cards[ni], x + off + (off > 0 ? -w : w) * 0.55f, w, (int)(alpha * t * 0.8), false);
                }
                G.Clip = old; old.Dispose();
                // titik indikator kartu
                if (cards.Count > 1)
                {
                    float d = Ui.Pf(5), gap = Ui.Pf(6), wide = Ui.Pf(14);
                    float total = (cards.Count - 1) * (d + gap) + wide;
                    float tx = x + w / 2f - total / 2f, ty = h - Ui.Pf(9);
                    float cxp = tx;
                    for (int i = 0; i < cards.Count; i++)
                    {
                        float k = Math.Max(0f, 1f - Math.Abs(_dotPos - i));          // 1 = aktif
                        float dw = d + (wide - d) * k;
                        RectangleF dr = new RectangleF(cxp, ty, dw, d);
                        using (GraphicsPath dp = Glass.Round(dr, d / 2))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(alpha * (0.32 + 0.63 * k)), 255, 255, 255))) G.FillPath(b, dp);
                        RectangleF hit = dr; hit.Inflate(Ui.Pf(3), Ui.Pf(5));
                        _btn["dot" + i] = hit;
                        cxp += dw + gap;
                    }
                }
            }
        }

        private void DrawCard(string id, float x, float w, int alpha, bool interactive)
        {
            Dictionary<string, RectangleF> saved = null;
            if (!interactive) saved = new Dictionary<string, RectangleF>(_btn);
            switch (id)
            {
                case "media": if (HasMedia) DrawMediaExpanded(x, w, alpha); break;
                case "notif": DrawNotifCard(x, w, alpha); break;
                case "timer": DrawTimerCard(x, w, alpha); break;
                case "weather": DrawWeatherCard(x, w, alpha); break;
                case "rec": DrawRecCard(x, w, alpha); break;
                default: DrawClockExpanded(x, w, alpha); break;
            }
            if (!interactive) { _btn.Clear(); foreach (KeyValuePair<string, RectangleF> kv in saved) _btn[kv.Key] = kv.Value; }
        }

        private void CardTitle(string t, float x, int alpha)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(alpha * 0.6), 220, 222, 232)))
                G.DrawString(t.ToUpperInvariant(), _labelFont, b, (float)Math.Round(x + Ui.Pf(22)), Ui.P(14));
        }

        private void DrawNotifCard(float x, float w, int alpha)
        {
            CardTitle("Notifikasi", x, alpha);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(alpha * 0.66), 215, 218, 228)))
            using (SolidBrush blue = new SolidBrush(Color.FromArgb(alpha, 100, 175, 255)))
            {
                if (_notifs.Count > 0)
                {
                    string all = "Hapus semua";
                    SizeF az = G.MeasureString(all, _labelFont);
                    RectangleF ar = new RectangleF(x + w - Ui.Pf(22) - az.Width, Ui.Pf(12), az.Width + Ui.Pf(4), az.Height + Ui.Pf(2));
                    G.DrawString(all, _labelFont, blue, (float)Math.Round(ar.X), Ui.P(14));
                    _btn["nclear"] = ar;
                }
                float y = Ui.Pf(36), tw = w - Ui.Pf(70);
                if (_notifs.Count == 0)
                {
                    Glyphs.Draw(G, "\uE73E", _gl, soft, new RectangleF(x + w / 2f - Ui.Pf(60), Ui.Pf(50), Ui.Pf(24), Ui.Pf(24)));
                    G.DrawString("Semua beres, tidak ada notifikasi", _subFont, soft, (float)Math.Round(x + w / 2f - Ui.Pf(30)), (float)Math.Round(Ui.Pf(53)));
                }
                for (int i = 0; i < Math.Min(2, _notifs.Count); i++)
                {
                    Notif n = _notifs[i];
                    float dx = 0, fade = 1;
                    if (n.Id == _removing) { float e = _removeT * _removeT; dx = e * w * 0.6f; fade = 1 - _removeT; }
                    RectangleF row = new RectangleF(x + Ui.Pf(14) + dx, y, w - Ui.Pf(28), Ui.Pf(48));
                    int ra = (int)(alpha * fade);
                    float glow = i < _rowGlow.Length ? _rowGlow[i] : 0;
                    using (GraphicsPath rp = Glass.Round(row, Ui.Pf(12)))
                    using (SolidBrush rb = new SolidBrush(Color.FromArgb((int)(ra * (0.10 + 0.10 * glow)), 255, 255, 255))) G.FillPath(rb, rp);
                    using (SolidBrush ws = new SolidBrush(Color.FromArgb(ra, 255, 255, 255)))
                    using (SolidBrush ss = new SolidBrush(Color.FromArgb((int)(ra * 0.66), 215, 218, 228)))
                    {
                        string head = n.App + "  \u00B7  " + NotifDb.Ago(n.Time);
                        G.DrawString(Trim(G, head, _labelFont, tw), _labelFont, ss, (float)Math.Round(row.X + Ui.Pf(10)), (float)Math.Round(row.Y + Ui.Pf(6)));
                        string body = (n.Title ?? "") + (string.IsNullOrEmpty(n.Body) ? "" : " \u2014 " + n.Body.Replace("\n", " "));
                        G.DrawString(Trim(G, body, _subFont, tw), _subFont, ws, (float)Math.Round(row.X + Ui.Pf(10)), (float)Math.Round(row.Y + Ui.Pf(23)));
                        // tombol hapus (x) muncul saat baris disorot
                        RectangleF xr = new RectangleF(row.Right - Ui.Pf(30), row.Y + (row.Height - Ui.Pf(22)) / 2f, Ui.Pf(22), Ui.Pf(22));
                        int xa = (int)(ra * (0.35 + 0.65 * glow));
                        using (SolidBrush xb = new SolidBrush(Color.FromArgb((int)(xa * (_btnHover == "ndel" + i ? 0.45 : 0.22)), 255, 255, 255))) G.FillEllipse(xb, xr);
                        using (SolidBrush xg = new SolidBrush(Color.FromArgb(xa, 255, 255, 255))) Glyphs.Draw(G, "\uE711", Glyphs.Font(Ui.Pf(10)), xg, xr);
                        if (_removing < 0) _btn["ndel" + i] = xr;
                    }
                    if (_removing < 0) _btn["notif" + i] = row;
                    // baris di bawah yang sedang dihapus naik perlahan
                    float step = Ui.Pf(54);
                    y += (n.Id == _removing) ? step * (1 - _removeT * _removeT) : step;
                }
            }
        }

        private void DrawTimerCard(float x, float w, int alpha)
        {
            CardTitle("Timer", x, alpha);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (SolidBrush orange = new SolidBrush(Color.FromArgb(alpha, 255, 159, 10)))
            {
                string t = IslandTimer.Text(IslandTimer.Left);
                G.DrawString(t, _bigFont, IslandTimer.Running ? orange : white, (float)Math.Round(x + Ui.Pf(20)), Ui.P(38));
                // progres melingkar
                RectangleF ring = new RectangleF(x + w - Ui.Pf(86), Ui.Pf(34), Ui.Pf(54), Ui.Pf(54));
                double frac = IslandTimer.Total.TotalSeconds > 0 ? IslandTimer.Left.TotalSeconds / IslandTimer.Total.TotalSeconds : 0;
                using (Pen bgp = new Pen(Color.FromArgb((int)(alpha * 0.2), 255, 255, 255), Ui.Pf(5))) G.DrawEllipse(bgp, ring);
                using (Pen fp = new Pen(Color.FromArgb(alpha, 255, 159, 10), Ui.Pf(5)))
                {
                    fp.StartCap = fp.EndCap = LineCap.Round;
                    if (frac > 0.002) G.DrawArc(fp, ring, -90, (float)(360 * frac));
                }
                Button("tgo", IslandTimer.Running ? "\uE769" : "\uE768", ring.X + ring.Width / 2f, ring.Y + ring.Height / 2f - Ui.Pf(14), Ui.Pf(28), alpha);
                float bx = x + Ui.Pf(22), by = Ui.Pf(92);
                string[] lbl = { "5 mnt", "15 mnt", "25 mnt", "Reset" };
                string[] ids = { "t5", "t15", "t25", "treset" };
                for (int i = 0; i < 4; i++)
                {
                    SizeF ls = G.MeasureString(lbl[i], _labelFont);
                    RectangleF br = new RectangleF(bx, by, ls.Width + Ui.Pf(16), Ui.Pf(24));
                    using (GraphicsPath bp = Glass.Round(br, br.Height / 2)) using (SolidBrush bb = new SolidBrush(Color.FromArgb((int)(alpha * (_btnHover == ids[i] ? 0.28 : 0.14)), 255, 255, 255))) G.FillPath(bb, bp);
                    G.DrawString(lbl[i], _labelFont, white, (float)Math.Round(br.X + Ui.Pf(8)), (float)Math.Round(br.Y + (br.Height - ls.Height) / 2f));
                    _btn[ids[i]] = br;
                    bx = br.Right + Ui.Pf(8);
                }
            }
        }

        private void DrawWeatherCard(float x, float w, int alpha)
        {
            CardTitle("Cuaca", x, alpha);
            WeatherInfo wi = Weather.Current;
            using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(alpha * 0.66), 215, 218, 228)))
            {
                if (wi == null)
                {
                    G.DrawString("Memuat cuaca...", _subFont, soft, (float)Math.Round(x + Ui.Pf(22)), Ui.P(60));
                    return;
                }
                Weather.DrawIcon(G, wi.Cond, new RectangleF(x + Ui.Pf(20), Ui.Pf(36), Ui.Pf(60), Ui.Pf(60)), alpha);
                G.DrawString(wi.Temp, _bigFont, white, (float)Math.Round(x + Ui.Pf(92)), Ui.P(34));
                G.DrawString(Trim(G, wi.Cond, _subFont, w - Ui.Pf(120)), _subFont, white, (float)Math.Round(x + Ui.Pf(94)), Ui.P(72));
                G.DrawString(Trim(G, wi.Place + "  \u00B7  Lembap " + wi.Humidity + "  \u00B7  Angin " + wi.Wind, _labelFont, w - Ui.Pf(120)), _labelFont, soft, (float)Math.Round(x + Ui.Pf(94)), Ui.P(94));
                _btn["weather"] = new RectangleF(x + Ui.Pf(14), Ui.Pf(30), w - Ui.Pf(28), Ui.Pf(90));
            }
        }

        private void RoundImage(Bitmap img, RectangleF r, float rad, int alpha)
        {
            using (GraphicsPath p = Glass.Round(r, rad))
            {
                if (img != null)
                {
                    Region old = G.Clip; G.SetClip(p, CombineMode.Intersect);
                    if (alpha >= 250) G.DrawImage(img, r);
                    else using (ImageAttributes ia = new ImageAttributes())
                        {
                            ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = alpha / 255f; ia.SetColorMatrix(cm);
                            G.DrawImage(img, Rectangle.Round(r), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                        }
                    G.Clip = old; old.Dispose();
                }
                else
                    using (LinearGradientBrush lb = new LinearGradientBrush(r, Color.FromArgb(alpha, 255, 55, 95), Color.FromArgb(alpha, 120, 80, 255), 45f)) G.FillPath(lb, p);
            }
        }

        private void DrawMediaCollapsed(float x, float w, float h)
        {
            float s = h - Ui.Pf(10);
            if (IslandTimer.Running)
            {
                using (SolidBrush ob = new SolidBrush(Color.FromArgb(255, 255, 159, 10)))
                {
                    if (HasMedia) RoundImage(_np.Art, new RectangleF(x + Ui.Pf(8), Ui.Pf(5.5), s, s), Ui.Pf(5), 255);
                    else Glyphs.Draw(G, "\uE916", _glS, ob, new RectangleF(x + Ui.Pf(8), 0, s, h));
                    string t = IslandTimer.Text(IslandTimer.Left);
                    SizeF ts = G.MeasureString(t, _valFont);
                    G.DrawString(t, _valFont, ob, (float)Math.Round(x + w - _ri - Ui.Pf(14) - ts.Width), (float)Math.Round((h - ts.Height) / 2f));
                }
                return;
            }
            RoundImage(_np.Art, new RectangleF(x + Ui.Pf(8), Ui.Pf(5.5), s, s), Ui.Pf(5), 255);
            // equalizer
            float bx = x + w - _ri - Ui.Pf(30), by = h / 2f + 0.5f, bw = Ui.Pf(2.6);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 92, 120)))
                for (int i = 0; i < 4; i++)
                {
                    double ph = _tick * 0.18 + i * 1.7;
                    float hh = _np.Playing ? (float)(Ui.Pf(4) + (Math.Sin(ph) * 0.5 + 0.5) * Ui.Pf(10) * (0.6 + 0.4 * Math.Sin(ph * 0.37 + i))) : Ui.Pf(3);
                    RectangleF br = new RectangleF(bx + i * Ui.Pf(5), by - hh / 2f, bw, hh);
                    using (GraphicsPath bp = Glass.Round(br, bw / 2)) G.FillPath(b, bp);
                }
        }

        private void DrawHud(float x, float w, float h)
        {
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                RectangleF ic = new RectangleF(x + Ui.Pf(12), 0, Ui.Pf(24), h);
                string label;
                Color fill;
                if (_hud == "timer")
                {
                    using (SolidBrush ob = new SolidBrush(Color.FromArgb(255, 159, 10))) Glyphs.Draw(G, "\uE916", _glS, ob, ic);
                    G.DrawString("Timer selesai", _valFont, white, (float)Math.Round(x + Ui.Pf(44)), (float)Math.Round((h - _valFont.GetHeight(G)) / 2f));
                    return;
                }
                if (_hud == "cam" || _hud == "mic")
                {
                    Color pc = _hud == "cam" ? Color.FromArgb(48, 209, 88) : Color.FromArgb(255, 159, 10);
                    using (SolidBrush cb = new SolidBrush(pc)) Glyphs.Draw(G, _hud == "cam" ? "\uE714" : "\uE720", _glS, cb, ic);
                    string t = _hud == "cam" ? "Kamera aktif" : "Mikrofon aktif";
                    float ty = (float)Math.Round((h - _valFont.GetHeight(G)) / 2f);
                    G.DrawString(t, _valFont, white, (float)Math.Round(x + Ui.Pf(44)), ty);
                    float used = G.MeasureString(t, _valFont).Width + Ui.Pf(56);
                    using (SolidBrush ab = new SolidBrush(Color.FromArgb(170, 225, 228, 240)))
                    {
                        string app = Trim(G, _hudText ?? "", _labelFont, w - used - Ui.Pf(16));
                        SizeF az = G.MeasureString(app, _labelFont);
                        G.DrawString(app, _labelFont, ab, (float)Math.Round(x + w - Ui.Pf(16) - az.Width), (float)Math.Round((h - az.Height) / 2f));
                    }
                    return;
                }
                if (_hud == "bright")
                {
                    Glyphs.Draw(G, Glyphs.Sun, _glS, white, ic);
                    label = ((int)Math.Round(_hudVal * 100)).ToString();
                    fill = Color.White;
                }
                else if (_hud == "vol")
                {
                    Glyphs.Draw(G, Glyphs.VolFor(_hudVal, _hudVal <= 0), _glS, white, ic);
                    label = ((int)Math.Round(_hudVal * 100)).ToString();
                    fill = Color.White;
                }
                else
                {
                    Glyphs.Draw(G, Glyphs.Bolt, _glS, new SolidBrush(Color.FromArgb(48, 209, 88)), ic);
                    label = ((int)Math.Round(_hudVal * 100)) + "%";
                    fill = Color.FromArgb(48, 209, 88);
                }
                SizeF ls = G.MeasureString(label, _valFont);
                float lx = (float)Math.Round(x + w - Ui.Pf(14) - ls.Width);
                G.DrawString(label, _valFont, white, lx, (float)Math.Round((h - ls.Height) / 2f));
                RectangleF track = new RectangleF(x + Ui.Pf(44), h / 2f - Ui.Pf(3), lx - (x + Ui.Pf(44)) - Ui.Pf(10), Ui.Pf(6));
                using (GraphicsPath tp = Glass.Round(track, track.Height / 2)) using (SolidBrush tb = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) G.FillPath(tb, tp);
                float fw = Math.Max(track.Height, track.Width * Math.Max(0f, Math.Min(1f, _hudVal)));
                using (GraphicsPath fp = Glass.Round(new RectangleF(track.X, track.Y, fw, track.Height), track.Height / 2)) using (SolidBrush fb = new SolidBrush(fill)) G.FillPath(fb, fp);
            }
        }

        private static float EaseIO(float v) { v = Math.Max(0, Math.Min(1, v)); return v < 0.5f ? 4 * v * v * v : 1 - (float)Math.Pow(-2 * v + 2, 3) / 2; }
        private static float EaseBack(float v) { v = Math.Max(0, Math.Min(1, v)); const float c1 = 1.9f, c3 = c1 + 1; return 1 + c3 * (float)Math.Pow(v - 1, 3) + c1 * (float)Math.Pow(v - 1, 2); }
        private static float Win(float t, float a, float b) { if (t <= a || t >= b) return 0; return (float)Math.Sin(Math.PI * (t - a) / (b - a)); }

        // Animasi standby: menyusut sedikit -> tepi bergelombang -> pusaran air di dalam -> memantul kembali.
        private void DrawIdle(float x0, float w, float h)
        {
            float t = _idleT;
            float k = t < 0.45f ? EaseIO(t / 0.45f) : t < 2.75f ? 1f : 1f - EaseBack((t - 2.75f) / 0.65f);
            float W = w * (1f - 0.09f * k), H = h * (1f - 0.16f * k);
            float cx = x0 + w / 2f, left = cx - W / 2f, top = 0.5f, r = H / 2f;
            bool heart = _idleKind == "jantung";
            float amp = Ui.Pf(3.4f) * Win(t, 0.35f, 1.75f) * (heart ? 0.35f : 1f);
            float ph = t * 13f;
            List<PointF> pts = new List<PointF>();
            int n = 36;
            for (int i = 0; i <= n; i++)                      // tepi atas (gelombang kecil)
            {
                float u = i / (float)n, px = left + r + (W - 2 * r) * u;
                pts.Add(new PointF(px, top + (float)(Math.Sin(u * Math.PI * 3 + ph) * amp * 0.25 * Math.Sin(Math.PI * u))));
            }
            for (int i = 1; i < 18; i++) { double a = -Math.PI / 2 + Math.PI * i / 18; pts.Add(new PointF(left + W - r + (float)Math.Cos(a) * r, top + r + (float)Math.Sin(a) * r)); }
            for (int i = 0; i <= n; i++)                      // tepi bawah (gelombang utama)
            {
                float u = i / (float)n, px = left + W - r - (W - 2 * r) * u;
                pts.Add(new PointF(px, top + H + (float)(Math.Sin(u * Math.PI * 3 - ph) * amp * Math.Sin(Math.PI * u))));
            }
            for (int i = 1; i < 18; i++) { double a = Math.PI / 2 + Math.PI * i / 18; pts.Add(new PointF(left + r + (float)Math.Cos(a) * r, top + r + (float)Math.Sin(a) * r)); }
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(pts.ToArray());
                using (SolidBrush b = new SolidBrush(Color.Black)) G.FillPath(b, p);
                using (Pen rim = new Pen(Color.FromArgb(55, 255, 255, 255), 1f)) G.DrawPath(rim, p);
                // pusaran air: titik-titik bercahaya berputar dari tepi ke tengah
                if (heart) { DrawHeartbeat(p, left, top, W, H, r, t); return; }
                float sw = Win(t, 1.25f, 3.0f);
                if (sw > 0.01f)
                {
                    Region old = G.Clip; G.SetClip(p, CombineMode.Intersect);
                    float rot = (t - 1.25f) * 7.5f, cy = top + H / 2f;
                    for (int arm = 0; arm < 3; arm++)
                    {
                        PointF prev = PointF.Empty;
                        for (int i = 0; i <= 44; i++)
                        {
                            float f = i / 44f;                       // 0 = tepi luar, 1 = pusat
                            double a = rot + arm * Math.PI * 2 / 3 + f * Math.PI * 2.4;
                            float rr = (float)Math.Pow(1f - f, 0.85);
                            PointF pt = new PointF(cx + (float)Math.Cos(a) * rr * (W / 2f - r * 0.3f), cy + (float)Math.Sin(a) * rr * (H / 2f - Ui.Pf(2)));
                            if (i > 0)
                            {
                                int al = (int)(sw * 150 * Math.Sin(Math.PI * f));
                                Color c = Color.FromArgb(Math.Max(0, al), (int)(178 + 52 * f), (int)(212 - 22 * f), 255);
                                using (Pen sp = new Pen(c, Ui.Pf(0.7 + 1.6 * Math.Sin(Math.PI * f)))) { sp.StartCap = sp.EndCap = LineCap.Round; G.DrawLine(sp, prev, pt); }
                            }
                            prev = pt;
                        }
                    }
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        float gr = H * 0.45f;
                        gp.AddEllipse(cx - gr * 1.6f, cy - gr, gr * 3.2f, gr * 2);
                        using (PathGradientBrush pg = new PathGradientBrush(gp)) { pg.CenterColor = Color.FromArgb((int)(sw * 32), 200, 215, 255); pg.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) }; G.FillPath(pg, gp); }
                    }
                    G.Clip = old; old.Dispose();
                }
            }
        }

        // Detak jantung: hati kecil berdenyut + garis EKG berjalan (warna pink lembut)
        private static readonly float[,] Ecg = { { 0, 0 }, { 0.10f, 0 }, { 0.14f, 0.16f }, { 0.18f, 0 }, { 0.27f, 0 }, { 0.30f, -0.2f }, { 0.34f, 1f }, { 0.38f, -0.38f }, { 0.42f, 0 }, { 0.55f, 0 }, { 0.62f, 0.24f }, { 0.70f, 0 }, { 1f, 0 } };
        private static float EcgAt(float ph)
        {
            for (int i = 0; i < Ecg.GetLength(0) - 1; i++)
                if (ph <= Ecg[i + 1, 0]) { float u = (ph - Ecg[i, 0]) / (Ecg[i + 1, 0] - Ecg[i, 0]); return Ecg[i, 1] + (Ecg[i + 1, 1] - Ecg[i, 1]) * u; }
            return 0;
        }

        private void DrawHeartbeat(GraphicsPath clipPath, float left, float top, float W, float H, float r, float t)
        {
            float env = Win(t, 0.3f, 3.15f);
            if (env <= 0.01f) return;
            env = Math.Min(1f, env * 1.6f);
            Region old = G.Clip; G.SetClip(clipPath, CombineMode.Intersect);
            float cy = top + H / 2f, hx = left + r * 1.15f;
            float x0 = hx + r * 0.75f, x1 = left + W - r * 0.55f, period = (x1 - x0) / 2.1f;
            float u = (t - 0.45f) / 2.45f, headX = x0 + u * (x1 - x0) * 1.25f, tail = (x1 - x0) * 0.75f;
            float amp = H * 0.34f;
            Color pink = Color.FromArgb(255, 160, 190);
            PointF prev = PointF.Empty; bool has = false;
            for (float x = Math.Max(x0, headX - tail); x <= Math.Min(headX, x1); x += 1.2f)
            {
                float ph = ((x - x0) / period) % 1f;
                PointF pt = new PointF(x, cy - EcgAt(ph) * amp);
                if (has)
                {
                    float fade = 1f - (headX - x) / tail;
                    using (Pen pen = new Pen(Color.FromArgb((int)(env * 210 * Math.Max(0, fade)), pink), Ui.Pf(1.5))) { pen.StartCap = pen.EndCap = LineCap.Round; G.DrawLine(pen, prev, pt); }
                }
                prev = pt; has = true;
            }
            if (has && headX <= x1)
                using (SolidBrush hb = new SolidBrush(Color.FromArgb((int)(env * 230), 255, 225, 235))) G.FillEllipse(hb, prev.X - Ui.Pf(1.8), prev.Y - Ui.Pf(1.8), Ui.Pf(3.6), Ui.Pf(3.6));
            // hati berdenyut tiap garis melewati puncak
            float phHead = ((headX - x0) / period) % 1f;
            float pulse = headX >= x0 && headX <= x1 ? (float)Math.Exp(-Math.Pow(phHead - 0.35f, 2) / 0.0035) : 0f;
            float hs = H * 0.46f * (1f + 0.38f * pulse);
            using (GraphicsPath hp = new GraphicsPath())
            {
                float x = hx - hs / 2f, y = cy - hs * 0.45f;
                hp.AddBezier(x + hs * 0.5f, y + hs * 0.25f, x + hs * 0.5f, y - hs * 0.05f, x, y - hs * 0.05f, x, y + hs * 0.30f);
                hp.AddBezier(x, y + hs * 0.30f, x, y + hs * 0.60f, x + hs * 0.35f, y + hs * 0.75f, x + hs * 0.5f, y + hs * 0.95f);
                hp.AddBezier(x + hs * 0.5f, y + hs * 0.95f, x + hs * 0.65f, y + hs * 0.75f, x + hs, y + hs * 0.60f, x + hs, y + hs * 0.30f);
                hp.AddBezier(x + hs, y + hs * 0.30f, x + hs, y - hs * 0.05f, x + hs * 0.5f, y - hs * 0.05f, x + hs * 0.5f, y + hs * 0.25f);
                hp.CloseFigure();
                using (SolidBrush gl = new SolidBrush(Color.FromArgb((int)(env * 22 * (0.3f + pulse)), 255, 130, 170))) G.FillEllipse(gl, hx - hs, cy - hs, hs * 2, hs * 2);
                using (LinearGradientBrush hb = new LinearGradientBrush(new RectangleF(x, y - hs * 0.1f, hs, hs * 1.1f), Color.FromArgb((int)(env * 255), 255, 170, 195), Color.FromArgb((int)(env * 255), 255, 110, 150), 90f)) G.FillPath(hb, hp);
            }
            G.Clip = old; old.Dispose();
        }

        // ---------- latar gradasi bergerak (ala Apple Music), dominan hitam ----------
        private Color[] _aura = { Color.FromArgb(110, 70, 230), Color.FromArgb(40, 120, 240), Color.FromArgb(220, 70, 150) };
        private static Color Vivid(Color c)
        {
            int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
            if (mx < 8) return Color.FromArgb(90, 80, 160);
            double k = 230.0 / mx;
            Func<int, int> f = delegate (int v) { double d = (v - mn * 0.6) * k; return (int)Math.Max(0, Math.Min(255, d)); };
            return Color.FromArgb(f(c.R), f(c.G), f(c.B));
        }
        private Color[] AuraTarget()
        {
            switch (_card)
            {
                case "media":
                    Bitmap ab = ArtBlur();
                    if (ab != null) try { return new Color[] { Vivid(ab.GetPixel(2, 2)), Vivid(ab.GetPixel(9, 3)), Vivid(ab.GetPixel(6, 4)) }; } catch { }
                    return new Color[] { Color.FromArgb(230, 60, 110), Color.FromArgb(120, 60, 230), Color.FromArgb(255, 120, 70) };
                case "rec": return new Color[] { Color.FromArgb(235, 60, 50), Color.FromArgb(255, 140, 50), Color.FromArgb(160, 40, 100) };
                case "timer": return new Color[] { Color.FromArgb(255, 150, 30), Color.FromArgb(230, 90, 40), Color.FromArgb(130, 70, 210) };
                case "weather": return new Color[] { Color.FromArgb(40, 150, 235), Color.FromArgb(60, 200, 200), Color.FromArgb(100, 100, 230) };
                case "notif": return new Color[] { Color.FromArgb(40, 175, 165), Color.FromArgb(60, 115, 235), Color.FromArgb(150, 85, 225) };
                default: return new Color[] { Color.FromArgb(110, 70, 230), Color.FromArgb(40, 120, 240), Color.FromArgb(220, 70, 150) };
            }
        }
        private static Color Mix(Color a, Color b, double k) { return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k)); }

        private void DrawAura(float x, float w, float h, float expandT)
        {
            Color[] tg = AuraTarget();
            for (int i = 0; i < 3; i++) _aura[i] = Mix(_aura[i], tg[i], 0.06);
            double t = Environment.TickCount / 1000.0;
            for (int i = 0; i < 3; i++)
            {
                float cx = x + w * (float)(0.5 + 0.40 * Math.Sin(t * 0.21 + i * 2.1));
                float cy = h * (float)(0.5 + 0.50 * Math.Sin(t * 0.29 + i * 1.37));
                float rx = w * (float)(0.40 + 0.08 * Math.Sin(t * 0.37 + i)), ry = rx * 0.62f;
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                    using (PathGradientBrush pg = new PathGradientBrush(gp))
                    {
                        pg.CenterColor = Color.FromArgb((int)(78 * expandT), _aura[i]);
                        pg.SurroundColors = new Color[] { Color.FromArgb(0, _aura[i]) };
                        G.FillPath(pg, gp);
                    }
                }
            }
        }

        // "Leher" cair yang menyambung island ke tepi atas layar (mode tersembunyi)
        private void DrawNeck(float x, float w, float h, float r)
        {
            float top = -_yOff - 1f, k = (float)Clamp(h / ColH);
            float c = Math.Min(r, w / 2f) * 0.55f;
            float l = x + c, rr = x + w - c;
            if (rr - l < 2) { l = x + w / 2f - 1; rr = l + 2; }
            float f = Math.Min(Ui.Pf(11), (rr - l) * 0.5f) * k;
            float bottom = 0.5f + Math.Min(h, r) * 0.6f;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddLine(l - f, top, rr + f, top);
                p.AddBezier(rr + f, top, rr, top, rr, top, rr, top + _yOff * 0.9f + 1);
                p.AddLine(rr, top + _yOff * 0.9f + 1, rr, bottom);
                p.AddLine(rr, bottom, l, bottom);
                p.AddLine(l, bottom, l, top + _yOff * 0.9f + 1);
                p.AddBezier(l, top + _yOff * 0.9f + 1, l, top, l, top, l - f, top);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(Color.Black)) G.FillPath(b, p);
            }
        }

        private void PrivDot(float x, float w, float h)
        {
            Color c = PrivacyWatch.Cam.Count > 0 ? Color.FromArgb(48, 209, 88) : Color.FromArgb(255, 159, 10);
            float d = Ui.Pf(7), cx = x + w - Ui.Pf(15), cy = h / 2f;
            float glow = (float)(0.5 + 0.5 * Math.Sin(_tick * 0.08));
            using (SolidBrush g = new SolidBrush(Color.FromArgb((int)(60 * glow), c))) G.FillEllipse(g, cx - d, cy - d, d * 2, d * 2);
            using (SolidBrush b = new SolidBrush(c)) G.FillEllipse(b, cx - d / 2f, cy - d / 2f, d, d);
        }

        private static string RecTime(TimeSpan t) { return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00"); }

        // Gelombang suara ala iPhone: batang membulat simetris
        private void Wave(float x0, float x1, float cy, float hmax, float[] lv, int alpha, Color c)
        {
            int n = lv.Length; if (n == 0) return;
            float step = (x1 - x0) / n, bw = Math.Max(Ui.Pf(2), step * 0.55f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, c)))
                for (int i = 0; i < n; i++)
                {
                    float v = Math.Max(0f, Math.Min(1f, lv[i]));
                    float hh = Math.Max(bw, hmax * (0.08f + 0.92f * v));
                    RectangleF br = new RectangleF(x0 + i * step + (step - bw) / 2f, cy - hh / 2f, bw, hh);
                    using (GraphicsPath bp = Glass.Round(br, bw / 2f)) G.FillPath(b, bp);
                }
        }

        private static readonly Color RecRed = Color.FromArgb(255, 69, 58), WaveCol = Color.FromArgb(255, 99, 72);

        private void DrawRecCollapsed(float x, float w, float h)
        {
            float cy = h / 2f;
            bool audio = Rec.Kind == "audio";
            float pulse = Rec.Paused ? 0.45f : (float)(0.6 + 0.4 * Math.Sin(_tick * 0.12));
            if (audio)
                using (SolidBrush rb = new SolidBrush(Color.FromArgb((int)(255 * pulse), RecRed))) Glyphs.Draw(G, "\uE720", _glS, rb, new RectangleF(x + Ui.Pf(8), 0, Ui.Pf(22), h));
            else
            {
                float d = Ui.Pf(9);
                using (SolidBrush rb = new SolidBrush(Color.FromArgb((int)(255 * pulse), RecRed))) G.FillEllipse(rb, x + Ui.Pf(14), cy - d / 2f, d, d);
            }
            string t = RecTime(Rec.Elapsed);
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(Rec.Paused ? 150 : 255, 255, 255, 255)))
                G.DrawString(t, _valFont, tb, (float)Math.Round(x + Ui.Pf(32)), (float)Math.Round((h - _valFont.GetHeight(G)) / 2f));
            float right = x + w - _ri - Ui.Pf(14);
            if (audio || Rec.HasMic) Wave(right - Ui.Pf(64), right, cy, h - Ui.Pf(13), Rec.Recent(12), 255, WaveCol);
            else using (SolidBrush rb = new SolidBrush(RecRed))
                {
                    SizeF rs = G.MeasureString("REC", _labelFont);
                    G.DrawString("REC", _labelFont, rb, (float)Math.Round(right - rs.Width), (float)Math.Round((h - rs.Height) / 2f));
                }
        }

        private void DrawRecCard(float x, float w, int alpha)
        {
            using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(alpha * 0.66), 215, 218, 228)))
            using (SolidBrush red = new SolidBrush(Color.FromArgb(alpha, RecRed)))
            {
                if (!Rec.Active)
                {
                    CardTitle("Rekam", x, alpha);
                    bool mic = Settings.Get("RecMic", false);
                    string ml = "Mikrofon: " + (mic ? "nyala" : "mati");
                    SizeF mz = G.MeasureString(ml, _labelFont);
                    RectangleF mr = new RectangleF(x + w - Ui.Pf(22) - mz.Width - Ui.Pf(8), Ui.Pf(10), mz.Width + Ui.Pf(16), mz.Height + Ui.Pf(6));
                    using (GraphicsPath mp = Glass.Round(mr, mr.Height / 2f))
                    using (SolidBrush mb = new SolidBrush(Color.FromArgb((int)(alpha * (mic ? 0.9 : (_btnHover == "recmic" ? 0.26 : 0.14))), mic ? WaveCol : Color.White))) G.FillPath(mb, mp);
                    G.DrawString(ml, _labelFont, white, (float)Math.Round(mr.X + Ui.Pf(8)), (float)Math.Round(mr.Y + Ui.Pf(3)));
                    _btn["recmic"] = mr;

                    string[] ids = { "recscreen", "recaudio", "recshot" };
                    string[] labels = { "Rekam Layar", "Rekam Suara", "Jepret Layar" };
                    float bs = Ui.Pf(50), gap = Ui.Pf(64);
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = x + w / 2f + (i - 1) * (bs + gap);
                        RectangleF br = new RectangleF(cx - bs / 2f, Ui.Pf(38), bs, bs);
                        if (_pressId == ids[i] && _press > 0) { float k = bs * 0.10f * _press; br.Inflate(-k, -k); }
                        using (SolidBrush bb = new SolidBrush(Color.FromArgb((int)(alpha * (_btnHover == ids[i] ? 0.26 : 0.14)), 255, 255, 255))) G.FillEllipse(bb, br);
                        if (i == 0)
                        {
                            RectangleF ring = br; ring.Inflate(-bs * 0.26f, -bs * 0.26f);
                            using (Pen rp = new Pen(Color.FromArgb(alpha, 255, 255, 255), Ui.Pf(2))) G.DrawEllipse(rp, ring);
                            RectangleF dot = br; dot.Inflate(-bs * 0.35f, -bs * 0.35f);
                            G.FillEllipse(red, dot);
                        }
                        else Glyphs.Draw(G, i == 1 ? "\uE720" : "\uE722", _gl, i == 1 ? red : white, br);
                        SizeF lz = G.MeasureString(labels[i], _labelFont);
                        G.DrawString(labels[i], _labelFont, white, (float)Math.Round(cx - lz.Width / 2f), (float)Math.Round(br.Bottom + Ui.Pf(6)));
                        RectangleF hit = new RectangleF(cx - (bs + gap) / 2f + Ui.Pf(4), br.Y - Ui.Pf(2), bs + gap - Ui.Pf(8), bs + Ui.Pf(26));
                        _btn[ids[i]] = hit;
                    }
                    return;
                }
                bool audio = Rec.Kind == "audio";
                CardTitle(audio ? "Rekaman suara" : "Rekaman layar", x, alpha);
                string t = RecTime(Rec.Elapsed);
                G.DrawString(t, _bigFont, Rec.Paused ? soft : white, (float)Math.Round(x + Ui.Pf(20)), Ui.P(36));
                string st = Rec.Paused ? "Dijeda" : audio ? "Merekam\u2026" : Rec.WithMic ? "Layar + mikrofon" : "Layar (tanpa suara)";
                float pulse = Rec.Paused ? 0.4f : (float)(0.55 + 0.45 * Math.Sin(_tick * 0.12));
                float d = Ui.Pf(8);
                using (SolidBrush pb = new SolidBrush(Color.FromArgb((int)(alpha * pulse), RecRed))) G.FillEllipse(pb, x + Ui.Pf(22), Ui.Pf(80), d, d);
                G.DrawString(st, _labelFont, soft, (float)Math.Round(x + Ui.Pf(36)), (float)Math.Round(Ui.Pf(77)));
                float wx0 = x + w * 0.44f, wx1 = x + w - Ui.Pf(24);
                if (audio || Rec.HasMic) Wave(wx0, wx1, Ui.Pf(62), Ui.Pf(46), Rec.Recent(32), alpha, WaveCol);
                else
                {
                    if (_shotThumb == null) { }
                    using (Pen fp = new Pen(Color.FromArgb((int)(alpha * 0.35), 255, 255, 255), 1f))
                    {
                        RectangleF fr = new RectangleF(wx0 + Ui.Pf(20), Ui.Pf(38), wx1 - wx0 - Ui.Pf(40), Ui.Pf(50));
                        using (GraphicsPath frp = Glass.Round(fr, Ui.Pf(8))) G.DrawPath(fp, frp);
                        using (SolidBrush rb = new SolidBrush(Color.FromArgb((int)(alpha * pulse), RecRed))) G.FillEllipse(rb, fr.X + fr.Width / 2f - Ui.Pf(6), fr.Y + fr.Height / 2f - Ui.Pf(6), Ui.Pf(12), Ui.Pf(12));
                    }
                }
                // tombol jeda & berhenti
                float by = Ui.Pf(102), bsz = Ui.Pf(38), cx2 = x + w / 2f;
                Button("recpause", Rec.Paused ? "\uE768" : "\uE769", cx2 - bsz * 0.8f, by, bsz, alpha);
                RectangleF sr = new RectangleF(cx2 + bsz * 0.8f - bsz / 2f, by, bsz, bsz);
                if (_pressId == "recstop" && _press > 0) { float k = bsz * 0.12f * _press; sr.Inflate(-k, -k); }
                using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(alpha * (_btnHover == "recstop" ? 0.32 : 0.18)), 255, 255, 255))) G.FillEllipse(sb, sr);
                RectangleF sq = sr; sq.Inflate(-bsz * 0.32f, -bsz * 0.32f);
                using (GraphicsPath sqp = Glass.Round(sq, Ui.Pf(3))) G.FillPath(red, sqp);
                _btn["recstop"] = sr;
            }
        }

        // Pratinjau tangkapan layar / rekaman yang baru disimpan
        private void DrawShot(float x, float w, float h)
        {
            int a = (int)(255 * Clamp((h - ShotH * 0.55f) / (ShotH * 0.4f)));
            if (a <= 0) return;
            float th = h - Ui.Pf(16);
            RectangleF tr;
            if (_shotThumb != null)
            {
                float tw = Math.Min(Ui.Pf(128), th * _shotThumb.Width / Math.Max(1f, _shotThumb.Height));
                tr = new RectangleF(x + Ui.Pf(12), Ui.Pf(8), tw, th);
                RoundImage(_shotThumb, tr, Ui.Pf(8), a);
                using (GraphicsPath tp = Glass.Round(tr, Ui.Pf(8))) using (Pen tpn = new Pen(Color.FromArgb(a * 70 / 255, 255, 255, 255), 1f)) G.DrawPath(tpn, tp);
            }
            else
            {
                tr = new RectangleF(x + Ui.Pf(12), Ui.Pf(8), th, th);
                using (GraphicsPath tp = Glass.Round(tr, Ui.Pf(10))) using (SolidBrush tb = new SolidBrush(Color.FromArgb(a * 40 / 255, 255, 255, 255))) G.FillPath(tb, tp);
                using (SolidBrush rb = new SolidBrush(Color.FromArgb(a, RecRed))) Glyphs.Draw(G, "\uE720", _gl, rb, tr);
            }
            string title = _shotKind == "audio" ? "Rekaman suara disimpan" : _shotKind == "video" ? "Rekaman layar disimpan" : "Tangkapan layar";
            float tx = tr.Right + Ui.Pf(14);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(a * 160 / 255, 215, 218, 228)))
            {
                // tombol: Bersihkan (tutup pratinjau, kembali ke menu Rekam) + buka map
                RectangleF fr = new RectangleF(x + w - Ui.Pf(46), h / 2f - Ui.Pf(15), Ui.Pf(30), Ui.Pf(30));
                string cl = "Bersihkan";
                SizeF cz = G.MeasureString(cl, _labelFont);
                RectangleF cr = new RectangleF(fr.X - Ui.Pf(10) - cz.Width - Ui.Pf(22), h / 2f - Ui.Pf(14), cz.Width + Ui.Pf(22), Ui.Pf(28));
                using (GraphicsPath cp = Glass.Round(cr, cr.Height / 2f))
                using (SolidBrush cb = new SolidBrush(Color.FromArgb((int)(a * (_btnHover == "shotclear" ? 0.32 : 0.16)), 255, 255, 255))) G.FillPath(cb, cp);
                G.DrawString(cl, _labelFont, white, (float)Math.Round(cr.X + Ui.Pf(11)), (float)Math.Round(cr.Y + (cr.Height - cz.Height) / 2f));
                float tw2 = cr.X - Ui.Pf(10) - tx;
                G.DrawString(Trim(G, title, _valFont, tw2), _valFont, white, (float)Math.Round(tx), (float)Math.Round(Ui.Pf(16)));
                G.DrawString(Trim(G, Path.GetFileName(_shotPath ?? ""), _labelFont, tw2), _labelFont, soft, (float)Math.Round(tx), (float)Math.Round(Ui.Pf(36)));
                G.DrawString("Klik untuk membuka", _labelFont, soft, (float)Math.Round(tx), (float)Math.Round(Ui.Pf(53)));
                using (SolidBrush fb = new SolidBrush(Color.FromArgb((int)(a * (_btnHover == "shotdir" ? 0.32 : 0.16)), 255, 255, 255))) G.FillEllipse(fb, fr);
                Glyphs.Draw(G, "\uE838", _glS, white, fr);
                _btn["shotopen"] = new RectangleF(x, 0, w, h);
                _btn["shotdir"] = fr;
                _btn["shotclear"] = cr;
            }
        }

        private void DrawErr(float x, float w, float h)
        {
            using (SolidBrush rb = new SolidBrush(RecRed)) Glyphs.Draw(G, "\uE7BA", _glS, rb, new RectangleF(x + Ui.Pf(12), 0, Ui.Pf(24), h));
            using (SolidBrush wb = new SolidBrush(Color.White))
                G.DrawString(Trim(G, _hudText ?? "", _labelFont, w - Ui.Pf(60)), _labelFont, wb, (float)Math.Round(x + Ui.Pf(44)), (float)Math.Round((h - _labelFont.GetHeight(G)) / 2f));
        }

        private static string Trim(Graphics g, string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (g.MeasureString(s, f).Width <= maxW) return s;
            while (s.Length > 1 && g.MeasureString(s + "…", f).Width > maxW) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        private void DrawMediaExpanded(float x, float w, int alpha)
        {
            // latar: sampul diburamkan, opacity rendah, digelapkan ke bawah
            Bitmap blur = ArtBlur();
            if (blur != null)
            {
                RectangleF bgR = new RectangleF(x - Ui.Pf(20), -Ui.Pf(10), w + Ui.Pf(40), (float)_h + Ui.Pf(20));
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = 0.30f * alpha / 255f; ia.SetColorMatrix(cm);
                    InterpolationMode im = G.InterpolationMode; G.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    G.DrawImage(blur, Rectangle.Round(bgR), 0, 0, blur.Width, blur.Height, GraphicsUnit.Pixel, ia);
                    G.InterpolationMode = im;
                }
                using (LinearGradientBrush shade = new LinearGradientBrush(new RectangleF(x, 0, w, (float)_h + 1), Color.FromArgb((int)(alpha * 0.30), 0, 0, 0), Color.FromArgb((int)(alpha * 0.72), 0, 0, 0), 90f))
                    G.FillRectangle(shade, x, 0, w, (float)_h);
            }
            float art = Ui.Pf(76);
            RectangleF ar = new RectangleF(x + Ui.Pf(20), Ui.Pf(20), art, art);
            RoundImage(_np.Art, ar, Ui.Pf(14), alpha);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(alpha * 0.66), 215, 218, 228)))
            {
                float tx = ar.Right + Ui.Pf(14), tw = x + w - Ui.Pf(20) - tx;
                // tombol tutup (x): hentikan & sembunyikan media dari island
                RectangleF xr = new RectangleF(x + w - Ui.Pf(38), Ui.Pf(16), Ui.Pf(22), Ui.Pf(22));
                using (SolidBrush xb = new SolidBrush(Color.FromArgb((int)(alpha * (_btnHover == "mclose" ? 0.32 : 0.14)), 255, 255, 255))) G.FillEllipse(xb, xr);
                using (SolidBrush xg = new SolidBrush(Color.FromArgb((int)(alpha * 0.85), 255, 255, 255))) Glyphs.Draw(G, "\uE711", Glyphs.Font(Ui.Pf(9)), xg, xr);
                _btn["mclose"] = xr;
                string appT = Trim(G, _np.App, _labelFont, tw - Ui.Pf(56));
                G.DrawString(appT, _labelFont, soft, (float)Math.Round(tx), (float)Math.Round(ar.Y + Ui.Pf(2)));
                SizeF appS = G.MeasureString(appT, _labelFont);
                Equalizer(tx + appS.Width + Ui.Pf(6), ar.Y + Ui.Pf(2) + appS.Height / 2f, Ui.Pf(11), alpha, _np.Playing);
                G.DrawString(Trim(G, _np.Title, _titleFont, tw - Ui.Pf(22)), _titleFont, white, (float)Math.Round(tx), (float)Math.Round(ar.Y + Ui.Pf(20)));
                G.DrawString(Trim(G, _np.Artist ?? "", _subFont, tw), _subFont, soft, (float)Math.Round(tx), (float)Math.Round(ar.Y + Ui.Pf(42)));

                // progress
                float py = ar.Bottom + Ui.Pf(16);
                TimeSpan pos = _np.LivePosition, dur = _np.Duration;
                string a = Media.Fmt(pos), b = dur > TimeSpan.Zero ? "-" + Media.Fmt(dur - pos) : "";
                SizeF bs = G.MeasureString(b.Length > 0 ? b : "0:00", _labelFont);
                float left = x + Ui.Pf(20), right = x + w - Ui.Pf(20);
                G.DrawString(a, _labelFont, soft, (float)Math.Round(left), (float)Math.Round(py - Ui.Pf(6)));
                if (b.Length > 0) G.DrawString(b, _labelFont, soft, (float)Math.Round(right - bs.Width), (float)Math.Round(py - Ui.Pf(6)));
                RectangleF track = new RectangleF(left + Ui.Pf(44), py, right - left - Ui.Pf(88), Ui.Pf(5));
                using (GraphicsPath tp = Glass.Round(track, track.Height / 2)) using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(alpha * 0.25), 255, 255, 255))) G.FillPath(tb, tp);
                double frac = dur.TotalSeconds > 0 ? Math.Max(0, Math.Min(1, pos.TotalSeconds / dur.TotalSeconds)) : 0;
                if (frac > 0)
                    using (GraphicsPath fp = Glass.Round(new RectangleF(track.X, track.Y, Math.Max(track.Height, (float)(track.Width * frac)), track.Height), track.Height / 2))
                        G.FillPath(white, fp);

                // kontrol
                float cy = py + Ui.Pf(12), bsz = Ui.Pf(30), cx = x + w / 2f;
                Button("prev", "", cx - bsz * 1.7f, cy, bsz, alpha);
                Button("play", _np.Playing ? "" : "", cx, cy, bsz, alpha);
                Button("next", "", cx + bsz * 1.7f, cy, bsz, alpha);
                VolSlider(cx + bsz * 1.7f + bsz / 2f + Ui.Pf(16), right, cy + bsz / 2f, alpha);
            }
        }

        private void DrawClockExpanded(float x, float w, int alpha)
        {
            CultureInfo id = new CultureInfo("id-ID");
            using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb((int)(alpha * 0.66), 215, 218, 228)))
            {
                float lx = x + Ui.Pf(26);
                G.DrawString(DateTime.Now.ToString("HH.mm", id), _timeFont, white, (float)Math.Round(lx - Ui.Pf(3)), Ui.P(22));
                G.DrawString(DateTime.Now.ToString("dddd, d MMMM", id), _dateFont, soft, (float)Math.Round(lx), Ui.P(70));
                float sx = x + w * 0.50f, sw = w * 0.50f - Ui.Pf(26);
                Stat("CPU", _cpu, sx, Ui.Pf(28), sw, alpha, Color.FromArgb(10, 132, 255));
                Stat("RAM", _ram, sx, Ui.Pf(50), sw, alpha, Color.FromArgb(191, 90, 242));
                if (_bat >= 0) Stat("Baterai", _bat, sx, Ui.Pf(72), sw, alpha, _bat <= 0.2f ? Color.FromArgb(255, 69, 58) : Color.FromArgb(48, 209, 88));
                float by = Ui.Pf(98), bsz = Ui.Pf(28), bc = sx + sw / 2f;
                Button("prev", "", bc - bsz * 1.6f, by, bsz, alpha);
                Button("play", "", bc, by, bsz, alpha);
                Button("next", "", bc + bsz * 1.6f, by, bsz, alpha);
                VolSlider(lx, sx - Ui.Pf(22), by + bsz / 2f, alpha);
            }
        }

        private void Stat(string label, float v, float x, float y, float w, int alpha, Color c)
        {
            using (SolidBrush lb = new SolidBrush(Color.FromArgb((int)(alpha * 0.7), 220, 220, 230)))
            using (SolidBrush vb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            {
                G.DrawString(label, _labelFont, lb, (float)Math.Round(x), (float)Math.Round(y - Ui.Pf(3)));
                string val = ((int)Math.Round(v * 100)).ToString() + "%";
                SizeF vs = G.MeasureString(val, _valFont);
                G.DrawString(val, _valFont, vb, (float)Math.Round(x + w - vs.Width), (float)Math.Round(y - Ui.Pf(3)));
            }
            float bx = x + Ui.Pf(54), bw = w - Ui.Pf(54) - Ui.Pf(42);
            RectangleF track = new RectangleF(bx, y + Ui.Pf(4), bw, Ui.Pf(5));
            using (GraphicsPath tp = Glass.Round(track, track.Height / 2))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(alpha * 0.22), 255, 255, 255))) G.FillPath(tb, tp);
            float fw = Math.Max(track.Height, bw * Math.Max(0f, Math.Min(1f, v)));
            using (GraphicsPath fp = Glass.Round(new RectangleF(bx, track.Y, fw, track.Height), track.Height / 2))
            using (SolidBrush fb = new SolidBrush(Color.FromArgb(alpha, c))) G.FillPath(fb, fp);
        }

        private void Button(string id, string glyph, float cx, float y, float s, int alpha)
        {
            RectangleF r = new RectangleF(cx - s / 2, y, s, s);
            if (_pressId == id && _press > 0) { float k = s * 0.12f * _press; r.Inflate(-k, -k); }
            _btn[id] = r;
            if (_btnHover == id)
                using (SolidBrush hb = new SolidBrush(Color.FromArgb((int)(alpha * 0.2), 255, 255, 255))) G.FillEllipse(hb, r);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                Glyphs.Draw(G, glyph, id == "play" ? _gl : _glS, b, r);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // prioritas: tombol kecil (hapus) di atas baris notifikasi
            List<KeyValuePair<string, RectangleF>> order = new List<KeyValuePair<string, RectangleF>>(_btn);
            order.Sort(delegate (KeyValuePair<string, RectangleF> a1, KeyValuePair<string, RectangleF> b1) { return (a1.Value.Width * a1.Value.Height).CompareTo(b1.Value.Width * b1.Value.Height); });
            foreach (KeyValuePair<string, RectangleF> kv in order)
            {
                if (!kv.Value.Contains(e.Location)) continue;
                string k = kv.Key;
                _press = 1f; _pressId = k;
                if (k == "vol") { _press = 0; _volDrag = true; SetVolFromX(e.X); return; }
                if (k == "mclose") { DismissMedia(); return; }
                if (k == "shotclear") { _hud = null; _hudLeft = 0; _wantCard = Settings.Get("IslandRec", true) ? "rec" : null; _wasExpanded = false; Dirty = true; return; }
                if (k == "shotdir") { if (_shotPath != null) Shell.Run("explorer.exe", "/select,\"" + _shotPath + "\""); _hud = null; _hudLeft = 0; Dirty = true; return; }
                if (k == "shotopen") { if (_shotPath != null) Shell.Run(_shotPath, null); _hud = null; _hudLeft = 0; Dirty = true; return; }
                if (k == "recmic") { Settings.Set("RecMic", !Settings.Get("RecMic", false)); Dirty = true; return; }
                if (k == "recscreen") { _pinned = false; Rec.StartScreen(Settings.Get("RecMic", false)); Dirty = true; return; }
                if (k == "recaudio") { Rec.StartAudio(); Dirty = true; return; }
                if (k == "recshot") { _pinned = false; Dirty = true; System.Windows.Forms.Timer st = new System.Windows.Forms.Timer(); st.Interval = 350; st.Tick += delegate { st.Stop(); st.Dispose(); Rec.Screenshot(); }; st.Start(); return; }
                if (k == "recpause") { Rec.TogglePause(); Dirty = true; return; }
                if (k == "recstop") { _pinned = false; Rec.Stop(); Dirty = true; return; }
                if (k.StartsWith("dot")) { List<string> c = Cards(); int i; if (int.TryParse(k.Substring(3), out i) && i < c.Count && c[i] != _card) { int cur = c.IndexOf(_card); _card = c[i]; _slide = i > cur ? 1 : -1; if (_card == "notif") _notifs = NotifDb.Visible(3); Dirty = true; } return; }
                if (k.StartsWith("ndel")) { int i = int.Parse(k.Substring(4)); if (i < _notifs.Count && _removing < 0) { _removing = _notifs[i].Id; _removeT = 0; SoundKit.Feedback(); } return; }
                if (k == "nclear") { if (_notifs.Count > 0) { NotifDb.ClearAll(_notifs[0].Arrival); _notifs = NotifDb.Visible(3); _slide = 0.15; } return; }
                if (k.StartsWith("notif")) { int i = int.Parse(k.Substring(5)); if (i < _notifs.Count) NotifDb.Open(_notifs[i]); return; }
                if (k == "weather") { Weather.Refresh(true); return; }
                if (k == "t5") { IslandTimer.Set(5); Dirty = true; return; }
                if (k == "t15") { IslandTimer.Set(15); Dirty = true; return; }
                if (k == "t25") { IslandTimer.Set(25); Dirty = true; return; }
                if (k == "treset") { IslandTimer.Reset(); Dirty = true; return; }
                if (k == "tgo") { IslandTimer.Toggle(); Dirty = true; return; }
                if (k == "play") Native.Keys(Native.VK_MEDIA_PLAY);
                else if (k == "next") Native.Keys(Native.VK_MEDIA_NEXT);
                else if (k == "prev") Native.Keys(Native.VK_MEDIA_PREV);
                return;
            }
            _pinned = !_pinned;
            Dirty = true;
        }

        private static double Clamp(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        public void SnapshotCards(string dir)
        {
            Weather.Refresh(true);
            System.Threading.Thread.Sleep(2500);
            _np = Media.Current; _notifs = NotifDb.Visible(3);
            foreach (string c in Cards())
            {
                _card = c; _slide = 0; _pinned = true; _w = ExpW; _h = MediaExpH;
                Snapshot(System.IO.Path.Combine(dir, "island-" + c + ".png"));
            }
            // status lain
            _pinned = false; _expanded = false; _card = "clock";
            _hud = "cam"; _hudText = "Zoom Meetings"; _w = HudW; _h = HudH; Snapshot(System.IO.Path.Combine(dir, "hud-cam.png"));
            Bitmap th = new Bitmap(240, 135); using (Graphics tg = Graphics.FromImage(th)) tg.Clear(Color.SteelBlue);
            ShowShot(@"C:\x\Tangkapan Layar 2026-11-09 pukul 14.30.00.png", th, "shot"); _w = ShotW; _h = ShotH; Snapshot(System.IO.Path.Combine(dir, "hud-shot.png"));
            _hud = null; Rec.Kind = "audio"; Random rnd = new Random(3); for (int i = 0; i < Rec.Levels.Length; i++) Rec.Levels[i] = (float)rnd.NextDouble();
            _w = MediaColW; _h = ColH; Snapshot(System.IO.Path.Combine(dir, "rec-collapsed.png"));
            _pinned = true; _expanded = true; _card = "rec"; _w = ExpW; _h = MediaExpH; Snapshot(System.IO.Path.Combine(dir, "rec-active.png"));
            Rec.Kind = "screen"; Snapshot(System.IO.Path.Combine(dir, "rec-active-screen.png"));
            _pinned = false; _expanded = false; Rec.Kind = null; _w = ColW; _h = ColH;
            _hud = null; _expanded = false; _pinned = false; _w = ColW; _h = ColH;
            float[] its = { 0.3f, 0.8f, 1.2f, 1.8f, 2.3f, 2.9f };
            for (int i = 0; i < its.Length; i++) { _idleKind = "air"; _idleT = its[i]; Snapshot(System.IO.Path.Combine(dir, "idle-" + i + ".png")); }
            for (int i = 0; i < its.Length; i++) { _idleKind = "jantung"; _idleT = its[i]; Snapshot(System.IO.Path.Combine(dir, "heart-" + i + ".png")); }
            _idleT = -1;
            ForceHidden = true; _yOff = Offset;
            double[][] st = { new double[] { 56, 6 }, new double[] { 90, 18 }, new double[] { 150, 30 }, new double[] { 300, 110 } };
            for (int i = 0; i < st.Length; i++) { _w = Ui.P(st[i][0]); _h = Ui.P(st[i][1]); _expanded = i == 3; _pinned = i == 3; Snapshot(System.IO.Path.Combine(dir, "hidden-" + i + ".png")); }
            ForceHidden = false; _yOff = 0; _pinned = false; _expanded = false; _w = ColW; _h = ColH;
        }

        public void PromoGo(int d) { Go(d); }
        public void PromoHud(string kind, float v) { _hud = kind; _hudVal = v; _hudLeft = 100; Dirty = true; }
        public void PromoMedia(NowPlaying n) { _np = n; _mediaVer = Media.Version; _dismissed = null; Dirty = true; }

        public void ForceExpand() { _pinned = true; _np = Media.Current; _w = ExpW; _h = HasMedia ? MediaExpH : ExpH; SampleStats(); System.Threading.Thread.Sleep(300); SampleStats(); }
    }

    // ============================================================
    //  DOCK
    // ============================================================
    internal sealed class DockItem
    {
        public string Name, Launch, Target, Entry, Aumid, Key, Args, ProfileDir;
        public ChromeProf Profile;      // item Dock untuk profil browser tertentu
        public int LastCount = -1;
        public Bitmap Icon;
        public bool Pinned;
        public List<IntPtr> Windows = new List<IntPtr>();
    }

    internal sealed class DockForm : OverlayForm
    {
        private int IconBase = Ui.P(48), Gap = Ui.P(8), PadX = Ui.P(10), PadV = Ui.P(9);
        private int SideMargin = Ui.P(60), Headroom = Ui.P(78), BottomGap = Ui.P(16);   // agak naik: tidak memicu taskbar Windows

        // Ukuran ikon dari pengaturan, mengecil otomatis bila ikon banyak agar Dock tetap muat di layar.
        private void FitSize(int slots)
        {
            int want = 48; int.TryParse(Settings.GetS("DockSize", "48"), out want);
            int size = (int)Math.Round(Ui.P(Math.Max(28, Math.Min(80, want))) * 0.9);   // 10% lebih kecil
            int maxW = Screen.PrimaryScreen.Bounds.Width - Ui.P(60);
            for (int guard = 0; guard < 60; guard++)
            {
                int gap = Math.Max(Ui.P(4), (int)Math.Ceiling(size * 0.168));   // jarak antar-ikon sedikit lebih lebar
                int pad = Math.Max(Ui.P(6), (int)Math.Round(size * 0.2));
                int width = slots * size + (slots - 1) * gap + gap * 4 + pad * 2;
                if (width <= maxW || size <= Ui.P(26)) { IconBase = size; Gap = gap; PadX = pad; break; }
                size -= 1;
            }
            PadV = Math.Max(Ui.P(6), (int)Math.Round(IconBase * 0.18));
            Headroom = (int)Math.Round(IconBase * 1.6);
            SideMargin = Math.Max(Ui.P(40), IconBase);
        }
        private const double Mag = 0.36, Sigma = 0.55; // ikon yang disentuh membesar; tetangga nyaris diam

        private List<DockItem> _pinned = new List<DockItem>();
        private List<DockItem> _items = new List<DockItem>();
        private double[] _scale = new double[0];
        private float[] _cx = new float[0];
        private RectangleF[] _hit = new RectangleF[0];
        private int _sepIndex = -1;
        private float _bgX, _bgY, _bgW, _bgH;
        private int _cpVer, _cpFade;
        public RectangleF PromoBar { get { return new RectangleF(_bgX, _bgY, _bgW, _bgH); } }
        private int _hoverIdx = -1;
        private int _pressIdx = -1; private double _pressT;
        private const int BouncePeriod = 30;   // ~0,5 detik per pantulan

        private double BounceLift()
        {
            double ph = (_bounceT % BouncePeriod) / (double)BouncePeriod;    // 0..1
            return Math.Sin(ph * Math.PI) * IconBase * 0.36;                  // naik-turun lembut
        }
        private double _mx = -1, _amt;        // posisi kursor yang dihaluskan & kekuatan pembesaran (0..1)
        private float _labelA;                // label nama memudar masuk/keluar
        private int _labelIdx = -1;
        private int _bounceIdx = -1, _bounceT;
        private int _tick;
        private bool _trashFull;
        private readonly AppBar _ab;
        private readonly Font _labelFont = Ui.F(9f, FontStyle.Regular);
        public bool Reserve = true;

        public DockForm()
        {
            _ab = new AppBar(this, Native.ABE_BOTTOM);
            WinAnim.DockTarget = IconRectFor;
            _pinned = LoadPinned();
            RefreshRunning(true);
            AllowDrop = true;
        }

        // ---------- tambah aplikasi dengan seret & lepas ----------
        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            int at = DropIndex(PointToClient(new Point(e.X, e.Y)).X);
            foreach (string f in files)
            {
                if (File.Exists(f) || Directory.Exists(f)) { DockStore.Add(f, at); at++; }
            }
            ReloadItems();
        }

        private int DropIndex(int x)
        {
            int idx = 0;
            for (int i = 0; i < _pinned.Count && i < _cx.Length; i++) if (x > _cx[i]) idx = i + 1;
            return idx;
        }

        public void AddEntry(string entry) { DockStore.Add(entry, -1); ReloadItems(); }

        private int Slots { get { return _items.Count + 1; } }   // +1 = Tong Sampah

        public void ReloadItems()
        {
            foreach (DockItem it in _pinned) if (it.Icon != null) it.Icon.Dispose();
            _pinned = LoadPinned();
            _items.Clear();
            RefreshRunning(true);
        }

        public void Release() { _ab.Remove(); }

        protected override void OnTaskbarCreated() { _ab.Reset(); Reposition(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)_ab.CallbackMsg && (int)m.WParam == Native.ABN_POSCHANGED && Reserve && Visible) Reposition();
            base.WndProc(ref m);
        }

        private void DoLayout()
        {
            int slots = Slots;
            FitSize(slots);
            float contentW = slots * IconBase + (slots - 1) * Gap + (_sepIndex >= 0 ? Gap * 2 : 0) + Gap * 2; // + pemisah sampah
            _bgW = contentW + PadX * 2;
            _bgH = PadV * 2 + IconBase;
            int bw = (int)(_bgW + SideMargin * 2);
            int bh = (int)(_bgH + Headroom + BottomGap);
            Configure(bw, bh);
            _bgX = (bw - _bgW) / 2f;
            _bgY = bh - _bgH - BottomGap;
            if (_scale.Length != slots) { _scale = new double[slots]; for (int i = 0; i < slots; i++) _scale[i] = 1; }
            _cx = new float[slots];
            _hit = new RectangleF[slots];
            float x = _bgX + PadX + IconBase / 2f;
            for (int i = 0; i < slots; i++)
            {
                if (i == _sepIndex) x += Gap * 2;
                if (i == slots - 1) x += Gap * 2;
                _cx[i] = x;
                x += IconBase + Gap;
            }
        }

        public override void Reposition()
        {
            DoLayout();
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            int bottom;
            if (Reserve && IsHandleCreated)
            {
                Rectangle r = _ab.Reserve(scr, (int)_bgH + BottomGap * 2);
                bottom = r.Bottom;
            }
            else
            {
                _ab.Remove();
                bottom = Screen.PrimaryScreen.WorkingArea.Bottom;
            }
            Location = new Point(scr.Left + scr.Width / 2 - Bw / 2, bottom - Bh);
            Dirty = true;
        }

        // Sinkronkan item: aplikasi tersemat + aplikasi berjalan yang tidak tersemat.
        private static string PathOfKey(string k) { int i = k.LastIndexOf('|'); return i < 0 ? k : k.Substring(0, i); }
        private static string AumidOfKey(string k) { int i = k.LastIndexOf('|'); return i < 0 ? "" : k.Substring(i + 1); }

        // Sinkronkan item: aplikasi tersemat + jendela berjalan yang tidak tersemat.
        // Jendela dikelompokkan per (exe + AppUserModelID) sehingga tiap profil Chrome/Edge punya ikon sendiri.
        private bool RefreshRunning(bool force)
        {
            Dictionary<string, List<IntPtr>> map;
            try { map = Windows.AppWindows(); } catch (Exception ex) { Log.Write("enum: " + ex.Message); return false; }
            bool changed = force;
            HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> pinnedAumids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DockItem it in _pinned) if (!string.IsNullOrEmpty(it.Aumid)) pinnedAumids.Add(it.Aumid);

            // 1) item tersemat dengan AUMID: cocokkan persis
            foreach (DockItem it in _pinned)
            {
                List<IntPtr> w = new List<IntPtr>();
                if (!string.IsNullOrEmpty(it.Aumid))
                    foreach (KeyValuePair<string, List<IntPtr>> kv in map)
                        if (string.Equals(AumidOfKey(kv.Key), it.Aumid, StringComparison.OrdinalIgnoreCase)) { w.AddRange(kv.Value); claimed.Add(kv.Key); }
                it.Windows = w;
            }
            // 1b) item profil browser: hanya jendela milik profil tersebut
            HashSet<IntPtr> taken = new HashSet<IntPtr>();
            foreach (DockItem it in _pinned)
            {
                if (string.IsNullOrEmpty(it.ProfileDir)) continue;
                List<IntPtr> w = new List<IntPtr>();
                foreach (KeyValuePair<string, List<IntPtr>> kv in map)
                {
                    if (!string.Equals(PathOfKey(kv.Key), it.Target, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (IntPtr h in kv.Value)
                    {
                        ChromeProf cp = ChromeProfiles.For(h);
                        if (cp != null && string.Equals(cp.Dir, it.ProfileDir, StringComparison.OrdinalIgnoreCase) && taken.Add(h)) w.Add(h);
                    }
                }
                it.Windows = w;
            }
            // 2) item tersemat tanpa AUMID: semua jendela exe yang sama yang belum diklaim
            foreach (DockItem it in _pinned)
            {
                if (!string.IsNullOrEmpty(it.ProfileDir)) continue;
                if (!string.IsNullOrEmpty(it.Aumid) || string.IsNullOrEmpty(it.Target)) continue;
                foreach (KeyValuePair<string, List<IntPtr>> kv in map)
                {
                    if (claimed.Contains(kv.Key)) continue;
                    if (!string.Equals(PathOfKey(kv.Key), it.Target, StringComparison.OrdinalIgnoreCase)) continue;
                    string au = AumidOfKey(kv.Key);
                    if (au.Length > 0 && pinnedAumids.Contains(au)) continue;
                    // profil lain (AUMID khas profil) jadi ikon terpisah, kecuali Explorer
                    if (au.IndexOf(".UserData.", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    foreach (IntPtr h in kv.Value) if (!taken.Contains(h)) it.Windows.Add(h);
                    claimed.Add(kv.Key);
                }
            }
            foreach (DockItem it in _pinned)
            {
                bool had = it.Key == "1";
                bool has = it.Windows.Count > 0;
                if (!string.IsNullOrEmpty(it.ProfileDir) && it.Windows.Count != it.LastCount) { it.LastCount = it.Windows.Count; changed = true; }
                if (had != has) { changed = true; it.Key = has ? "1" : "0"; }
            }

            // 3) sisanya: satu ikon per kelompok
            List<DockItem> extras = new List<DockItem>();
            Func<List<IntPtr>, List<IntPtr>> free = delegate (List<IntPtr> l) { List<IntPtr> r = new List<IntPtr>(); foreach (IntPtr h in l) if (!taken.Contains(h)) r.Add(h); return r; };
            foreach (DockItem old in _items)
                if (!old.Pinned && map.ContainsKey(old.Key) && !claimed.Contains(old.Key))
                {
                    List<IntPtr> fw = free(map[old.Key]);
                    claimed.Add(old.Key);
                    if (fw.Count == 0) continue;
                    old.Windows = fw; extras.Add(old);
                }
            foreach (KeyValuePair<string, List<IntPtr>> kv in map)
            {
                if (claimed.Contains(kv.Key)) continue;
                if (free(kv.Value).Count == 0) continue;
                string path = PathOfKey(kv.Key), au = AumidOfKey(kv.Key);
                string exe = Path.GetFileName(path).ToLowerInvariant();
                if (exe == "applicationframehost.exe" || exe == "explorer.exe" || exe == "textinputhost.exe") continue;
                if (extras.Count >= 12) break;
                DockItem it = new DockItem();
                it.Key = kv.Key; it.Target = path; it.Aumid = au.Length > 0 ? au : null;
                string appName = au.Length > 0 ? Apps.NameFor(au) : null;
                it.Name = appName ?? Windows.NameOf(path);
                it.Launch = (au.Length > 0 && appName != null) ? "shell:AppsFolder\\" + au : path;
                Bitmap raw = (au.Length > 0 && appName != null) ? ShellImage.Get("shell:AppsFolder\\" + au, 256) : null;
                it.Icon = MakeTile(raw ?? Icons.Get(path));
                it.Windows = free(kv.Value);
                extras.Add(it);
                claimed.Add(kv.Key);
                changed = true;
            }
            foreach (DockItem old in _items) if (!old.Pinned && !extras.Contains(old)) { changed = true; if (old.Icon != null) old.Icon.Dispose(); }

            if (!changed && extras.Count + _pinned.Count == _items.Count) return false;
            _items = new List<DockItem>(_pinned);
            _sepIndex = extras.Count > 0 ? _items.Count : -1;
            _items.AddRange(extras);
            _scale = new double[0];
            if (IsHandleCreated) Reposition(); else DoLayout();
            return true;
        }

        protected override bool OnFrame()
        {
            bool dirty = false;
            _tick++;
            if (_tick % 45 == 0 && RefreshRunning(false)) dirty = true;
            if (ChromeProfiles.Version != _cpVer) { _cpVer = ChromeProfiles.Version; _cpFade = 22; }
            if (_cpFade > 0) { _cpFade--; dirty = true; }   // badge profil muncul halus
            if (_tick % 180 == 1)
            {
                bool full = TrashFull();
                if (full != _trashFull) { _trashFull = full; dirty = true; }
            }

            Point p = LocalCursor();
            bool inside = p.Y >= _bgY - Ui.P(30) && p.Y <= Bh && p.X >= _bgX - Ui.P(10) && p.X <= _bgX + _bgW + Ui.P(10);
            int hover = -1;
            float slot = IconBase + Gap;
            if (inside)
            {
                bool onIcon = false;
                for (int i = 0; i < _cx.Length; i++) if (Math.Abs(p.X - _cx[i]) <= IconBase / 2f + Ui.P(2)) { onIcon = true; break; }
                if (!onIcon && _amt < 0.05) inside = false;     // di sela ikon: belum ada yang membesar
            }
            // kursor dihaluskan (tidak meloncat antar-frame) + pembesaran naik/turun dengan easing
            if (inside) { if (_mx < 0 || _amt < 0.02) _mx = p.X; else if (Math.Abs(p.X - _mx) > 0.3) { _mx += (p.X - _mx) * 0.45; dirty = true; } }
            double amtT = inside ? 1 : 0;
            if (Math.Abs(_amt - amtT) > 0.002) { _amt += (amtT - _amt) * (inside ? 0.32 : 0.24); dirty = true; } else _amt = amtT;
            if (!inside && _amt == 0) _mx = -1;
            for (int i = 0; i < _scale.Length; i++)
            {
                double target = 1.0;
                if (_mx >= 0)
                {
                    double d = (_mx - _cx[i]) / slot;
                    target = 1.0 + Mag * _amt * Math.Exp(-(d * d) / (2 * Sigma * Sigma) * 2.2);
                }
                if (inside && Math.Abs(p.X - _cx[i]) <= slot / 2f && p.Y >= _bgY - Ui.P(30)) hover = i;
                double diff = target - _scale[i];
                if (Math.Abs(diff) > 0.0015) { _scale[i] += diff * 0.48; dirty = true; }
                else _scale[i] = target;
            }
            if (hover != _hoverIdx)
            {
                if (hover >= 0 && Settings.Get("DockHoverSound", true)) HoverTick.Play();
                _hoverIdx = hover; dirty = true;
                if (hover >= 0) _labelIdx = hover;
            }
            float la = _hoverIdx >= 0 ? 1f : 0f;
            if (Math.Abs(_labelA - la) > 0.02f) { _labelA += (la - _labelA) * 0.40f; dirty = true; } else _labelA = la;
            if (_bounceIdx >= 0)
            {
                // memantul seperti macOS selama aplikasi dibuka; berhenti saat jendelanya muncul (maks 10 detik)
                _bounceT++; dirty = true;
                bool opened = _bounceIdx < _items.Count && _items[_bounceIdx].Windows.Count > 0 && _bounceT > 20;
                if (_bounceT % 12 == 0) RefreshRunning(false);
                if (((opened || _bounceT > 600) && _bounceT % BouncePeriod == 0) || _bounceIdx >= _items.Count) _bounceIdx = -1;
            }
            if (_pressT > 0) { _pressT = Math.Max(0, _pressT - 0.14); dirty = true; }
            return dirty;
        }

        protected override void Render()
        {
            G.Clear(Color.Transparent);
            RectangleF bg = new RectangleF(_bgX, _bgY, _bgW, _bgH);
            string style = Settings.GetS("DockBg", "gradient");
            if (style == "glass") { Glass.Shadow(G, bg, Ui.Pf(20), 90); Glass.GlassPanel(G, bg, Ui.Pf(20), 165); }
            else if (style == "gradient") G.DrawImageUnscaled(GradientBg(), 0, 0);

            float baseline = _bgY + _bgH - PadV;
            if (Settings.Get("DockShelf", true)) DrawShelf(baseline);
            int slots = Slots;

            // pemisah
            using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, Ui.Pf(1))))
            {
                if (_sepIndex >= 0) { float sx = _cx[_sepIndex] - IconBase / 2f - Gap * 1.5f; G.DrawLine(pen, sx, _bgY + Ui.Pf(12), sx, _bgY + _bgH - Ui.Pf(12)); }
                float tx = _cx[slots - 1] - IconBase / 2f - Gap * 1.5f;
                G.DrawLine(pen, tx, _bgY + Ui.Pf(12), tx, _bgY + _bgH - Ui.Pf(12));
            }

            for (int i = 0; i < slots; i++)
            {
                double s = i < _scale.Length ? _scale[i] : 1;
                if (i == _pressIdx && _pressT > 0) s *= 1 - 0.10 * Math.Sin(_pressT * Math.PI / 2);   // efek tekan
                float size = (float)(Math.Round(IconBase * s / 2.0) * 2.0);
                float lift = (float)Math.Round((s - 1.0) * IconBase * 0.32);
                if (i == _bounceIdx) lift += (float)Math.Round(BounceLift());
                float bottom = baseline - lift - (float)Math.Round(IconBase * 0.05);   // ikon sedikit naik dari papan
                RectangleF ir = new RectangleF((float)Math.Round(_cx[i] - size / 2f), bottom - size, size, size);
                _hit[i] = new RectangleF(_cx[i] - (IconBase + Gap) / 2f, _bgY - Ui.Pf(30), IconBase + Gap, _bgH + Ui.Pf(30));

                if (i < _items.Count)
                {
                    DockItem it = _items[i];
                    if (it.Icon != null) G.DrawImage(it.Icon, ir);
                    if ((it.Windows.Count > 0 || it.Profile != null) && ChromeProfiles.IsBrowser(it.Target) && Settings.Get("DockBrowserProfile", true))
                    {
                        List<ChromeProf> profs = new List<ChromeProf>();
                        if (it.Profile != null) profs.Add(it.Profile);
                        foreach (IntPtr wh in it.Windows) { ChromeProf cp = ChromeProfiles.For(wh); if (cp != null && !profs.Exists(delegate (ChromeProf o) { return o.Name == cp.Name; })) profs.Add(cp); }
                        ChromeProfiles.DrawBadges(G, ir, profs);
                    }
                    if (it.Entry == DockStore.LaunchpadEntry && Program.LaunchpadOpen)
                        using (SolidBrush dot = new SolidBrush(DotColor()))
                            G.FillEllipse(dot, _cx[i] - Ui.Pf(2.75), _bgY + _bgH - Ui.Pf(6.55), Ui.Pf(5.5), Ui.Pf(5.5));   // 25% lebih besar
                    if (it.Windows.Count > 0)
                        using (SolidBrush dot = new SolidBrush(DotColor()))
                            G.FillEllipse(dot, _cx[i] - Ui.Pf(2.75), _bgY + _bgH - Ui.Pf(6.55), Ui.Pf(5.5), Ui.Pf(5.5));   // 25% lebih besar
                }
                else DrawTrash(ir);
            }

            // label melayang di atas ikon yang disorot
            int li = _hoverIdx >= 0 ? _hoverIdx : _labelIdx;
            if (_labelA > 0.02f && li >= 0 && li < slots)
            {
                string name = li < _items.Count ? _items[li].Name : (_trashFull ? "Tong Sampah (berisi)" : "Tong Sampah");
                SizeF ts = G.MeasureString(name, _labelFont);
                double s = li < _scale.Length ? _scale[li] : 1;
                float top = baseline - (float)(IconBase * s) - (float)((s - 1.0) * IconBase * 0.32) - Ui.Pf(10) + (1 - _labelA) * Ui.Pf(6);
                RectangleF lr = new RectangleF(_cx[li] - ts.Width / 2f - Ui.Pf(9), top - ts.Height - Ui.Pf(6), ts.Width + Ui.Pf(18), ts.Height + Ui.Pf(6));
                if (lr.X < 2) lr.X = 2;
                if (lr.Right > Bw - 2) lr.X = Bw - 2 - lr.Width;
                if (lr.Y < 0) lr.Y = 0;
                Glass.Pill(G, lr, (int)(225 * _labelA));
                using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(245 * _labelA), 255, 255, 255)))
                    G.DrawString(name, _labelFont, tb, lr.X + Ui.Pf(9), lr.Y + Ui.Pf(3));
            }
        }

        // Warna titik aplikasi aktif (Pengaturan → Dock)
        public static readonly string[] DotNames = { "Hijau", "Biru", "Oranye", "Putih" };
        private static Color DotColor()
        {
            switch (Settings.GetS("DockDotColor", "Hijau"))
            {
                case "Biru": return Color.FromArgb(240, 10, 132, 255);
                case "Oranye": return Color.FromArgb(240, 255, 159, 10);
                case "Putih": return Color.FromArgb(235, 255, 255, 255);
                default: return Color.FromArgb(240, 48, 209, 88);
            }
        }

        // Papan kaca di bawah ikon (perspektif ringan): permukaan atas, bibir depan, kilau tepi, bayangan.
        private void DrawShelf(float baseline)
        {
            float l = _bgX + PadX * 0.35f, r = _bgX + _bgW - PadX * 0.35f;
            float depth = Math.Max(Ui.Pf(7), IconBase * 0.17f), lip = Math.Max(Ui.Pf(2.5f), IconBase * 0.055f);
            float inset = depth * 1.6f;
            float topY = baseline - depth * 0.55f, frontY = topY + depth;
            using (GraphicsPath top = new GraphicsPath())
            {
                top.AddPolygon(new PointF[] { new PointF(l + inset, topY), new PointF(r - inset, topY), new PointF(r, frontY), new PointF(l, frontY) });
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(l, topY - 1, r - l, depth + 2), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(28, 255, 255, 255), 90f))
                    G.FillPath(b, top);
            }
            // garis kilau tepi belakang
            using (Pen hl = new Pen(Color.FromArgb(110, 255, 255, 255), 1f)) G.DrawLine(hl, l + inset, topY + 0.5f, r - inset, topY + 0.5f);
            // bibir depan
            RectangleF front = new RectangleF(l, frontY, r - l, lip);
            using (LinearGradientBrush fb = new LinearGradientBrush(new RectangleF(l, frontY - 1, r - l, lip + 2), Color.FromArgb(120, 235, 238, 255), Color.FromArgb(60, 180, 185, 210), 90f))
                G.FillRectangle(fb, front);
            using (Pen fl = new Pen(Color.FromArgb(160, 255, 255, 255), 1f)) G.DrawLine(fl, l, frontY + 0.5f, r, frontY + 0.5f);
            // bayangan lembut di bawah papan
            using (LinearGradientBrush sb = new LinearGradientBrush(new RectangleF(l, frontY + lip, r - l, Ui.Pf(8)), Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
                G.FillRectangle(sb, l + Ui.Pf(4), frontY + lip, r - l - Ui.Pf(8), Ui.Pf(8));
        }

        // Latar gradasi: gelap di bawah, memudar ke atas & ke sisi (tanpa sudut membulat). Dibuat sekali per ukuran.
        private Bitmap _gradBg;
        private Bitmap GradientBg()
        {
            if (_gradBg != null && _gradBg.Width == Bw && _gradBg.Height == Bh) return _gradBg;
            if (_gradBg != null) _gradBg.Dispose();
            Bitmap b = new Bitmap(Bw, Bh, PixelFormat.Format32bppPArgb);
            BitmapData d = b.LockBits(new Rectangle(0, 0, Bw, Bh), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            int[] row = new int[Bw];
            float top = _bgY - Ui.Pf(22), side = SideMargin + PadX;
            for (int y = 0; y < Bh; y++)
            {
                double vy = Math.Max(0, Math.Min(1, (y - top) / (Bh - top)));
                double va = Math.Pow(vy, 1.4) * 0.80;                   // makin ke bawah makin pekat
                for (int x = 0; x < Bw; x++)
                {
                    double ex = Math.Min(x, Bw - 1 - x) / side;
                    double ha = ex >= 1 ? 1 : ex * ex * (3 - 2 * ex);    // pudar halus di kedua sisi
                    int a = (int)(255 * va * ha);
                    int c = a * 14 / 255;                              // warna hampir hitam (premultiplied)
                    row[x] = (a << 24) | (c << 16) | (c << 8) | (a * 20 / 255);
                }
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, Bw);
            }
            b.UnlockBits(d);
            _gradBg = b;
            return b;
        }

        private void DrawTrash(RectangleF box)
        {
            // kaleng sampah kaca (mesh) gaya macOS
            RectangleF r = new RectangleF(box.X + box.Width * 0.18f, box.Y + box.Height * 0.08f, box.Width * 0.64f, box.Height * 0.86f);
            float lidH = r.Height * 0.12f;
            RectangleF body = new RectangleF(r.X + r.Width * 0.04f, r.Y + lidH, r.Width * 0.92f, r.Height - lidH);
            using (GraphicsPath bp = Glass.Round(body, r.Width * 0.12f))
            {
                using (LinearGradientBrush gb = new LinearGradientBrush(body, Color.FromArgb(150, 235, 238, 245), Color.FromArgb(90, 180, 185, 200), 0f))
                    G.FillPath(gb, bp);
                Region old = G.Clip;
                G.SetClip(bp, CombineMode.Intersect);
                using (Pen mesh = new Pen(Color.FromArgb(70, 60, 60, 70), Math.Max(1f, body.Width / 40f)))
                    for (float lx = body.X + body.Width / 7f; lx < body.Right; lx += body.Width / 7f)
                        G.DrawLine(mesh, lx, body.Y, lx, body.Bottom);
                if (_trashFull)
                    using (SolidBrush paper = new SolidBrush(Color.FromArgb(235, 250, 250, 252)))
                    {
                        G.FillEllipse(paper, body.X + body.Width * 0.08f, body.Y - body.Height * 0.10f, body.Width * 0.5f, body.Height * 0.32f);
                        G.FillEllipse(paper, body.X + body.Width * 0.42f, body.Y - body.Height * 0.06f, body.Width * 0.5f, body.Height * 0.28f);
                    }
                G.Clip = old; old.Dispose();
                using (Pen edge = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, body.Width / 30f))) G.DrawPath(edge, bp);
            }
            RectangleF lid = new RectangleF(r.X, r.Y, r.Width, lidH);
            using (GraphicsPath lp = Glass.Round(lid, lidH / 2))
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(230, 225, 228, 236)))
                G.FillPath(lb, lp);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = -1;
            for (int i = 0; i < _hit.Length; i++) if (_hit[i].Contains(e.Location)) { idx = i; break; }
            if (idx < 0) return;
            bool trash = idx >= _items.Count;
            if (e.Button == MouseButtons.Right) { ShowItemMenu(idx, trash); return; }
            if (e.Button != MouseButtons.Left) return;
            _pressIdx = idx; _pressT = 1; Dirty = true;
            if (trash) { Shell.Explore("shell:RecycleBinFolder"); return; }
            ClickItem(idx);
        }

        private void Run(DockItem it)
        {
            if (it.Entry == DockStore.LaunchpadEntry) { Program.Launchpad(); return; }
            if (it.Launch.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)) { Shell.Run("explorer.exe", it.Launch); return; }
            Shell.Run(it.Launch, it.Args);
        }

        // Kotak ikon (layar) milik jendela h, untuk animasi minimize/buka.
        private Rectangle IconRectFor(IntPtr h)
        {
            for (int i = 0; i < _items.Count && i < _cx.Length; i++)
                if (_items[i].Windows.Contains(h)) return IconRect(i);
            return Rectangle.Empty;
        }

        public Rectangle PromoIcon(int i) { i = Math.Max(0, Math.Min(_cx.Length - 1, i)); return new Rectangle(Left + (int)(_cx[i] - IconBase / 2f), Top + (int)(_bgY + PadV), IconBase, IconBase); }

        private Rectangle IconRect(int i)
        {
            if (!Visible || i < 0 || i >= _cx.Length) return Rectangle.Empty;
            return new Rectangle(Left + (int)(_cx[i] - IconBase / 2f), Top + (int)(_bgY + PadV), IconBase, IconBase);
        }

        private void ClickItem(int idx)
        {
            DockItem it = _items[idx];
            List<IntPtr> wins = it.Windows.FindAll(Native.IsWindow);
            if (wins.Count == 0)
            {
                Run(it);
                if (it.Entry != DockStore.LaunchpadEntry) { _bounceIdx = idx; _bounceT = 0; }
                return;
            }
            IntPtr fg = Native.GetForegroundWindow();
            if (wins.Contains(fg))
            {
                if (wins.Count == 1) WinAnim.Minimize(fg);
                else Shell.Activate(wins[wins.Count - 1]);
                return;
            }
            // utamakan jendela yang tidak diminimalkan; bila semua diminimalkan, pulihkan yang terakhir dipakai
            IntPtr pick = wins.Find(delegate (IntPtr h) { return !Native.IsIconic(h); });
            if (pick == IntPtr.Zero) WinAnim.Restore(wins[0], IconRect(idx));   // buka dari Dock dengan animasi
            else Shell.Activate(pick);
        }

        private void ShowItemMenu(int idx, bool trash)
        {
            ContextMenuStrip m = Menus.Create();
            if (trash)
            {
                Menus.Add(m, "Buka", delegate { Shell.Explore("shell:RecycleBinFolder"); });
                Menus.Add(m, "Kosongkan Tong Sampah...", delegate { Native.SHEmptyRecycleBin(Handle, null, 0); _trashFull = TrashFull(); Dirty = true; });
            }
            else
            {
                DockItem it = _items[idx];
                Menus.Add(m, it.Name, null);
                Menus.Sep(m);
                foreach (IntPtr h in it.Windows)
                {
                    IntPtr hh = h;
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hh, sb, sb.Capacity);
                    string t = sb.ToString();
                    if (t.Length > 48) t = t.Substring(0, 45) + "...";
                    if (t.Length > 0) Menus.Add(m, t, delegate { Shell.Activate(hh); });
                }
                if (it.Windows.Count > 0) Menus.Sep(m);
                if (it.Entry != DockStore.LaunchpadEntry)
                    Menus.Add(m, it.Windows.Count > 0 ? "Jendela Baru" : "Buka", delegate { Run(it); _bounceIdx = idx; _bounceT = 0; });
                if (!string.IsNullOrEmpty(it.Target) && File.Exists(it.Target))
                    Menus.Add(m, "Tampilkan di Explorer", delegate { Shell.Run("explorer.exe", "/select,\"" + it.Target + "\""); });
                Menus.Sep(m);
                if (ChromeProfiles.IsBrowser(it.Target) && File.Exists(it.Target))
                {
                    string exe = it.Target;
                    // profil dari jendela yang sedang terbuka: satu klik
                    List<string> shown = new List<string>();
                    foreach (IntPtr wh in it.Windows)
                    {
                        ChromeProf cp = ChromeProfiles.For(wh);
                        if (cp == null || string.IsNullOrEmpty(cp.Dir) || shown.Contains(cp.Dir) || !string.IsNullOrEmpty(it.ProfileDir)) continue;
                        shown.Add(cp.Dir);
                        string en = "browser:" + exe + "|" + cp.Dir;
                        if (!DockStore.Contains(en)) Menus.Add(m, "Simpan profil \u201C" + cp.Name + "\u201D di Dock", delegate { DockStore.Add(en, -1); ReloadItems(); });
                    }
                    List<ChromeProf> all = ChromeProfiles.Known(ChromeProfiles.BrowserOf(exe));
                    if (all.Count > 0)
                    {
                        ToolStripMenuItem sub = Menus.Add(m, "Simpan profil lain di Dock", null);
                        foreach (ChromeProf cp in all)
                        {
                            string en = "browser:" + exe + "|" + cp.Dir;
                            bool has = DockStore.Contains(en);
                            ToolStripMenuItem mi = new ToolStripMenuItem((has ? "\u2713  " : "     ") + cp.Name);
                            if (!has) mi.Click += delegate { DockStore.Add(en, -1); ReloadItems(); };
                            else mi.Enabled = false;
                            sub.DropDownItems.Add(mi);
                        }
                    }
                }
                if (it.Pinned)
                {
                    string entry = it.Entry;
                    Menus.Add(m, "Geser ke kiri", delegate { DockStore.Move(entry, -1); ReloadItems(); });
                    Menus.Add(m, "Geser ke kanan", delegate { DockStore.Move(entry, 1); ReloadItems(); });
                    Menus.Add(m, "Hapus dari Dock", delegate { DockStore.Remove(entry); ReloadItems(); });
                }
                else
                {
                    string entry = it.Launch.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) ? "app:" + it.Aumid : it.Target;
                    Menus.Add(m, "Simpan di Dock", delegate { DockStore.Add(entry, -1); ReloadItems(); });
                }
                if (it.Windows.Count > 0)
                {
                    Menus.Sep(m);
                    Menus.Add(m, "Keluar", delegate { foreach (IntPtr h in it.Windows) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); });
                }
            }
            Menus.Sep(m);
            Menus.Add(m, "Tambah aplikasi ke Dock...", delegate { PickAndAdd(); });
            Menus.Add(m, "Pilih dari Launchpad...", delegate { Program.Launchpad(); });
            m.Show(Cursor.Position);
        }

        public void PickAndAdd()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Pilih aplikasi untuk Dock";
                d.Filter = "Aplikasi atau pintasan (*.exe;*.lnk)|*.exe;*.lnk|Semua berkas (*.*)|*.*";
                d.Multiselect = true;
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + "\\Programs";
                d.DereferenceLinks = false;
                if (d.ShowDialog() != DialogResult.OK) return;
                foreach (string f in d.FileNames) DockStore.Add(f, -1);
                ReloadItems();
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);

        private static bool TrashFull()
        {
            try
            {
                Native.SHQUERYRBINFO q = new Native.SHQUERYRBINFO();
                q.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
                return Native.SHQueryRecycleBin(null, ref q) == 0 && q.i64NumItems > 0;
            }
            catch { return false; }
        }

        private static Bitmap MakeTile(Bitmap raw)
        {
            if (raw == null) return Glyph();
            if (Settings.Get("DockPlainIcons", true))
            {
                Bitmap o = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(o))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(raw, new Rectangle(4, 4, 120, 120));
                }
                raw.Dispose();
                return o;
            }
            Bitmap t = Tiles.Make(raw, 128);
            raw.Dispose();
            return t;
        }

        // ----- isi Dock: dari dock.txt (awalnya disalin dari pin taskbar) -----
        private List<DockItem> LoadPinned()
        {
            if (!DockStore.Exists) DockStore.Save(SeedEntries());
            List<DockItem> list = new List<DockItem>();
            foreach (string e in DockStore.Load())
            {
                DockItem it = null;
                try { it = FromEntry(e); } catch (Exception ex) { Log.Write("dock entry " + e + ": " + ex.Message); }
                if (it != null) list.Add(it);
            }
            return list;
        }

        public static List<string> SeedEntries()
        {
            List<string> l = new List<string>();
            l.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            l.Add(DockStore.LaunchpadEntry);
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.lnk");
                    Array.Sort(files, delegate (string a, string b) { return File.GetCreationTime(a).CompareTo(File.GetCreationTime(b)); });
                    foreach (string f in files)
                    {
                        string n = Path.GetFileNameWithoutExtension(f);
                        if (n.IndexOf("Explorer", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Internet", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        l.Add(f);
                    }
                }
            }
            catch (Exception ex) { Log.Write("seed: " + ex.Message); }
            if (l.Count <= 2)
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                foreach (string f in new string[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "calc.exe") })
                    if (File.Exists(f)) l.Add(f);
            }
            return l;
        }

        private DockItem FromEntry(string e)
        {
            DockItem it = new DockItem();
            it.Entry = e; it.Pinned = true;
            if (e == DockStore.LaunchpadEntry)
            {
                it.Name = "Launchpad"; it.Launch = e; it.Icon = Tiles.LaunchpadGlyph(128);
                return it;
            }
            if (e.StartsWith("browser:", StringComparison.OrdinalIgnoreCase))
            {
                // browser:<exe>|<folder profil>  ->  buka langsung profil itu (tanpa pemilih profil)
                string[] pp = e.Substring(8).Split('|');
                string exe = pp[0], dir = pp.Length > 1 ? pp[1] : "";
                if (!File.Exists(exe)) return null;
                it.Target = Path.GetFullPath(exe); it.Launch = exe; it.ProfileDir = dir;
                it.Args = "--profile-directory=\"" + dir + "\"";
                ChromeProf cp = ChromeProfiles.ByDir(ChromeProfiles.BrowserOf(exe), dir);
                if (cp != null) { cp.Seen = DateTime.MinValue; it.Profile = cp; }
                it.Name = Windows.NameOf(exe) + " \u2013 " + (cp != null ? cp.Name : dir);
                it.Icon = MakeTile(Icons.Get(exe));
                return it;
            }
            if (e.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            {
                string id = e.Substring(4);
                it.Name = Apps.NameFor(id) ?? id;
                it.Launch = "shell:AppsFolder\\" + id;
                if (File.Exists(id)) it.Target = Path.GetFullPath(id);
                else it.Aumid = id;
                it.Icon = MakeTile(ShellImage.Get(it.Launch, 256));
                return it;
            }
            if (Directory.Exists(e))
            {
                it.Name = Path.GetFileName(e.TrimEnd('\\')); if (it.Name.Length == 0) it.Name = e;
                it.Launch = e;
                it.Icon = MakeTile(ShellImage.Get(e, 256));
                return it;
            }
            if (!File.Exists(e)) return null;
            if (e.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target = ResolveTarget(e);
                it.Name = Path.GetFileNameWithoutExtension(e);
                it.Launch = e;
                it.Target = target;
                it.Aumid = Aumid.OfFile(e);
                Bitmap raw = Icons.Get(e);
                if (raw == null && target != null) raw = Icons.Get(target);
                it.Icon = MakeTile(raw);
            }
            else
            {
                it.Target = Path.GetFullPath(e);
                it.Launch = e;
                it.Name = Windows.NameOf(e);
                it.Icon = MakeTile(Icons.Get(e));
            }
            if (it.Target != null && it.Target.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase)) it.Name = "Finder";
            return it;
        }

        private static string ResolveTarget(string lnk)
        {
            object sh = null, sc = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                sh = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                string tp = sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
                if (string.IsNullOrEmpty(tp) || !File.Exists(tp)) return null;
                return Path.GetFullPath(tp);
            }
            catch { return null; }
            finally
            {
                if (sc != null && Marshal.IsComObject(sc)) Marshal.ReleaseComObject(sc);
                if (sh != null && Marshal.IsComObject(sh)) Marshal.ReleaseComObject(sh);
            }
        }

        private static Bitmap Glyph()
        {
            Bitmap b = new Bitmap(128, 128);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, 128, 128), Color.FromArgb(150, 120, 255), Color.FromArgb(255, 90, 150), 45f))
                using (GraphicsPath p = Glass.Round(new Rectangle(8, 8, 112, 112), 26))
                    g.FillPath(br, p);
            }
            return b;
        }
    }

    // ============================================================
    //  WALLPAPER
    // ============================================================
    internal static class Wallpaper
    {
        public static void Apply()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string cur = k == null ? null : k.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(cur) && cur.IndexOf("LiquidMac", StringComparison.OrdinalIgnoreCase) < 0)
                        Settings.SetS("PrevWallpaper", cur);
                }
                Directory.CreateDirectory(Log.Dir);
                string path = Path.Combine(Log.Dir, "wallpaper.png");
                Rectangle s = Screen.PrimaryScreen.Bounds;
                Generate(path, Math.Max(1920, s.Width), Math.Max(1080, s.Height));
                Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, path, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
            }
            catch (Exception ex) { Log.Write("wallpaper: " + ex.Message); }
        }

        public static bool CanRestore { get { string p = Settings.GetS("PrevWallpaper", ""); return p.Length > 0 && File.Exists(p); } }

        public static void Restore()
        {
            string p = Settings.GetS("PrevWallpaper", "");
            if (p.Length > 0 && File.Exists(p))
                Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, p, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }

        private static void Blob(Graphics g, float cx, float cy, float r, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = c;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(b, p);
                }
            }
        }

        private static void Generate(string path, int w, int h)
        {
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(22, 14, 42), Color.FromArgb(6, 6, 16), 60f))
                    g.FillRectangle(bg, 0, 0, w, h);
                Blob(g, w * 0.20f, h * 0.30f, w * 0.55f, Color.FromArgb(210, 120, 70, 235));
                Blob(g, w * 0.78f, h * 0.24f, w * 0.50f, Color.FromArgb(200, 60, 140, 255));
                Blob(g, w * 0.55f, h * 0.78f, w * 0.60f, Color.FromArgb(200, 255, 110, 90));
                Blob(g, w * 0.88f, h * 0.82f, w * 0.45f, Color.FromArgb(175, 255, 70, 160));
                Blob(g, w * 0.30f, h * 0.88f, w * 0.42f, Color.FromArgb(150, 60, 230, 200));
                using (GraphicsPath vp = new GraphicsPath())
                {
                    vp.AddEllipse(-w * 0.2f, -h * 0.2f, w * 1.4f, h * 1.4f);
                    using (PathGradientBrush v = new PathGradientBrush(vp))
                    {
                        v.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        v.SurroundColors = new Color[] { Color.FromArgb(150, 0, 0, 0) };
                        g.FillRectangle(v, 0, 0, w, h);
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    // ============================================================
    //  AUTOSTART
    // ============================================================
    internal static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled
        {
            get
            {
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue("LiquidMac") != null; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                    {
                        if (value) k.SetValue("LiquidMac", "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue("LiquidMac", false);
                    }
                }
                catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
            }
        }
    }

    // ============================================================
    //  TRAY / PENGENDALI UTAMA
    // ============================================================
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _ni;
        private readonly TopBarForm _bar;
        private readonly NotchForm _notch;
        private readonly DockForm _dock;
        private readonly System.Windows.Forms.Timer _watch;
        private readonly MessageWindow _msg;
        private SettingsForm _settings;
        private LaunchpadForm _launchpad;
        private NotificationCenterForm _nc;
        private ControlPanelForm _cp;
        private ProfileForm _profile;
        private System.Windows.Forms.Timer _donate;
        private DonateForm _donateForm;

        // Pengingat donasi seminggu sekali; berhenti selamanya setelah tautan donasi diklik.
        private void CheckDonate()
        {
            if (Settings.Get("DonateNagOff", false) || Settings.Get("DonateDone", false)) return;
            if (_fullscreenHidden || Program.LaunchpadOpen || (_donateForm != null && _donateForm.Visible)) return;
            long last; string ls = Settings.GetS("DonateLast", "");
            if (!long.TryParse(ls, out last)) { Settings.SetS("DonateLast", DateTime.Now.Ticks.ToString()); return; }   // minggu pertama bebas
            if (DateTime.Now - new DateTime(last) < TimeSpan.FromDays(7)) return;
            Settings.SetS("DonateLast", DateTime.Now.Ticks.ToString());
            if (_donateForm == null || _donateForm.IsDisposed) _donateForm = new DonateForm();
            _donateForm.Popup();
        }
        private System.Windows.Forms.Timer _lockDebounce;
        private TrafficLights _lights;
        private System.Windows.Forms.Timer _upd;

        private void AutoUpdateCheck()
        {
            if (!Settings.Get("AutoUpdate", true)) return;
            Updater.Check(_bar, delegate { if (Updater.Newer) Updater.Apply(_bar, ExitApp); });
        }

        public void SetDockBg(string style) { Settings.SetS("DockBg", style); _dock.Invalidate2(); }
        public void SetIdleStyle(string st) { Settings.SetS("IslandIdleStyle", st); _notch.PlayIdle(); }
        public void SetDockDot(string c) { Settings.SetS("DockDotColor", c); _dock.Invalidate2(); }

        // Dari tombol di Pengaturan
        public void CheckUpdateNow(IWin32Window owner)
        {
            Updater.Check(_bar, delegate
            {
                if (Updater.Newer)
                {
                    if (MessageBox.Show(owner, "LiquidMac " + Updater.LatestTag + " tersedia (sekarang " + Program.Version + ").\nPerbarui sekarang? LiquidMac akan dimulai ulang.", "Pembaruan LiquidMac", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Updater.Apply(_bar, ExitApp);
                }
                else MessageBox.Show(owner, Updater.Status, "Pembaruan LiquidMac", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }
        private readonly System.Windows.Forms.Timer _trim = new System.Windows.Forms.Timer();
        private readonly List<BannerForm> _banners = new List<BannerForm>();
        private readonly System.Windows.Forms.Timer _notifTimer;
        private long _lastNotif = -1;
        private int _notifTicks;
        private bool _fullscreenHidden;
        private int _watchTicks;

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private bool _exiting;

        public TrayApp(bool openSettings)
        {
            _bar = new TopBarForm();
            _notch = new NotchForm();
            _dock = new DockForm();
            _bar.AutoHide = Settings.Get("AutoHideBar", true);
            _bar.Reserve = Settings.Get("ShowBar", true) && Settings.Get("ReserveBar", true);
            Tiles.Enabled = Settings.Get("IconTiles", true);
            _dock.Reserve = Settings.Get("ShowDock", true) && Settings.Get("ReserveDock", true);

            // buat handle dulu agar AppBar bisa didaftarkan, lalu tampilkan sesuai pengaturan
            _bar.CreateControl(); IntPtr h1 = _bar.Handle;
            _notch.CreateControl(); IntPtr h2 = _notch.Handle;
            _dock.CreateControl(); IntPtr h3 = _dock.Handle;
            WinAnim.DockHwnd = h3;
            ApplyVisibility();
            _bar.Reposition();
            _dock.Reposition();

            _ni = new NotifyIcon();
            _ni.Icon = MakeIcon();
            _ni.Text = "LiquidMac - macOS Liquid Glass untuk Windows";
            _ni.Visible = true;
            _ni.ContextMenuStrip = Menus.Create();
            _ni.ContextMenuStrip.Opening += delegate { BuildTrayMenu(); };
            _ni.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            BuildTrayMenu();

            Program.SettingsRequested = ShowSettings;
            _msg = new MessageWindow(delegate (int cmd)
            {
                if (cmd == 1) _launchpad.Toggle();
                else if (cmd == 2) OpenNotificationCenter();
                else if (cmd == 3) TestBanner();
                else if (cmd == 8) ExplorerIcons.Apply();
                else if (cmd == 9) ExitApp();
                else if (cmd == 10) Uninstall();
                else if (cmd == 11) { if (_donateForm == null || _donateForm.IsDisposed) _donateForm = new DonateForm(); _donateForm.Popup(); }
                else if (cmd >= 4 && cmd <= 7) { string[] md = { "wifi", "sound", "battery", "control" }; _cp.Toggle(md[cmd - 4], Screen.PrimaryScreen.Bounds.Right - Ui.P(60), _bar.Visible ? _bar.VisibleBottom : 0); }
                else ShowSettings();
            });

            _launchpad = new LaunchpadForm();
            _launchpad.AddToDock = delegate (string e) { _dock.AddEntry(e); };
            _nc = new NotificationCenterForm();
            _cp = new ControlPanelForm();
            Program.ControlAction = delegate (string mode, int anchorRight) { _cp.Toggle(mode, anchorRight, _bar.Visible ? _bar.VisibleBottom : 0); };
            _profile = new ProfileForm();
            Program.ProfileAction = delegate (int anchorRight) { _profile.Toggle(anchorRight, _bar.Visible ? _bar.VisibleBottom : 0); };
            SysInfo.LoadAsync(_bar, delegate { _bar.Invalidate2(); });
            if (Program.SnapshotDir == null) StartLook();
            if (Program.SnapshotDir == null)
            {
                _donate = new System.Windows.Forms.Timer(); _donate.Interval = 5 * 60 * 1000;
                _donate.Tick += delegate { _donate.Interval = 30 * 60 * 1000; CheckDonate(); };
                _donate.Start();
            }
            Media.Start();
            _lights = new TrafficLights();
            if (Program.SnapshotDir == null && Settings.Get("MacButtons", true)) _lights.Start();
            if (Settings.Get("CleanDesktop", false)) DesktopIcons.Visible = false;
            if (Program.SnapshotDir == null && Settings.Get("MacSounds", false) && !File.Exists(Path.Combine(SoundKit.Dir, "backup.txt"))) SoundKit.Apply();
            // rapikan memori berkala (hemat RAM)
            _trim.Interval = 45000;
            _trim.Tick += delegate { MemoryTrim.Run(); };
            _trim.Start();
            Program.LaunchpadAction = delegate { _launchpad.Toggle(); };
            Program.NotificationAction = delegate { _nc.Toggle(_bar.Visible ? _bar.VisibleBottom : 0); };
            Program.LaunchpadVisible = delegate { return _launchpad.Visible; };
            if (Program.SnapshotDir == null && Settings.Get("WinKeyLaunchpad", true)) WinKeyHook.Start(_bar, delegate { _launchpad.Toggle(); });
            if (Program.SnapshotDir == null && Settings.Get("IslandVolumeKeys", true)) WinKeyHook.StartVolume(_bar, _notch.VolumeKey);
            if (Program.SnapshotDir == null && Settings.Get("IslandBrightness", true)) StartBrightness();
            if (Program.SnapshotDir == null && Settings.Get("MacButtons", true)) WinAnim.SyncSystem();

            if (Program.SnapshotDir == null && Settings.Get("MacBanners", true)) NotifDb.SuppressWindowsBanners();

            _notifTimer = new System.Windows.Forms.Timer();
            _notifTimer.Interval = 2000;
            _notifTimer.Tick += delegate { try { CheckNotifications(); } catch (Exception ex) { Log.Write("notif: " + ex.Message); } };
            _notifTimer.Start();

            System.Windows.Forms.Timer warm = new System.Windows.Forms.Timer();
            warm.Interval = 6000;
            warm.Tick += delegate { warm.Stop(); warm.Dispose(); try { Apps.All(); LaunchpadForm.Preload(); } catch { } };
            warm.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            Application.ApplicationExit += delegate { Cleanup(); };

            if (Program.SnapshotDir == null)
            {
                _upd = new System.Windows.Forms.Timer();
                _upd.Interval = 90 * 1000;                 // cek pertama 1,5 menit setelah mulai, lalu tiap 6 jam
                _upd.Tick += delegate { _upd.Interval = 6 * 3600 * 1000; AutoUpdateCheck(); };
                _upd.Start();
            }
            _watch = new System.Windows.Forms.Timer();
            _watch.Interval = 250;   // layar penuh cepat terdeteksi
            _watch.Tick += delegate
            {
                CheckFullscreen();
                // island selalu di atas bilah menu
                if (_notch.Visible) SetWindowPos(_notch.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                if (++_watchTicks % 16 == 0 && Settings.Get("CleanDesktop", false) && DesktopIcons.Visible) DesktopIcons.Visible = false;
            };
            _watch.Start();

            if (Program.SnapshotDir != null)
            {
                System.Windows.Forms.Timer st = new System.Windows.Forms.Timer(); st.Interval = 1500;
                st.Tick += delegate
                {
                    st.Stop();
                    string d = Program.SnapshotDir; Directory.CreateDirectory(d);
                    _bar.Snapshot(Path.Combine(d, "bar.png"));
                    _dock.Snapshot(Path.Combine(d, "dock.png"));
                    _notch.ForceExpand(); _notch.Snapshot(Path.Combine(d, "notch.png"));
                    _notch.SnapshotCards(d);
                    try { new ProfileForm().SnapshotTo(Path.Combine(d, "profile.png")); } catch (Exception ex) { Log.Write("snap profile: " + ex); }
                    try { MacLock.Render(Path.Combine(d, "lock.jpg")); } catch (Exception ex) { Log.Write("snap lock: " + ex); }
                    try { DonateForm df = new DonateForm(); for (int di = 0; di < 4; di++) df.SnapshotTo(Path.Combine(d, "donate-" + di + ".png"), 0.3 + di * 0.13); } catch (Exception ex) { Log.Write("snap donate: " + ex); }
                    if (Environment.GetEnvironmentVariable("LM_PROMO") != null) { try { if (Environment.GetEnvironmentVariable("LM_ANIMONLY") == null) Promo.Run(d, _notch, _dock); Promo.RunAnim(d, _notch, _dock); } catch (Exception ex) { Log.Write("promo: " + ex); } }
                    if (Environment.GetEnvironmentVariable("LM_BADGE") != null) { _dock.PromoFrame(Path.Combine(d, "dock-a.png")); System.Threading.Thread.Sleep(6000); for (int bi = 0; bi < 30; bi++) _dock.PromoFrame(Path.Combine(d, "dock-badge.png")); }
                    if (Environment.GetEnvironmentVariable("LM_NOSETTINGS") != null) { ExitApp(); return; }
                    SettingsForm sf = new SettingsForm(this); sf.Show();
                    using (Bitmap b = new Bitmap(sf.Width, sf.Height)) { sf.DrawToBitmap(b, new Rectangle(0, 0, sf.Width, sf.Height)); b.Save(Path.Combine(d, "settings.png")); }
                    sf.AutoScrollToEnd();
                    using (Bitmap b = new Bitmap(sf.Width, sf.Height)) { sf.DrawToBitmap(b, new Rectangle(0, 0, sf.Width, sf.Height)); b.Save(Path.Combine(d, "settings2.png")); }
                    ExitApp();
                };
                st.Start();
                return;
            }
            if (openSettings || !Settings.Get("FirstRunDone", false))
            {
                Settings.Set("FirstRunDone", true);
                if (!AutoStart.Enabled) AutoStart.Enabled = true;   // dijalankan pertama kali (mis. exe portable): aktifkan autostart
                ShowSettings();
            }
        }

        // ---------- API untuk jendela Pengaturan ----------
        private static readonly string[] OffByDefault = { "HideTaskbar", "MacSounds", "CleanDesktop", "MacFont", "MacCursor", "MacCursorLarge", "MacLock", "IslandHidden" };
        public bool Get(string key) { return Settings.Get(key, Array.IndexOf(OffByDefault, key) < 0); }

        // Tangkapan layar, privasi, font, kursor, layar kunci
        private void StartLook()
        {
            if (Settings.Get("IslandShots", true)) ShotWatch.Start(_notch);
            if (Settings.Get("IslandPrivacy", true)) PrivacyWatch.Start(_notch);
            try { if (Settings.Get("MacFont", false) && !MacFont.Applied) MacFont.Apply(); } catch (Exception ex) { Log.Write("font: " + ex.Message); }
            try { if (Settings.Get("MacCursor", false) && !MacCursor.Applied) MacCursor.Apply(Settings.Get("MacCursorLarge", false)); } catch (Exception ex) { Log.Write("cursor: " + ex.Message); }
            if (Settings.Get("MacLock", false) && !File.Exists(Path.Combine(Log.Dir, "lockscreen.jpg"))) MacLock.ApplyAsync();
            _lockDebounce = new System.Windows.Forms.Timer(); _lockDebounce.Interval = 4000;
            _lockDebounce.Tick += delegate { _lockDebounce.Stop(); if (Settings.Get("MacLock", false)) MacLock.ApplyAsync(); };
            SystemEvents.UserPreferenceChanged += OnPrefChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        // Setelah layar dibuka: perbarui gambar layar kunci (wallpaper hidup bisa sudah berganti)
        private void OnSessionSwitch(object s, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock && _lockDebounce != null && Settings.Get("MacLock", false)) { _lockDebounce.Stop(); _lockDebounce.Start(); }
        }

        private void OnPrefChanged(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Desktop && _lockDebounce != null && Settings.Get("MacLock", false)) { _lockDebounce.Stop(); _lockDebounce.Start(); }
        }

        // Mulai ulang LiquidMac (mis. setelah font antarmuka berganti)
        public void RestartApp()
        {
            try
            {
                ProcessStartInfo si = new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & start \"\" \"" + Application.ExecutablePath + "\"");
                si.CreateNoWindow = true; si.UseShellExecute = false; si.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(si);
            }
            catch (Exception ex) { Log.Write("restart: " + ex.Message); return; }
            ExitApp();
        }

        private void StartBrightness()
        {
            OsdSuppressor.Start();
            BrightnessWatch.Start(_bar, _notch.BrightnessChanged);
            int b = SystemControls.Brightness; if (b >= 0) _notch.BrightnessChanged(b);   // nilai awal, tanpa HUD
        }

        public void SetComponent(string key, bool on)
        {
            Settings.Set(key, on);
            if (key == "ShowBar" || key == "ReserveBar")
            {
                _bar.Reserve = Settings.Get("ShowBar", true) && Settings.Get("ReserveBar", true);
                _bar.Reposition();
            }
            if (key == "ShowDock" || key == "ReserveDock")
            {
                _dock.Reserve = Settings.Get("ShowDock", true) && Settings.Get("ReserveDock", true);
                _dock.Reposition();
            }
            if (key == "HideOnFullscreen" && !on && _fullscreenHidden) _fullscreenHidden = false;
            if (key == "AutoHideBar") { _bar.AutoHide = on; _bar.Reposition(); }
            if (key == "IconTiles") { Tiles.Enabled = on; Apps.ResetTiles(); _dock.ReloadItems(); }
            if (key == "DockPlainIcons") _dock.ReloadItems();
            if (key == "DockShelf") _dock.Invalidate2();
            if (key == "MacWindowAnim" || key == "MacButtons") { if (on && Settings.Get("MacButtons", true)) WinAnim.SyncSystem(); else WinAnim.RestoreSystem(); }
            if (key == "WinKeyLaunchpad") { if (on) WinKeyHook.Start(_bar, delegate { _launchpad.Toggle(); }); else WinKeyHook.Stop(); }
            if (key == "IslandVolumeKeys") { if (on) WinKeyHook.StartVolume(_bar, _notch.VolumeKey); else WinKeyHook.StopVolume(); }
            if (key == "IslandBrightness") { if (on) StartBrightness(); else { OsdSuppressor.Stop(); BrightnessWatch.Stop(); } }
            if (key == "MacBanners") { if (on) NotifDb.SuppressWindowsBanners(); else NotifDb.RestoreWindowsBanners(); }
            if (key == "MacButtons") { if (on) _lights.Start(); else _lights.Stop(); }
            if (key == "MacSounds") { if (on) SoundKit.Apply(); else SoundKit.Restore(); }
            if (key == "CleanDesktop") DesktopIcons.Visible = !on;
            if (key == "MacFont") { try { if (on) MacFont.Apply(); else MacFont.Restore(); } catch (Exception ex) { Log.Write("font: " + ex.Message); } }
            if (key == "MacCursor" || key == "MacCursorLarge") { try { if (Settings.Get("MacCursor", false)) MacCursor.Apply(Settings.Get("MacCursorLarge", false)); else MacCursor.Restore(); } catch (Exception ex) { Log.Write("cursor: " + ex.Message); } }
            if (key == "MacLock") { if (on) MacLock.ApplyAsync(); else MacLock.RestoreAsync(); }
            if (key == "IslandShots") { if (on) ShotWatch.Start(_notch); else ShotWatch.Stop(); }
            if (key == "IslandPrivacy") { if (on) PrivacyWatch.Start(_notch); else PrivacyWatch.Stop(); }
            if (key == "IslandHideInCapture") _notch.SetCaptureHidden(on);
            if (key == "IslandHidden" || key == "IslandRec") _notch.Invalidate2();
            if (key == "BarProfile") _bar.Invalidate2();
            ApplyVisibility();
        }

        public bool TaskbarAutoHide
        {
            get { return AppBar.TaskbarAutoHide; }
            set
            {
                AppBar.TaskbarAutoHide = value;
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 700;
                t.Tick += delegate { t.Stop(); t.Dispose(); _bar.Reposition(); _dock.Reposition(); };
                t.Start();
            }
        }

        public void ReloadDock() { _dock.ReloadItems(); }
        public void SetDockSize(int px) { Settings.SetS("DockSize", px.ToString()); _dock.ReloadItems(); }
        public void AddToDock() { _dock.PickAndAdd(); }
        public void ResetDock() { DockStore.Reset(); _dock.ReloadItems(); }
        public void OpenLaunchpad() { _launchpad.Toggle(); }
        public void OpenNotificationCenter() { _nc.Toggle(_bar.Visible ? _bar.VisibleBottom : 0); }

        public void TestBanner()
        {
            Notif n = new Notif();
            n.App = "LiquidMac"; n.Title = "Banner notifikasi aktif";
            n.Body = "Notifikasi aplikasi akan muncul di sini, di kanan atas, seperti macOS.";
            n.Arrival = DateTime.Now.ToFileTimeUtc();
            ShowBanner(n);
        }

        private void CheckNotifications()
        {
            List<Notif> l = NotifDb.Read(6);
            long max = _lastNotif;
            foreach (Notif n in l) if (n.Arrival > max) max = n.Arrival;
            if (_lastNotif < 0) { _lastNotif = Math.Max(0, max); return; }
            List<Notif> fresh = l.FindAll(delegate (Notif n) { return n.Arrival > _lastNotif; });
            _lastNotif = max;
            if (++_notifTicks % 30 == 0 && Settings.Get("MacBanners", true)) NotifDb.SuppressWindowsBanners();
            if (!Settings.Get("MacBanners", true) || Settings.Get("DND", false) || _fullscreenHidden || fresh.Count == 0) return;
            fresh.Reverse();
            foreach (Notif n in fresh) ShowBanner(n);
        }

        private void ShowBanner(Notif n)
        {
            while (_banners.Count >= 3) { BannerForm old = _banners[0]; _banners.RemoveAt(0); old.Dismiss(); }
            BannerForm b = new BannerForm(n);
            b.TopOffset = _bar.Visible ? _bar.VisibleBottom : 0;
            b.Slot = _banners.Count;
            b.Done += delegate
            {
                _banners.Remove(b);
                for (int i = 0; i < _banners.Count; i++) { _banners[i].Slot = i; _banners[i].Reposition(); }
            };
            _banners.Add(b);
            b.Show();
        }

        // Dipakai uninstaller: kembalikan semua perubahan tampilan Windows, lalu keluar tanpa autostart.
        public void Uninstall()
        {
            try { if (Settings.Get("MacSounds", false)) SetComponent("MacSounds", false); } catch (Exception ex) { Log.Write("uninstall sounds: " + ex.Message); }
            try { if (Settings.Get("CleanDesktop", false)) SetComponent("CleanDesktop", false); } catch (Exception ex) { Log.Write("uninstall desktop: " + ex.Message); }
            try { if (AppBar.TaskbarAutoHide) AppBar.TaskbarAutoHide = false; } catch { }
            try { if (Settings.Get("MacExplorerIcons", false)) ExplorerIcons.Restore(); } catch (Exception ex) { Log.Write("uninstall icons: " + ex.Message); }
            try { MacFont.Restore(); } catch { }
            try { MacCursor.Restore(); } catch { }
            try { if (Settings.Get("MacLock", false)) MacLock.Restore(); } catch { }
            DisableCompletely();
        }

        public void DisableCompletely()
        {
            AutoStart.Enabled = false;
            ExitApp();
        }

        public void ExitApp()
        {
            Cleanup();
            _watch.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            if (_settings != null && !_settings.IsDisposed) _settings.Close();
            Application.Exit();
        }

        public void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(this);
            _settings.Show();
            try { if (_lights != null && _lights.Running) _lights.Poke(); } catch { }
            if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
            _settings.BringToFront();
        }

        // ---------------------------------------------------
        private void ApplyVisibility()
        {
            bool hide = _fullscreenHidden;
            SetVis(_bar, !hide && Settings.Get("ShowBar", true));
            SetVis(_notch, !hide && Settings.Get("ShowNotch", true));
            SetVis(_dock, !hide && Settings.Get("ShowDock", true));
        }

        private static void SetVis(Form f, bool v) { if (f.Visible != v) { if (v) f.Show(); else f.Hide(); } }

        private void OnDisplayChanged(object s, EventArgs e)
        {
            try { _bar.Reposition(); _notch.Reposition(); _dock.Reposition(); }
            catch (Exception ex) { Log.Write("display: " + ex.Message); }
        }

        private void OnSessionEnding(object s, SessionEndingEventArgs e) { Cleanup(); }

        private void CheckFullscreen()
        {
            bool fs = false;
            if (Settings.Get("HideOnFullscreen", true))
                try { fs = Windows.IsFullscreen(Native.GetForegroundWindow()); } catch { }
            if (fs == _fullscreenHidden) return;
            _fullscreenHidden = fs;
            ApplyVisibility();
        }

        private void BuildTrayMenu()
        {
            ContextMenuStrip cm = _ni.ContextMenuStrip;
            cm.Items.Clear();
            ToolStripMenuItem s = Menus.Add(cm, "Pengaturan...", delegate { ShowSettings(); });
            s.Font = new Font(cm.Font, FontStyle.Bold);
            Menus.Sep(cm);
            Check(cm, "Bilah menu atas", Settings.Get("ShowBar", true), delegate { SetComponent("ShowBar", !Settings.Get("ShowBar", true)); });
            Check(cm, "Notch (Dynamic Island)", Settings.Get("ShowNotch", true), delegate { SetComponent("ShowNotch", !Settings.Get("ShowNotch", true)); });
            Check(cm, "Dock", Settings.Get("ShowDock", true), delegate { SetComponent("ShowDock", !Settings.Get("ShowDock", true)); });
            Menus.Sep(cm);
            Check(cm, "Jalankan saat Windows mulai", AutoStart.Enabled, delegate { AutoStart.Enabled = !AutoStart.Enabled; });
            Menus.Add(cm, "Muat ulang ikon Dock", delegate { _dock.ReloadItems(); });
            Menus.Sep(cm);
            Menus.Add(cm, "Donasi (Saweria)...", delegate { Shell.Run(Program.DonateUrl, null); });
            Menus.Add(cm, "Keluar", delegate { ExitApp(); });
        }

        private static void Check(ContextMenuStrip m, string text, bool on, EventHandler click)
        {
            Menus.Add(m, (on ? "\u2713  " : "     ") + text, click);
        }

        private void Cleanup()
        {
            if (_exiting) return;
            _exiting = true;
            try { _bar.Release(); } catch { }
            try { _dock.Release(); } catch { }
            try { WinKeyHook.StopAll(); } catch { }
            try { OsdSuppressor.Stop(); BrightnessWatch.Stop(); } catch { }
            try { WinAnim.RestoreSystem(); } catch { }

            try { Media.Stop(); } catch { }
            try { if (Rec.Active) Rec.Stop(); } catch { }
            try { ShotWatch.Stop(); PrivacyWatch.Stop(); } catch { }
            try { SystemEvents.UserPreferenceChanged -= OnPrefChanged; SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
            try { _lights.Stop(); } catch { }
            try { if (Program.SnapshotDir == null && Settings.Get("MacBanners", true)) NotifDb.RestoreWindowsBanners(); } catch { }
            try { _ni.Visible = false; } catch { }
        }

        public static Icon MakeIcon()
        {
            using (Bitmap b = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Glass.Round(new Rectangle(2, 2, 28, 28), 8))
                    using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(2, 2, 28, 28), Color.FromArgb(120, 80, 255), Color.FromArgb(255, 90, 150), 45f))
                        g.FillPath(br, p);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(235, 0, 0, 0)))
                    using (GraphicsPath np = Glass.Round(new Rectangle(10, 7, 12, 6), 3))
                        g.FillPath(sb, np);
                    using (SolidBrush wb = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                    using (GraphicsPath dp = Glass.Round(new Rectangle(7, 21, 18, 5), 2))
                        g.FillPath(wb, dp);
                }
                IntPtr h = b.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return ic;
            }
        }
    }

    // Jendela tersembunyi penerima pesan "buka pengaturan" dari instance kedua.
    internal sealed class MessageWindow : NativeWindow
    {
        public static readonly uint Msg = Native.RegisterWindowMessage("LiquidMacShowSettings");
        private readonly Action<int> _onShow;
        public MessageWindow(Action<int> onShow) { _onShow = onShow; CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Msg) { try { _onShow(m.WParam.ToInt32()); } catch (Exception ex) { Log.Write("command: " + ex.Message); } }
            base.WndProc(ref m);
        }
    }

    // ============================================================
    //  SAKELAR (toggle switch) gaya Windhawk / macOS
    // ============================================================
    internal sealed class ToggleSwitch : Control
    {
        private bool _on;
        private float _pos;
        private readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer();
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Size = new Size(Ui.P(44), Ui.P(24));
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            _anim.Interval = 15;
            _anim.Tick += delegate
            {
                float target = _on ? 1f : 0f;
                _pos += (target - _pos) * 0.35f;
                if (Math.Abs(target - _pos) < 0.01f) { _pos = target; _anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return _on; }
            set { SetChecked(value, false); }
        }

        private void SetChecked(bool v, bool user)
        {
            if (_on == v) return;
            _on = v;
            if (user) _anim.Start(); else { _pos = v ? 1f : 0f; Invalidate(); }
            if (user && CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) SetChecked(!_on, true); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            Color off = Color.FromArgb(70, 70, 82), on = Color.FromArgb(48, 209, 88);
            Color c = Color.FromArgb(
                (int)(off.R + (on.R - off.R) * _pos), (int)(off.G + (on.G - off.G) * _pos), (int)(off.B + (on.B - off.B) * _pos));
            if (!Enabled) c = Color.FromArgb(50, 50, 58);
            using (GraphicsPath p = Glass.Round(r, r.Height / 2))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
            float d = r.Height - Ui.Pf(4);
            float x = r.X + Ui.Pf(2) + (r.Width - d - Ui.Pf(4)) * _pos;
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(sh, x, r.Y + Ui.Pf(3), d, d);
            using (SolidBrush k = new SolidBrush(Enabled ? Color.White : Color.FromArgb(140, 140, 150))) g.FillEllipse(k, x, r.Y + Ui.Pf(2), d, d);
        }
    }

    // ============================================================
    //  JENDELA PENGATURAN (kartu ala Windhawk)
    // ============================================================
    internal sealed class SettingsForm : Form
    {
        private readonly TrayApp _app;
        private readonly FlowLayoutPanel _list;
        private static readonly Color Bg = Color.FromArgb(24, 24, 30), Card = Color.FromArgb(36, 36, 44), CardLine = Color.FromArgb(54, 54, 64);
        private static readonly Color Text1 = Color.FromArgb(240, 240, 245), Text2 = Color.FromArgb(160, 162, 175), Accent = Color.FromArgb(10, 132, 255);
        private readonly Font _h1 = Ui.F(16f, FontStyle.Bold), _h2 = Ui.F(10.5f, FontStyle.Bold), _t = Ui.F(10f, FontStyle.Regular), _s = Ui.F(8.75f, FontStyle.Regular);

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public SettingsForm(TrayApp app)
        {
            _app = app;
            Text = "Pengaturan LiquidMac";
            Icon = TrayApp.MakeIcon();
            BackColor = Bg;
            ForeColor = Text1;
            Font = _t;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.P(620), Ui.P(720));
            MinimumSize = new Size(Ui.P(520), Ui.P(420));
            ShowInTaskbar = true;

            _list = new FlowLayoutPanel();
            _list.Dock = DockStyle.Fill;
            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.AutoScroll = true;
            _list.Padding = new Padding(Ui.P(22), Ui.P(18), Ui.P(22), Ui.P(18));
            _list.BackColor = Bg;
            Controls.Add(_list);
            _list.SizeChanged += delegate { FitWidths(); };

            Build();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, 4);       // judul gelap
            int corner = 2;
            DwmSetWindowAttribute(Handle, 33, ref corner, 4);   // sudut membulat
        }

        public void AutoScrollToEnd() { _list.ScrollControlIntoView(_list.Controls[_list.Controls.Count - 1]); _list.Refresh(); }

        private void FitWidths()
        {
            int w = _list.ClientSize.Width - _list.Padding.Horizontal - (_list.VerticalScroll.Visible ? 0 : 0);
            foreach (Control c in _list.Controls) c.Width = Math.Max(Ui.P(300), w);
        }

        private void Build()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();

            // header
            Panel head = new Panel();
            head.Height = Ui.P(70);
            head.BackColor = Bg;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (Icon ic = TrayApp.MakeIcon()) g.DrawIcon(ic, new Rectangle(0, Ui.P(6), Ui.P(40), Ui.P(40)));
                using (SolidBrush b1 = new SolidBrush(Text1)) g.DrawString("LiquidMac", _h1, b1, Ui.P(50), Ui.P(2));
                using (SolidBrush b2 = new SolidBrush(Text2)) g.DrawString("Menu bar, notch, dan Dock gaya macOS Liquid Glass untuk Windows", _s, b2, Ui.P(52), Ui.P(34));
            };
            _list.Controls.Add(head);

            Section("Komponen");
            ToggleCard("Bilah menu atas", "Menu bar macOS: menu aplikasi aktif, menu Go/Window/Edit yang berfungsi, status Wi-Fi, volume, baterai, jam.", "ShowBar");
            ToggleCard("Notch / Dynamic Island", "Pil hitam di tengah atas. Arahkan kursor untuk jam, CPU, RAM, baterai, dan kontrol media.", "ShowNotch");
            ToggleCard("Dock", "Aplikasi tersemat di taskbar + aplikasi yang sedang berjalan, efek pembesaran, titik indikator, Tong Sampah.", "ShowDock");
            ToggleCard("Traffic light (tombol jendela macOS)", "Tombol tutup/kecilkan/perbesar semua jendela diganti lampu merah-kuning-hijau. Matikan untuk kembali ke tombol Windows.", "MacButtons");

            Section("Perilaku");
            ToggleCard("Sembunyikan otomatis bilah atas", "Bilah menu turun saat kursor menyentuh tepi atas layar, lalu naik lagi.", "AutoHideBar");
            ToggleCard("Jendela tidak menutupi bilah atas", "Jendela yang dimaksimalkan berhenti di bawah bilah menu (tidak berlaku saat bilah sembunyi otomatis).", "ReserveBar");
            ToggleCard("Jendela tidak menutupi Dock", "Sisihkan ruang di bawah untuk Dock, seperti macOS.", "ReserveDock");
            ToggleCard("Sembunyi saat layar penuh", "Bilah, notch, dan Dock disembunyikan saat video, game, atau presentasi layar penuh.", "HideOnFullscreen");
            ToggleSwitch tb = ToggleCardRaw("Sembunyikan taskbar Windows otomatis", "Taskbar muncul hanya saat kursor di tepi bawah; cocok bila Dock LiquidMac dipakai.", _app.TaskbarAutoHide);
            tb.CheckedChanged += delegate { _app.TaskbarAutoHide = tb.Checked; };

            Section("Startup");
            ToggleSwitch au = ToggleCardRaw("Jalankan otomatis saat Windows mulai", "LiquidMac langsung aktif lagi setiap kali laptop dinyalakan atau di-restart.", AutoStart.Enabled);
            au.CheckedChanged += delegate { AutoStart.Enabled = au.Checked; };

            Section("Dynamic Island");
            ToggleCard("Media di island", "Lagu/video yang sedang diputar (YouTube, Spotify, dll.): sampul, judul, progres, dan tombol kontrol.", "IslandMedia");
            ToggleCard("Kartu cuaca", "Kartu cuaca di island (kota, suhu, kondisi) dari wttr.in; lokasi ditebak dari alamat IP. Matikan bila tidak perlu.", "IslandWeather");
            ToggleCard("HUD volume & pengisian daya", "Island melebar sebentar saat volume diubah atau charger dicolok.", "IslandHud");
            ToggleCard("Sembunyikan media yang dijeda", "Media yang dijeda lebih dari 3 menit hilang dari island (muncul lagi saat diputar). Tombol x di kartu media menutupnya kapan saja.", "IslandMediaAutoHide");
            ToggleCard("Island tersembunyi", "Notch tidak terlihat. Arahkan kursor ke tengah atas layar: island muncul dan mengembang dengan animasi cair. Rekaman, HUD, dan kamera/mic tetap tampil.", "IslandHidden");
            ToggleCard("Pratinjau tangkapan layar", "Setelah Win+PrtScn / Snipping Tool, island menampilkan pratinjau beberapa detik. Klik untuk membuka, tombol map untuk menunjukkan berkasnya.", "IslandShots");
            ToggleCard("Kartu Rekam di island", "Rekam layar (MP4, opsional dengan mikrofon) dan rekam suara (M4A) dengan gelombang suara ala iPhone, dikendalikan dari island.", "IslandRec");
            ToggleCard("Indikator kamera & mikrofon", "Island memberi tahu saat aplikasi mulai memakai kamera (hijau) atau mikrofon (oranye), lalu titik kecil tetap menyala selama dipakai.", "IslandPrivacy");
            ToggleCard("Island tidak ikut terekam", "Seperti notch MacBook: island tidak muncul di tangkapan layar, rekaman, dan berbagi layar.", "IslandHideInCapture");
            ToggleCard("Latar gradasi bergerak", "Saat island dibuka, latar hitam diberi gradasi warna lembut yang bergerak perlahan (seperti Apple Music). Warnanya mengikuti kartu; untuk musik diambil dari sampul lagu.", "IslandAura");
            ToggleCard("Animasi lucu saat standby", "Sesekali island yang diam menyusut, bergelombang, lalu memainkan animasi kecil sebelum kembali.", "IslandIdleAnim");
            ButtonCard("Gaya animasi standby", "Pusaran air (spiral lembut), detak jantung (hati berdenyut + garis EKG), atau bergantian. Klik untuk langsung melihat contohnya di island.",
                new string[] { "Pusaran air", "Detak jantung", "Bergantian" },
                new EventHandler[] { delegate { _app.SetIdleStyle("air"); }, delegate { _app.SetIdleStyle("jantung"); }, delegate { _app.SetIdleStyle("acak"); } });
            ToggleCard("Bunyi saat island disentuh", "Bunyi \"pop\" sangat pelan saat kursor membuka Dynamic Island.", "IslandHoverSound");
            ToggleCard("Tombol volume lewat island", "Fn+Vol ditangani LiquidMac: pop-up volume bawaan Windows disembunyikan, diganti HUD island.", "IslandVolumeKeys");
            ToggleCard("Kecerahan lewat island", "Pop-up kecerahan (dan pop-up hardware lain) bawaan Windows disembunyikan, diganti HUD island.", "IslandBrightness");

            Section("Gaya macOS");
            {
                ToggleSwitch fs = ToggleCardRaw("Font macOS (Inter)", "Judul jendela, menu klasik, kotak pesan, label ikon desktop, dan seluruh LiquidMac memakai Inter (mirip SF Pro). Aplikasi modern Windows tetap Segoe.", _app.Get("MacFont"));
                fs.CheckedChanged += delegate
                {
                    _app.SetComponent("MacFont", fs.Checked);
                    if (MessageBox.Show(this, "Font sistem sudah " + (fs.Checked ? "diganti" : "dikembalikan") + ". Mulai ulang LiquidMac sekarang agar tampilannya ikut berganti?", "LiquidMac", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) _app.RestartApp();
                };
            }
            ToggleCard("Kursor macOS", "Panah, I-beam, tangan, roda berputar, dan kursor ubah ukuran gaya macOS. Dimatikan = kursor Windows dikembalikan.", "MacCursor");
            ToggleCard("Kursor macOS ukuran besar", "Pakai versi besar (cocok untuk layar beresolusi tinggi).", "MacCursorLarge");
            ToggleCard("Layar kunci gaya macOS", "Gambar layar kunci dibuat dari wallpaper Anda + foto, nama, dan kolom sandi di tengah bawah, tanpa iklan Spotlight. Ikut diperbarui saat wallpaper berganti.", "MacLock");
            ToggleCard("Profil di bilah atas", "Foto pengguna di bilah menu; klik untuk info akun, perangkat, prosesor, memori, penyimpanan, dan baterai.", "BarProfile");

            Section("Start & Launchpad");
            ToggleCard("Tombol Windows membuka Launchpad", "Tekan tombol Windows untuk Launchpad (semua aplikasi + pencarian, latar blur). Ctrl+Esc tetap membuka Start bawaan.", "WinKeyLaunchpad");
            ButtonCard("Launchpad", "Pengganti Start menu gaya macOS. Ketik untuk mencari, roda mouse untuk pindah halaman, klik kanan aplikasi untuk menambahkannya ke Dock.",
                new string[] { "Buka Launchpad" }, new EventHandler[] { delegate { _app.OpenLaunchpad(); } });

            Section("Notifikasi");
            ToggleCard("Banner notifikasi di kanan atas", "Banner Windows (kanan bawah) diganti banner LiquidMac di kanan atas. Semua notifikasi tetap tersimpan di Pusat Notifikasi.", "MacBanners");
            ButtonCard("Pusat Notifikasi", "Klik jam di bilah atas: kalender, volume, kecerahan, Wi-Fi/Bluetooth/Fokus, dan daftar notifikasi.",
                new string[] { "Buka", "Uji banner" }, new EventHandler[] { delegate { _app.OpenNotificationCenter(); }, delegate { _app.TestBanner(); } });

            Section("Dock");
            ButtonCard("Latar Dock", "Sekarang: " + (Settings.GetS("DockBg", "gradient") == "glass" ? "kaca membulat" : Settings.GetS("DockBg", "gradient") == "none" ? "tanpa latar" : "gradasi") + ".",
                new string[] { "Gradasi", "Kaca membulat", "Tanpa latar" },
                new EventHandler[] { delegate { _app.SetDockBg("gradient"); }, delegate { _app.SetDockBg("glass"); }, delegate { _app.SetDockBg("none"); } });
            ButtonCard("Warna titik aplikasi aktif", "Titik kecil di bawah ikon aplikasi yang sedang terbuka. Sekarang: " + Settings.GetS("DockDotColor", "Hijau") + ".",
                DockForm.DotNames,
                new EventHandler[] { delegate { _app.SetDockDot("Hijau"); }, delegate { _app.SetDockDot("Biru"); }, delegate { _app.SetDockDot("Oranye"); }, delegate { _app.SetDockDot("Putih"); } });
            ToggleCard("Inisial profil browser", "Ikon Chrome/Edge/Brave yang sedang berjalan diberi badge inisial profil (warna sesuai profil) di pojok kanan bawah.", "DockBrowserProfile");
            ToggleCard("Papan Dock", "Papan kaca tipis di bawah ikon (gaya rak macOS klasik).", "DockShelf");
            ToggleCard("Ikon Dock polos", "Ikon aplikasi ditampilkan apa adanya, tanpa kotak membulat.", "DockPlainIcons");
            ToggleCard("Bunyi hover Dock", "Bunyi \"tik\" sangat pelan saat kursor berpindah ikon di Dock.", "DockHoverSound");
            ButtonCard("Ukuran ikon Dock", "Dock otomatis mengecilkan ikon bila isinya banyak agar tetap muat di layar.",
                new string[] { "Kecil", "Sedang", "Besar" },
                new EventHandler[] { delegate { _app.SetDockSize(40); }, delegate { _app.SetDockSize(48); }, delegate { _app.SetDockSize(58); } });
            ToggleCard("Ikon gaya macOS (kotak kaca)", "Setiap ikon aplikasi ditaruh di kotak kaca membulat seperti macOS 26, termasuk di Launchpad.", "IconTiles");
            ButtonCard("Kelola isi Dock", "Tambah: seret aplikasi/folder ke Dock, tombol di bawah, atau klik kanan di Launchpad. Kurangi/geser: klik kanan ikon di Dock.",
                new string[] { "Tambah aplikasi...", "Atur ulang" },
                new EventHandler[] { delegate { _app.AddToDock(); }, delegate { if (MessageBox.Show(this, "Kembalikan isi Dock ke aplikasi yang tersemat di taskbar?", "LiquidMac", MessageBoxButtons.YesNo) == DialogResult.Yes) _app.ResetDock(); } });

            Section("Tampilan");
            ButtonCard("Wallpaper Liquid Glass", "Pasang wallpaper gradien ungu-biru-merah muda. Wallpaper lama disimpan dan bisa dikembalikan.",
                new string[] { "Pasang", "Kembalikan" },
                new EventHandler[] {
                    delegate { Wallpaper.Apply(); },
                    delegate { if (Wallpaper.CanRestore) Wallpaper.Restore(); else MessageBox.Show(this, "Belum ada wallpaper lama yang tersimpan.", "LiquidMac"); } });

            Section("File Explorer");
            ExplorerCard();
            ButtonCard("Ikon Explorer gaya macOS", "Folder biru, drive, This PC, dan Tong Sampah bergaya macOS (digambar LiquidMac). Explorer dimuat ulang; butuh izin admin.",
                new string[] { "Pasang", "Kembalikan" },
                new EventHandler[] { delegate { ExplorerIcons.Apply(); }, delegate { ExplorerIcons.Restore(); } });

            Section("Jendela & Desktop");
            ToggleCard("Animasi jendela LiquidMac", "Minimize mengecil ke ikon Dock, buka dari Dock membesar, zoom halus, dan traffic light ikut beranimasi. Animasi bawaan Windows dimatikan selama aktif.", "MacWindowAnim");
            ButtonCard("Gaya animasi minimize", "Genie (bawaan): jendela tersedot ke belakang ikon Dock. Kantong Ajaib: kantong muncul di atas ikon, jendela berputar masuk, dan saat dibuka keluar lagi dari kantong.",
                new string[] { "Genie (bawaan)", "Kantong Ajaib" },
                new EventHandler[] { delegate { Settings.SetS("WindowAnimStyle", "genie"); }, delegate { Settings.SetS("WindowAnimStyle", "pocket"); } });
            ToggleCard("Suara animasi jendela", "Bunyi \"gelembung\" lembut saat minimize ke Dock dan saat dibuka dari Dock.", "WindowAnimSound");
            ToggleCard("Desktop bersih", "Sembunyikan semua ikon di desktop. Berkas tidak dihapus; buka lewat Finder > Desktop.", "CleanDesktop");

            Section("Suara");
            ToggleCard("Suara sistem gaya macOS", "Suara peringatan, notifikasi, dan colok/cabut perangkat diganti suara LiquidMac. Matikan untuk kembali ke suara Windows.", "MacSounds");
            string[] sn = SoundKit.Names;
            ButtonCard("Suara peringatan", "Klik untuk mendengar & memilih. Sekarang: " + Settings.GetS("AlertSound", "Kaca") + ".",
                new string[] { sn[0], sn[1], sn[2], sn[3] }, new EventHandler[] { Pick("AlertSound", sn[0]), Pick("AlertSound", sn[1]), Pick("AlertSound", sn[2]), Pick("AlertSound", sn[3]) });
            ButtonCard("Suara peringatan (lainnya)", "Pilihan suara peringatan lain.",
                new string[] { sn[4], sn[5], sn[6], sn[7] }, new EventHandler[] { Pick("AlertSound", sn[4]), Pick("AlertSound", sn[5]), Pick("AlertSound", sn[6]), Pick("AlertSound", sn[7]) });
            ButtonCard("Suara notifikasi", "Sekarang: " + Settings.GetS("NotifSound", "Denting") + ".",
                new string[] { "Denting", "Kaca", "Gelembung", "Sonar" }, new EventHandler[] { Pick("NotifSound", "Denting"), Pick("NotifSound", "Kaca"), Pick("NotifSound", "Gelembung"), Pick("NotifSound", "Sonar") });
            ToggleCard("Umpan balik saat volume diubah", "Bunyi \"tik\" singkat saat slider volume dilepas, seperti macOS.", "VolumeFeedback");


            Section("Pembaruan");
            ToggleCard("Perbarui otomatis", "LiquidMac memeriksa GitHub tiap beberapa jam dan memasang versi baru sendiri (dimulai ulang sebentar).", "AutoUpdate");
            ButtonCard("Versi " + Program.Version, "Kode & rilis: github.com/amarashaff/LiquidMac",
                new string[] { "Periksa pembaruan" },
                new EventHandler[] { delegate { _app.CheckUpdateNow(this); } });

            Section("Dukung LiquidMac");
            ButtonCard("Traktir pengembang", "LiquidMac gratis. Kalau bermanfaat, dukung pengembangannya lewat Saweria (QRIS, GoPay, OVO, DANA, bank).",
                new string[] { "Donasi via Saweria", "Halaman GitHub" },
                new EventHandler[] { delegate { Shell.Run(Program.DonateUrl, null); }, delegate { Shell.Run(Program.RepoUrl, null); } });

            Section("Nonaktifkan");
            ButtonCard("Matikan LiquidMac", "Keluar sekarang. \"Nonaktifkan total\" juga mematikan jalan-otomatis, jadi tidak muncul lagi setelah restart.",
                new string[] { "Keluar", "Nonaktifkan total" },
                new EventHandler[] {
                    delegate { _app.ExitApp(); },
                    delegate {
                        if (MessageBox.Show(this, "Matikan LiquidMac dan hapus dari startup Windows?\nBisa diaktifkan lagi dengan membuka LiquidMac.exe.", "LiquidMac", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                            _app.DisableCompletely();
                    } }, true);

            _list.ResumeLayout();
            FitWidths();
        }

        private void Section(string title)
        {
            Label l = new Label();
            l.Text = title.ToUpperInvariant();
            l.Font = Ui.F(8.25f, FontStyle.Bold);
            l.ForeColor = Text2;
            l.AutoSize = false;
            l.Height = Ui.P(30);
            l.TextAlign = ContentAlignment.BottomLeft;
            l.Margin = new Padding(Ui.P(2), Ui.P(8), 0, Ui.P(4));
            _list.Controls.Add(l);
        }

        private Panel CardPanel(int height)
        {
            Panel p = new Panel();
            p.Height = height;
            p.BackColor = Bg;
            p.Margin = new Padding(0, 0, 0, Ui.P(8));
            p.Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(0.5f, 0.5f, p.Width - 1.5f, p.Height - 1.5f);
                using (GraphicsPath gp = Glass.Round(r, Ui.Pf(10)))
                {
                    using (SolidBrush b = new SolidBrush(Card)) e.Graphics.FillPath(b, gp);
                    using (Pen pen = new Pen(CardLine)) e.Graphics.DrawPath(pen, gp);
                }
            };
            _list.Controls.Add(p);
            return p;
        }

        private void Texts(Panel p, string title, string desc, int rightReserve)
        {
            Label t = new Label();
            t.Text = title; t.Font = _h2; t.ForeColor = Text1; t.BackColor = Card;
            t.AutoSize = false; t.Location = new Point(Ui.P(16), Ui.P(12)); t.Height = Ui.P(22);
            Label d = new Label();
            d.Text = desc; d.Font = _s; d.ForeColor = Text2; d.BackColor = Card;
            d.AutoSize = false; d.Location = new Point(Ui.P(16), Ui.P(36)); d.Height = Math.Min(p.Height - Ui.P(44), Ui.P(18) * (desc.Split((char)10).Length + 1));
            p.Controls.Add(t); p.Controls.Add(d);
            p.Resize += delegate { t.Width = p.Width - Ui.P(32) - rightReserve; d.Width = t.Width; };
        }

        private ToggleSwitch ToggleCardRaw(string title, string desc, bool value)
        {
            Panel p = CardPanel(Ui.P(76));
            Texts(p, title, desc, Ui.P(64));
            ToggleSwitch sw = new ToggleSwitch();
            sw.BackColor = Card;
            sw.Checked = value;
            p.Controls.Add(sw);
            p.Resize += delegate { sw.Location = new Point(p.Width - sw.Width - Ui.P(16), (p.Height - sw.Height) / 2); };
            return sw;
        }

        private void ToggleCard(string title, string desc, string key)
        {
            ToggleSwitch sw = ToggleCardRaw(title, desc, _app.Get(key));
            sw.CheckedChanged += delegate { _app.SetComponent(key, sw.Checked); };
        }

        private Button Btn(string text, bool danger)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = danger ? Color.FromArgb(255, 69, 58) : CardLine;
            b.FlatAppearance.MouseOverBackColor = danger ? Color.FromArgb(90, 30, 30) : Color.FromArgb(56, 56, 68);
            b.BackColor = danger ? Color.FromArgb(60, 24, 26) : Color.FromArgb(46, 46, 56);
            b.ForeColor = danger ? Color.FromArgb(255, 120, 110) : Text1;
            b.Font = _s;
            Size ts = TextRenderer.MeasureText(text, _s);
            b.AutoSize = false;
            b.Size = new Size(ts.Width + Ui.P(28), Ui.P(30));
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void ButtonCard(string title, string desc, string[] labels, EventHandler[] actions) { ButtonCard(title, desc, labels, actions, false); }

        private void ButtonCard(string title, string desc, string[] labels, EventHandler[] actions, bool danger)
        {
            Panel p = CardPanel(Ui.P(112));
            Texts(p, title, desc, 0);
            int bx = Ui.P(16);
            for (int i = 0; i < labels.Length; i++)
            {
                Button b = Btn(labels[i], danger && i == labels.Length - 1);
                b.Click += actions[i];
                b.Location = new Point(bx, p.Height - Ui.P(42));
                bx += b.Width + Ui.P(8);
                p.Controls.Add(b);
                b.BringToFront();
            }
        }

        private const string EbmDir = @"C:\Program Files\ExplorerBlurMica";

        private EventHandler Pick(string key, string name)
        {
            return delegate
            {
                Settings.SetS(key, name);
                SoundKit.Preview(name);
                if (SoundKit.Applied) SoundKit.Apply();
            };
        }

        private void ExplorerCard()
        {
            bool installed = File.Exists(Path.Combine(EbmDir, "ExplorerBlurMica.dll"));
            string eff = "?";
            try
            {
                foreach (string line in File.ReadAllLines(Path.Combine(EbmDir, "config.ini")))
                    if (line.StartsWith("effect=")) eff = line.Substring(7).Trim();
            }
            catch { }
            string[] names = { "Blur (Gaussian)", "Acrylic", "Mica", "Blur jernih", "Mica Alt" };
            int ei; string cur = int.TryParse(eff, out ei) && ei >= 0 && ei < names.Length ? names[ei] : "-";
            string desc = installed
                ? "Explorer tembus pandang (ExplorerBlurMica). Efek sekarang: " + cur + ". Ganti efek di bawah; Explorer dimuat ulang otomatis (butuh izin admin)."
                : "ExplorerBlurMica belum terpasang di " + EbmDir + ".";
            if (!installed) { ButtonCard("Liquid Glass untuk Explorer", desc, new string[0], new EventHandler[0]); return; }
            ButtonCard("Liquid Glass untuk Explorer", desc,
                new string[] { "Blur", "Acrylic", "Mica", "Matikan" },
                new EventHandler[] {
                    delegate { ExplorerEffect(0); }, delegate { ExplorerEffect(1); }, delegate { ExplorerEffect(2); },
                    delegate { if (MessageBox.Show(this, "Matikan efek kaca di File Explorer?", "LiquidMac", MessageBoxButtons.YesNo) == DialogResult.Yes) ExplorerEffect(-1); } }, true);
        }

        private void ExplorerEffect(int effect)
        {
            try
            {
                string ps = Path.Combine(Path.GetTempPath(), "liquidmac-ebm.ps1");
                string body = effect < 0
                    ? "Start-Process regsvr32.exe -ArgumentList '/s','/u','\"" + EbmDir + "\\ExplorerBlurMica.dll\"' -Wait"
                    : "$f='" + EbmDir + "\\config.ini'; $c=Get-Content $f; $c=$c -replace '^effect=.*','effect=" + effect + "'; Set-Content $f $c -Encoding ASCII; Start-Process regsvr32.exe -ArgumentList '/s','\"" + EbmDir + "\\ExplorerBlurMica.dll\"' -Wait";
                File.WriteAllText(ps, body);
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\"");
                psi.Verb = "runas"; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process pr = Process.Start(psi);
                pr.WaitForExit(30000);
                foreach (Process e in Process.GetProcessesByName("explorer")) { try { e.Kill(); } catch { } }
                System.Threading.Thread.Sleep(1500);
                if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
                Build();
            }
            catch (Exception ex) { Log.Write("ebm: " + ex.Message); }
        }

        private void WindhawkCard()
        {
            string[][] mods = {
                new string[] { "windows-11-taskbar-styler", "Taskbar" },
                new string[] { "windows-11-start-menu-styler", "Start menu" },
                new string[] { "windows-11-notification-center-styler", "Notification center" },
                new string[] { "windows-11-file-explorer-styler", "File Explorer" },
                new string[] { "taskbar-dock-animation", "Animasi dock taskbar" } };
            StringBuilder sb = new StringBuilder();
            bool any = false;
            foreach (string[] m in mods)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Windhawk\Engine\Mods\" + m[0]))
                    {
                        if (k == null) continue;
                        any = true;
                        object dis = k.GetValue("Disabled");
                        bool off = dis != null && Convert.ToInt32(dis) != 0;
                        string theme = "";
                        using (RegistryKey s = k.OpenSubKey("Settings")) if (s != null) theme = s.GetValue("theme") as string ?? "";
                        sb.Append(off ? "\u25CB  " : "\u25CF  ").Append(m[1]);
                        if (theme.Length > 0) sb.Append(" \u2014 ").Append(theme);
                        sb.Append(off ? "  (mati)" : "").Append("\n");
                    }
                }
                catch { }
            }
            string exe = @"C:\Program Files\Windhawk\windhawk.exe";
            string desc = any ? sb.ToString().TrimEnd() : "Windhawk tidak ditemukan. LiquidMac tetap berjalan tanpa Windhawk.";
            int lines = any ? desc.Split('\n').Length : 1;

            Panel p = CardPanel(Ui.P(76 + lines * 17 + 40));
            bool svcOff = false;
            try { using (RegistryKey sk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Windhawk")) svcOff = sk != null && Convert.ToInt32(sk.GetValue("Start", 2)) == 4; } catch { }
            Texts(p, svcOff ? "Windhawk (dimatikan, diganti LiquidMac)" : "Tema Liquid Glass di Windhawk", "Referensi gaya LiquidMac (taskbar, Start, notifikasi, Explorer):\n" + desc, 0);
            if (File.Exists(exe))
            {
                Button b = Btn("Buka Windhawk", false);
                b.Location = new Point(Ui.P(16), p.Height - Ui.P(42));
                b.Click += delegate { Shell.Run(exe, null); };
                p.Controls.Add(b);
                b.BringToFront();
                if (Settings.Get("ShowDock", true) && !svcOff)
                {
                    Label hint = new Label();
                    hint.Text = "Tip: taskbar DockLike + Dock LiquidMac = dua dock. Aktifkan \"Sembunyikan taskbar Windows otomatis\" agar rapi.";
                    hint.Font = _s; hint.ForeColor = Color.FromArgb(255, 204, 0); hint.BackColor = Card;
                    hint.AutoSize = false; hint.Height = Ui.P(34);
                    hint.Location = new Point(b.Right + Ui.P(12), p.Height - Ui.P(44));
                    p.Controls.Add(hint);
                    hint.BringToFront();
                    p.Resize += delegate { hint.Width = p.Width - hint.Left - Ui.P(14); };
                }
            }
        }
    }

    internal static class Program
    {
        public static Action SettingsRequested;
        public static Action LaunchpadAction, NotificationAction;
        public static Action<string, int> ControlAction;
        public static Action<int> ProfileAction;
        public static void ControlPanel(string mode, int anchorRight) { if (ControlAction != null) ControlAction(mode, anchorRight); }
        public static Func<bool> LaunchpadVisible;
        public static void Launchpad() { if (LaunchpadAction != null) LaunchpadAction(); }
        public static void NotificationCenter() { if (NotificationAction != null) NotificationAction(); }
        public static bool LaunchpadOpen { get { return LaunchpadVisible != null && LaunchpadVisible(); } }
        public static string SnapshotDir;

        public static void RequestSettings() { if (SettingsRequested != null) SettingsRequested(); }

        public const string Version = "1.6.7";
        public const string DonateUrl = "https://saweria.co/aashaf";
        public const string RepoUrl = "https://github.com/amarashaff/LiquidMac";

        [STAThread]
        private static void Main(string[] args)
        {
            bool wantSettings = Array.Exists(args, delegate (string a) { return a.Equals("--settings", StringComparison.OrdinalIgnoreCase) || a.Equals("/settings", StringComparison.OrdinalIgnoreCase); });
            bool quit = Array.Exists(args, delegate (string a) { return a.Equals("--exit", StringComparison.OrdinalIgnoreCase); });
            if (Array.IndexOf(args, "--build-tiles") >= 0)
            {
                try { Ui.Init(); Settings.Load(); Tiles.Enabled = Settings.Get("IconTiles", true); Apps.BuildCache(); }
                catch (Exception ex) { Log.Write("build-tiles: " + ex.Message); }
                return;
            }
            int si = Array.FindIndex(args, delegate (string a) { return a == "--snapshot"; });
            if (si >= 0 && si + 1 < args.Length) SnapshotDir = args[si + 1];
            bool created;
            using (Mutex mutex = new Mutex(true, SnapshotDir != null ? "LiquidMacSnapshot" : "LiquidMacSingleInstance", out created))
            {
                if (!created)
                {
                    // sudah berjalan: buka jendela Pengaturan milik instance yang aktif
                    int cmd = 0;
                    foreach (string a in args)
                    {
                        if (a == "--launchpad") cmd = 1;
                        else if (a == "--notifications") cmd = 2;
                        else if (a == "--test-banner") cmd = 3;
                        else if (a == "--wifi") cmd = 4;
                        else if (a == "--sound") cmd = 5;
                        else if (a == "--battery") cmd = 6;
                        else if (a == "--control") cmd = 7;
                        else if (a == "--explorer-icons") cmd = 8;
                        else if (a == "--exit") cmd = 9;
                        else if (a == "--uninstall") cmd = 10;
                        else if (a == "--donate") cmd = 11;
                    }
                    Native.PostMessage((IntPtr)0xFFFF, (int)MessageWindow.Msg, (IntPtr)cmd, IntPtr.Zero);
                    return;
                }
                if (quit || Array.IndexOf(args, "--uninstall") >= 0) return;   // tidak sedang berjalan: tidak ada yang perlu dilakukan
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("ui: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("fatal: " + e.ExceptionObject); };
                Settings.Load();
                try { MacFont.Install(); } catch (Exception ex) { Log.Write("font: " + ex.Message); }
                Ui.Init();
                Glyphs.Init();
                try { Application.Run(new TrayApp(wantSettings)); }
                catch (Exception ex) { Log.Write("run: " + ex); }
                GC.KeepAlive(mutex);
            }
        }
    }
}

