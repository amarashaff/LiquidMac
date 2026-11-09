// LiquidMac - Now Playing (YouTube, Spotify, dll.) lewat Windows Media Session (GSMTC), dibaca di thread latar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LiquidMac
{
    internal sealed class NowPlaying
    {
        public string Title, Artist, App, AppId;
        public bool Playing;
        public TimeSpan Position, Duration;
        public DateTime Sampled;
        public Bitmap Art;
        public string Key { get { return (AppId ?? "") + "|" + (Title ?? "") + "|" + (Artist ?? ""); } }

        // Posisi diperkirakan maju sejak sampel terakhir agar progress bar mulus.
        public TimeSpan LivePosition
        {
            get
            {
                TimeSpan p = Position;
                if (Playing) p += DateTime.Now - Sampled;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p;
            }
        }
    }

    internal static class Media
    {
        private static readonly object _lock = new object();
        private static NowPlaying _current;
        private static Thread _thread;
        private static volatile bool _stop;
        private static string _artKey;
        private static Bitmap _art;
        public static int Version;   // naik setiap ada perubahan

        public static NowPlaying Current { get { lock (_lock) return _current; } }

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public static void Stop() { _stop = true; }

        private static T Wait<T>(IAsyncOperation<T> op)
        {
            int n = 0;
            while (op.Status == AsyncStatus.Started && n++ < 300) Thread.Sleep(10);
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        private static void Loop()
        {
            GlobalSystemMediaTransportControlsSessionManager mgr = null;
            int fails = 0;
            while (!_stop)
            {
                try
                {
                    if (mgr == null) mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                    NowPlaying np = null;
                    if (mgr != null)
                    {
                        GlobalSystemMediaTransportControlsSession s = mgr.GetCurrentSession();
                        if (s != null)
                        {
                            GlobalSystemMediaTransportControlsSessionMediaProperties p = Wait(s.TryGetMediaPropertiesAsync());
                            if (p != null && !string.IsNullOrEmpty(p.Title))
                            {
                                np = new NowPlaying();
                                np.Title = p.Title; np.Artist = p.Artist; np.AppId = s.SourceAppUserModelId;
                                np.App = AppLabel(np.AppId);
                                GlobalSystemMediaTransportControlsSessionPlaybackInfo pb = s.GetPlaybackInfo();
                                np.Playing = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                                GlobalSystemMediaTransportControlsSessionTimelineProperties tl = s.GetTimelineProperties();
                                if (tl != null)
                                {
                                    np.Duration = tl.EndTime - tl.StartTime;
                                    np.Position = tl.Position;
                                    DateTime upd = tl.LastUpdatedTime.LocalDateTime;
                                    np.Sampled = (upd > DateTime.Now.AddHours(-12) && upd <= DateTime.Now) ? upd : DateTime.Now;
                                }
                                else np.Sampled = DateTime.Now;
                                if (np.Key != _artKey)
                                {
                                    _artKey = np.Key;
                                    Bitmap old = _art;
                                    _art = p.Thumbnail != null ? ReadThumb(p.Thumbnail) : null;
                                    if (old != null) ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(3000); old.Dispose(); });
                                }
                                np.Art = _art;
                            }
                        }
                    }
                    lock (_lock)
                    {
                        bool changed = (np == null) != (_current == null) || (np != null && (np.Key != _current.Key || np.Playing != _current.Playing || Math.Abs((np.Position - _current.Position).TotalSeconds) > 2.5));
                        _current = np;
                        if (changed) Version++;
                    }
                    fails = 0;
                }
                catch (Exception ex)
                {
                    if (++fails <= 3) Log.Write("media: " + ex.Message);
                    mgr = null;
                    if (fails > 20) { Thread.Sleep(30000); fails = 4; }
                }
                Thread.Sleep(1000);
            }
        }

        private static Bitmap ReadThumb(IRandomAccessStreamReference r)
        {
            try
            {
                IRandomAccessStreamWithContentType st = Wait(r.OpenReadAsync());
                if (st == null || st.Size == 0 || st.Size > 20000000) return null;
                DataReader dr = new DataReader(st);
                uint n = Wait(dr.LoadAsync((uint)st.Size));
                byte[] buf = new byte[n];
                dr.ReadBytes(buf);
                using (MemoryStream ms = new MemoryStream(buf))
                using (Image img = Image.FromStream(ms))
                {
                    // potong persegi dari tengah (thumbnail YouTube 16:9)
                    int side = Math.Min(img.Width, img.Height);
                    Bitmap b = new Bitmap(256, 256, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(img, new Rectangle(0, 0, 256, 256), new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                    }
                    return b;
                }
            }
            catch (Exception ex) { Log.Write("thumb: " + ex.Message); return null; }
        }

        private static string AppLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Media";
            string l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge") || l.Contains("edge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("opera")) return "Opera";
            if (l.Contains("zune") || l.Contains("media")) return "Media Player";
            if (l.Contains("vlc")) return "VLC";
            string n = null;
            if (n != null) return n;
            int bang = id.IndexOf('!'); if (bang > 0) id = id.Substring(0, bang);
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = Path.GetFileNameWithoutExtension(id);
            return id;
        }

        public static string Fmt(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }
    }
}
