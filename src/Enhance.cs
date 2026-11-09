// Layar Jernih (filter warna layar penuh lewat Magnification API) + bunyi hover kecil untuk Dock.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LiquidMac
{
    // Saturasi & kontras sedikit dinaikkan untuk seluruh layar. Diproses DWM di GPU, hampir tanpa beban.
    // Ini filter warna (bukan penajaman piksel); efek hilang otomatis saat LiquidMac keluar.
    internal static class ScreenEnhance
    {
        [DllImport("Magnification.dll")] private static extern bool MagInitialize();
        [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] private static extern bool MagSetFullscreenColorEffect(float[] effect);

        private static bool _init;
        public static int Level { get; private set; }   // 0 mati, 1 ringan, 2 sedang, 3 kuat

        public static readonly string[] Names = { "Mati", "Ringan", "Sedang", "Kuat" };

        public static bool Apply(int level)
        {
            try
            {
                if (level <= 0) { Off(); return true; }
                if (!_init) { _init = MagInitialize(); if (!_init) { Log.Write("enhance: MagInitialize gagal"); return false; } }
                float s = level == 1 ? 1.10f : level == 2 ? 1.20f : 1.32f;   // saturasi
                float c = level == 1 ? 1.03f : level == 2 ? 1.06f : 1.09f;   // kontras
                bool ok = MagSetFullscreenColorEffect(Matrix(s, c));
                if (!ok) Log.Write("enhance: MagSetFullscreenColorEffect gagal");
                Level = ok ? level : 0;
                return ok;
            }
            catch (Exception ex) { Log.Write("enhance: " + ex.Message); return false; }
        }

        public static void Off()
        {
            if (!_init) { Level = 0; return; }
            try { MagSetFullscreenColorEffect(Matrix(1, 1)); MagUninitialize(); } catch { }
            _init = false; Level = 0;
        }

        // Matriks 5x5 (baris = sumber, kolom = tujuan), seperti ColorMatrix GDI+.
        private static float[] Matrix(float s, float c)
        {
            const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
            float[,] m = new float[5, 5];
            m[0, 0] = lr * (1 - s) + s; m[0, 1] = lr * (1 - s);     m[0, 2] = lr * (1 - s);
            m[1, 0] = lg * (1 - s);     m[1, 1] = lg * (1 - s) + s; m[1, 2] = lg * (1 - s);
            m[2, 0] = lb * (1 - s);     m[2, 1] = lb * (1 - s);     m[2, 2] = lb * (1 - s) + s;
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] *= c;
            float off = (1 - c) * 0.5f;
            m[4, 0] = off; m[4, 1] = off; m[4, 2] = off;
            m[3, 3] = 1; m[4, 4] = 1;
            float[] flat = new float[25];
            for (int i = 0; i < 5; i++) for (int j = 0; j < 5; j++) flat[i * 5 + j] = m[i, j];
            return flat;
        }
    }

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
