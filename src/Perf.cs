using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiquidMac
{
    // ============================================================
    //  PENJAGA PERFORMA: saat CPU sedang berat atau RAM hampir penuh, LiquidMac mengurangi animasi hiasan
    //  (cahaya notch/Dock, lampu LED, gradasi, equalizer, animasi standby), menjaga prioritas prosesnya sedikit
    //  di atas normal agar notch/Dock tetap responsif, dan melepas memori yang tidak dipakai.
    //  Tidak ada pengaturan sistem yang diubah. Normal kembali otomatis setelah beban turun.
    // ============================================================
    internal static class PerfGuard
    {
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        public static bool Heavy { get; private set; }     // CPU berat atau RAM hampir penuh
        public static bool LowMem { get; private set; }    // RAM hampir penuh
        public static int Cpu { get; private set; }
        public static int Ram { get; private set; }

        private static System.Windows.Forms.Timer _t;
        private static long _pIdle, _pKern, _pUser;
        private static int _hot, _cool;
        private static DateTime _lastTrim = DateTime.MinValue;

        public static void Start()
        {
            if (_t != null) return;
            Sample();
            _t = new System.Windows.Forms.Timer(); _t.Interval = 1000;
            _t.Tick += delegate { try { Sample(); } catch (Exception ex) { Log.Write("perf guard: " + ex.Message); } };
            _t.Start();
        }

        private static void Sample()
        {
            long idle, kern, user;
            if (GetSystemTimes(out idle, out kern, out user))
            {
                long di = idle - _pIdle, dt = (kern - _pKern) + (user - _pUser);   // kernel sudah termasuk idle
                if (_pKern != 0 && dt > 0) Cpu = (int)Math.Max(0, Math.Min(100, 100 - di * 100 / dt));
                _pIdle = idle; _pKern = kern; _pUser = user;
            }
            Native.MEMORYSTATUSEX m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref m)) Ram = (int)m.dwMemoryLoad;

            // histeresis: masuk mode ringan setelah 3 dtk berat, keluar setelah 8 dtk tenang
            LowMem = Ram >= (LowMem ? 86 : 90);
            bool hot = Cpu >= 85 || LowMem;
            bool cool = Cpu < 65 && !LowMem;
            _hot = hot ? _hot + 1 : 0;
            _cool = cool ? _cool + 1 : 0;
            if (!Heavy && (_hot >= 3 || (LowMem && _hot >= 1))) Set(true);
            else if (Heavy && _cool >= 8) Set(false);

            // RAM hampir penuh: lepas memori LiquidMac yang tidak terpakai (paling sering tiap 60 dtk)
            if (LowMem && (DateTime.Now - _lastTrim).TotalSeconds >= 60) { _lastTrim = DateTime.Now; MemoryTrim.Run(); }
        }

        private static void Set(bool heavy)
        {
            Heavy = heavy;
            try { Process.GetCurrentProcess().PriorityClass = heavy ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal; } catch { }
            Log.Write("mode ringan " + (heavy ? "AKTIF" : "selesai") + " (CPU " + Cpu + "%, RAM " + Ram + "%)");
        }
    }
}
