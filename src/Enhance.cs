// Bunyi hover kecil untuk Dock.
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LiquidMac
{
    // "Tik" sangat pelan & pendek saat kursor pindah ikon di Dock (dari memori, asinkron).
    internal static class HoverTick
    {
        [DllImport("winmm.dll")] private static extern bool PlaySound(byte[] data, IntPtr mod, uint flags);
        private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;
        private static byte[] _wav;
        private static DateTime _last;

        public static void Play()
        {
            if ((DateTime.Now - _last).TotalMilliseconds < 45) return;
            _last = DateTime.Now;
            try
            {
                if (_wav == null) _wav = Make();
                PlaySound(_wav, IntPtr.Zero, SND_ASYNC | SND_NODEFAULT | SND_MEMORY);
            }
            catch { }
        }

        private static byte[] Make()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.035);
            short[] smp = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double env = Math.Min(1, t / 0.0015) * Math.Exp(-t / 0.007);
                double v = Math.Sin(2 * Math.PI * 2100 * t) * 0.8 + Math.Sin(2 * Math.PI * 3300 * t) * 0.2;
                smp[i] = (short)(v * env * 0.10 * 32767);   // sangat pelan
            }
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int bytes = n * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (short v in smp) w.Write(v);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
