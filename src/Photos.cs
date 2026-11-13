extern alias WPF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;
using WC = WPF::System.Windows.Controls;
using WF = WPF::System.Windows;

namespace LiquidMac
{
    // ============================================================
    //  FOTO: penampil & penyunting foto + pemutar & pemangkas video gaya macOS Photos
    //  LiquidMac.exe --photos [berkas/folder...]   (proses sendiri)
    //  Uji tanpa UI : LM_PHOTOS_TEST=1 (LM_PHOTOS_OUT=folder)  -> sunting + simpan + pangkas pada SALINAN, tulis photos-test.log
    //  Uji tangkapan: LM_PHOTOS_SHOT=berkas.png [LM_PHOTOS_SHOT_MODE=edit|crop|trim|zoom|full] [LM_PHOTOS_SHOT_DELAY=ms]
    // ============================================================
    internal static class PhotosApp
    {
        public static readonly string[] ImageExts = {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".hif",
            ".avif", ".jxr", ".wdp", ".hdp", ".jxl", ".dds", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".sr2", ".dng", ".orf", ".rw2", ".raf", ".srw", ".pef", ".erf", ".kdc", ".3fr", ".mrw", ".x3f" };
        public static readonly string[] VideoExts = {
            ".mp4", ".m4v", ".mov", ".qt", ".mkv", ".webm", ".avi", ".wmv", ".asf", ".flv", ".3gp", ".3g2", ".mts", ".m2ts", ".ts", ".mpg", ".mpeg", ".vob", ".ogv",
            ".mp3", ".m4a", ".wav", ".flac", ".aac", ".wma", ".ogg", ".opus" };

