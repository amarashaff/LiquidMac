using System;
using System.Collections.Generic;

namespace LiquidMac
{
    // Terjemahan tampilan: teks sumber berbahasa Indonesia; L.T(teks) mengembalikan padanan Inggris bila Lang=en.
    internal static partial class L
    {
        private static int _en = -1;
        public static bool En { get { if (_en < 0) _en = (Environment.GetEnvironmentVariable("LM_LANG") ?? Settings.GetS("Lang", "id")) == "en" ? 1 : 0; return _en == 1; } }
        public static void Reload() { _en = -1; }
        private static readonly System.Globalization.CultureInfo _cid = new System.Globalization.CultureInfo("id-ID"), _cen = new System.Globalization.CultureInfo("en-US");
        public static System.Globalization.CultureInfo Culture { get { return En ? _cen : _cid; } }

        public static string T(string s)
        {
            if (s == null || s.Length == 0 || !En) return s;
            string r;
            return Map.TryGetValue(s, out r) ? r : s;
        }
    }
}
