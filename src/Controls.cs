// LiquidMac - panel Wi-Fi, Suara, Baterai, dan Control Center buatan sendiri (tanpa membuka panel Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LiquidMac
{
    // ============================================================
    //  Wi-Fi (netsh + wlanapi untuk memicu pemindaian)
    // ============================================================
    internal sealed class WifiNet
    {
        public string Ssid, Auth;
        public int Signal;
        public bool Known, Connected;
        public bool Secured { get { return Auth != null && Auth.IndexOf("Open", StringComparison.OrdinalIgnoreCase) < 0; } }
    }

    internal static class Wifi
    {
        public static string Iface = "Wi-Fi";
        public static string Ssid;
        public static int Signal = -1;
        public static bool Connected, HasAdapter = true;

        [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint ver, IntPtr res, out uint neg, out IntPtr h);
        [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr h, IntPtr res);
        [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr h, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern int WlanScan(IntPtr h, ref Guid g, IntPtr ssid, IntPtr ie, IntPtr res);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr p);

        public static string Netsh(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh.exe", args);
                psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(CultureInfoOem());
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return o;
                }
            }
            catch (Exception ex) { Log.Write("netsh " + args + ": " + ex.Message); return ""; }
        }

        private static int CultureInfoOem() { try { return System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage; } catch { return 437; } }

        private static bool KV(string line, out string key, out string val)
        {
            key = val = null;
            int i = line.IndexOf(':');
            if (i <= 0) return false;
            key = line.Substring(0, i).Trim();
            val = line.Substring(i + 1).Trim();
            return true;
        }

        public static void RefreshStatus()
        {
            string o = Netsh("wlan show interfaces");
            if (o.IndexOf("There is no wireless interface", StringComparison.OrdinalIgnoreCase) >= 0 || o.Trim().Length == 0) { HasAdapter = false; Connected = false; return; }
            HasAdapter = true;
            bool conn = false; string ssid = null; int sig = -1;
            foreach (string line in o.Split('\n'))
            {
                string k, v;
                if (!KV(line, out k, out v)) continue;
                if (k == "Name") Iface = v;
                else if (k == "State") conn = v.StartsWith("connected", StringComparison.OrdinalIgnoreCase);
                else if (k == "SSID") ssid = v;
                else if (k == "Signal") { int s; if (int.TryParse(v.TrimEnd('%', ' '), out s)) sig = s; }
            }
            Connected = conn; Ssid = conn ? ssid : null; Signal = conn ? sig : -1;
        }

        public static void TriggerScan()
        {
            IntPtr h = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                uint neg;
                if (WlanOpenHandle(2, IntPtr.Zero, out neg, out h) != 0) return;
                if (WlanEnumInterfaces(h, IntPtr.Zero, out list) != 0) return;
                int n = Marshal.ReadInt32(list);
                for (int i = 0; i < n; i++)
                {
                    IntPtr item = new IntPtr(list.ToInt64() + 8 + (long)i * 532);
                    Guid g = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    WlanScan(h, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex) { Log.Write("wlanscan: " + ex.Message); }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (h != IntPtr.Zero) WlanCloseHandle(h, IntPtr.Zero);
            }
        }

        public static List<WifiNet> Networks()
        {
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in Netsh("wlan show profiles").Split('\n'))
            {
                string k, v;
                if (KV(line, out k, out v) && k.IndexOf("Profile", StringComparison.OrdinalIgnoreCase) >= 0 && v.Length > 0) known.Add(v);
            }
            List<WifiNet> list = new List<WifiNet>();
            WifiNet cur = null;
            foreach (string line in Netsh("wlan show networks mode=bssid").Split('\n'))
            {
                string k, v;
                if (!KV(line, out k, out v)) continue;
                if (k.StartsWith("SSID ", StringComparison.Ordinal) && !k.StartsWith("BSSID", StringComparison.Ordinal))
                {
                    if (v.Length == 0) { cur = null; continue; }
                    cur = new WifiNet(); cur.Ssid = v; cur.Known = known.Contains(v); cur.Connected = Connected && v == Ssid;
                    list.Add(cur);
                }
                else if (cur != null && k == "Authentication") cur.Auth = v;
                else if (cur != null && k == "Signal") { int s; if (int.TryParse(v.TrimEnd('%', ' '), out s) && s > cur.Signal) cur.Signal = s; }
            }
            list.Sort(delegate (WifiNet a, WifiNet b)
            {
                if (a.Connected != b.Connected) return a.Connected ? -1 : 1;
                if (a.Known != b.Known) return a.Known ? -1 : 1;
                return b.Signal.CompareTo(a.Signal);
            });
            return list;
        }

        private static string X(string s) { return SecurityElement.Escape(s); }

        // Mengembalikan pesan hasil (null = berhasil dikirim).
        public static string Connect(WifiNet n, string password)
        {
            if (!n.Known)
            {
                string auth = n.Auth ?? "";
                string a, enc;
                if (!n.Secured) { a = "open"; enc = "none"; }
                else if (auth.IndexOf("WPA3", StringComparison.OrdinalIgnoreCase) >= 0) { a = "WPA3SAE"; enc = "AES"; }
                else if (auth.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0) return L.T("Jaringan Enterprise: hubungkan lewat Pengaturan Wi-Fi.");
                else if (auth.IndexOf("WPA2", StringComparison.OrdinalIgnoreCase) >= 0) { a = "WPA2PSK"; enc = "AES"; }
                else { a = "WPAPSK"; enc = "AES"; }
                StringBuilder xml = new StringBuilder();
                xml.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
                xml.Append("<name>").Append(X(n.Ssid)).Append("</name><SSIDConfig><SSID><name>").Append(X(n.Ssid)).Append("</name></SSID></SSIDConfig>");
                xml.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption>");
                xml.Append("<authentication>").Append(a).Append("</authentication><encryption>").Append(enc).Append("</encryption><useOneX>false</useOneX></authEncryption>");
                if (n.Secured) xml.Append("<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>").Append(X(password ?? "")).Append("</keyMaterial></sharedKey>");
                xml.Append("</security></MSM></WLANProfile>");
                string tmp = Path.Combine(Path.GetTempPath(), "liquidmac-wifi-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(tmp, xml.ToString(), Encoding.UTF8);
                    string r = Netsh("wlan add profile filename=\"" + tmp + "\" user=current");
                    if (r.IndexOf("is added", StringComparison.OrdinalIgnoreCase) < 0 && r.IndexOf("ditambahkan", StringComparison.OrdinalIgnoreCase) < 0)
                        return L.T("Gagal menyimpan profil: ") + r.Trim();
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
            string res = Netsh("wlan connect name=\"" + n.Ssid + "\" ssid=\"" + n.Ssid + "\" interface=\"" + Iface + "\"");
            if (res.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0 || res.IndexOf("berhasil", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            return res.Trim().Length > 0 ? res.Trim() : L.T("Tidak bisa terhubung.");
        }

        public static void Disconnect() { Netsh("wlan disconnect interface=\"" + Iface + "\""); }
    }

    // ============================================================
    //  PERANGKAT AUDIO (daftar output + ganti default)
    // ============================================================
    internal sealed class AudioDevice { public string Id, Name; public bool Default; }

    internal static class AudioDevices
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnum
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out ICollection c);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice d);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICollection
        {
            [PreserveSig] int GetCount(out uint n);
            [PreserveSig] int Item(uint i, out IDevice d);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
            [PreserveSig] int OpenPropertyStore(int access, out IProps ps);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int s);
        }
        [StructLayout(LayoutKind.Sequential)] private struct PKEY { public Guid fmt; public int pid; }
        [StructLayout(LayoutKind.Explicit)] private struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(16)] public IntPtr pad; }
        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IProps
        {
            [PreserveSig] int GetCount(out uint c);
            [PreserveSig] int GetAt(uint i, out PKEY k);
            [PreserveSig] int GetValue(ref PKEY k, out PROPVARIANT v);
        }
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT v);

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] private class PolicyConfigClient { }
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat();
            [PreserveSig] int SetDeviceFormat(); [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod();
            [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode(); [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        }

        public static List<AudioDevice> Outputs()
        {
            List<AudioDevice> list = new List<AudioDevice>();
            try
            {
                IEnum en = (IEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                string defId = null;
                IDevice def;
                if (en.GetDefaultAudioEndpoint(0, 1, out def) == 0 && def != null) def.GetId(out defId);
                ICollection col;
                if (en.EnumAudioEndpoints(0, 1 /* ACTIVE */, out col) != 0) return list;
                uint n; col.GetCount(out n);
                PKEY name = new PKEY(); name.fmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); name.pid = 14;
                for (uint i = 0; i < n; i++)
                {
                    IDevice d; if (col.Item(i, out d) != 0) continue;
                    AudioDevice a = new AudioDevice();
                    d.GetId(out a.Id);
                    IProps ps;
                    if (d.OpenPropertyStore(0, out ps) == 0)
                    {
                        PROPVARIANT v;
                        if (ps.GetValue(ref name, out v) == 0)
                        {
                            if (v.vt == 31) a.Name = Marshal.PtrToStringUni(v.p);
                            PropVariantClear(ref v);
                        }
                    }
                    if (string.IsNullOrEmpty(a.Name)) a.Name = L.T("Perangkat audio");
                    a.Default = a.Id == defId;
                    list.Add(a);
                }
            }
            catch (Exception ex) { Log.Write("audio list: " + ex.Message); }
            return list;
        }

        public static void SetDefault(string id)
        {
            try
            {
                IPolicyConfig pc = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
            }
            catch (Exception ex) { Log.Write("audio default: " + ex.Message); }
        }
    }

    // ============================================================
    //  MODE DAYA Windows 11 (Hemat / Seimbang / Performa)
    // ============================================================
    internal static class PowerMode
    {
        [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")] private static extern int GetOverlay(out Guid g);
        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")] private static extern int SetOverlay(Guid g);

        public static readonly Guid[] Modes = {
            new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"),   // efisiensi terbaik
            Guid.Empty,                                          // seimbang
            new Guid("ded574b5-45a0-4f42-8737-46345c09c238") }; // performa terbaik
        public static readonly string[] Names = { "Hemat", "Seimbang", "Performa" };

        public static int Current
        {
            get
            {
                try { Guid g; if (GetOverlay(out g) == 0) for (int i = 0; i < Modes.Length; i++) if (Modes[i] == g) return i; return 1; }
                catch { return -1; }
            }
            set
            {
                try { if (value >= 0 && value < Modes.Length) SetOverlay(Modes[value]); }
                catch (Exception ex) { Log.Write("power mode: " + ex.Message); }
            }
        }
    }

    // ============================================================
    //  GLYPH IKON TAJAM (Segoe Fluent Icons / MDL2)
    // ============================================================
    internal static class Glyphs
    {
        public static string Family;
        public static void Init()
        {
            try
            {
                using (InstalledFontCollection fc = new InstalledFontCollection())
                    foreach (string p in new string[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                        foreach (FontFamily f in fc.Families)
                            if (f.Name == p) { Family = p; return; }
            }
            catch { }
        }
        public const string Wifi0 = "", Wifi1 = "", Wifi2 = "", Wifi3 = "", WifiOff = "";
        public const string Vol0 = "", Vol1 = "", Vol2 = "", Vol3 = "";
        public const string Search = "", Bluetooth = "", Moon = "", Cast = "", Lock = "", Check = "", Speaker = "", Sun = "", Bolt = "", Gear = "";

        public static string WifiFor(int signal, bool connected)
        {
            if (!connected) return WifiOff;
            if (signal >= 70 || signal < 0) return Wifi3;   // E701: tiga busur
            if (signal >= 40) return Wifi2;                  // E874: dua busur
            return Wifi1;                                    // E873: satu busur
        }

        public static string VolFor(float v, bool muted)
        {
            if (muted || v <= 0.001f) return Vol0;
            if (v < 0.34f) return Vol1;
            if (v < 0.67f) return Vol2;
            return Vol3;
        }

        public static Font Font(float px) { return new Font(Family ?? "Segoe UI Symbol", px, FontStyle.Regular, GraphicsUnit.Pixel); }

        // Gambar glyph tepat di tengah kotak, posisi dibulatkan ke piksel agar tajam.
        public static void Draw(Graphics g, string glyph, Font f, Brush b, RectangleF box)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                RectangleF r = new RectangleF((float)Math.Round(box.X), (float)Math.Round(box.Y), (float)Math.Round(box.Width), (float)Math.Round(box.Height));
                g.DrawString(glyph, f, b, r, sf);
            }
        }
    }

    // ============================================================
    //  PROMPT KATA SANDI Wi-Fi
    // ============================================================
    internal sealed class PasswordPrompt : Form
    {
        private readonly TextBox _box;
        public string Value { get { return _box.Text; } }

        public PasswordPrompt(string ssid)
        {
            Text = "Wi-Fi: " + ssid;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            BackColor = Color.FromArgb(30, 30, 38); ForeColor = Color.White;
            Font = Ui.F(10f, FontStyle.Regular);
            ClientSize = new Size(Ui.P(360), Ui.P(150));
            Label l = new Label(); l.Text = L.T("Masukkan kata sandi untuk \"") + ssid + "\""; l.AutoSize = false;
            l.SetBounds(Ui.P(16), Ui.P(14), Ui.P(330), Ui.P(40));
            _box = new TextBox(); _box.UseSystemPasswordChar = true; _box.SetBounds(Ui.P(16), Ui.P(58), Ui.P(328), Ui.P(26));
            _box.BackColor = Color.FromArgb(50, 50, 60); _box.ForeColor = Color.White; _box.BorderStyle = BorderStyle.FixedSingle;
            CheckBox show = new CheckBox(); show.Text = L.T("Tampilkan"); show.SetBounds(Ui.P(16), Ui.P(92), Ui.P(120), Ui.P(24));
            show.CheckedChanged += delegate { _box.UseSystemPasswordChar = !show.Checked; };
            Button ok = new Button(); ok.Text = L.T("Gabung"); ok.DialogResult = DialogResult.OK; ok.SetBounds(Ui.P(186), Ui.P(108), Ui.P(76), Ui.P(30));
            Button cancel = new Button(); cancel.Text = L.T("Batal"); cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(Ui.P(268), Ui.P(108), Ui.P(76), Ui.P(30));
            foreach (Button b in new Button[] { ok, cancel }) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(55, 55, 66); b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 92); }
            ok.BackColor = Color.FromArgb(10, 110, 230);
            Controls.AddRange(new Control[] { l, _box, show, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ============================================================
    //  PANEL KONTROL (Wi-Fi / Suara / Baterai / Control Center)
    // ============================================================
    internal sealed class ControlPanelForm : GlassPopup
    {
        public string Mode = "control";
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private List<WifiNet> _nets;
        private bool _scanning;
        private string _status;
        private List<AudioDevice> _outs = new List<AudioDevice>();
        private float _vol = -1; private bool _muted; private int _bri = -1;
        private string _drag; private DateTime _lastBri = DateTime.MinValue;
        private RectangleF _volBar, _briBar;
        private bool _modal;
        private int _hoverRow = -1;
        private readonly List<RectangleF> _rows = new List<RectangleF>();
        private Rectangle _maxBounds;
        private readonly Font _h = Ui.F(10.5f, FontStyle.Bold), _t = Ui.F(10f, FontStyle.Regular), _s = Ui.F(9f, FontStyle.Regular), _sb = Ui.F(9f, FontStyle.Bold), _big = Ui.F(22f, FontStyle.Bold);
        private readonly Font _gl = Glyphs.Font(Ui.Pf(16)), _glBig = Glyphs.Font(Ui.Pf(18)), _glS = Glyphs.Font(Ui.Pf(12));

        public ControlPanelForm()
        {
            Text = "Control Center";
            Deactivate += delegate { if (!_modal && _drag == null) Hide(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
        }

        public void Toggle(string mode, int anchorRight, int top)
        {
            if (Visible && Mode == mode) { Hide(); return; }
            Mode = mode;
            _status = null;
            LoadData();
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            int w = Ui.P(330);
            int x = Math.Min(scr.Right - w - Ui.P(8), Math.Max(scr.Left + Ui.P(8), anchorRight - w));
            _maxBounds = new Rectangle(x, top + Ui.P(6), w, Math.Min(Ui.P(640), scr.Bottom - top - Ui.P(20)));
            ShowAt(_maxBounds, 0.42f);
            Round(true);
        }

        protected override void OnBeforeShow() { FitHeight(); }

        private void LoadData()
        {
            _vol = SystemControls.Volume; _muted = SystemControls.Muted; _bri = SystemControls.Brightness;
            if (Mode == "sound") _outs = AudioDevices.Outputs();
            if (Mode == "wifi" || Mode == "control") { Wifi.RefreshStatus(); }
            if (Mode == "wifi") StartScan();
        }

        private void StartScan()
        {
            if (_scanning) return;
            _scanning = true;
            if (_nets == null) _nets = new List<WifiNet>();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<WifiNet> nets = Wifi.Networks();
                Wifi.TriggerScan();
                Thread.Sleep(3500);
                Wifi.RefreshStatus();
                List<WifiNet> nets2 = Wifi.Networks();
                if (nets2.Count > 0) nets = nets2;
                try { BeginInvoke((Action)delegate { _nets = nets; _scanning = false; FitHeight(); Invalidate(); }); } catch { }
            });
        }

        private void SwitchMode(string m) { Mode = m; _status = null; LoadData(); FitHeight(); Invalidate(); }

        private void FitHeight()
        {
            int h;
            using (Graphics g = CreateGraphics()) h = (int)Math.Ceiling(PaintAll(g, true));
            h = Math.Min(h, _maxBounds.Height);
            if (Height != h) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e) { PaintAll(e.Graphics, false); }

        private static void Crisp(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        private static float R(float v) { return (float)Math.Round(v); }

        private void Txt(Graphics g, string s, Font f, Brush b, float x, float y) { g.DrawString(s, f, b, R(x), R(y)); }

        private void Card(Graphics g, RectangleF r, int alpha)
        {
            using (GraphicsPath p = Glass.Round(r, Ui.Pf(12)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(45, 255, 255, 255))) g.DrawPath(pen, p);
            }
        }

        private void Hit(RectangleF r, Action a) { _hits.Add(new KeyValuePair<RectangleF, Action>(r, a)); }

        private void Circle(Graphics g, RectangleF r, bool on, string glyph)
        {
            using (SolidBrush b = new SolidBrush(on ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(70, 255, 255, 255))) g.FillEllipse(b, r);
            using (SolidBrush w = new SolidBrush(Color.White)) Glyphs.Draw(g, glyph, _glS, w, r);
        }

        // Menggambar (atau hanya mengukur) isi panel; mengembalikan tinggi konten.
        private float PaintAll(Graphics g, bool measureOnly)
        {
            Crisp(g);
            if (!measureOnly) { _hits.Clear(); _rows.Clear(); }
            float W = Width, pad = Ui.Pf(14), x = pad, y = pad, cw = W - pad * 2;
            SolidBrush white = new SolidBrush(Color.FromArgb(250, 255, 255, 255));
            SolidBrush soft = new SolidBrush(Color.FromArgb(185, 235, 235, 245));
            SolidBrush blue = new SolidBrush(Color.FromArgb(255, 100, 175, 255));
            try
            {
                if (!measureOnly)
                    using (Pen border = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

                if (Mode == "wifi") y = PaintWifi(g, measureOnly, x, y, cw, white, soft, blue);
                else if (Mode == "sound") y = PaintSound(g, measureOnly, x, y, cw, white, soft, blue);
                else if (Mode == "battery") y = PaintBattery(g, measureOnly, x, y, cw, white, soft, blue);
                else y = PaintControl(g, measureOnly, x, y, cw, white, soft, blue);
                return y + pad;
            }
            finally { white.Dispose(); soft.Dispose(); blue.Dispose(); }
        }

        private float Footer(Graphics g, bool m, float x, float y, float cw, Brush blue, string[] labels, Action[] acts)
        {
            if (!m) using (Pen sep = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawLine(sep, R(x), R(y), R(x + cw), R(y));
            y += Ui.Pf(8);
            for (int i = 0; i < labels.Length; i++)
            {
                RectangleF r = new RectangleF(x, y, cw, Ui.Pf(26));
                if (!m)
                {
                    _rows.Add(r);
                    if (_hoverRow == _rows.Count - 1) Card(g, r, 35);
                    Txt(g, labels[i], _t, Brushes.White, x + Ui.Pf(8), y + Ui.Pf(4));
                    Hit(r, acts[i]);
                }
                y += Ui.Pf(28);
            }
            return y;
        }

        // ---------------- Wi-Fi ----------------
        private float PaintWifi(Graphics g, bool m, float x, float y, float cw, Brush white, Brush soft, Brush blue)
        {
            if (!m)
            {
                Txt(g, "Wi-Fi", _h, white, x, y);
                string st = !Wifi.HasAdapter ? L.T("Tidak ada adaptor") : (Wifi.Connected ? L.T("Terhubung") : L.T("Terputus"));
                SizeF ss = g.MeasureString(st, _s);
                Txt(g, st, _s, soft, x + cw - ss.Width, y + Ui.Pf(2));
            }
            y += Ui.Pf(30);
            if (_status != null)
            {
                if (!m) g.DrawString(_status, _s, soft, new RectangleF(x, y, cw, Ui.Pf(34)));
                y += Ui.Pf(36);
            }
            List<WifiNet> nets = _nets ?? new List<WifiNet>();
            WifiNet cur = nets.Find(delegate (WifiNet n) { return n.Connected; });
            if (Wifi.Connected)
            {
                RectangleF r = new RectangleF(x, y, cw, Ui.Pf(44));
                if (!m)
                {
                    Card(g, r, 40);
                    Circle(g, new RectangleF(x + Ui.Pf(8), y + Ui.Pf(8), Ui.Pf(28), Ui.Pf(28)), true, Glyphs.WifiFor(Wifi.Signal, true));
                    Txt(g, Wifi.Ssid ?? "", _sb, white, x + Ui.Pf(44), y + Ui.Pf(6));
                    Txt(g, L.T("Terhubung • sinyal ") + Wifi.Signal + "%", _s, soft, x + Ui.Pf(44), y + Ui.Pf(23));
                    string dc = L.T("Putuskan");
                    SizeF ds = g.MeasureString(dc, _s);
                    RectangleF dr = new RectangleF(x + cw - ds.Width - Ui.Pf(14), y + Ui.Pf(12), ds.Width + Ui.Pf(6), Ui.Pf(20));
                    Txt(g, dc, _s, blue, dr.X, dr.Y + Ui.Pf(1));
                    Hit(dr, delegate { _status = L.T("Memutuskan..."); Invalidate(); ThreadPool.QueueUserWorkItem(delegate { Wifi.Disconnect(); Thread.Sleep(800); Wifi.RefreshStatus(); BeginInvoke((Action)delegate { _status = null; StartScan(); FitHeight(); Invalidate(); }); }); });
                }
                y += Ui.Pf(52);
            }
            if (!m) Txt(g, _scanning && nets.Count == 0 ? L.T("Memindai jaringan...") : L.T("Jaringan lain"), _sb, soft, x + Ui.Pf(2), y);
            y += Ui.Pf(22);
            int shown = 0;
            foreach (WifiNet n in nets)
            {
                if (n.Connected) continue;
                if (++shown > 9) break;
                RectangleF r = new RectangleF(x, y, cw, Ui.Pf(32));
                if (!m)
                {
                    _rows.Add(r);
                    if (_hoverRow == _rows.Count - 1) Card(g, r, 35);
                    Circle(g, new RectangleF(x + Ui.Pf(6), y + Ui.Pf(4), Ui.Pf(24), Ui.Pf(24)), false, Glyphs.WifiFor(n.Signal, true));
                    string name = n.Ssid.Length > 30 ? n.Ssid.Substring(0, 28) + "..." : n.Ssid;
                    Txt(g, name, _t, white, x + Ui.Pf(38), y + Ui.Pf(6));
                    float rx = x + cw - Ui.Pf(8);
                    if (n.Secured) { Glyphs.Draw(g, Glyphs.Lock, _glS, soft, new RectangleF(rx - Ui.Pf(16), y + Ui.Pf(8), Ui.Pf(16), Ui.Pf(16))); rx -= Ui.Pf(22); }
                    if (n.Known) { SizeF ks = g.MeasureString("tersimpan", _s); Txt(g, "tersimpan", _s, soft, rx - ks.Width, y + Ui.Pf(8)); }
                    WifiNet nn = n;
                    Hit(r, delegate { Join(nn); });
                }
                y += Ui.Pf(34);
            }
            if (nets.Count == 0 && !_scanning && !m) Txt(g, L.T("Tidak ada jaringan ditemukan."), _s, soft, x + Ui.Pf(2), y);
            if (nets.Count == 0) y += Ui.Pf(24);
            y += Ui.Pf(6);
            return Footer(g, m, x, y, cw, blue, new string[] { L.T("Pindai ulang"), L.T("Pengaturan Wi-Fi...") },
                new Action[] { delegate { StartScan(); Invalidate(); }, delegate { Hide(); Shell.Run("ms-settings:network-wifi", null); } });
        }

        private void Join(WifiNet n)
        {
            string pass = null;
            if (!n.Known && n.Secured && (n.Auth ?? "").IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) < 0)
            {
                _modal = true;
                using (PasswordPrompt p = new PasswordPrompt(n.Ssid))
                {
                    DialogResult r = p.ShowDialog(this);
                    pass = p.Value;
                    _modal = false;
                    if (r != DialogResult.OK) { Activate(); return; }
                }
                Activate();
            }
            _status = L.T("Menghubungkan ke ") + n.Ssid + "...";
            Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = Wifi.Connect(n, pass);
                if (err == null) { for (int i = 0; i < 10; i++) { Thread.Sleep(1000); Wifi.RefreshStatus(); if (Wifi.Connected && Wifi.Ssid == n.Ssid) break; } }
                string msg = err ?? (Wifi.Connected && Wifi.Ssid == n.Ssid ? null : L.T("Belum terhubung ke ") + n.Ssid + L.T(". Periksa kata sandi."));
                try { BeginInvoke((Action)delegate { _status = msg; StartScan(); FitHeight(); Invalidate(); }); } catch { }
            });
        }

        // ---------------- Suara ----------------
        private float PaintSound(Graphics g, bool m, float x, float y, float cw, Brush white, Brush soft, Brush blue)
        {
            if (!m) Txt(g, L.T("Suara"), _h, white, x, y);
            y += Ui.Pf(30);
            RectangleF mb = new RectangleF(x, y, Ui.Pf(30), Ui.Pf(30));
            if (!m)
            {
                Circle(g, mb, !_muted, Glyphs.VolFor(_vol, _muted));
                Hit(mb, delegate { _muted = !_muted; SystemControls.Muted = _muted; Invalidate(); });
                _volBar = Slider(g, x + Ui.Pf(40), y + Ui.Pf(8), cw - Ui.Pf(40), _muted ? 0 : _vol);
            }
            y += Ui.Pf(44);
            if (!m) Txt(g, "Output", _sb, soft, x + Ui.Pf(2), y);
            y += Ui.Pf(22);
            foreach (AudioDevice d in _outs)
            {
                RectangleF r = new RectangleF(x, y, cw, Ui.Pf(32));
                if (!m)
                {
                    _rows.Add(r);
                    if (_hoverRow == _rows.Count - 1) Card(g, r, 35);
                    Circle(g, new RectangleF(x + Ui.Pf(6), y + Ui.Pf(4), Ui.Pf(24), Ui.Pf(24)), d.Default, Glyphs.Speaker);
                    string nm = d.Name.Length > 34 ? d.Name.Substring(0, 32) + "..." : d.Name;
                    Txt(g, nm, _t, white, x + Ui.Pf(38), y + Ui.Pf(6));
                    if (d.Default) Glyphs.Draw(g, Glyphs.Check, _glS, white, new RectangleF(x + cw - Ui.Pf(24), y + Ui.Pf(8), Ui.Pf(16), Ui.Pf(16)));
                    string id = d.Id;
                    Hit(r, delegate { AudioDevices.SetDefault(id); _outs = AudioDevices.Outputs(); _vol = SystemControls.Volume; _muted = SystemControls.Muted; Invalidate(); });
                }
                y += Ui.Pf(34);
            }
            if (_outs.Count == 0) { if (!m) Txt(g, L.T("Tidak ada perangkat output."), _s, soft, x + Ui.Pf(2), y); y += Ui.Pf(24); }
            y += Ui.Pf(6);
            return Footer(g, m, x, y, cw, blue, new string[] { L.T("Mixer volume aplikasi..."), L.T("Pengaturan Suara...") },
                new Action[] { delegate { Hide(); Shell.Run("ms-settings:apps-volume", null); }, delegate { Hide(); Shell.Run("ms-settings:sound", null); } });
        }

        // ---------------- Baterai ----------------
        private float PaintBattery(Graphics g, bool m, float x, float y, float cw, Brush white, Brush soft, Brush blue)
        {
            PowerStatus ps = SystemInformation.PowerStatus;
            bool none = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
            bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
            float pct = Math.Max(0f, Math.Min(1f, ps.BatteryLifePercent));
            if (!m)
            {
                Txt(g, L.T("Baterai"), _h, white, x, y);
                string src = charging ? L.T("Sumber daya: Adaptor") : L.T("Sumber daya: Baterai");
                SizeF ss = g.MeasureString(src, _s);
                Txt(g, src, _s, soft, x + cw - ss.Width, y + Ui.Pf(2));
            }
            y += Ui.Pf(30);
            if (!none)
            {
                if (!m)
                {
                    Txt(g, ((int)Math.Round(pct * 100)) + "%", _big, white, x, y);
                    string st;
                    if (charging) st = pct >= 0.995f ? L.T("Terisi penuh") : L.T("Sedang mengisi");
                    else
                    {
                        int sec = ps.BatteryLifeRemaining;
                        st = sec > 0 ? string.Format(L.T("Sisa sekitar {0} j {1} mnt"), sec / 3600, (sec % 3600) / 60) : L.T("Menggunakan baterai");
                    }
                    Txt(g, st, _t, soft, x + Ui.Pf(86), y + Ui.Pf(12));
                    RectangleF bar = new RectangleF(x, y + Ui.Pf(44), cw, Ui.Pf(10));
                    using (GraphicsPath bp = Glass.Round(bar, bar.Height / 2)) using (SolidBrush tb = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillPath(tb, bp);
                    Color fc = pct <= 0.2f && !charging ? Color.FromArgb(255, 69, 58) : Color.FromArgb(48, 209, 88);
                    using (GraphicsPath fp = Glass.Round(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * pct), bar.Height), bar.Height / 2)) using (SolidBrush fb = new SolidBrush(fc)) g.FillPath(fb, fp);
                }
                y += Ui.Pf(66);
            }
            else { if (!m) Txt(g, L.T("Tidak ada baterai (daya listrik)."), _t, soft, x, y); y += Ui.Pf(28); }

            int mode = PowerMode.Current;
            if (mode >= 0)
            {
                if (!m) Txt(g, L.T("Mode daya"), _sb, soft, x + Ui.Pf(2), y);
                y += Ui.Pf(22);
                if (!m)
                {
                    RectangleF seg = new RectangleF(x, y, cw, Ui.Pf(32));
                    Card(g, seg, 30);
                    float sw = cw / 3f;
                    for (int i = 0; i < 3; i++)
                    {
                        RectangleF r = new RectangleF(x + i * sw + Ui.Pf(3), y + Ui.Pf(3), sw - Ui.Pf(6), Ui.Pf(26));
                        if (i == mode) using (GraphicsPath p = Glass.Round(r, Ui.Pf(8))) using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 10, 132, 255))) g.FillPath(b, p);
                        SizeF ts = g.MeasureString(PowerMode.Names[i], _s);
                        Txt(g, PowerMode.Names[i], _s, white, r.X + (r.Width - ts.Width) / 2, r.Y + (r.Height - ts.Height) / 2);
                        int mi = i;
                        Hit(r, delegate { PowerMode.Current = mi; Invalidate(); });
                    }
                }
                y += Ui.Pf(42);
            }
            return Footer(g, m, x, y, cw, blue, new string[] { L.T("Penghemat baterai..."), L.T("Opsi daya...") },
                new Action[] { delegate { Hide(); Shell.Run("ms-settings:batterysaver", null); }, delegate { Hide(); Shell.Run("control.exe", "powercfg.cpl"); } });
        }

        // ---------------- Control Center ----------------
        private float PaintControl(Graphics g, bool m, float x, float y, float cw, Brush white, Brush soft, Brush blue)
        {
            float tw = (cw - Ui.Pf(10)) / 2f, th = Ui.Pf(56);
            string[] titles = { "Wi-Fi", "Bluetooth", L.T("Jangan Ganggu"), L.T("Proyeksikan") };
            string[] subs = { Wifi.Connected ? (Wifi.Ssid ?? L.T("Terhubung")) : L.T("Terputus"), L.T("Pengaturan"), Settings.Get("DND", false) ? L.T("Nyala") : L.T("Mati"), L.T("Layar nirkabel") };
            string[] gl = { Glyphs.WifiFor(Wifi.Signal, Wifi.Connected), Glyphs.Bluetooth, Glyphs.Moon, Glyphs.Cast };
            bool[] on = { Wifi.Connected, false, Settings.Get("DND", false), false };
            Action[] acts = {
                delegate { SwitchMode("wifi"); },
                delegate { Hide(); Shell.Run("ms-settings:bluetooth", null); },
                delegate { Settings.Set("DND", !Settings.Get("DND", false)); Invalidate(); },
                delegate { Hide(); Native.Keys(Native.VK_LWIN, (byte)'K'); } };
            for (int i = 0; i < 4; i++)
            {
                RectangleF r = new RectangleF(x + (i % 2) * (tw + Ui.Pf(10)), y + (i / 2) * (th + Ui.Pf(10)), tw, th);
                if (!m)
                {
                    Card(g, r, 40);
                    Circle(g, new RectangleF(r.X + Ui.Pf(10), r.Y + Ui.Pf(13), Ui.Pf(30), Ui.Pf(30)), on[i], gl[i]);
                    Txt(g, titles[i], _sb, white, r.X + Ui.Pf(48), r.Y + Ui.Pf(11));
                    string sub = subs[i].Length > 16 ? subs[i].Substring(0, 14) + "..." : subs[i];
                    Txt(g, sub, _s, soft, r.X + Ui.Pf(48), r.Y + Ui.Pf(29));
                    Hit(r, acts[i]);
                }
            }
            y += th * 2 + Ui.Pf(20);

            if (_bri >= 0)
            {
                RectangleF c = new RectangleF(x, y, cw, Ui.Pf(64));
                if (!m)
                {
                    Card(g, c, 40);
                    Txt(g, L.T("Layar"), _sb, white, x + Ui.Pf(12), y + Ui.Pf(8));
                    Glyphs.Draw(g, Glyphs.Sun, _glS, soft, new RectangleF(x + Ui.Pf(10), y + Ui.Pf(32), Ui.Pf(18), Ui.Pf(18)));
                    _briBar = Slider(g, x + Ui.Pf(34), y + Ui.Pf(34), cw - Ui.Pf(46), _bri / 100f);
                }
                y += Ui.Pf(74);
            }
            else _briBar = RectangleF.Empty;

            RectangleF sc = new RectangleF(x, y, cw, Ui.Pf(64));
            if (!m)
            {
                Card(g, sc, 40);
                Txt(g, L.T("Suara"), _sb, white, x + Ui.Pf(12), y + Ui.Pf(8));
                RectangleF mb = new RectangleF(x + Ui.Pf(10), y + Ui.Pf(32), Ui.Pf(18), Ui.Pf(18));
                Glyphs.Draw(g, Glyphs.VolFor(_vol, _muted), _glS, soft, mb);
                Hit(mb, delegate { _muted = !_muted; SystemControls.Muted = _muted; Invalidate(); });
                _volBar = Slider(g, x + Ui.Pf(34), y + Ui.Pf(34), cw - Ui.Pf(46), _muted ? 0 : _vol);
                RectangleF more = new RectangleF(x + cw - Ui.Pf(70), y + Ui.Pf(6), Ui.Pf(62), Ui.Pf(20));
                SizeF ms = g.MeasureString("Output ›", _s);
                Txt(g, "Output ›", _s, blue, x + cw - Ui.Pf(10) - ms.Width, y + Ui.Pf(9));
                Hit(more, delegate { SwitchMode("sound"); });
            }
            y += Ui.Pf(74);

            RectangleF mc = new RectangleF(x, y, cw, Ui.Pf(52));
            if (!m)
            {
                Card(g, mc, 40);
                Txt(g, "Media", _sb, white, x + Ui.Pf(12), y + Ui.Pf(16));
                string[] mg = { "", "", "" };
                byte[] keys = { Native.VK_MEDIA_PREV, Native.VK_MEDIA_PLAY, Native.VK_MEDIA_NEXT };
                for (int i = 0; i < 3; i++)
                {
                    RectangleF br = new RectangleF(x + cw - Ui.Pf(120) + i * Ui.Pf(38), y + Ui.Pf(11), Ui.Pf(30), Ui.Pf(30));
                    Glyphs.Draw(g, mg[i], _gl, white, br);
                    byte k = keys[i];
                    Hit(br, delegate { Native.Keys(k); });
                }
            }
            y += Ui.Pf(62);

            PowerStatus ps = SystemInformation.PowerStatus;
            if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0)
            {
                RectangleF bc = new RectangleF(x, y, cw, Ui.Pf(40));
                if (!m)
                {
                    Card(g, bc, 40);
                    string bt = L.T("Baterai ") + ((int)Math.Round(ps.BatteryLifePercent * 100)) + "%" + (ps.PowerLineStatus == PowerLineStatus.Online ? L.T(" • mengisi") : "");
                    Txt(g, bt, _sb, white, x + Ui.Pf(12), y + Ui.Pf(11));
                    SizeF ms = g.MeasureString("Detail ›", _s);
                    Txt(g, "Detail ›", _s, blue, x + cw - Ui.Pf(10) - ms.Width, y + Ui.Pf(12));
                    Hit(bc, delegate { SwitchMode("battery"); });
                }
                y += Ui.Pf(48);
            }
            return y;
        }

        private RectangleF Slider(Graphics g, float x, float y, float w, float v)
        {
            RectangleF bar = new RectangleF(R(x), R(y), R(w), Ui.Pf(14));
            using (GraphicsPath p = Glass.Round(bar, bar.Height / 2)) using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, p);
            if (v >= 0)
            {
                float fw = Math.Max(bar.Height, bar.Width * Math.Max(0f, Math.Min(1f, v)));
                using (GraphicsPath fp = Glass.Round(new RectangleF(bar.X, bar.Y, fw, bar.Height), bar.Height / 2)) using (SolidBrush fb = new SolidBrush(Color.White)) g.FillPath(fb, fp);
            }
            return new RectangleF(bar.X, bar.Y - Ui.Pf(8), bar.Width, bar.Height + Ui.Pf(16));
        }

        private void SetFromSlider(string which, int mx)
        {
            RectangleF bar = which == "vol" ? _volBar : _briBar;
            float v = Math.Max(0f, Math.Min(1f, (mx - bar.X) / bar.Width));
            if (which == "vol") { _vol = v; if (_muted && v > 0) _muted = false; SystemControls.Volume = v; }
            else { _bri = (int)Math.Round(v * 100); if ((DateTime.Now - _lastBri).TotalMilliseconds > 120) { SystemControls.Brightness = _bri; _lastBri = DateTime.Now; } }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_volBar.IsEmpty && _volBar.Contains(e.Location) && _vol >= 0) { _drag = "vol"; SetFromSlider(_drag, e.X); return; }
            if (!_briBar.IsEmpty && _briBar.Contains(e.Location)) { _drag = "bri"; SetFromSlider(_drag, e.X); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != null) { SetFromSlider(_drag, e.X); return; }
            int h = -1;
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Contains(e.Location)) { h = i; break; }
            if (h != _hoverRow) { _hoverRow = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag != null) { if (_drag == "bri") SystemControls.Brightness = _bri; else SoundKit.Feedback(); _drag = null; return; }
            if (e.Button != MouseButtons.Left) return;
            for (int i = _hits.Count - 1; i >= 0; i--)
                if (_hits[i].Key.Contains(e.Location)) { _hits[i].Value(); return; }
        }
    }
}