        public static bool IsImage(string p) { return Array.IndexOf(ImageExts, Ext(p)) >= 0; }
        public static bool IsVideo(string p) { return Array.IndexOf(VideoExts, Ext(p)) >= 0; }
        public static bool IsMedia(string p) { return IsImage(p) || IsVideo(p); }
        private static string Ext(string p) { try { return Path.GetExtension(p).ToLowerInvariant(); } catch { return ""; } }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrCmpLogicalW(string a, string b);
        public static int Logical(string a, string b) { try { return StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)); } catch { return string.Compare(a, b, StringComparison.OrdinalIgnoreCase); } }

        public static List<string> MediaIn(string dir)
        {
            List<string> l = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    try { if (IsMedia(f) && (File.GetAttributes(f) & FileAttributes.Hidden) == 0) l.Add(f); } catch { }
                }
            }
            catch { }
            l.Sort(Logical);
            return l;
        }

        public static void Run(string[] files)
        {
            try { Glyphs.Init(); } catch { }
            if (Environment.GetEnvironmentVariable("LM_PHOTOS_TEST") == "1") { PhotosTest.Run(files); return; }
            try { Application.EnableVisualStyles(); } catch { }
            try { Application.SetCompatibleTextRenderingDefault(false); } catch { }
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("photos ui: " + e.Exception); };
            Application.Run(new PhotosForm(files));
        }

        public static string Unique(string dir, string baseName, string ext)
        {
            string p = Path.Combine(dir, baseName + ext);
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, baseName + " " + i + ext);
            return p;
        }

        public static string Time(double s, bool tenths)
        {
            if (double.IsNaN(s) || s < 0) s = 0;
            int t = (int)Math.Floor(s);
            string r = t >= 3600 ? (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00") : (t / 60) + ":" + (t % 60).ToString("00");
            if (tenths) r += "," + ((int)Math.Floor((s - t) * 10)).ToString();
            return r;
        }
    }

    // ============================================================
    //  BACA / TULIS GAMBAR (WIC lewat WPF, cadangan GDI+)
    // ============================================================
    internal static class ImageIO
    {
        public static Bitmap Load(string path, out Image anim)
        {
            anim = null;
            byte[] data = File.ReadAllBytes(path);
            if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Image im = Image.FromStream(new MemoryStream(data));
                    int n = 1; try { n = im.GetFrameCount(FrameDimension.Time); } catch { }
                    if (n > 1) { anim = im; return Copy32(im); }
                    im.Dispose();
                }
                catch { }
            }
            Bitmap b = null;
            try { b = DecodeWic(data); }
            catch (Exception ex) { Log.Write("photos wic " + Path.GetFileName(path) + ": " + ex.Message); }
            if (b == null) b = DecodeGdi(data);
            return b;
        }

        private static Bitmap DecodeWic(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                WMI.BitmapDecoder dec = WMI.BitmapDecoder.Create(ms, WMI.BitmapCreateOptions.IgnoreColorProfile | WMI.BitmapCreateOptions.PreservePixelFormat, WMI.BitmapCacheOption.OnLoad);
                if (dec.Frames.Count == 0) return null;
                WMI.BitmapFrame fr = dec.Frames[0];
                int orient = 1;
                try
                {
                    WMI.BitmapMetadata md = fr.Metadata as WMI.BitmapMetadata;
                    if (md != null) { object o = md.GetQuery("System.Photo.Orientation"); if (o != null) orient = Convert.ToInt32(o); }
                }
                catch { }
                Bitmap b = FromSource(fr);
                Orient(b, orient);
                return b;
            }
        }

        // gambar kecil cepat (JPEG didekode langsung pada skala kecil oleh WIC)
        public static Bitmap Thumb(string path, int px)
        {
            try
            {
                WMI.BitmapImage bi = new WMI.BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path); bi.DecodePixelWidth = px * 2;
                bi.CacheOption = WMI.BitmapCacheOption.OnLoad; bi.CreateOptions = WMI.BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                return FromSource(bi);
            }
            catch { return null; }
        }

        public static Bitmap FromSource(WM.Imaging.BitmapSource src)
        {
            WM.Imaging.BitmapSource s = src;
            if (s.Format != WM.PixelFormats.Bgra32) s = new WMI.FormatConvertedBitmap(s, WM.PixelFormats.Bgra32, null, 0);
            int w = s.PixelWidth, h = s.PixelHeight;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { s.CopyPixels(System.Windows.Int32Rect.Empty, bd.Scan0, bd.Stride * h, bd.Stride); }
            finally { b.UnlockBits(bd); }
            return b;
        }

        private static Bitmap DecodeGdi(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            using (Image im = Image.FromStream(ms))
            {
                int orient = 1;
                try { foreach (PropertyItem pi in im.PropertyItems) if (pi.Id == 0x0112 && pi.Value != null && pi.Value.Length >= 2) orient = BitConverter.ToUInt16(pi.Value, 0); } catch { }
                Bitmap b = Copy32(im);
                Orient(b, orient);
                return b;
            }
        }

        private static void Orient(Bitmap b, int o)
        {
            RotateFlipType t;
            switch (o)
            {
                case 2: t = RotateFlipType.RotateNoneFlipX; break;
                case 3: t = RotateFlipType.Rotate180FlipNone; break;
                case 4: t = RotateFlipType.Rotate180FlipX; break;
                case 5: t = RotateFlipType.Rotate90FlipX; break;
                case 6: t = RotateFlipType.Rotate90FlipNone; break;
                case 7: t = RotateFlipType.Rotate270FlipX; break;
                case 8: t = RotateFlipType.Rotate270FlipNone; break;
                default: return;
            }
            b.RotateFlip(t);
        }

        public static Bitmap Copy32(Image src)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) { g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.NearestNeighbor; g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height)); }
            return b;
        }

        public static Bitmap Scaled(Image src, int maxSide)
        {
            int w = src.Width, h = src.Height;
            double k = Math.Min(1.0, (double)maxSide / Math.Max(w, h));
            int nw = Math.Max(1, (int)Math.Round(w * k)), nh = Math.Max(1, (int)Math.Round(h * k));
            Bitmap b = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (ImageAttributes ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(src, new Rectangle(0, 0, nw, nh), 0, 0, w, h, GraphicsUnit.Pixel, ia); }
            }
            return b;
        }

        // Format keluaran: format yang bisa ditulis GDI+ tetap, selain itu (webp/heic/raw/...) disimpan sebagai salinan .jpg / .png
        public static string SaveExt(string src)
        {
            string e = Path.GetExtension(src).ToLowerInvariant();
            switch (e)
            {
                case ".jpg": case ".jpeg": case ".jpe": case ".jfif": case ".png": case ".bmp": case ".tif": case ".tiff": return e;
                case ".gif": case ".webp": case ".ico": case ".dds": return ".png";
                default: return ".jpg";
            }
        }
        public static bool CanOverwrite(string src) { return string.Equals(SaveExt(src), Path.GetExtension(src), StringComparison.OrdinalIgnoreCase); }

        public static void Save(Bitmap b, string path, string ext)
        {
            ext = ext.ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".jpe" || ext == ".jfif" || ext == ".bmp")
            {
                using (Bitmap flat = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(b, new Rectangle(0, 0, b.Width, b.Height)); }
                    if (ext == ".bmp") { flat.Save(path, ImageFormat.Bmp); return; }
                    ImageCodecInfo jpg = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.FormatID == ImageFormat.Jpeg.Guid) jpg = c;
                    using (EncoderParameters ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                        flat.Save(path, jpg, ep);
                    }
                }
                return;
            }
            if (ext == ".tif" || ext == ".tiff") { b.Save(path, ImageFormat.Tiff); return; }
            b.Save(path, ImageFormat.Png);
        }
    }

    // ============================================================
    //  KEADAAN SUNTINGAN (geometri + warna), dipakai untuk pratinjau & resolusi penuh
    // ============================================================
    internal sealed class PhotoEdit
    {
        public int Rot;                  // putaran 90 derajat searah jarum jam (0..3)
        public bool FlipH, FlipV;        // dibalik setelah diputar
        public float Straight;           // derajat, -45..45 (setelah balik)
        public RectangleF Crop = new RectangleF(0, 0, 1, 1);   // ternormalisasi pada hasil geometri
        public float Bright, Contrast, Sat, Warm;              // -1..1
        public string Aspect = "free";
        public float AspectR;                                  // rasio kunci (lebar/tinggi piksel), 0 = bebas

        public bool HasColor { get { return Bright != 0 || Contrast != 0 || Sat != 0 || Warm != 0; } }
        public bool HasGeo { get { return Rot != 0 || FlipH || FlipV || Math.Abs(Straight) > 0.01f; } }
        public bool HasCrop { get { return Crop.X > 0.0005f || Crop.Y > 0.0005f || Crop.Width < 0.9995f || Crop.Height < 0.9995f; } }
        public bool Changed { get { return HasColor || HasGeo || HasCrop; } }

        public PhotoEdit Clone() { return (PhotoEdit)MemberwiseClone(); }

        public void RotateCW()
        {
            Rot = (Rot + 1) & 3; bool t = FlipH; FlipH = FlipV; FlipV = t;
            Crop = new RectangleF(1 - Crop.Bottom, Crop.X, Crop.Height, Crop.Width);
            if (AspectR > 0) AspectR = 1f / AspectR;
        }
        public void RotateCCW()
        {
            Rot = (Rot + 3) & 3; bool t = FlipH; FlipH = FlipV; FlipV = t;
            Crop = new RectangleF(Crop.Y, 1 - Crop.Right, Crop.Height, Crop.Width);
            if (AspectR > 0) AspectR = 1f / AspectR;
        }
        public void MirrorH() { FlipH = !FlipH; Straight = -Straight; Crop = new RectangleF(1 - Crop.Right, Crop.Y, Crop.Width, Crop.Height); }
        public void MirrorV() { FlipV = !FlipV; Straight = -Straight; Crop = new RectangleF(Crop.X, 1 - Crop.Bottom, Crop.Width, Crop.Height); }

        public Bitmap Geometry(Bitmap src)
        {
            Bitmap a = ImageIO.Copy32(src);
            if (Rot == 1) a.RotateFlip(RotateFlipType.Rotate90FlipNone);
            else if (Rot == 2) a.RotateFlip(RotateFlipType.Rotate180FlipNone);
            else if (Rot == 3) a.RotateFlip(RotateFlipType.Rotate270FlipNone);
            if (FlipH) a.RotateFlip(RotateFlipType.RotateNoneFlipX);
            if (FlipV) a.RotateFlip(RotateFlipType.RotateNoneFlipY);
            if (Math.Abs(Straight) > 0.01f)
            {
                int w = a.Width, h = a.Height;
                float sc = CoverScale(w, h, Straight);
                Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TranslateTransform(w / 2f, h / 2f); g.RotateTransform(Straight); g.ScaleTransform(sc, sc);
                    g.DrawImage(a, new RectangleF(-w / 2f, -h / 2f, w, h));
                }
                a.Dispose(); a = b;
            }
            return a;
        }

        // skala agar gambar yang diluruskan tetap menutupi seluruh bingkai (tanpa sudut kosong)
        public static float CoverScale(int w, int h, float deg)
        {
            double th = Math.Abs(deg) * Math.PI / 180, c = Math.Cos(th), s = Math.Sin(th);
            return (float)(Math.Max(c + (double)h / w * s, (double)w / h * s + c) * 1.004);
        }

        public Rectangle CropPx(Size sz)
        {
            int x = (int)Math.Round(Crop.X * sz.Width), y = (int)Math.Round(Crop.Y * sz.Height);
            int r = (int)Math.Round(Crop.Right * sz.Width), b = (int)Math.Round(Crop.Bottom * sz.Height);
            x = Math.Max(0, Math.Min(sz.Width - 1, x)); y = Math.Max(0, Math.Min(sz.Height - 1, y));
            r = Math.Max(x + 1, Math.Min(sz.Width, r)); b = Math.Max(y + 1, Math.Min(sz.Height, b));
            return new Rectangle(x, y, r - x, b - y);
        }

        public Bitmap Render(Bitmap src)
        {
            using (Bitmap geo = Geometry(src)) return Apply(geo, HasColor ? Matrix() : null, CropPx(geo.Size));
        }

        public static Bitmap Apply(Bitmap src, ColorMatrix cm, Rectangle r)
        {
            Bitmap o = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o))
            using (ImageAttributes ia = new ImageAttributes())
            {
                g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                ia.SetWrapMode(WrapMode.TileFlipXY);
                if (cm != null) ia.SetColorMatrix(cm);
                g.DrawImage(src, new Rectangle(0, 0, r.Width, r.Height), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, ia);
            }
            return o;
        }

        public ColorMatrix Matrix()
        {
            float[][] m = Id();
            // kehangatan
            float w = Warm;
            m = Mul(m, Diag(1 + 0.14f * w, 1 + 0.03f * w, 1 - 0.16f * w, 0, 0, 0));
            // saturasi (bobot luminans Rec.709)
            float s = 1 + Sat;
            float lr = 0.2126f * (1 - s), lg = 0.7152f * (1 - s), lb = 0.0722f * (1 - s);
            float[][] sm = Id();
            sm[0][0] = lr + s; sm[0][1] = lr; sm[0][2] = lr;
            sm[1][0] = lg; sm[1][1] = lg + s; sm[1][2] = lg;
            sm[2][0] = lb; sm[2][1] = lb; sm[2][2] = lb + s;
            m = Mul(m, sm);
            // kontras di sekitar abu-abu tengah
            float c = 1 + Contrast * (Contrast > 0 ? 0.9f : 0.65f), t = 0.5f * (1 - c);
            m = Mul(m, Diag(c, c, c, t, t, t));
            // kecerahan: angkat + geser
            float b = Bright;
            m = Mul(m, Diag(1 + 0.3f * b, 1 + 0.3f * b, 1 + 0.3f * b, 0.1f * b, 0.1f * b, 0.1f * b));
            return new ColorMatrix(m);
        }

        private static float[][] Id() { float[][] m = new float[5][]; for (int i = 0; i < 5; i++) { m[i] = new float[5]; m[i][i] = 1; } return m; }
        private static float[][] Diag(float r, float g, float b, float or, float og, float ob)
        {
            float[][] m = Id(); m[0][0] = r; m[1][1] = g; m[2][2] = b; m[4][0] = or; m[4][1] = og; m[4][2] = ob; return m;
        }
        private static float[][] Mul(float[][] a, float[][] b)
        {
            float[][] r = new float[5][];
            for (int i = 0; i < 5; i++) { r[i] = new float[5]; for (int j = 0; j < 5; j++) { float v = 0; for (int k = 0; k < 5; k++) v += a[i][k] * b[k][j]; r[i][j] = v; } }
            return r;
        }

        public static float Ratio(string id, float orig)
        {
            switch (id)
            {
                case "orig": return orig;
                case "1:1": return 1f;
                case "4:3": return 4f / 3f;
                case "3:2": return 3f / 2f;
                case "16:9": return 16f / 9f;
                case "9:16": return 9f / 16f;
                case "4:5": return 4f / 5f;
            }
            return 0;
        }

        // kotak terbesar dengan rasio r di tengah gambar W x H (ternormalisasi)
        public void SetAspect(string id, int W, int H)
        {
            Aspect = id; AspectR = Ratio(id, (float)W / H);
            if (AspectR <= 0) return;
            float w, h;
            if ((float)W / H > AspectR) { h = H; w = H * AspectR; } else { w = W; h = W / AspectR; }
            Crop = new RectangleF((W - w) / 2f / W, (H - h) / 2f / H, w / W, h / H);
        }
    }

    internal static class PhotoSaver
    {
        // Simpan hasil suntingan. overwrite=false -> "nama (diedit).ext" di samping berkas asli.
        public static string Save(string src, Bitmap full, PhotoEdit ed, bool overwrite)
        {
            string ext = ImageIO.SaveExt(src);
            string dir = Path.GetDirectoryName(src);
            string dst = overwrite && ImageIO.CanOverwrite(src) ? src : PhotosApp.Unique(dir, Path.GetFileNameWithoutExtension(src) + " (" + L.T("diedit") + ")", ext);
            string tmp = dst + ".lmtmp";
            using (Bitmap o = ed.Render(full)) ImageIO.Save(o, tmp, ext);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
            return dst;
        }
    }

    // ============================================================
    //  VIDEO: pangkas (ffmpeg -c copy, cadangan WinRT MediaComposition) & bingkai filmstrip
    // ============================================================
    internal static class VideoTools
    {
        private static string _ff; private static bool _ffDone;

        public static string Ffmpeg
        {
            get
            {
                if (_ffDone) return _ff;
                _ffDone = true;
                List<string> dirs = new List<string>();
                string bd = AppDomain.CurrentDomain.BaseDirectory;
                dirs.Add(bd); dirs.Add(Path.Combine(bd, "ffmpeg")); dirs.Add(Path.Combine(bd, "ffmpeg\\bin"));
                foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) if (d.Trim().Length > 0) dirs.Add(d.Trim().Trim('"'));
                foreach (string d in dirs) { try { string p = Path.Combine(d, "ffmpeg.exe"); if (File.Exists(p)) { _ff = p; break; } } catch { } }
                return _ff;
            }
        }

        private static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        // Hasil null = berhasil; selain itu pesan galat.
        public static string Trim(string src, double a, double b, bool forceWinRT, out string dst)
        {
            dst = null;
            string ff = forceWinRT ? null : Ffmpeg;
            string dir = Path.GetDirectoryName(src), name = Path.GetFileNameWithoutExtension(src) + " (" + L.T("dipangkas") + ")";
            if (ff != null)
            {
                dst = PhotosApp.Unique(dir, name, Path.GetExtension(src));
                string err = TrimFfmpeg(ff, src, a, b, dst);
                if (err == null) return null;
                Log.Write("photos ffmpeg trim: " + err);
                try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                if (PhotosApp.IsVideo(src) && !IsAudio(src)) { /* coba WinRT */ } else return err;
            }
            dst = PhotosApp.Unique(dir, name, ".mp4");
            try { TrimWinRT(src, dst, a, b); return null; }
            catch (Exception ex)
            {
                Log.Write("photos winrt trim: " + ex);
                try { if (File.Exists(dst) && new FileInfo(dst).Length == 0) File.Delete(dst); } catch { }
                return ex.Message;
            }
        }

        private static bool IsAudio(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".mp3" || e == ".m4a" || e == ".wav" || e == ".flac" || e == ".aac" || e == ".wma" || e == ".ogg" || e == ".opus";
        }

        // Salin aliran tanpa encode ulang (cepat, lossless). Bila jarak keyframe membuat potongan meleset jauh
        // (umum pada rekaman layar), ulangi dengan encode ulang yang presisi.
        private static string TrimFfmpeg(string ff, string src, double a, double b, string dst)
        {
            string err = RunFf(ff, "-hide_banner -loglevel error -y -ss " + N(a) + " -to " + N(b) + " -i \"" + src + "\" -c copy -avoid_negative_ts make_zero \"" + dst + "\"", dst);
            if (err != null || IsAudio(src)) return err;
            double got = Probe(dst);
            if (got < 0 || Math.Abs(got - (b - a)) <= 0.35) return null;
            string e = Path.GetExtension(dst).ToLowerInvariant(), codec;
            if (e == ".webm") codec = "-c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 160k";
            else if (e == ".mp4" || e == ".m4v" || e == ".mov" || e == ".mkv" || e == ".ts" || e == ".mts" || e == ".m2ts") codec = "-c:v libx264 -crf 18 -preset fast -pix_fmt yuv420p -c:a aac -b:a 192k" + (e == ".mkv" || e.EndsWith("ts") ? "" : " -movflags +faststart");
            else return null;
            Log.Write("photos trim: keyframe meleset (" + N(got) + " vs " + N(b - a) + "), encode ulang");
            string tmp = dst + ".lmtmp" + e;
            err = RunFf(ff, "-hide_banner -loglevel error -y -ss " + N(a) + " -to " + N(b) + " -i \"" + src + "\" " + codec + " \"" + tmp + "\"", tmp);
            if (err != null) { try { File.Delete(tmp); } catch { } return null; }   // hasil salinan tetap dipakai
            File.Delete(dst); File.Move(tmp, dst);
            return null;
        }

        // durasi lewat ffprobe di samping ffmpeg; -1 bila tidak tersedia
        public static double Probe(string file)
        {
            try
            {
                string fp = Ffmpeg == null ? null : Path.Combine(Path.GetDirectoryName(Ffmpeg), "ffprobe.exe");
                if (fp == null || !File.Exists(fp)) return -1;
                ProcessStartInfo psi = new ProcessStartInfo(fp, "-v error -show_entries format=duration -of csv=p=0 \"" + file + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(10000);
                    double d; return double.TryParse(o.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : -1;
                }
            }
            catch { return -1; }
        }

        private static string RunFf(string ff, string args, string dst)
        {
            ProcessStartInfo psi = new ProcessStartInfo(ff, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEndAsync();
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(30 * 60 * 1000)) { try { p.Kill(); } catch { } return "timeout"; }
                if (p.ExitCode != 0) return err.Trim().Length > 0 ? err.Trim() : "ffmpeg exit " + p.ExitCode;
            }
            if (!File.Exists(dst) || new FileInfo(dst).Length == 0) return "output kosong";
            return null;
        }

        private static void WaitInfo(global::Windows.Foundation.IAsyncInfo info)
        {
            DateTime t0 = DateTime.Now;
            while (info.Status == global::Windows.Foundation.AsyncStatus.Started)
            {
                Thread.Sleep(30);
                if ((DateTime.Now - t0).TotalMinutes > 30) { info.Cancel(); throw new TimeoutException(); }
            }
            if (info.Status == global::Windows.Foundation.AsyncStatus.Error) throw info.ErrorCode ?? new Exception("WinRT error");
            if (info.Status == global::Windows.Foundation.AsyncStatus.Canceled) throw new OperationCanceledException();
        }
        private static T Wait<T>(global::Windows.Foundation.IAsyncOperation<T> op) { WaitInfo(op); return op.GetResults(); }
        private static T Wait<T, P>(global::Windows.Foundation.IAsyncOperationWithProgress<T, P> op) { WaitInfo(op); return op.GetResults(); }

        private static void TrimWinRT(string src, string dst, double a, double b)
        {
            Exception err = null;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    global::Windows.Storage.StorageFile f = Wait(global::Windows.Storage.StorageFile.GetFileFromPathAsync(src));
                    global::Windows.Media.Editing.MediaClip clip = Wait(global::Windows.Media.Editing.MediaClip.CreateFromFileAsync(f));
                    TimeSpan dur = clip.OriginalDuration;
                    clip.TrimTimeFromStart = TimeSpan.FromSeconds(Math.Max(0, a));
                    TimeSpan endTrim = dur - TimeSpan.FromSeconds(b);
                    clip.TrimTimeFromEnd = endTrim > TimeSpan.Zero ? endTrim : TimeSpan.Zero;
                    global::Windows.Media.Editing.MediaComposition comp = new global::Windows.Media.Editing.MediaComposition();
                    comp.Clips.Add(clip);
                    global::Windows.Storage.StorageFolder folder = Wait(global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(dst)));
                    global::Windows.Storage.StorageFile o = Wait(folder.CreateFileAsync(Path.GetFileName(dst), global::Windows.Storage.CreationCollisionOption.ReplaceExisting));
                    global::Windows.Media.Transcoding.TranscodeFailureReason r = Wait(comp.RenderToFileAsync(o, global::Windows.Media.Editing.MediaTrimmingPreference.Precise));
                    if (r != global::Windows.Media.Transcoding.TranscodeFailureReason.None) throw new Exception("MediaComposition: " + r);
                }
                catch (Exception ex) { err = ex; }
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.MTA); t.Start(); t.Join();
            if (err != null) throw err;
            if (!File.Exists(dst) || new FileInfo(dst).Length == 0) throw new Exception(L.T("Berkas hasil kosong"));
        }

        // Salinan proksi H.264/AAC (cache di %LOCALAPPDATA%\LiquidMac\photos-cache) untuk codec yang tidak bisa diputar WPF.
        public static string Proxy(string src, Action<double> progress, Func<bool> cancel, out string err)
        {
            err = null;
            string ff = Ffmpeg; if (ff == null) { err = "ffmpeg tidak ada"; return null; }
            try
            {
                string dir = Path.Combine(Log.Dir, "photos-cache");
                Directory.CreateDirectory(dir);
                foreach (string old in Directory.GetFiles(dir)) { try { if ((DateTime.Now - File.GetLastAccessTime(old)).TotalDays > 3) File.Delete(old); } catch { } }
                FileInfo fi = new FileInfo(src);
                string key;
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    key = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(src.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks))).Replace("-", "");
                string dst = Path.Combine(dir, key + ".mp4");
                if (File.Exists(dst) && new FileInfo(dst).Length > 0) { try { File.SetLastAccessTime(dst, DateTime.Now); } catch { } return dst; }
                double dur = Probe(src);
                string tmp = dst + ".part.mp4";
                string args = "-hide_banner -loglevel error -nostats -y -i \"" + src + "\" -map 0:v:0? -map 0:a:0? -c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p -vf \"scale='min(1920,iw)':-2\" -c:a aac -b:a 160k -movflags +faststart -progress pipe:1 \"" + tmp + "\"";
                ProcessStartInfo psi = new ProcessStartInfo(ff, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> errT = p.StandardError.ReadToEndAsync();
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        if (cancel != null && cancel()) { try { p.Kill(); } catch { } break; }
                        if (dur > 0 && (line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms=")))
                        {
                            long us; if (long.TryParse(line.Substring(line.IndexOf('=') + 1), out us) && progress != null) progress(Math.Max(0, Math.Min(1, us / 1e6 / dur)));
                        }
                    }
                    p.WaitForExit();
                    if (cancel != null && cancel()) { try { File.Delete(tmp); } catch { } err = "dibatalkan"; return null; }
                    if (p.ExitCode != 0 || !File.Exists(tmp)) { err = errT.Result.Trim(); try { File.Delete(tmp); } catch { } return null; }
                }
                File.Move(tmp, dst);
                return dst;
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        // satu bingkai (tinggi h) lewat ffmpeg, null bila gagal
        public static Bitmap Frame(string ff, string src, double t, int h)
        {
            try
            {
                string args = "-hide_banner -loglevel error -ss " + N(t) + " -i \"" + src + "\" -frames:v 1 -vf scale=-2:" + h + " -f image2pipe -vcodec bmp -";
                ProcessStartInfo psi = new ProcessStartInfo(ff, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                using (MemoryStream ms = new MemoryStream())
                {
                    p.StandardError.ReadToEndAsync();
                    p.StandardOutput.BaseStream.CopyTo(ms);
                    p.WaitForExit(15000);
                    if (ms.Length < 100) return null;
                    ms.Position = 0;
                    using (Image im = Image.FromStream(ms)) return ImageIO.Copy32(im);
                }
            }
            catch (Exception ex) { Log.Write("photos frame: " + ex.Message); return null; }
        }
    }

    // ============================================================
    //  JENDELA FOTO
    // ============================================================
    internal sealed class PhotosForm : Form
    {
        private enum Mode { Library, Image, Video }
        private sealed class LibItem { public string File; public DateTime Time; public bool Video; }

        private Mode _mode = Mode.Library;
        private bool _full, _editing, _trimming;
        private FormWindowState _preFullState; private Rectangle _preFullBounds;

        private readonly List<string> _list = new List<string>();
        private int _idx = -1; private bool _hasLib; private string _cur;

        // warna & huruf
        private static readonly Color CBg = Color.FromArgb(30, 30, 31), CTool = Color.FromArgb(42, 42, 44), CPanel = Color.FromArgb(37, 37, 39), CLine = Color.FromArgb(60, 60, 64);
        private static readonly Color CText = Color.FromArgb(236, 236, 240), CDim = Color.FromArgb(150, 150, 158), CAccent = Color.FromArgb(10, 132, 255), CYellow = Color.FromArgb(255, 204, 0), CBtn = Color.FromArgb(64, 64, 68);
        private readonly Font _f = Ui.F(9.5f, FontStyle.Regular), _fb = Ui.F(9.5f, FontStyle.Bold), _fs = Ui.F(8.5f, FontStyle.Regular), _fsb = Ui.F(8.5f, FontStyle.Bold), _ft = Ui.F(10.5f, FontStyle.Bold), _fh = Ui.F(17f, FontStyle.Bold);
        private readonly Font _gl = Glyphs.Font(Ui.Pf(14)), _glS = Glyphs.Font(Ui.Pf(12)), _glB = Glyphs.Font(Ui.Pf(20));
        private readonly Font _sym = new Font("Segoe UI Symbol", Ui.Pf(19), FontStyle.Regular, GraphicsUnit.Pixel);

        private readonly int TopH = Ui.P(52), PanelW = Ui.P(272), BarH = Ui.P(56), TrimBarH = Ui.P(96);
        private int Pad { get { return WindowState == FormWindowState.Maximized && !_full ? Ui.P(8) : 0; } }

        // titik interaksi (digambar ulang tiap paint)
        private readonly List<KeyValuePair<string, RectangleF>> _hits = new List<KeyValuePair<string, RectangleF>>();
        private string _hover, _drag; private int _hoverLight = -1;
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();
        private string _toast; private DateTime _toastAt;
        private string _busyMsg;

        // pustaka
        private List<LibItem> _lib = new List<LibItem>(), _libView = new List<LibItem>();
        private string _libFilter = "all"; private bool _libLoading, _libScanned;
        private float _libScroll, _libScrollT, _libContentH; private int _libSel = -1, _libHover = -1;
        private readonly Dictionary<string, Bitmap> _thumbs = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _thumbWant = new List<string>();
        private readonly AutoResetEvent _thumbWake = new AutoResetEvent(false);
        private volatile bool _closed; private volatile int _thumbPx = 160;

        // gambar
        private Bitmap _img; private Image _anim; private string _imgErr; private bool _loading; private int _gen;
        private bool _fit = true; private float _zoom = 1; private PointF _pan; private Point _dragPt; private PointF _dragPan;
        private Bitmap _cache; private float _cacheSc = -1; private DateTime _zoomAt = DateTime.MinValue;
        private readonly EventHandler _animTick;

        // suntingan
        private PhotoEdit _ed = new PhotoEdit();
        private Bitmap _pv, _pvGeo, _pvCol; private string _edTab = "adjust";
        private RectangleF _cropStart; private PointF _cropDown;

        // video
        private ElementHost _host; private WC.MediaElement _me; private WC.TextBlock _note;
        private bool _playing, _vidReady, _muted; private double _dur; private float _vol = 0.8f;
        private double _trimA, _trimB; private Bitmap[] _strip; private int _stripGen; private bool _wasPlaying;
        private System.Windows.Forms.Timer _clickT;

        public PhotosForm(string[] files)
        {
            Text = L.T("Foto");
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Ui.P(1180), wa.Width - Ui.P(40)), Math.Min(Ui.P(780), wa.Height - Ui.P(40)));
            MinimumSize = new Size(Ui.P(640), Ui.P(440));
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max(Ui.P(10), (wa.Height - Height) / 2));
            BackColor = CBg; KeyPreview = true; AllowDrop = true;
            try { Icon = PhotosIcon.MakeIcon(); } catch { }
            HandleCreated += delegate { PhotosIcon.MarkOwn(Handle); };   // traffic light LiquidMac tidak menempel ke jendela ini
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            float v; if (float.TryParse(Settings.GetS("PhotosVolume", "0.8"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) _vol = Math.Max(0, Math.Min(1, v));
            _animTick = delegate { try { if (IsHandleCreated && !IsDisposed) BeginInvoke((MethodInvoker)delegate { Invalidate(); }); } catch { } };

            _tick.Interval = 16; _tick.Tick += delegate { Step(); }; _tick.Start();
            Thread tt = new Thread(ThumbLoop); tt.IsBackground = true; tt.SetApartmentState(ApartmentState.STA); tt.Start();

            OpenPaths(files ?? new string[0]);
            ShotHook();
        }

        // ---------- membuka ----------
        private void OpenPaths(string[] files)
        {
            List<string> paths = new List<string>();
            foreach (string f in files)
            {
                if (string.IsNullOrEmpty(f)) continue;
                try
                {
                    if (Directory.Exists(f)) paths.AddRange(PhotosApp.MediaIn(f));
                    else if (File.Exists(f)) paths.Add(Path.GetFullPath(f));
                }
                catch { }
            }
            if (paths.Count == 0) { _hasLib = true; ShowLibrary(); return; }
            _list.Clear();
            int idx = 0;
            if (paths.Count == 1 && files.Length == 1 && File.Exists(files[0]))
            {
                List<string> sib = PhotosApp.MediaIn(Path.GetDirectoryName(paths[0]));
                idx = sib.FindIndex(delegate (string s) { return string.Equals(s, paths[0], StringComparison.OrdinalIgnoreCase); });
                if (idx < 0) { sib.Clear(); sib.Add(paths[0]); idx = 0; }
                _list.AddRange(sib);
            }
            else _list.AddRange(paths);
            ShowItem(idx);
        }

        private void ShowItem(int i)
        {
            if (i < 0 || i >= _list.Count) return;
            if (_editing) EndEdit();
            if (_trimming) EndTrim();
            StopVideo();
            _idx = i; _cur = _list[i];
            Text = Path.GetFileName(_cur) + " — " + L.T("Foto");
            if (PhotosApp.IsVideo(_cur)) { _mode = Mode.Video; DisposeImage(); OpenVideo(_cur); }
            else { _mode = Mode.Image; LoadImage(_cur); }
            LayoutHost();
            Invalidate();
        }

        private void Nav(int d)
        {
            int n = _idx + d;
            if (n >= 0 && n < _list.Count) ShowItem(n);
        }

        private void ShowLibrary()
        {
            if (_editing) EndEdit();
            if (_trimming) EndTrim();
            StopVideo(); DisposeImage();
            _mode = Mode.Library; _cur = null; Text = L.T("Foto");
            if (_idx >= 0 && _idx < _list.Count && _libView.Count > 0)
            {
                string p = _list[_idx];
                int k = _libView.FindIndex(delegate (LibItem it) { return string.Equals(it.File, p, StringComparison.OrdinalIgnoreCase); });
                if (k >= 0) { _libSel = k; EnsureLibVisible(k); }
            }
            if (!_libScanned) ScanLibrary();
            LayoutHost();
            Invalidate();
        }

        // ---------- jendela tanpa bilah judul (seperti Finder) ----------
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr h, string name, IntPtr data);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 1; try { DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
            v = 2; try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
            MARGINS m = new MARGINS(); m.cyTopHeight = 1;
            try { DwmExtendFrameIntoClientArea(Handle, ref m); } catch { }
            try { SetProp(Handle, "LiquidMac.OwnChrome", (IntPtr)1); } catch { }   // traffic light digambar sendiri
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
        }

        private Size _normalSize; private bool _leftNormal;
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (_leftNormal && (specified & BoundsSpecified.Size) != 0)
            {
                Size frame = SizeFromClientSize(Size.Empty);
                if (width == _normalSize.Width + frame.Width && height == _normalSize.Height + frame.Height) { width = _normalSize.Width; height = _normalSize.Height; _leftNormal = false; }
            }
            base.SetBoundsCore(x, y, width, height, specified);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0005)
            {
                long kind = m.WParam.ToInt64(), lp = m.LParam.ToInt64();
                if (kind == 1 || kind == 2) _leftNormal = true;
                else if (kind == 0 && !_full) _normalSize = new Size((int)(lp & 0xFFFF), (int)((lp >> 16) & 0xFFFF));
            }
            if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x0084)
            {
                if (_full) { m.Result = (IntPtr)1; return; }
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                int b = Ui.P(6), W = ClientSize.Width, H = ClientSize.Height;
                if (WindowState != FormWindowState.Maximized)
                {
                    bool l = p.X < b, r = p.X >= W - b, t = p.Y < b, bt = p.Y >= H - b;
                    if (t && l) { m.Result = (IntPtr)13; return; }
                    if (t && r) { m.Result = (IntPtr)14; return; }
                    if (bt && l) { m.Result = (IntPtr)16; return; }
                    if (bt && r) { m.Result = (IntPtr)17; return; }
                    if (l) { m.Result = (IntPtr)10; return; }
                    if (r) { m.Result = (IntPtr)11; return; }
                    if (t) { m.Result = (IntPtr)12; return; }
                    if (bt) { m.Result = (IntPtr)15; return; }
                }
                if (p.Y < Pad + TopH && HitId(p) == null && LightHit(p) < 0) { m.Result = (IntPtr)2; return; }
                m.Result = (IntPtr)1; return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closed = true; _thumbWake.Set();
            _tick.Stop();
            StopVideo();
            DisposeImage();
            lock (_thumbs) { foreach (Bitmap b in _thumbs.Values) if (b != null) b.Dispose(); _thumbs.Clear(); }
            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutHost(); Invalidate(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Invalidate(); }

        // ---------- tata letak ----------
        private Rectangle ContentRect
        {
            get
            {
                int p = Pad, top = _full ? 0 : TopH;
                return new Rectangle(p, p + top, Math.Max(1, ClientSize.Width - 2 * p), Math.Max(1, ClientSize.Height - 2 * p - top));
            }
        }
        private int VBarH { get { return _trimming ? TrimBarH : BarH; } }
        private Rectangle VideoRect { get { Rectangle c = ContentRect; return new Rectangle(c.X, c.Y, c.Width, Math.Max(1, c.Height - VBarH)); } }
        private Rectangle VBarRect { get { Rectangle c = ContentRect; return new Rectangle(c.X, c.Bottom - VBarH, c.Width, VBarH); } }
        private Rectangle EditArea { get { Rectangle c = ContentRect; return new Rectangle(c.X, c.Y, Math.Max(1, c.Width - PanelW), c.Height); } }

        private void LayoutHost()
        {
            if (_host == null) return;
            bool show = _mode == Mode.Video;
            if (show) { Rectangle r = VideoRect; if (_host.Bounds != r) _host.Bounds = r; }
            if (_host.Visible != show) _host.Visible = show;
        }

        private RectangleF LightRect(int i) { int p = Pad; float d = Ui.Pf(13); return new RectangleF(p + Ui.P(18) + i * Ui.Pf(21), p + (TopH - d) / 2f, d, d); }
        private int LightHit(Point p)
        {
            if (_full) return -1;
            for (int i = 0; i < 3; i++) { RectangleF r = LightRect(i); r.Inflate(Ui.Pf(3), Ui.Pf(3)); if (r.Contains(p)) return i; }
            return -1;
        }

        private string HitId(Point p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--) if (_hits[i].Value.Contains(p)) return _hits[i].Key;
            return null;
        }
        private void Hit(string id, RectangleF r) { _hits.Add(new KeyValuePair<string, RectangleF>(id, r)); }
        private RectangleF HitRect(string id) { foreach (KeyValuePair<string, RectangleF> kv in _hits) if (kv.Key == id) return kv.Value; return RectangleF.Empty; }

        // ---------- animasi / timer ----------
        private int _frame;
        private void Step()
        {
            _frame++;
            bool inv = false;
            if (_mode == Mode.Library && Math.Abs(_libScroll - _libScrollT) > 0.5f) { _libScroll += (_libScrollT - _libScroll) * 0.28f; inv = true; }
            else if (_mode == Mode.Library) _libScroll = _libScrollT;
            if (_loading || _busyMsg != null || (_libLoading && _mode == Mode.Library && _frame % 6 == 0)) inv = true;
            if (_toast != null && (DateTime.Now - _toastAt).TotalMilliseconds > 2600) { _toast = null; inv = true; }
            if (_toast != null && (DateTime.Now - _toastAt).TotalMilliseconds > 2200) inv = true;
            if (_mode == Mode.Image && _cacheSc > 0 && !_fit && (DateTime.Now - _zoomAt).TotalMilliseconds > 140 && (DateTime.Now - _zoomAt).TotalMilliseconds < 200) inv = true;
            if (_mode == Mode.Video && _me != null && _frame % 2 == 0)
            {
                if (_trimming && _playing && _vidReady)
                {
                    double pos = _me.Position.TotalSeconds;
                    if (pos >= _trimB - 0.02 || pos < _trimA - 0.3) _me.Position = TimeSpan.FromSeconds(_trimA);
                }
                if (_drag == null || !_drag.StartsWith("trim")) Invalidate(VBarRect);
            }
            if (_mode == Mode.Image && !_fit && (DateTime.Now - _zoomAt).TotalMilliseconds > 150 && _needHq) { _needHq = false; inv = true; }
            if (inv) Invalidate();
        }
        private bool _needHq;

        private void Toast(string s) { _toast = s; _toastAt = DateTime.Now; Invalidate(); }

        private void RunBusy(string msg, ThreadStart work, Action<Exception> done)
        {
            _busyMsg = msg; Cursor = Cursors.WaitCursor; Invalidate();
            Thread t = new Thread(delegate ()
            {
                Exception err = null;
                try { work(); } catch (Exception ex) { err = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busyMsg = null; Cursor = Cursors.Default; Invalidate();
                        done(err);
                    });
                }
                catch { }
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        // ============================================================
        //  PUSTAKA
        // ============================================================
        private void ScanLibrary()
        {
            _libScanned = true; _libLoading = true;
            Thread t = new Thread(delegate ()
            {
                List<LibItem> l = new List<LibItem>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] roots = { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
                foreach (string root in roots)
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    Stack<string> st = new Stack<string>(); st.Push(root);
                    int dirs = 0;
                    while (st.Count > 0 && l.Count < 20000 && dirs < 4000 && !_closed)
                    {
                        string d = st.Pop(); dirs++;
                        try
                        {
                            foreach (FileSystemInfo fi in new DirectoryInfo(d).EnumerateFileSystemInfos())
                            {
                                try
                                {
                                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                                    if ((fi.Attributes & FileAttributes.Directory) != 0) { if ((fi.Attributes & FileAttributes.ReparsePoint) == 0) st.Push(fi.FullName); continue; }
                                    if (!PhotosApp.IsMedia(fi.Name) || !seen.Add(fi.FullName)) continue;
                                    LibItem it = new LibItem(); it.File = fi.FullName; it.Time = fi.LastWriteTime; it.Video = PhotosApp.IsVideo(fi.Name);
                                    l.Add(it);
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                l.Sort(delegate (LibItem a, LibItem b) { return b.Time.CompareTo(a.Time); });
                try { BeginInvoke((MethodInvoker)delegate { _lib = l; _libLoading = false; ApplyFilter(); Invalidate(); }); } catch { }
            });
            t.IsBackground = true; t.Start();
        }

        private void ApplyFilter()
        {
            List<LibItem> v = new List<LibItem>();
            foreach (LibItem it in _lib) if (_libFilter == "all" || (_libFilter == "vid") == it.Video) v.Add(it);
            _libView = v; _libSel = -1; _libScroll = _libScrollT = 0;
        }

        private void LibGeom(Rectangle area, out int cols, out float cell, out float pad, out float gap, out float headH)
        {
            pad = Ui.Pf(18); gap = Ui.Pf(3); headH = Ui.Pf(64);
            float usable = area.Width - 2 * pad;
            cols = Math.Max(2, (int)((usable + gap) / (Ui.Pf(168) + gap)));
            cell = (usable - (cols - 1) * gap) / cols;
        }

        private RectangleF LibCell(Rectangle area, int i)
        {
            int cols; float cell, pad, gap, headH; LibGeom(area, out cols, out cell, out pad, out gap, out headH);
            int r = i / cols, c = i % cols;
            return new RectangleF(area.Left + pad + c * (cell + gap), area.Top + headH + r * (cell + gap) - _libScroll, cell, cell);
        }

        private int LibHit(Point p)
        {
            Rectangle area = ContentRect;
            if (!area.Contains(p)) return -1;
            int cols; float cell, pad, gap, headH; LibGeom(area, out cols, out cell, out pad, out gap, out headH);
            float x = p.X - area.Left - pad, y = p.Y - area.Top - headH + _libScroll;
            if (x < 0 || y < 0) return -1;
            int c = (int)(x / (cell + gap)), r = (int)(y / (cell + gap));
            if (c >= cols || x - c * (cell + gap) > cell || y - r * (cell + gap) > cell) return -1;
            int i = r * cols + c;
            return i < _libView.Count ? i : -1;
        }

        private float LibMaxScroll { get { return Math.Max(0, _libContentH - ContentRect.Height); } }

        private void EnsureLibVisible(int i)
        {
            Rectangle area = ContentRect;
            int cols; float cell, pad, gap, headH; LibGeom(area, out cols, out cell, out pad, out gap, out headH);
            float top = headH + (i / cols) * (cell + gap), bot = top + cell;
            if (top - Ui.Pf(8) < _libScrollT) _libScrollT = Math.Max(0, top - Ui.Pf(8));
            else if (bot + Ui.Pf(8) > _libScrollT + area.Height) _libScrollT = bot + Ui.Pf(8) - area.Height;
        }

        private void DrawLibrary(Graphics g)
        {
            Rectangle area = ContentRect;
            g.SetClip(area);
            int cols; float cell, pad, gap, headH; LibGeom(area, out cols, out cell, out pad, out gap, out headH);
            _thumbPx = Math.Max(64, ((int)cell + 31) / 32 * 32);
            float hy = area.Top - _libScroll;
            using (SolidBrush tb = new SolidBrush(CText)) g.DrawString(L.T("Pustaka"), _fh, tb, area.Left + pad - Ui.Pf(2), hy + Ui.Pf(14));
            string sub;
            if (_libLoading) sub = L.T("Memindai Gambar & Video…");
            else
            {
                int ni = 0, nv = 0; foreach (LibItem it in _lib) { if (it.Video) nv++; else ni++; }
                sub = ni + " " + L.T("foto") + ", " + nv + " " + L.T("video");
                if (_libView.Count > 0) sub += "  ·  " + _libView[_libView.Count - 1].Time.ToString("MMM yyyy", L.Culture) + " – " + _libView[0].Time.ToString("MMM yyyy", L.Culture);
            }
            using (SolidBrush sb = new SolidBrush(CDim)) g.DrawString(sub, _fs, sb, area.Left + pad, hy + Ui.Pf(42));
            int n = _libView.Count;
            int rows = (n + cols - 1) / cols;
            _libContentH = headH + rows * (cell + gap) + pad;
            if (n == 0)
            {
                string msg = _libLoading ? L.T("Memuat…") : L.T("Tidak ada foto atau video di folder Gambar dan Video.");
                Txt(g, msg, _f, CDim, area, SfC);
                if (_libLoading) Spinner(g, new PointF(area.Left + area.Width / 2f, area.Top + area.Height / 2f + Ui.Pf(30)), Ui.Pf(9));
                g.ResetClip(); return;
            }
            int r0 = Math.Max(0, (int)((_libScroll - headH) / (cell + gap)));
            int r1 = Math.Min(rows - 1, (int)((_libScroll - headH + area.Height) / (cell + gap)) + 1);
            List<string> want = new List<string>();
            using (SolidBrush ph = new SolidBrush(Color.FromArgb(46, 46, 49)))
            {
                for (int r = r0; r <= r1; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c; if (i >= n) break;
                        LibItem it = _libView[i];
                        RectangleF cr = LibCell(area, i);
                        Bitmap tb; bool has;
                        lock (_thumbs) has = _thumbs.TryGetValue(it.File, out tb);
                        if (has && tb != null)
                        {
                            float s = Math.Max(cr.Width / tb.Width, cr.Height / tb.Height);
                            float sw = cr.Width / s, sh = cr.Height / s;
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.DrawImage(tb, cr, new RectangleF((tb.Width - sw) / 2f, (tb.Height - sh) / 2f, sw, sh), GraphicsUnit.Pixel);
                        }
                        else
                        {
                            g.FillRectangle(ph, cr);
                            if (!has) want.Add(it.File);
                            else using (SolidBrush ib = new SolidBrush(CDim)) Glyphs.Draw(g, it.Video ? "" : "", _glB, ib, cr);
                        }
                        if (it.Video)
                        {
                            RectangleF gr = new RectangleF(cr.X, cr.Bottom - Ui.Pf(26), cr.Width, Ui.Pf(26));
                            using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(0, gr.Top - 1), new PointF(0, gr.Bottom + 1), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(140, 0, 0, 0))) g.FillRectangle(lb, gr);
                            using (SolidBrush wb = new SolidBrush(Color.White)) Glyphs.Draw(g, "", _glS, wb, new RectangleF(cr.X + Ui.Pf(4), gr.Y, Ui.Pf(22), gr.Height));
                        }
                        if (i == _libHover && i != _libSel) using (SolidBrush hb = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillRectangle(hb, cr);
                        if (i == _libSel)
                        {
                            RectangleF sr = cr; sr.Inflate(-Ui.Pf(1.5), -Ui.Pf(1.5));
                            using (Pen sp = new Pen(CAccent, Ui.Pf(3))) g.DrawRectangle(sp, sr.X, sr.Y, sr.Width, sr.Height);
                        }
                    }
            }
            // muat lebih dulu satu layar di bawah
            for (int i = (r1 + 1) * cols; i < Math.Min(n, (r1 + 3) * cols); i++) { bool has; lock (_thumbs) has = _thumbs.ContainsKey(_libView[i].File); if (!has) want.Add(_libView[i].File); }
            lock (_thumbs)
            {
                _thumbWant.Clear();
                for (int i = want.Count - 1; i >= 0; i--) _thumbWant.Add(want[i]);   // yang paling atas diambil dulu (dari ujung)
                if (_thumbs.Count > 700)
                {
                    HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = Math.Max(0, (r0 - 6) * cols); i < Math.Min(n, (r1 + 8) * cols); i++) keep.Add(_libView[i].File);
                    List<string> drop = new List<string>();
                    foreach (string k in _thumbs.Keys) if (!keep.Contains(k)) drop.Add(k);
                    foreach (string k in drop) { Bitmap b = _thumbs[k]; _thumbs.Remove(k); if (b != null) b.Dispose(); }
                }
            }
            if (want.Count > 0) _thumbWake.Set();
            // indikator gulir
            if (_libContentH > area.Height)
            {
                float h = Math.Max(Ui.Pf(30), area.Height * area.Height / _libContentH);
                float y = area.Top + (area.Height - h) * (_libScroll / Math.Max(1, LibMaxScroll));
                using (GraphicsPath sp = Glass.Round(new RectangleF(area.Right - Ui.Pf(8), y + Ui.Pf(2), Ui.Pf(5), h - Ui.Pf(4)), Ui.Pf(2.5f))) using (SolidBrush sb = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(sb, sp);
            }
            g.ResetClip();
        }

        private void ThumbLoop()
        {
            while (!_closed)
            {
                string p = null;
                lock (_thumbs) { if (_thumbWant.Count > 0) { p = _thumbWant[_thumbWant.Count - 1]; _thumbWant.RemoveAt(_thumbWant.Count - 1); if (_thumbs.ContainsKey(p)) p = null; } }
                if (p == null) { lock (_thumbs) if (_thumbWant.Count > 0) continue; _thumbWake.WaitOne(400); continue; }
                Bitmap b = null;
                try
                {
                    int px = _thumbPx;
                    Bitmap s = ShellImage.Get(p, px, 0x8 /* THUMBNAILONLY */);
                    if (s == null && PhotosApp.IsImage(p)) s = ImageIO.Thumb(p, px);
                    if (s == null) s = ShellImage.Get(p, px, 0);
                    if (s != null)
                    {
                        int side = Math.Min(s.Width, s.Height);
                        float k = Math.Min(1f, (float)px / side);
                        int ow = Math.Max(1, (int)(side * k));
                        b = new Bitmap(ow, ow, PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.Clear(Color.FromArgb(46, 46, 49));
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(s, new Rectangle(0, 0, ow, ow), (s.Width - side) / 2, (s.Height - side) / 2, side, side, GraphicsUnit.Pixel);
                        }
                        s.Dispose();
                    }
                }
                catch (Exception ex) { Log.Write("photos thumb: " + ex.Message); }
                lock (_thumbs) { Bitmap old; if (_thumbs.TryGetValue(p, out old) && old != null) old.Dispose(); _thumbs[p] = b; }
                try { if (!_closed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (_mode == Mode.Library) Invalidate(); }); } catch { }
            }
        }

        // ============================================================
        //  GAMBAR
        // ============================================================
        private void LoadImage(string path)
        {
            int gen = ++_gen;
            DisposeImage();
            _loading = true; _imgErr = null; _fit = true; _pan = PointF.Empty;
            Thread t = new Thread(delegate ()
            {
                Bitmap b = null; Image anim = null; string err = null;
                try { b = ImageIO.Load(path, out anim); if (b == null) err = L.T("Format tidak didukung."); }
                catch (Exception ex) { err = ex.Message; Log.Write("photos load " + path + ": " + ex.Message); }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (gen != _gen || IsDisposed) { if (b != null) b.Dispose(); if (anim != null) anim.Dispose(); return; }
                        _img = b; _anim = anim; _imgErr = err; _loading = false;
                        if (_anim != null) { try { ImageAnimator.Animate(_anim, _animTick); } catch { } }
                        Invalidate();
                    });
                }
                catch { if (b != null) b.Dispose(); }
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        private void DisposeImage()
        {
            if (_anim != null) { try { ImageAnimator.StopAnimate(_anim, _animTick); } catch { } _anim.Dispose(); _anim = null; }
            if (_img != null) { _img.Dispose(); _img = null; }
            if (_cache != null) { _cache.Dispose(); _cache = null; _cacheSc = -1; }
            _loading = false;
        }

        private Rectangle ImageArea { get { return ContentRect; } }

        private float FitScale(Rectangle area)
        {
            if (_img == null) return 1;
            int m = _full ? 0 : Ui.P(16);
            return Math.Min((float)(area.Width - 2 * m) / _img.Width, (float)(area.Height - 2 * m) / _img.Height);
        }
        private float CurScale { get { return _fit ? FitScale(ImageArea) : _zoom; } }

        private RectangleF ImageRectF()
        {
            Rectangle a = ImageArea; float s = CurScale;
            float w = _img.Width * s, h = _img.Height * s;
            ClampPan(w, h, a);
            return new RectangleF(a.Left + (a.Width - w) / 2f + _pan.X, a.Top + (a.Height - h) / 2f + _pan.Y, w, h);
        }

        private void ClampPan(float w, float h, Rectangle a)
        {
            float mx = Math.Max(0, (w - a.Width) / 2f), my = Math.Max(0, (h - a.Height) / 2f);
            _pan = new PointF(Math.Max(-mx, Math.Min(mx, _pan.X)), Math.Max(-my, Math.Min(my, _pan.Y)));
        }

        private void ZoomTo(float ns, PointF at)
        {
            if (_img == null) return;
            float fit = FitScale(ImageArea);
            ns = Math.Max(Math.Min(fit, 0.05f), Math.Min(16f, ns));
            RectangleF r = ImageRectF(); float s = CurScale;
            float ix = (at.X - r.X) / s, iy = (at.Y - r.Y) / s;
            if (ns <= fit * 1.001f) { _fit = true; _pan = PointF.Empty; }
            else
            {
                _fit = false; _zoom = ns;
                Rectangle a = ImageArea;
                float w = _img.Width * ns, h = _img.Height * ns;
                // posisi dasar (pan 0) lalu geser agar titik di bawah kursor tetap
                float bx = a.Left + (a.Width - w) / 2f, by = a.Top + (a.Height - h) / 2f;
                _pan = new PointF(at.X - ix * ns - bx, at.Y - iy * ns - by);
            }
            _zoomAt = DateTime.Now; _needHq = true;
            Invalidate();
        }

        private void ZoomStep(float k)
        {
            Rectangle a = ImageArea;
            ZoomTo(CurScale * k, new PointF(a.Left + a.Width / 2f, a.Top + a.Height / 2f));
        }

        private void DrawImageView(Graphics g)
        {
            Rectangle a = ImageArea;
            if (_full) using (SolidBrush bb = new SolidBrush(Color.Black)) g.FillRectangle(bb, a);
            if (_img == null)
            {
                if (_loading) Spinner(g, new PointF(a.Left + a.Width / 2f, a.Top + a.Height / 2f), Ui.Pf(12));
                else if (_imgErr != null)
                {
                    using (SolidBrush ib = new SolidBrush(CDim)) Glyphs.Draw(g, "", Glyphs.Font(Ui.Pf(40)), ib, new RectangleF(a.Left, a.Top + a.Height / 2f - Ui.Pf(70), a.Width, Ui.Pf(60)));
                    Txt(g, L.T("Berkas ini tidak bisa dibuka."), _fb, CText, new RectangleF(a.Left, a.Top + a.Height / 2f, a.Width, Ui.Pf(22)), SfC);
                    Txt(g, _imgErr, _fs, CDim, new RectangleF(a.Left + Ui.Pf(20), a.Top + a.Height / 2f + Ui.Pf(22), a.Width - Ui.Pf(40), Ui.Pf(20)), SfC);
                }
                return;
            }
            g.SetClip(a);
            RectangleF r = ImageRectF(); float s = CurScale;
            if (_anim != null)
            {
                try { ImageAnimator.UpdateFrames(_anim); } catch { }
                g.InterpolationMode = s >= 2 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.DrawImage(_anim, r);
            }
            else if (s < 0.999f)
            {
                bool settle = (DateTime.Now - _zoomAt).TotalMilliseconds > 130;
                if (_cache == null || Math.Abs(_cacheSc - s) > 0.0005f)
                {
                    if (settle)
                    {
                        if (_cache != null) _cache.Dispose();
                        _cache = ImageIO.Scaled(_img, (int)Math.Round(Math.Max(_img.Width, _img.Height) * s));
                        _cacheSc = s;
                    }
                }
                if (_cache != null && Math.Abs(_cacheSc - s) <= 0.0005f) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(_cache, new RectangleF((float)Math.Round(r.X), (float)Math.Round(r.Y), _cache.Width, _cache.Height)); g.PixelOffsetMode = PixelOffsetMode.Default; }
                else { g.InterpolationMode = InterpolationMode.Bilinear; g.DrawImage(_img, r); _needHq = true; }
            }
            else
            {
                // hanya bagian yang terlihat
                RectangleF vis = RectangleF.Intersect(r, a);
                if (vis.Width > 0 && vis.Height > 0)
                {
                    RectangleF src = new RectangleF((vis.X - r.X) / s, (vis.Y - r.Y) / s, vis.Width / s, vis.Height / s);
                    g.InterpolationMode = s >= 2.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_img, vis, src, GraphicsUnit.Pixel);
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
            }
            // panah navigasi saat kursor di tepi
            if (_list.Count > 1)
            {
                Point mp = PointToClient(Cursor.Position);
                bool inside = a.Contains(mp) && _drag == null;
                float d = Ui.Pf(34);
                if (inside && mp.X < a.Left + Ui.P(120) && _idx > 0) NavArrow(g, "ovl_prev", "", new RectangleF(a.Left + Ui.Pf(14), a.Top + (a.Height - d) / 2f, d, d));
                if (inside && mp.X > a.Right - Ui.P(120) && _idx < _list.Count - 1) NavArrow(g, "ovl_next", "", new RectangleF(a.Right - Ui.Pf(14) - d, a.Top + (a.Height - d) / 2f, d, d));
            }
            if (!_fit || s > FitScale(a) * 1.01f)
            {
                string zs = (int)Math.Round(s * 100) + "%";
                RectangleF zr = new RectangleF(a.Right - Ui.Pf(70), a.Bottom - Ui.Pf(36), Ui.Pf(58), Ui.Pf(24));
                using (GraphicsPath zp = Glass.Round(zr, Ui.Pf(12))) using (SolidBrush zb = new SolidBrush(Color.FromArgb(170, 20, 20, 22))) g.FillPath(zb, zp);
                Txt(g, zs, _fs, CText, zr, SfC);
            }
            g.ResetClip();
        }

        private void NavArrow(Graphics g, string id, string glyph, RectangleF r)
        {
            using (SolidBrush b = new SolidBrush(_hover == id ? Color.FromArgb(200, 70, 70, 74) : Color.FromArgb(150, 40, 40, 44))) g.FillEllipse(b, r);
            using (SolidBrush w = new SolidBrush(Color.White)) Glyphs.Draw(g, glyph, _glS, w, r);
            Hit(id, r);
        }

        // ============================================================
        //  SUNTING FOTO
        // ============================================================
        private void BeginEdit()
        {
            if (_img == null || _editing) return;
            _editing = true; _ed = new PhotoEdit(); _edTab = "adjust";
            if (_pv != null) _pv.Dispose();
            _pv = Math.Max(_img.Width, _img.Height) > 1600 ? ImageIO.Scaled(_img, 1600) : ImageIO.Copy32(_img);
            RebuildGeo();
            Invalidate();
        }

        private void EndEdit()
        {
            _editing = false; _drag = null;
            if (_pvCol != null && _pvCol != _pvGeo) _pvCol.Dispose();
            if (_pvGeo != null) _pvGeo.Dispose();
            if (_pv != null) _pv.Dispose();
            _pv = _pvGeo = _pvCol = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        private void CancelEdit()
        {
            if (_ed.Changed && MessageBox.Show(this, L.T("Buang semua perubahan pada foto ini?"), L.T("Foto"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            EndEdit();
        }

        private void RebuildGeo()
        {
            if (_pv == null) return;
            if (_pvCol != null && _pvCol != _pvGeo) _pvCol.Dispose();
            if (_pvGeo != null) _pvGeo.Dispose();
            _pvGeo = _ed.Geometry(_pv); _pvCol = null;
            RebuildColor();
        }

        private void RebuildColor()
        {
            if (_pvGeo == null) return;
            if (_pvCol != null && _pvCol != _pvGeo) _pvCol.Dispose();
            _pvCol = _ed.HasColor ? PhotoEdit.Apply(_pvGeo, _ed.Matrix(), new Rectangle(0, 0, _pvGeo.Width, _pvGeo.Height)) : _pvGeo;
            Invalidate();
        }

        private void SaveEdit(bool overwrite)
        {
            if (!_ed.Changed) { EndEdit(); return; }
            string src = _cur;
            if (overwrite)
            {
                if (!ImageIO.CanOverwrite(src)) { MessageBox.Show(this, L.T("Format berkas ini tidak bisa ditulis ulang. Gunakan Simpan Salinan (hasil disimpan sebagai ") + ImageIO.SaveExt(src).ToUpperInvariant().TrimStart('.') + ").", L.T("Foto"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (MessageBox.Show(this, L.T("Timpa berkas asli dengan hasil suntingan? Tindakan ini tidak bisa dibatalkan."), L.T("Foto"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            Bitmap copy = ImageIO.Copy32(_img); PhotoEdit ed = _ed.Clone();
            string outPath = null;
            RunBusy(L.T("Menyimpan…"), delegate { try { outPath = PhotoSaver.Save(src, copy, ed, overwrite); } finally { copy.Dispose(); } }, delegate (Exception err)
            {
                if (err != null) { Log.Write("photos save: " + err); MessageBox.Show(this, L.T("Gagal menyimpan: ") + err.Message, L.T("Foto"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                EndEdit();
                if (overwrite) { Toast(L.T("Perubahan disimpan")); LoadImage(src); }
                else
                {
                    if (!_list.Contains(outPath)) _list.Insert(_idx + 1, outPath);
                    Toast(L.T("Disimpan sebagai ") + Path.GetFileName(outPath));
                }
            });
        }

        private RectangleF EditImageRect(out float k)
        {
            Rectangle a = EditArea; k = 1;
            if (_pvCol == null) return RectangleF.Empty;
            float m = Ui.Pf(_edTab == "crop" ? 44 : 28);
            float iw, ih;
            if (_edTab == "crop") { iw = _pvCol.Width; ih = _pvCol.Height; }
            else { Rectangle cr = _ed.CropPx(_pvCol.Size); iw = cr.Width; ih = cr.Height; }
            k = Math.Min((a.Width - 2 * m) / iw, (a.Height - 2 * m) / ih);
            float w = iw * k, h = ih * k;
            return new RectangleF(a.Left + (a.Width - w) / 2f, a.Top + (a.Height - h) / 2f, w, h);
        }

        private RectangleF CropDisplay(RectangleF ir)
        {
            RectangleF c = _ed.Crop;
            return new RectangleF(ir.X + c.X * ir.Width, ir.Y + c.Y * ir.Height, c.Width * ir.Width, c.Height * ir.Height);
        }

        private void DrawEdit(Graphics g)
        {
            Rectangle a = EditArea;
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(24, 24, 25))) g.FillRectangle(bb, a);
            float k; RectangleF ir = EditImageRect(out k);
            if (_pvCol != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
                if (_edTab == "crop")
                {
                    g.DrawImage(_pvCol, ir);
                    RectangleF cr = CropDisplay(ir);
                    using (SolidBrush db = new SolidBrush(Color.FromArgb(170, 18, 18, 19)))
                    {
                        g.FillRectangle(db, ir.X, ir.Y, ir.Width, cr.Y - ir.Y);
                        g.FillRectangle(db, ir.X, cr.Bottom, ir.Width, ir.Bottom - cr.Bottom);
                        g.FillRectangle(db, ir.X, cr.Y, cr.X - ir.X, cr.Height);
                        g.FillRectangle(db, cr.Right, cr.Y, ir.Right - cr.Right, cr.Height);
                    }
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                    using (Pen gp = new Pen(Color.FromArgb(_drag != null && _drag.StartsWith("crop") ? 150 : 90, 255, 255, 255), 1))
                        for (int i = 1; i < 3; i++)
                        {
                            g.DrawLine(gp, cr.X + cr.Width * i / 3f, cr.Y, cr.X + cr.Width * i / 3f, cr.Bottom);
                            g.DrawLine(gp, cr.X, cr.Y + cr.Height * i / 3f, cr.Right, cr.Y + cr.Height * i / 3f);
                        }
                    using (Pen bp = new Pen(Color.FromArgb(230, 255, 255, 255), Ui.Pf(1.2))) g.DrawRectangle(bp, cr.X, cr.Y, cr.Width, cr.Height);
                    // pegangan kuning (sudut berbentuk L + garis tengah sisi)
                    float L_ = Ui.Pf(20), T = Ui.Pf(4.5f), o = T / 2f;
                    using (Pen hp = new Pen(CYellow, T))
                    {
                        hp.StartCap = hp.EndCap = LineCap.Square;
                        float x0 = cr.X - o, y0 = cr.Y - o, x1 = cr.Right + o, y1 = cr.Bottom + o;
                        g.DrawLines(hp, new PointF[] { new PointF(x0, y0 + L_), new PointF(x0, y0), new PointF(x0 + L_, y0) });
                        g.DrawLines(hp, new PointF[] { new PointF(x1 - L_, y0), new PointF(x1, y0), new PointF(x1, y0 + L_) });
                        g.DrawLines(hp, new PointF[] { new PointF(x0, y1 - L_), new PointF(x0, y1), new PointF(x0 + L_, y1) });
                        g.DrawLines(hp, new PointF[] { new PointF(x1 - L_, y1), new PointF(x1, y1), new PointF(x1, y1 - L_) });
                        float mx = cr.X + cr.Width / 2f, my = cr.Y + cr.Height / 2f, hl = Ui.Pf(11);
                        if (cr.Width > L_ * 3) { g.DrawLine(hp, mx - hl, y0, mx + hl, y0); g.DrawLine(hp, mx - hl, y1, mx + hl, y1); }
                        if (cr.Height > L_ * 3) { g.DrawLine(hp, x0, my - hl, x0, my + hl); g.DrawLine(hp, x1, my - hl, x1, my + hl); }
                    }
                    Rectangle px = _ed.CropPx(_img.Size.Width > 0 ? GeoSizeFull() : _pvCol.Size);
                    string dim = px.Width + " × " + px.Height;
                    RectangleF dr = new RectangleF(cr.X + cr.Width / 2f - Ui.Pf(55), Math.Min(cr.Bottom + Ui.Pf(10), a.Bottom - Ui.Pf(28)), Ui.Pf(110), Ui.Pf(22));
                    using (GraphicsPath dp = Glass.Round(dr, Ui.Pf(11))) using (SolidBrush db = new SolidBrush(Color.FromArgb(170, 20, 20, 22))) g.FillPath(db, dp);
                    Txt(g, dim, _fs, CText, dr, SfC);
                }
                else
                {
                    Rectangle cr = _ed.CropPx(_pvCol.Size);
                    g.DrawImage(_pvCol, ir, cr, GraphicsUnit.Pixel);
                }
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            DrawEditPanel(g);
        }

        // ukuran gambar resolusi penuh setelah putaran 90 derajat
        private Size GeoSizeFull() { return (_ed.Rot & 1) == 1 ? new Size(_img.Height, _img.Width) : _img.Size; }

        private static readonly string[] AdjIds = { "bright", "contrast", "sat", "warm" };
        private static readonly string[] AdjNames = { L.T("Kecerahan"), L.T("Kontras"), L.T("Saturasi"), L.T("Kehangatan") };
        private static readonly string[] AspIds = { "free", "orig", "1:1", "4:3", "3:2", "16:9", "9:16", "4:5" };

        private void DrawEditPanel(Graphics g)
        {
            Rectangle c = ContentRect;
            RectangleF pr = new RectangleF(c.Right - PanelW, c.Top, PanelW, c.Height);
            using (SolidBrush pb = new SolidBrush(CPanel)) g.FillRectangle(pb, pr);
            using (Pen lp = new Pen(CLine)) g.DrawLine(lp, pr.X, pr.Y, pr.X, pr.Bottom);
            float x = pr.X + Ui.Pf(18), w = pr.Width - Ui.Pf(36), y = pr.Y + Ui.Pf(18);
            if (_edTab == "adjust")
            {
                Head(g, L.T("Cahaya & Warna"), x, ref y, w);
                for (int i = 0; i < AdjIds.Length; i++) SliderRow(g, AdjIds[i], AdjNames[i], x, ref y, w, -1, 1, false);
                y += Ui.Pf(6);
                using (SolidBrush hb = new SolidBrush(CDim)) g.DrawString(L.T("Klik dua kali penggeser untuk mengatur ulang."), _fs, hb, new RectangleF(x, y, w, Ui.Pf(40)));
            }
            else
            {
                Head(g, L.T("Rasio Aspek"), x, ref y, w);
                string[] names = { L.T("Bebas"), L.T("Asli"), "1:1", "4:3", "3:2", "16:9", "9:16", "4:5" };
                float cw = (w - Ui.Pf(8)) / 2f, ch = Ui.Pf(28);
                for (int i = 0; i < AspIds.Length; i++)
                {
                    RectangleF r = new RectangleF(x + (i % 2) * (cw + Ui.Pf(8)), y + (i / 2) * (ch + Ui.Pf(7)), cw, ch);
                    bool sel = _ed.Aspect == AspIds[i], hov = _hover == "asp:" + AspIds[i];
                    using (GraphicsPath gp = Glass.Round(r, Ui.Pf(7)))
                    {
                        using (SolidBrush b = new SolidBrush(sel ? Color.FromArgb(70, 255, 204, 0) : hov ? Color.FromArgb(76, 76, 82) : CBtn)) g.FillPath(b, gp);
                        if (sel) using (Pen sp = new Pen(CYellow, Ui.Pf(1.3))) g.DrawPath(sp, gp);
                    }
                    Txt(g, names[i], sel ? _fb : _f, sel ? CYellow : CText, r, SfC);
                    Hit("asp:" + AspIds[i], r);
                }
                y += 4 * (ch + Ui.Pf(7)) + Ui.Pf(14);
                Head(g, L.T("Putar & Balik"), x, ref y, w);
                string[] ids = { "rotL", "rotR", "flipH", "flipV" };
                string[] sy = { "⟲", "⟳", "⇆", "⇅" };
                string[] tips = { L.T("Putar kiri"), L.T("Putar kanan"), L.T("Balik horizontal"), L.T("Balik vertikal") };
                float bw = (w - 3 * Ui.Pf(8)) / 4f, bh = Ui.Pf(40);
                for (int i = 0; i < 4; i++)
                {
                    RectangleF r = new RectangleF(x + i * (bw + Ui.Pf(8)), y, bw, bh);
                    using (GraphicsPath gp = Glass.Round(r, Ui.Pf(8))) using (SolidBrush b = new SolidBrush(_hover == ids[i] ? Color.FromArgb(80, 80, 86) : CBtn)) g.FillPath(b, gp);
                    Txt(g, sy[i], _sym, CText, r, SfC);
                    Hit(ids[i], r);
                }
                string tip = null; for (int i = 0; i < 4; i++) if (_hover == ids[i]) tip = tips[i];
                y += bh + Ui.Pf(6);
                if (tip != null) Txt(g, tip, _fs, CDim, new RectangleF(x, y, w, Ui.Pf(16)), SfC);
                y += Ui.Pf(24);
                Head(g, L.T("Luruskan"), x, ref y, w);
                SliderRow(g, "straight", null, x, ref y, w, -45, 45, true);
            }
        }

        private void Head(Graphics g, string t, float x, ref float y, float w)
        {
            using (SolidBrush b = new SolidBrush(CText)) g.DrawString(t, _fb, b, x, y);
            y += Ui.Pf(28);
        }

        private float GetVal(string id)
        {
            switch (id)
            {
                case "bright": return _ed.Bright;
                case "contrast": return _ed.Contrast;
                case "sat": return _ed.Sat;
                case "warm": return _ed.Warm;
                case "straight": return _ed.Straight;
            }
            return 0;
        }

        private void SetVal(string id, float v)
        {
            switch (id)
            {
                case "bright": _ed.Bright = v; break;
                case "contrast": _ed.Contrast = v; break;
                case "sat": _ed.Sat = v; break;
                case "warm": _ed.Warm = v; break;
                case "straight": _ed.Straight = v; RebuildGeo(); return;
            }
            RebuildColor();
        }

        private void SliderRow(Graphics g, string id, string label, float x, ref float y, float w, float min, float max, bool deg)
        {
            float v = GetVal(id);
            if (label != null)
            {
                using (SolidBrush b = new SolidBrush(CText)) g.DrawString(label, _f, b, x, y);
                y += Ui.Pf(20);
            }
            string vs = deg ? v.ToString("0.0", L.Culture) + "°" : ((int)Math.Round(v * 100)).ToString();
            RectangleF sr = new RectangleF(x, y, w - Ui.Pf(46), Ui.Pf(22));
            Txt(g, vs, _f, Math.Abs(v) > 0.001f ? CYellow : CDim, new RectangleF(sr.Right + Ui.Pf(4), y, Ui.Pf(42), sr.Height), SfR);
            float cy = sr.Y + sr.Height / 2f;
            RectangleF tr = new RectangleF(sr.X, cy - Ui.Pf(2), sr.Width, Ui.Pf(4));
            using (GraphicsPath tp = Glass.Round(tr, Ui.Pf(2))) using (SolidBrush tb = new SolidBrush(Color.FromArgb(78, 78, 84))) g.FillPath(tb, tp);
            float zx = sr.X + sr.Width * (0 - min) / (max - min), vx = sr.X + sr.Width * (v - min) / (max - min);
            using (SolidBrush fb = new SolidBrush(CYellow)) g.FillRectangle(fb, Math.Min(zx, vx), tr.Y, Math.Abs(vx - zx), tr.Height);
            using (Pen zp = new Pen(Color.FromArgb(120, 255, 255, 255), 1)) g.DrawLine(zp, zx, cy - Ui.Pf(5), zx, cy + Ui.Pf(5));
            float kd = Ui.Pf(_drag == "sl:" + id ? 18 : 16);
            RectangleF kr = new RectangleF(vx - kd / 2f, cy - kd / 2f, kd, kd);
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(sh, kr.X, kr.Y + Ui.Pf(1), kr.Width, kr.Height);
            using (SolidBrush kb = new SolidBrush(Color.FromArgb(245, 245, 247))) g.FillEllipse(kb, kr);
            RectangleF hr = sr; hr.Inflate(Ui.Pf(8), Ui.Pf(4));
            Hit("sl:" + id, hr);
            _sliderRects[id] = sr; _sliderRange[id] = new PointF(min, max);
            y += sr.Height + Ui.Pf(16);
        }
        private readonly Dictionary<string, RectangleF> _sliderRects = new Dictionary<string, RectangleF>();
        private readonly Dictionary<string, PointF> _sliderRange = new Dictionary<string, PointF>();

        private void DragSlider(string id, int mx)
        {
            RectangleF sr; PointF rg;
            if (!_sliderRects.TryGetValue(id, out sr) || !_sliderRange.TryGetValue(id, out rg)) return;
            float t = Math.Max(0, Math.Min(1, (mx - sr.X) / sr.Width));
            float v = rg.X + t * (rg.Y - rg.X);
            if (Math.Abs(v) < (rg.Y - rg.X) * 0.012f) v = 0;   // magnet ke nol
            SetVal(id, v);
        }

        private void ApplyAspect(string id)
        {
            if (_pvGeo == null) return;
            if (id == "free") { _ed.Aspect = "free"; _ed.AspectR = 0; }
            else _ed.SetAspect(id, _pvGeo.Width, _pvGeo.Height);
            Invalidate();
        }

        private string CropHandle(Point p)
        {
            if (!_editing || _edTab != "crop" || _pvCol == null) return null;
            float k; RectangleF ir = EditImageRect(out k);
            RectangleF cr = CropDisplay(ir);
            float t = Ui.Pf(16);
            bool l = Math.Abs(p.X - cr.Left) < t, r = Math.Abs(p.X - cr.Right) < t, tp = Math.Abs(p.Y - cr.Top) < t, b = Math.Abs(p.Y - cr.Bottom) < t;
            bool inX = p.X > cr.Left - t && p.X < cr.Right + t, inY = p.Y > cr.Top - t && p.Y < cr.Bottom + t;
            if (tp && l) return "nw"; if (tp && r) return "ne"; if (b && l) return "sw"; if (b && r) return "se";
            if (tp && inX) return "n"; if (b && inX) return "s"; if (l && inY) return "w"; if (r && inY) return "e";
            if (cr.Contains(p)) return "move";
            return null;
        }

        private static Cursor CropCursor(string h)
        {
            switch (h)
            {
                case "nw": case "se": return Cursors.SizeNWSE;
                case "ne": case "sw": return Cursors.SizeNESW;
                case "n": case "s": return Cursors.SizeNS;
                case "w": case "e": return Cursors.SizeWE;
                case "move": return Cursors.SizeAll;
            }
            return Cursors.Default;
        }

        private void CropDrag(string h, Point pt)
        {
            float k; RectangleF ir = EditImageRect(out k);
            float W = _pvGeo.Width, H = _pvGeo.Height, sc = ir.Width / W;
            float mx = (pt.X - ir.X) / sc, my = (pt.Y - ir.Y) / sc;
            RectangleF c0 = new RectangleF(_cropStart.X * W, _cropStart.Y * H, _cropStart.Width * W, _cropStart.Height * H);
            float r = _ed.AspectR, min = Math.Min(Math.Min(W, H), Ui.Pf(40) / sc);
            RectangleF c = c0;
            if (h == "move")
            {
                float dx = mx - _cropDown.X, dy = my - _cropDown.Y;
                c.X = Math.Max(0, Math.Min(W - c0.Width, c0.X + dx)); c.Y = Math.Max(0, Math.Min(H - c0.Height, c0.Y + dy));
            }
            else if (h.Length == 2)
            {
                int dirx = h[1] == 'w' ? -1 : 1, diry = h[0] == 'n' ? -1 : 1;
                float ax = dirx < 0 ? c0.Right : c0.X, ay = diry < 0 ? c0.Bottom : c0.Y;
                float px = Math.Max(0, Math.Min(W, mx)), py = Math.Max(0, Math.Min(H, my));
                float maxW = dirx > 0 ? W - ax : ax, maxH = diry > 0 ? H - ay : ay;
                float w = Math.Min(maxW, Math.Max(min, (px - ax) * dirx)), hh = Math.Min(maxH, Math.Max(min, (py - ay) * diry));
                if (r > 0)
                {
                    if (w / hh > r) w = hh * r; else hh = w / r;
                    if (w > maxW) { w = maxW; hh = w / r; }
                    if (hh > maxH) { hh = maxH; w = hh * r; }
                }
                c = new RectangleF(dirx > 0 ? ax : ax - w, diry > 0 ? ay : ay - hh, w, hh);
            }
            else if (h == "w" || h == "e")
            {
                float x, w;
                if (h == "w") { x = Math.Max(0, Math.Min(c0.Right - min, mx)); w = c0.Right - x; }
                else { float right = Math.Max(c0.X + min, Math.Min(W, mx)); x = c0.X; w = right - x; }
                if (r > 0)
                {
                    float hh = w / r;
                    if (hh > H) { hh = H; w = hh * r; if (h == "w") x = c0.Right - w; }
                    float y = Math.Max(0, Math.Min(H - hh, c0.Y + c0.Height / 2f - hh / 2f));
                    c = new RectangleF(x, y, w, hh);
                }
                else c = new RectangleF(x, c0.Y, w, c0.Height);
            }
            else
            {
                float y, hh;
                if (h == "n") { y = Math.Max(0, Math.Min(c0.Bottom - min, my)); hh = c0.Bottom - y; }
                else { float bot = Math.Max(c0.Y + min, Math.Min(H, my)); y = c0.Y; hh = bot - y; }
                if (r > 0)
                {
                    float w = hh * r;
                    if (w > W) { w = W; hh = w / r; if (h == "n") y = c0.Bottom - hh; }
                    float x = Math.Max(0, Math.Min(W - w, c0.X + c0.Width / 2f - w / 2f));
                    c = new RectangleF(x, y, w, hh);
                }
                else c = new RectangleF(c0.X, y, c0.Width, hh);
            }
            _ed.Crop = new RectangleF(c.X / W, c.Y / H, c.Width / W, c.Height / H);
            Invalidate();
        }

        // ============================================================
        //  VIDEO
        // ============================================================
        private void EnsureHost()
        {
            if (_host != null) return;
            _me = new WC.MediaElement();
            _me.LoadedBehavior = WC.MediaState.Manual; _me.UnloadedBehavior = WC.MediaState.Manual;
            _me.ScrubbingEnabled = true; _me.Stretch = WM.Stretch.Uniform; _me.Focusable = false;
            _me.MediaOpened += delegate { OnMediaOpened(); };
            _me.MediaEnded += delegate { OnMediaEnded(); };
            _me.MediaFailed += delegate (object s, WF.ExceptionRoutedEventArgs e) { OnMediaFailed(e.ErrorException); };
            _note = new WC.TextBlock();
            _note.Foreground = new WM.SolidColorBrush(WM.Color.FromRgb(160, 160, 168));
            _note.FontSize = 15; _note.TextAlignment = System.Windows.TextAlignment.Center; _note.TextWrapping = System.Windows.TextWrapping.Wrap;
            _note.HorizontalAlignment = WF.HorizontalAlignment.Center; _note.VerticalAlignment = WF.VerticalAlignment.Center;
            _note.Visibility = System.Windows.Visibility.Collapsed; _note.IsHitTestVisible = false;
            WC.Grid grid = new WC.Grid();
            grid.Background = new WM.SolidColorBrush(WM.Color.FromRgb(16, 16, 17)); grid.Focusable = false;
            grid.Children.Add(_me); grid.Children.Add(_note);
            grid.MouseLeftButtonDown += delegate (object s, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.ClickCount >= 2) { if (_clickT != null) _clickT.Stop(); ToggleFull(); }
                else
                {
                    if (_clickT == null) { _clickT = new System.Windows.Forms.Timer(); _clickT.Interval = SystemInformation.DoubleClickTime; _clickT.Tick += delegate { _clickT.Stop(); TogglePlay(); }; }
                    _clickT.Stop(); _clickT.Start();
                }
                e.Handled = true;
                BeginInvoke((MethodInvoker)delegate { ActiveControl = null; Focus(); });
            };
            _host = new ElementHost();
            _host.TabStop = false; _host.BackColor = Color.FromArgb(16, 16, 17);
            _host.Child = grid;
            _host.Visible = false;
            Controls.Add(_host);
        }

        private static Uri FileUri(string p)
        {
            UriBuilder ub = new UriBuilder("file", "");
            ub.Path = p;
            return ub.Uri;
        }

        private void OpenVideo(string p)
        {
            EnsureHost();
            _vidReady = false; _dur = 0; _playing = false;
            _vidGen++; _proxyFor = null;
            ClearStrip();
            _note.Visibility = System.Windows.Visibility.Collapsed;
            _me.Volume = _vol; _me.IsMuted = _muted;
            try { _me.Source = FileUri(p); _me.Play(); _playing = true; }
            catch (Exception ex) { OnMediaFailed(ex); }
        }

        private void OnMediaOpened()
        {
            _vidReady = true;
            _dur = _me.NaturalDuration.HasTimeSpan ? _me.NaturalDuration.TimeSpan.TotalSeconds : 0;
            _trimA = 0; _trimB = _dur;
            if (!_me.HasVideo) { _note.Text = "♫\n" + Path.GetFileName(_cur); _note.FontSize = 18; _note.Visibility = System.Windows.Visibility.Visible; }
            Invalidate();
        }

        private void OnMediaEnded()
        {
            if (_trimming) { _me.Position = TimeSpan.FromSeconds(_trimA); _me.Play(); return; }
            _me.Pause(); _playing = false; Invalidate();
        }

        private void OnMediaFailed(Exception ex)
        {
            _vidReady = false; _playing = false;
            Log.Write("photos media: " + (ex != null ? ex.Message : "?"));
            // codec tidak didukung pemutar WPF (mis. VP9/AV1/webm): putar salinan proksi H.264 buatan ffmpeg
            if (_mode == Mode.Video && _cur != null && _proxyFor != _cur && VideoTools.Ffmpeg != null) { StartProxy(_cur); return; }
            _note.Text = L.T("Video ini tidak bisa diputar.") + "\n" + L.T("Codec mungkin belum terpasang (mis. HEVC/AV1 dari Microsoft Store).") + (ex != null ? "\n\n" + ex.Message : "");
            _note.FontSize = 14; _note.Visibility = System.Windows.Visibility.Visible;
            Invalidate();
        }

        private int _vidGen; private string _proxyFor;

        private void StartProxy(string src)
        {
            _proxyFor = src; int gen = _vidGen;
            _note.Text = L.T("Menyiapkan video untuk diputar…"); _note.FontSize = 15; _note.Visibility = System.Windows.Visibility.Visible;
            Thread t = new Thread(delegate ()
            {
                string err;
                string px = VideoTools.Proxy(src, delegate (double pct)
                {
                    try { BeginInvoke((MethodInvoker)delegate { if (gen == _vidGen) _note.Text = L.T("Menyiapkan video untuk diputar…") + " " + (int)(pct * 100) + "%"; }); } catch { }
                }, delegate { return gen != _vidGen || _closed; }, out err);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (gen != _vidGen || IsDisposed) return;
                        if (px == null) { OnMediaFailed(new Exception(err ?? "ffmpeg")); return; }
                        _note.Visibility = System.Windows.Visibility.Collapsed;
                        _me.Source = FileUri(px); _me.Play(); _playing = true;
                    });
                }
                catch { }
            });
            t.IsBackground = true; t.Start();
        }

        private void StopVideo()
        {
            _vidGen++;
            if (_me == null) return;
            try { _me.Stop(); _me.Source = null; } catch { }
            _playing = false; _vidReady = false; _trimming = false;
            if (_host != null) _host.Visible = false;
            ClearStrip();
        }

        private void TogglePlay()
        {
            if (_me == null || !_vidReady) return;
            if (_playing) { _me.Pause(); _playing = false; }
            else
            {
                double pos = _me.Position.TotalSeconds;
                if (_trimming && (pos < _trimA || pos >= _trimB - 0.05)) _me.Position = TimeSpan.FromSeconds(_trimA);
                else if (!_trimming && _dur > 0 && pos >= _dur - 0.05) _me.Position = TimeSpan.Zero;
                _me.Play(); _playing = true;
            }
            Invalidate();
        }

        private void Seek(double s)
        {
            if (_me == null || !_vidReady) return;
            double lo = _trimming ? _trimA : 0, hi = _trimming ? _trimB : _dur;
            s = Math.Max(lo, Math.Min(Math.Max(lo, hi - 0.01), s));
            _me.Position = TimeSpan.FromSeconds(s);
            Invalidate();
        }

        private void SetVolume(float v)
        {
            _vol = Math.Max(0, Math.Min(1, v)); _muted = false;
            if (_me != null) { _me.Volume = _vol; _me.IsMuted = false; }
            Invalidate();
        }

        private void ToggleFull()
        {
            if (!_full)
            {
                _preFullState = WindowState; _preFullBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                _full = true;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                _full = false;
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = _preFullBounds;
                if (_preFullState == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            }
            _cacheSc = -1;
            LayoutHost(); Invalidate();
        }

        private void BeginTrim()
        {
            if (!_vidReady || _dur < 0.6) return;
            _trimming = true; _trimA = 0; _trimB = _dur;
            if (_playing) { _me.Pause(); _playing = false; }
            LayoutHost();
            BuildStrip();
            Invalidate();
        }

        private void EndTrim()
        {
            _trimming = false; _drag = null;
            LayoutHost(); Invalidate();
        }

        private void SaveTrim()
        {
            if (_trimB - _trimA < 0.2) return;
            if (_playing) TogglePlay();
            string src = _cur; double a = _trimA, b = _trimB;
            if (a < 0.05 && b > _dur - 0.05) { Toast(L.T("Tidak ada yang dipangkas")); EndTrim(); return; }
            string dst = null, err = null;
            RunBusy(L.T("Mengekspor…"), delegate { err = VideoTools.Trim(src, a, b, Environment.GetEnvironmentVariable("LM_PHOTOS_WINRT") == "1", out dst); }, delegate (Exception ex)
            {
                if (ex != null) err = ex.Message;
                if (err != null) { MessageBox.Show(this, L.T("Gagal memangkas video: ") + err, L.T("Foto"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                EndTrim();
                if (dst != null && !_list.Contains(dst)) _list.Insert(_idx + 1, dst);
                Toast(L.T("Disimpan sebagai ") + Path.GetFileName(dst));
            });
        }

        private void ClearStrip()
        {
            _stripGen++;
            if (_strip != null) foreach (Bitmap b in _strip) if (b != null) b.Dispose();
            _strip = null;
        }

        private RectangleF StripRect
        {
            get
            {
                Rectangle b = VBarRect;
                return new RectangleF(b.Left + Ui.Pf(64), b.Top + Ui.Pf(14), b.Width - Ui.Pf(64) - Ui.Pf(20), Ui.Pf(50));
            }
        }

        private void BuildStrip()
        {
            ClearStrip();
            int gen = _stripGen;
            RectangleF sr = StripRect;
            double ar = 16.0 / 9;
            try { if (_me.NaturalVideoHeight > 0) ar = (double)_me.NaturalVideoWidth / _me.NaturalVideoHeight; } catch { }
            if (!_me.HasVideo) return;
            int n = Math.Max(4, Math.Min(24, (int)Math.Ceiling(sr.Width / (sr.Height * ar))));
            _strip = new Bitmap[n];
            int h = (int)sr.Height; string src = _cur; double dur = _dur;
            string ff = VideoTools.Ffmpeg;
            if (ff != null)
            {
                Thread t = new Thread(delegate ()
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (gen != _stripGen || _closed) return;
                        Bitmap f = VideoTools.Frame(ff, src, dur * (i + 0.5) / n, h);
                        int ii = i;
                        try { BeginInvoke((MethodInvoker)delegate { if (gen == _stripGen && _strip != null && ii < _strip.Length) { _strip[ii] = f; Invalidate(VBarRect); } else if (f != null) f.Dispose(); }); } catch { }
                    }
                });
                t.IsBackground = true; t.Start();
                return;
            }
            // tanpa ffmpeg: ambil bingkai lewat WPF MediaPlayer tersembunyi
            WM.MediaPlayer mp = new WM.MediaPlayer();
            mp.ScrubbingEnabled = true; mp.IsMuted = true;
            int idx = -1;
            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer(); tm.Interval = 280;
            mp.MediaOpened += delegate { mp.Pause(); idx = 0; mp.Position = TimeSpan.FromSeconds(dur * 0.5 / n); tm.Start(); };
            mp.MediaFailed += delegate { tm.Stop(); try { mp.Close(); } catch { } };
            tm.Tick += delegate
            {
                if (gen != _stripGen || _strip == null || idx >= n || IsDisposed) { tm.Stop(); tm.Dispose(); try { mp.Close(); } catch { } return; }
                try
                {
                    int w = Math.Max(1, (int)(h * ar));
                    WM.DrawingVisual dv = new WM.DrawingVisual();
                    using (WM.DrawingContext dc = dv.RenderOpen()) dc.DrawVideo(mp, new System.Windows.Rect(0, 0, w, h));
                    WMI.RenderTargetBitmap rtb = new WMI.RenderTargetBitmap(w, h, 96, 96, WM.PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    _strip[idx] = ImageIO.FromSource(rtb);
                }
                catch (Exception ex) { Log.Write("photos strip: " + ex.Message); }
                idx++;
                if (idx < n) mp.Position = TimeSpan.FromSeconds(dur * (idx + 0.5) / n);
                Invalidate(VBarRect);
            };
            try { mp.Open(FileUri(src)); } catch { }
        }

        private void DrawVideoBar(Graphics g)
        {
            Rectangle b = VBarRect;
            using (SolidBrush bb = new SolidBrush(CTool)) g.FillRectangle(bb, b);
            using (Pen lp = new Pen(CLine)) g.DrawLine(lp, b.Left, b.Top, b.Right, b.Top);
            double pos = 0;
            try { if (_me != null && _vidReady) pos = _me.Position.TotalSeconds; } catch { }
            float cy = _trimming ? StripRect.Y + StripRect.Height / 2f : b.Top + b.Height / 2f;
            RectangleF pr = new RectangleF(b.Left + Ui.Pf(14), cy - Ui.Pf(18), Ui.Pf(36), Ui.Pf(36));
            if (_hover == "v_play") using (SolidBrush hb = new SolidBrush(Color.FromArgb(36, 255, 255, 255))) g.FillEllipse(hb, pr);
            using (SolidBrush wb = new SolidBrush(_vidReady ? CText : CDim)) Glyphs.Draw(g, _playing ? "" : "", _glB, wb, pr);
            Hit("v_play", pr);
            if (_trimming) { DrawTrimStrip(g, pos); return; }

            float x = pr.Right + Ui.Pf(8);
            float right = b.Right - Ui.Pf(12);
            RectangleF fr = new RectangleF(right - Ui.Pf(32), cy - Ui.Pf(16), Ui.Pf(32), Ui.Pf(32));
            IconAt(g, "v_full", _full ? "" : "", fr, true);
            RectangleF vs = new RectangleF(fr.X - Ui.Pf(10) - Ui.Pf(80), cy - Ui.Pf(10), Ui.Pf(80), Ui.Pf(20));
            float vv = _muted ? 0 : _vol;
            RectangleF vt = new RectangleF(vs.X, cy - Ui.Pf(2), vs.Width, Ui.Pf(4));
            using (GraphicsPath tp = Glass.Round(vt, Ui.Pf(2))) using (SolidBrush tb = new SolidBrush(Color.FromArgb(80, 80, 86))) g.FillPath(tb, tp);
            using (SolidBrush fb = new SolidBrush(CText)) g.FillRectangle(fb, vt.X, vt.Y, vt.Width * vv, vt.Height);
            float kd = Ui.Pf(12);
            using (SolidBrush kb = new SolidBrush(Color.White)) g.FillEllipse(kb, vt.X + vt.Width * vv - kd / 2f, cy - kd / 2f, kd, kd);
            RectangleF vh = vs; vh.Inflate(Ui.Pf(6), Ui.Pf(4)); Hit("v_vol", vh);
            _sliderRects["v_vol"] = vs;
            RectangleF mr = new RectangleF(vs.X - Ui.Pf(36), cy - Ui.Pf(16), Ui.Pf(32), Ui.Pf(32));
            IconAt(g, "v_mute", Glyphs.VolFor(_vol, _muted), mr, true);
            // waktu + penggeser
            float tw = Ui.Pf(58);
            Txt(g, PhotosApp.Time(pos, false), _fs, CText, new RectangleF(x, cy - Ui.Pf(10), tw, Ui.Pf(20)), SfC);
            RectangleF rem = new RectangleF(mr.X - Ui.Pf(8) - tw, cy - Ui.Pf(10), tw, Ui.Pf(20));
            Txt(g, "-" + PhotosApp.Time(Math.Max(0, _dur - pos), false), _fs, CDim, rem, SfC);
            RectangleF sr = new RectangleF(x + tw + Ui.Pf(6), cy - Ui.Pf(10), rem.X - Ui.Pf(6) - (x + tw + Ui.Pf(6)), Ui.Pf(20));
            if (sr.Width > Ui.Pf(30))
            {
                float frac = _dur > 0 ? (float)Math.Max(0, Math.Min(1, pos / _dur)) : 0;
                bool act = _hover == "v_scrub" || _drag == "v_scrub";
                RectangleF st = new RectangleF(sr.X, cy - Ui.Pf(act ? 3 : 2), sr.Width, Ui.Pf(act ? 6 : 4));
                using (GraphicsPath tp = Glass.Round(st, st.Height / 2f)) using (SolidBrush tb = new SolidBrush(Color.FromArgb(80, 80, 86))) g.FillPath(tb, tp);
                if (frac > 0) using (GraphicsPath tp = Glass.Round(new RectangleF(st.X, st.Y, Math.Max(st.Height, st.Width * frac), st.Height), st.Height / 2f)) using (SolidBrush fb = new SolidBrush(CText)) g.FillPath(fb, tp);
                if (act) using (SolidBrush kb = new SolidBrush(Color.White)) g.FillEllipse(kb, st.X + st.Width * frac - Ui.Pf(7), cy - Ui.Pf(7), Ui.Pf(14), Ui.Pf(14));
                RectangleF sh = sr; sh.Inflate(Ui.Pf(4), Ui.Pf(4)); Hit("v_scrub", sh);
                _sliderRects["v_scrub"] = sr;
            }
        }

        private void IconAt(Graphics g, string id, string glyph, RectangleF r, bool enabled)
        {
            if (_hover == id && enabled) using (GraphicsPath gp = Glass.Round(r, Ui.Pf(7))) using (SolidBrush hb = new SolidBrush(Color.FromArgb(36, 255, 255, 255))) g.FillPath(hb, gp);
            using (SolidBrush b = new SolidBrush(enabled ? CText : Color.FromArgb(90, 90, 96))) Glyphs.Draw(g, glyph, _gl, b, r);
            if (enabled) Hit(id, r);
        }

        private void DrawTrimStrip(Graphics g, double pos)
        {
            RectangleF sr = StripRect;
            using (GraphicsPath bp = Glass.Round(sr, Ui.Pf(6))) using (SolidBrush b = new SolidBrush(Color.FromArgb(54, 54, 58))) g.FillPath(b, bp);
            g.SetClip(sr);
            if (_strip != null)
            {
                float cw = sr.Width / _strip.Length;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                for (int i = 0; i < _strip.Length; i++)
                {
                    Bitmap f = _strip[i]; if (f == null) continue;
                    RectangleF cr = new RectangleF(sr.X + i * cw, sr.Y, cw + 1, sr.Height);
                    float s = Math.Max(cr.Width / f.Width, cr.Height / f.Height);
                    float w = cr.Width / s, h = cr.Height / s;
                    g.DrawImage(f, cr, new RectangleF((f.Width - w) / 2f, (f.Height - h) / 2f, w, h), GraphicsUnit.Pixel);
                }
            }
            g.ResetClip();
            float xa = TX(_trimA), xb = TX(_trimB), hw = Ui.Pf(13);
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(170, 20, 20, 22)))
            {
                if (xa > sr.X) g.FillRectangle(dim, sr.X, sr.Y, xa - sr.X, sr.Height);
                if (xb < sr.Right) g.FillRectangle(dim, xb, sr.Y, sr.Right - xb, sr.Height);
            }
            // bingkai kuning + pegangan
            float bt = Ui.Pf(3);
            RectangleF fr = new RectangleF(xa, sr.Y - bt, xb - xa, sr.Height + 2 * bt);
            using (SolidBrush yb = new SolidBrush(CYellow))
            {
                g.FillRectangle(yb, fr.X + hw - 1, fr.Y, fr.Width - 2 * hw + 2, bt);
                g.FillRectangle(yb, fr.X + hw - 1, fr.Bottom - bt, fr.Width - 2 * hw + 2, bt);
                using (GraphicsPath lp = RoundSide(new RectangleF(fr.X, fr.Y, hw, fr.Height), Ui.Pf(5), true)) g.FillPath(yb, lp);
                using (GraphicsPath rp = RoundSide(new RectangleF(fr.Right - hw, fr.Y, hw, fr.Height), Ui.Pf(5), false)) g.FillPath(yb, rp);
            }
            using (Pen cp = new Pen(Color.FromArgb(120, 70, 50, 0), Ui.Pf(2)))
            {
                cp.StartCap = cp.EndCap = LineCap.Round;
                float my = fr.Y + fr.Height / 2f, d = Ui.Pf(4);
                g.DrawLines(cp, new PointF[] { new PointF(fr.X + hw / 2f + d / 2f, my - d * 1.4f), new PointF(fr.X + hw / 2f - d / 2f, my), new PointF(fr.X + hw / 2f + d / 2f, my + d * 1.4f) });
                g.DrawLines(cp, new PointF[] { new PointF(fr.Right - hw / 2f - d / 2f, my - d * 1.4f), new PointF(fr.Right - hw / 2f + d / 2f, my), new PointF(fr.Right - hw / 2f - d / 2f, my + d * 1.4f) });
            }
            // kepala putar
            if (_vidReady)
            {
                float px = TX(Math.Max(_trimA, Math.Min(_trimB, pos)));
                px = Math.Max(fr.X + hw, Math.Min(fr.Right - hw, px));
                using (GraphicsPath pp = Glass.Round(new RectangleF(px - Ui.Pf(1.5f), sr.Y - Ui.Pf(6), Ui.Pf(3), sr.Height + Ui.Pf(12)), Ui.Pf(1.5f))) using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, pp);
            }
            RectangleF hit = sr; hit.Inflate(Ui.Pf(8), Ui.Pf(8)); Hit("v_strip", hit);
            float ly = sr.Bottom + Ui.Pf(8);
            Txt(g, PhotosApp.Time(_trimA, true), _fs, CDim, new RectangleF(sr.X, ly, Ui.Pf(80), Ui.Pf(18)), SfL);
            Txt(g, PhotosApp.Time(_trimB, true), _fs, CDim, new RectangleF(sr.Right - Ui.Pf(80), ly, Ui.Pf(80), Ui.Pf(18)), SfR);
            Txt(g, L.T("Durasi ") + PhotosApp.Time(_trimB - _trimA, true), _fsb, CYellow, new RectangleF(sr.X, ly, sr.Width, Ui.Pf(18)), SfC);
        }

        private static GraphicsPath RoundSide(RectangleF r, float rad, bool left)
        {
            GraphicsPath p = new GraphicsPath(); float d = rad * 2;
            if (left)
            {
                p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddLine(r.X + rad, r.Y, r.Right, r.Y); p.AddLine(r.Right, r.Y, r.Right, r.Bottom);
                p.AddLine(r.Right, r.Bottom, r.X + rad, r.Bottom); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            }
            else
            {
                p.AddLine(r.X, r.Y, r.Right - rad, r.Y); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddLine(r.Right - rad, r.Bottom, r.X, r.Bottom);
            }
            p.CloseFigure(); return p;
        }

        private float TX(double t) { RectangleF sr = StripRect; return sr.X + (float)(_dur > 0 ? t / _dur : 0) * sr.Width; }
        private double XT(float x) { RectangleF sr = StripRect; return Math.Max(0, Math.Min(1, (x - sr.X) / sr.Width)) * _dur; }

        // ============================================================
        //  MENGGAMBAR
        // ============================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            _hits.Clear();
            int W = ClientSize.Width, H = ClientSize.Height;
            using (SolidBrush b = new SolidBrush(_full ? Color.Black : CBg)) g.FillRectangle(b, 0, 0, W, H);
            if (_mode == Mode.Library) DrawLibrary(g);
            else if (_mode == Mode.Image) { if (_editing) DrawEdit(g); else DrawImageView(g); }
            else if (_mode == Mode.Video) DrawVideoBar(g);
            if (!_full) DrawTop(g);
            if (_toast != null)
            {
                double age = (DateTime.Now - _toastAt).TotalMilliseconds;
                int al = age > 2200 ? (int)Math.Max(0, 255 * (1 - (age - 2200) / 400)) : 255;
                Rectangle c = _mode == Mode.Image && _editing ? EditArea : ContentRect;
                SizeF ts = g.MeasureString(_toast, _fb);
                RectangleF tr = new RectangleF(c.Left + (c.Width - ts.Width) / 2f - Ui.Pf(16), c.Top + Ui.Pf(16), ts.Width + Ui.Pf(32), Ui.Pf(34));
                using (GraphicsPath tp = Glass.Round(tr, Ui.Pf(17))) using (SolidBrush tb = new SolidBrush(Color.FromArgb(al * 230 / 255, 52, 52, 56))) g.FillPath(tb, tp);
                Txt(g, _toast, _fb, Color.FromArgb(al, CText), tr, SfC);
            }
        }

        private void DrawTop(Graphics g)
        {
            int p = Pad, W = ClientSize.Width;
            Rectangle t = new Rectangle(p, p, W - 2 * p, TopH);
            using (SolidBrush b = new SolidBrush(CTool)) g.FillRectangle(b, t);
            using (Pen ln = new Pen(CLine)) g.DrawLine(ln, t.Left, t.Bottom - 1, t.Right, t.Bottom - 1);
            // traffic light (digambar sendiri seperti Finder)
            Color[] on = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
            string[] gly = { "", "", WindowState == FormWindowState.Maximized ? "" : "" };
            bool act = Form.ActiveForm == this || _hoverLight >= 0;
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = LightRect(i);
                using (SolidBrush lb = new SolidBrush(act ? on[i] : Color.FromArgb(80, 80, 88))) g.FillEllipse(lb, r);
                if (_hoverLight >= 0) using (Font f = Glyphs.Font(Ui.Pf(8))) using (SolidBrush gb = new SolidBrush(Color.FromArgb(190, 60, 20, 10))) Glyphs.Draw(g, gly[i], f, gb, r);
            }
            float x = LightRect(2).Right + Ui.Pf(20), cy = t.Top + TopH / 2f, xr = t.Right - Ui.Pf(14);
            string title = null, sub = null;
            bool busy = _busyMsg != null;
            if (_mode == Mode.Library)
            {
                title = L.T("Foto");
                xr = Seg(g, "flt:", new string[] { "all", "img", "vid" }, new string[] { L.T("Semua"), L.T("Foto"), L.T("Video") }, _libFilter, xr, cy, false);
            }
            else if (_editing)
            {
                x = Pill(g, "ed_cancel", L.T("Batal"), x, cy, 0, !busy, false).Right + Ui.Pf(8);
                xr = Pill(g, "ed_save", L.T("Simpan Salinan"), xr, cy, 1, !busy, true).X - Ui.Pf(8);
                xr = Pill(g, "ed_over", L.T("Timpa"), xr, cy, 0, !busy && _ed.Changed, true).X - Ui.Pf(8);
                xr = Pill(g, "ed_revert", L.T("Kembalikan"), xr, cy, 2, !busy && _ed.Changed, true).X - Ui.Pf(8);
                Seg(g, "tab:", new string[] { "adjust", "crop" }, new string[] { L.T("Sesuaikan"), L.T("Pangkas") }, _edTab, t.Left + t.Width / 2f, cy, true);
            }
            else if (_trimming)
            {
                x = Pill(g, "tr_cancel", L.T("Batal"), x, cy, 0, !busy, false).Right + Ui.Pf(8);
                xr = Pill(g, "tr_save", L.T("Simpan"), xr, cy, 1, !busy && _trimB - _trimA >= 0.2, true).X - Ui.Pf(8);
                title = L.T("Pangkas Video"); sub = Path.GetFileName(_cur);
            }
            else
            {
                if (_hasLib) { x = IconBtn(g, "lib", "", x, cy, true) + Ui.Pf(2); }
                x = IconBtn(g, "prev", "", x, cy, _idx > 0);
                x = IconBtn(g, "next", "", x, cy, _idx < _list.Count - 1) + Ui.Pf(6);
                if (_mode == Mode.Image)
                {
                    xr = Pill(g, "edit", L.T("Edit"), xr, cy, 0, _img != null && !busy, true).X - Ui.Pf(10);
                    xr = IconBtnR(g, "zin", "", xr, cy, _img != null);
                    xr = IconBtnR(g, "zout", "", xr, cy, _img != null);
                    xr = IconBtnR(g, "fullscr", "", xr, cy, true);
                }
                else
                {
                    xr = Pill(g, "trim", L.T("Edit"), xr, cy, 0, _vidReady && _dur > 0.6 && !busy, true).X - Ui.Pf(10);
                }
                title = _cur != null ? Path.GetFileName(_cur) : "";
                sub = Info();
            }
            if (busy)
            {
                SizeF bs = g.MeasureString(_busyMsg, _fs);
                float bx = xr - bs.Width - Ui.Pf(4);
                Txt(g, _busyMsg, _fs, CDim, new RectangleF(bx, cy - Ui.Pf(10), bs.Width + Ui.Pf(4), Ui.Pf(20)), SfL);
                Spinner(g, new PointF(bx - Ui.Pf(14), cy), Ui.Pf(7));
                xr = bx - Ui.Pf(28);
            }
            if (title != null)
            {
                float cx = t.Left + t.Width / 2f;
                float half = Math.Min(cx - x, xr - cx) - Ui.Pf(8);
                RectangleF tr = half > Ui.Pf(70) ? new RectangleF(cx - half, 0, half * 2, 0) : new RectangleF(x, 0, Math.Max(10, xr - x), 0);
                if (sub != null)
                {
                    Txt(g, title, _ft, CText, new RectangleF(tr.X, cy - Ui.Pf(17), tr.Width, Ui.Pf(18)), SfC);
                    Txt(g, sub, _fs, CDim, new RectangleF(tr.X, cy + Ui.Pf(1), tr.Width, Ui.Pf(16)), SfC);
                }
                else Txt(g, title, _ft, CText, new RectangleF(tr.X, cy - Ui.Pf(10), tr.Width, Ui.Pf(20)), SfC);
            }
        }

        private string Info()
        {
            if (_cur == null) return null;
            List<string> parts = new List<string>();
            try { parts.Add(File.GetLastWriteTime(_cur).ToString("d MMM yyyy  HH.mm", L.Culture)); } catch { }
            if (_mode == Mode.Image && _img != null) parts.Add(_img.Width + " × " + _img.Height);
            if (_mode == Mode.Video && _vidReady)
            {
                parts.Add(PhotosApp.Time(_dur, false));
                try { if (_me.HasVideo) parts.Add(_me.NaturalVideoWidth + " × " + _me.NaturalVideoHeight); } catch { }
            }
            if (_list.Count > 1) parts.Add((_idx + 1) + " / " + _list.Count);
            return string.Join("  ·  ", parts.ToArray());
        }

        private RectangleF Pill(Graphics g, string id, string text, float x, float cy, int style, bool enabled, bool alignRight)
        {
            float w = g.MeasureString(text, _fb).Width + Ui.Pf(22), h = Ui.Pf(27);
            RectangleF r = new RectangleF(alignRight ? x - w : x, cy - h / 2f, w, h);
            bool hov = _hover == id && enabled;
            Color bg, fg;
            if (style == 1) { bg = enabled ? (hov ? Color.FromArgb(255, 216, 60) : CYellow) : Color.FromArgb(90, 80, 40); fg = enabled ? Color.FromArgb(28, 28, 28) : Color.FromArgb(140, 130, 100); }
            else if (style == 2) { bg = hov ? Color.FromArgb(36, 255, 255, 255) : Color.Transparent; fg = enabled ? CYellow : Color.FromArgb(96, 96, 100); }
            else { bg = hov ? Color.FromArgb(82, 82, 88) : CBtn; fg = enabled ? CText : Color.FromArgb(110, 110, 116); }
            if (bg.A > 0) using (GraphicsPath gp = Glass.Round(r, Ui.Pf(7))) using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, gp);
            Txt(g, text, _fb, fg, r, SfC);
            if (enabled) Hit(id, r);
            return r;
        }

        private float IconBtn(Graphics g, string id, string glyph, float x, float cy, bool enabled)
        {
            RectangleF r = new RectangleF(x, cy - Ui.Pf(14), Ui.Pf(30), Ui.Pf(28));
            IconAt(g, id, glyph, r, enabled);
            return r.Right + Ui.Pf(2);
        }
        private float IconBtnR(Graphics g, string id, string glyph, float xr, float cy, bool enabled)
        {
            RectangleF r = new RectangleF(xr - Ui.Pf(30), cy - Ui.Pf(14), Ui.Pf(30), Ui.Pf(28));
            IconAt(g, id, glyph, r, enabled);
            return r.X - Ui.Pf(2);
        }

        // kontrol bersegmen; center=true -> x adalah titik tengah, selain itu x = tepi kanan. Mengembalikan tepi kiri.
        private float Seg(Graphics g, string prefix, string[] ids, string[] labels, string cur, float x, float cy, bool center)
        {
            float[] ws = new float[ids.Length]; float total = 0;
            for (int i = 0; i < ids.Length; i++) { ws[i] = g.MeasureString(labels[i], _fb).Width + Ui.Pf(24); total += ws[i]; }
            float h = Ui.Pf(27), x0 = center ? x - total / 2f - Ui.Pf(2) : x - total - Ui.Pf(4);
            RectangleF all = new RectangleF(x0, cy - h / 2f, total + Ui.Pf(4), h);
            using (GraphicsPath gp = Glass.Round(all, Ui.Pf(7))) using (SolidBrush b = new SolidBrush(Color.FromArgb(56, 56, 60))) g.FillPath(b, gp);
            float cx = x0 + Ui.Pf(2);
            for (int i = 0; i < ids.Length; i++)
            {
                RectangleF r = new RectangleF(cx, all.Y + Ui.Pf(2), ws[i], h - Ui.Pf(4));
                bool sel = cur == ids[i];
                if (sel) using (GraphicsPath sp = Glass.Round(r, Ui.Pf(5.5f))) using (SolidBrush sb = new SolidBrush(Color.FromArgb(104, 104, 112))) g.FillPath(sb, sp);
                else if (_hover == prefix + ids[i]) using (GraphicsPath sp = Glass.Round(r, Ui.Pf(5.5f))) using (SolidBrush sb = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillPath(sb, sp);
                Txt(g, labels[i], sel ? _fb : _f, CText, r, SfC);
                Hit(prefix + ids[i], r);
                cx += ws[i];
            }
            return all.X - Ui.Pf(8);
        }

        private static readonly StringFormat SfC = MakeSf(StringAlignment.Center), SfL = MakeSf(StringAlignment.Near), SfR = MakeSf(StringAlignment.Far);
        private static StringFormat MakeSf(StringAlignment a)
        {
            StringFormat f = new StringFormat(StringFormatFlags.NoWrap);
            f.Alignment = a; f.LineAlignment = StringAlignment.Center; f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }
        private static void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        private static void Spinner(Graphics g, PointF c, float r)
        {
            float a = (Environment.TickCount / 3) % 360;
            using (Pen p = new Pen(Color.FromArgb(200, 200, 200, 206), Math.Max(2f, r / 4f)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawArc(p, c.X - r, c.Y - r, 2 * r, 2 * r, a, 270);
            }
        }

        // ============================================================
        //  MOUSE & KEYBOARD
        // ============================================================
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int lh = LightHit(e.Location);
            if (lh >= 0)
            {
                if (lh == 0) Close();
                else if (lh == 1) WindowState = FormWindowState.Minimized;
                else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                return;
            }
            if (_busyMsg != null) return;
            string id = HitId(e.Location);
            if (id != null)
            {
                if (id.StartsWith("sl:"))
                {
                    string sid = id.Substring(3);
                    if (e.Clicks >= 2) { SetVal(sid, 0); _drag = null; }
                    else { _drag = id; DragSlider(sid, e.X); }
                    return;
                }
                if (id == "v_scrub") { _drag = id; _wasPlaying = _playing; ScrubAt(e.X); return; }
                if (id == "v_vol") { _drag = id; VolAt(e.X); return; }
                if (id == "v_strip") { StripDown(e.X); return; }
                Click(id);
                return;
            }
            if (_mode == Mode.Library)
            {
                int i = LibHit(e.Location);
                _libSel = i; Invalidate();
                if (i >= 0 && e.Clicks >= 2) OpenLib(i);
                return;
            }
            if (_mode == Mode.Image && _editing)
            {
                string h = CropHandle(e.Location);
                if (h != null)
                {
                    float k; RectangleF ir = EditImageRect(out k);
                    float sc = ir.Width / _pvGeo.Width;
                    _drag = "crop:" + h; _cropStart = _ed.Crop; _cropDown = new PointF((e.X - ir.X) / sc, (e.Y - ir.Y) / sc);
                }
                return;
            }
            if (_mode == Mode.Image && _img != null && ImageArea.Contains(e.Location))
            {
                if (e.Clicks >= 2)
                {
                    float fit = FitScale(ImageArea);
                    if (_fit) ZoomTo(fit < 1 ? 1f : fit * 2f, e.Location);
                    else { _fit = true; _pan = PointF.Empty; _zoomAt = DateTime.Now; Invalidate(); }
                    return;
                }
                _drag = "pan"; _dragPt = e.Location; _dragPan = _pan;
                if (!_fit) Cursor = Cursors.SizeAll;
            }
        }

        private void OpenLib(int i)
        {
            _list.Clear();
            foreach (LibItem it in _libView) _list.Add(it.File);
            ShowItem(i);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != null)
            {
                if (_drag.StartsWith("sl:")) DragSlider(_drag.Substring(3), e.X);
                else if (_drag.StartsWith("crop:")) CropDrag(_drag.Substring(5), e.Location);
                else if (_drag == "pan")
                {
                    if (!_fit) { _pan = new PointF(_dragPan.X + e.X - _dragPt.X, _dragPan.Y + e.Y - _dragPt.Y); Invalidate(); }
                }
                else if (_drag == "v_scrub") ScrubAt(e.X);
                else if (_drag == "v_vol") VolAt(e.X);
                else if (_drag == "trimA" || _drag == "trimB" || _drag == "trimHead") StripMove(e.X);
                return;
            }
            string id = HitId(e.Location);
            int li = -1;
            { RectangleF all = RectangleF.Union(LightRect(0), LightRect(2)); all.Inflate(Ui.Pf(6), Ui.Pf(6)); if (!_full && all.Contains(e.Location)) li = 9; }
            int lh = _mode == Mode.Library ? LibHit(e.Location) : -1;
            if (id != _hover || li != _hoverLight || lh != _libHover) { _hover = id; _hoverLight = li; _libHover = lh; Invalidate(); }
            else if (_mode == Mode.Image && !_editing && _list.Count > 1) Invalidate();   // panah tepi
            Cursor c = Cursors.Default;
            if (_mode == Mode.Image && _editing && id == null) c = CropCursor(CropHandle(e.Location));
            else if (id != null && (id.StartsWith("sl:") || id == "v_scrub" || id == "v_vol" || id == "v_strip")) c = Cursors.Hand;
            else if (_mode == Mode.Image && !_editing && !_fit && ImageArea.Contains(e.Location)) c = Cursors.SizeAll;
            if (Cursor != c && _busyMsg == null) Cursor = c;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag == "v_scrub" && _wasPlaying && _me != null) { _me.Play(); _playing = true; }
            if (_drag != null && _drag.StartsWith("trim") && _wasPlaying && _me != null) { _me.Play(); _playing = true; }
            if (_drag != null) { _drag = null; Invalidate(); }
            if (_mode == Mode.Image && !_editing && _busyMsg == null) Cursor = _fit ? Cursors.Default : Cursors.SizeAll;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = null; _hoverLight = -1; _libHover = -1; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_mode == Mode.Library)
            {
                _libScrollT = Math.Max(0, Math.Min(LibMaxScroll, _libScrollT - e.Delta * Ui.Pf(1.1)));
                Invalidate(); return;
            }
            if (_mode == Mode.Image && !_editing && _img != null)
            {
                float k = (float)Math.Pow(1.0015, e.Delta);
                ZoomTo(CurScale * k, e.Location);
                return;
            }
            if (_mode == Mode.Video && VBarRect.Contains(e.Location) && !_trimming) SetVolume(_vol + e.Delta / 1200f);
        }

        private void ScrubAt(int x)
        {
            RectangleF sr; if (!_sliderRects.TryGetValue("v_scrub", out sr) || _dur <= 0) return;
            if (_playing) { _me.Pause(); _playing = false; }
            Seek(Math.Max(0, Math.Min(1, (x - sr.X) / sr.Width)) * _dur);
        }

        private void VolAt(int x)
        {
            RectangleF sr; if (!_sliderRects.TryGetValue("v_vol", out sr)) return;
            SetVolume((x - sr.X) / sr.Width);
        }

        private void StripDown(int x)
        {
            if (!_vidReady) return;
            float xa = TX(_trimA), xb = TX(_trimB), hw = Ui.Pf(13);
            _wasPlaying = _playing;
            if (_playing) { _me.Pause(); _playing = false; }
            if (x >= xa - Ui.Pf(8) && x <= xa + hw + Ui.Pf(2)) _drag = "trimA";
            else if (x >= xb - hw - Ui.Pf(2) && x <= xb + Ui.Pf(8)) _drag = "trimB";
            else _drag = "trimHead";
            StripMove(x);
        }

        private void StripMove(int x)
        {
            double t = XT(x), minLen = Math.Min(0.5, _dur / 4);
            if (_drag == "trimA") { _trimA = Math.Max(0, Math.Min(_trimB - minLen, t)); Seek(_trimA); }
            else if (_drag == "trimB") { _trimB = Math.Min(_dur, Math.Max(_trimA + minLen, t)); _me.Position = TimeSpan.FromSeconds(Math.Max(_trimA, _trimB - 0.04)); }
            else Seek(t);
            Invalidate();
        }

        private new void Click(string id)
        {
            if (id.StartsWith("flt:")) { _libFilter = id.Substring(4); ApplyFilter(); Invalidate(); return; }
            if (id.StartsWith("tab:")) { _edTab = id.Substring(4); Invalidate(); return; }
            if (id.StartsWith("asp:")) { ApplyAspect(id.Substring(4)); return; }
            switch (id)
            {
                case "lib": ShowLibrary(); break;
                case "prev": case "ovl_prev": Nav(-1); break;
                case "next": case "ovl_next": Nav(1); break;
                case "edit": BeginEdit(); break;
                case "zin": ZoomStep(1.25f); break;
                case "zout": ZoomStep(0.8f); break;
                case "fullscr": case "v_full": ToggleFull(); break;
                case "trim": BeginTrim(); break;
                case "tr_cancel": EndTrim(); break;
                case "tr_save": SaveTrim(); break;
                case "ed_cancel": CancelEdit(); break;
                case "ed_save": SaveEdit(false); break;
                case "ed_over": SaveEdit(true); break;
                case "ed_revert": { string tab = _edTab; _ed = new PhotoEdit(); _edTab = tab; RebuildGeo(); break; }
                case "rotL": _ed.RotateCCW(); RebuildGeo(); break;
                case "rotR": _ed.RotateCW(); RebuildGeo(); break;
                case "flipH": _ed.MirrorH(); RebuildGeo(); break;
                case "flipV": _ed.MirrorV(); RebuildGeo(); break;
                case "v_play": TogglePlay(); break;
                case "v_mute": _muted = !_muted; if (_me != null) _me.IsMuted = _muted; Invalidate(); break;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0;
            if (ctrl && (k == Keys.W || k == Keys.Q)) { Close(); return true; }
            if (_busyMsg != null) return base.ProcessCmdKey(ref msg, keyData);
            if (k == Keys.Escape)
            {
                if (_editing) CancelEdit();
                else if (_trimming) EndTrim();
                else if (_full) ToggleFull();
                else if (_mode != Mode.Library && _hasLib) ShowLibrary();
                return true;
            }
            if (k == Keys.F && !ctrl && !_editing && _mode != Mode.Library) { ToggleFull(); return true; }
            if (_mode == Mode.Library)
            {
                int n = _libView.Count; if (n == 0) return base.ProcessCmdKey(ref msg, keyData);
                int cols; float cell, pad, gap, headH; LibGeom(ContentRect, out cols, out cell, out pad, out gap, out headH);
                int s = _libSel;
                if (k == Keys.Right) s++; else if (k == Keys.Left) s--; else if (k == Keys.Down) s += cols; else if (k == Keys.Up) s -= cols;
                else if (k == Keys.Home) s = 0; else if (k == Keys.End) s = n - 1;
                else if ((k == Keys.Enter || k == Keys.Space) && _libSel >= 0) { OpenLib(_libSel); return true; }
                else return base.ProcessCmdKey(ref msg, keyData);
                _libSel = Math.Max(0, Math.Min(n - 1, s)); EnsureLibVisible(_libSel); Invalidate();
                return true;
            }
            if (_mode == Mode.Image)
            {
                if (_editing)
                {
                    if (k == Keys.Enter) { SaveEdit(false); return true; }
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                if (k == Keys.Left) { Nav(-1); return true; }
                if (k == Keys.Right) { Nav(1); return true; }
                if (k == Keys.Enter && _img != null) { BeginEdit(); return true; }
                if (k == Keys.Oemplus || k == Keys.Add) { ZoomStep(1.25f); return true; }
                if (k == Keys.OemMinus || k == Keys.Subtract) { ZoomStep(0.8f); return true; }
                if (k == Keys.D0 || k == Keys.NumPad0) { _fit = true; _pan = PointF.Empty; Invalidate(); return true; }
                if (k == Keys.PageUp) { Nav(-1); return true; }
                if (k == Keys.PageDown) { Nav(1); return true; }
            }
            if (_mode == Mode.Video)
            {
                double pos = 0; try { pos = _me.Position.TotalSeconds; } catch { }
                if (k == Keys.Space || k == Keys.K) { TogglePlay(); return true; }
                if (k == Keys.Left) { Seek(pos - 5); return true; }
                if (k == Keys.Right) { Seek(pos + 5); return true; }
                if (k == Keys.Up) { SetVolume(_vol + 0.1f); return true; }
                if (k == Keys.Down) { SetVolume(_vol - 0.1f); return true; }
                if (k == Keys.M) { Click("v_mute"); return true; }
                if (k == Keys.Enter && _trimming) { SaveTrim(); return true; }
                if (k == Keys.PageUp && !_trimming) { Nav(-1); return true; }
                if (k == Keys.PageDown && !_trimming) { Nav(1); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_editing && _ed.Changed && Environment.GetEnvironmentVariable("LM_PHOTOS_SHOT") == null &&
                MessageBox.Show(this, L.T("Buang perubahan yang belum disimpan?"), L.T("Foto"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) { e.Cancel = true; return; }
            try { Settings.SetS("PhotosVolume", _vol.ToString("0.00", CultureInfo.InvariantCulture)); } catch { }
            base.OnFormClosing(e);
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] f = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (f != null && f.Length > 0 && _busyMsg == null) { if (_editing) EndEdit(); OpenPaths(f); }
        }

        // ============================================================
        //  UJI TANGKAPAN LAYAR (tanpa input pengguna)
        // ============================================================
        private void ShotHook()
        {
            string shot = Environment.GetEnvironmentVariable("LM_PHOTOS_SHOT");
            if (string.IsNullOrEmpty(shot)) return;
            string mode = Environment.GetEnvironmentVariable("LM_PHOTOS_SHOT_MODE") ?? "";
            int delay = 3000; int.TryParse(Environment.GetEnvironmentVariable("LM_PHOTOS_SHOT_DELAY") ?? "3000", out delay);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = Math.Max(500, delay);
            int stage = 0;
            t.Tick += delegate
            {
                try
                {
                    if (stage == 0)
                    {
                        stage = 1; t.Interval = 2500;
                        if (mode == "edit" && _img != null) { BeginEdit(); _ed.Bright = 0.25f; _ed.Sat = 0.4f; _ed.Warm = 0.3f; _ed.Contrast = 0.15f; _ed.SetAspect("4:3", _pvGeo.Width, _pvGeo.Height); RebuildColor(); _hover = "ed_save"; }
                        else if (mode == "crop" && _img != null) { BeginEdit(); _edTab = "crop"; _ed.Straight = 6; _ed.SetAspect("16:9", _pvGeo.Width, _pvGeo.Height); _ed.Crop = new RectangleF(_ed.Crop.X + _ed.Crop.Width * 0.1f, _ed.Crop.Y + _ed.Crop.Height * 0.1f, _ed.Crop.Width * 0.8f, _ed.Crop.Height * 0.8f); RebuildGeo(); }
                        else if (mode == "trim") { BeginTrim(); _trimA = _dur * 0.2; _trimB = _dur * 0.7; Seek(_trimA + 0.5); t.Interval = 5000; }
                        else if (mode == "zoom" && _img != null) { Rectangle a = ImageArea; ZoomTo(1.5f, new PointF(a.Left + a.Width / 2f, a.Top + a.Height / 2f)); }
                        else if (mode == "full") ToggleFull();
                        else if (mode == "lib" && _mode == Mode.Library && _libView.Count > 0) { _libSel = 0; }
                        Invalidate();
                        return;
                    }
                    t.Stop();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(Handle, dc, 2); g.ReleaseHdc(dc); }
                        bmp.Save(shot, ImageFormat.Png);
                    }
                    Log.Write("photos shot ok: " + shot + " mode=" + _mode + " vidReady=" + _vidReady + " dur=" + _dur.ToString("0.0") + " strip=" + (_strip == null ? -1 : Array.FindAll(_strip, delegate (Bitmap b) { return b != null; }).Length));
                }
                catch (Exception ex) { Log.Write("photos shot: " + ex); }
                _editing = false;
                Close();
            };
            t.Start();
        }
    }

    // ============================================================
    //  UJI OTOMATIS (LM_PHOTOS_TEST=1): bekerja pada SALINAN berkas
    // ============================================================
    internal static class PhotosTest
    {
        public static void Run(string[] files)
        {
            string outDir = Environment.GetEnvironmentVariable("LM_PHOTOS_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetTempPath(), "lmphotos-test");
            Directory.CreateDirectory(outDir);
            StringBuilder log = new StringBuilder();
            log.AppendLine("photos test " + DateTime.Now.ToString("s") + "  ffmpeg=" + (VideoTools.Ffmpeg ?? "(tidak ada)"));
            foreach (string f in files)
            {
                try
                {
                    string copy = Path.Combine(outDir, Path.GetFileName(f));
                    if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase)) File.Copy(f, copy, true);
                    if (PhotosApp.IsImage(copy))
                    {
                        Image anim; Stopwatch sw = Stopwatch.StartNew();
                        Bitmap b = ImageIO.Load(copy, out anim);
                        log.AppendLine("IMG " + Path.GetFileName(copy) + " decoded " + b.Width + "x" + b.Height + " in " + sw.ElapsedMilliseconds + "ms");
                        PhotoEdit ed = new PhotoEdit();
                        ed.RotateCW(); ed.MirrorH(); ed.Straight = 5;
                        Size gs = new Size(b.Height, b.Width);
                        ed.SetAspect("4:3", gs.Width, gs.Height);
                        ed.Bright = 0.2f; ed.Contrast = 0.1f; ed.Sat = 0.3f; ed.Warm = 0.3f;
                        sw.Restart();
                        string o1 = PhotoSaver.Save(copy, b, ed, false);
                        using (Image r = Image.FromFile(o1)) log.AppendLine("  copy -> " + Path.GetFileName(o1) + " " + r.Width + "x" + r.Height + " (" + new FileInfo(o1).Length + " B, " + sw.ElapsedMilliseconds + "ms)");
                        PhotoEdit ed2 = new PhotoEdit(); ed2.RotateCCW(); ed2.SetAspect("1:1", b.Height, b.Width);
                        if (ImageIO.CanOverwrite(copy))
                        {
                            string o2 = PhotoSaver.Save(copy, b, ed2, true);
                            using (Image r = Image.FromFile(o2)) log.AppendLine("  overwrite -> " + Path.GetFileName(o2) + " " + r.Width + "x" + r.Height);
                        }
                        else log.AppendLine("  overwrite: tidak didukung untuk format ini (benar)");
                        b.Dispose(); if (anim != null) anim.Dispose();
                    }
                    else if (PhotosApp.IsVideo(copy))
                    {
                        string dst; Stopwatch sw = Stopwatch.StartNew();
                        double dur = VideoTools.Probe(copy), a = 1.0, bb = 3.0;
                        if (dur > 0) { a = dur * 0.3; bb = dur * 0.8; }
                        string err = VideoTools.Trim(copy, a, bb, false, out dst);
                        log.AppendLine("VID " + Path.GetFileName(copy) + " dur=" + dur.ToString("0.00") + " trim " + a.ToString("0.00") + ".." + bb.ToString("0.00") + " (ffmpeg/auto) -> " + (err ?? (Path.GetFileName(dst) + " " + new FileInfo(dst).Length + " B dur=" + VideoTools.Probe(dst).ToString("0.00"))) + " " + sw.ElapsedMilliseconds + "ms");
                        sw.Restart();
                        err = VideoTools.Trim(copy, a, bb, true, out dst);
                        log.AppendLine("    trim(WinRT) -> " + (err ?? (Path.GetFileName(dst) + " " + new FileInfo(dst).Length + " B dur=" + VideoTools.Probe(dst).ToString("0.00"))) + " " + sw.ElapsedMilliseconds + "ms");
                        string ff = VideoTools.Ffmpeg;
                        if (ff != null) { Bitmap fr = VideoTools.Frame(ff, copy, 1.5, 72); log.AppendLine("    frame -> " + (fr == null ? "null" : fr.Width + "x" + fr.Height)); if (fr != null) fr.Dispose(); }
                    }
                    else log.AppendLine("SKIP " + f);
                }
                catch (Exception ex) { log.AppendLine("ERR " + f + ": " + ex); }
            }
            File.WriteAllText(Path.Combine(outDir, "photos-test.log"), log.ToString());
        }
    }
}
