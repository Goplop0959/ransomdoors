using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace rans0m
{
    /// <summary>
    /// Animated desktop background: the face gif is split into native-res BMP
    /// frames ONCE (background, at startup), then cycled with one
    /// SystemParametersInfo call per frame using the gif's own frame delays.
    /// Runs only while a ransom is unresolved; TryRestore stops it before the
    /// original wallpaper goes back. Zero per-frame allocations.
    /// </summary>
    public static class WallpaperAnimator
    {
        private static readonly object _lock = new();
        private static CancellationTokenSource? _cts;
        private static Task? _loop;
        private static bool _prepared;
        private static readonly List<(string path, int delayMs)> _frames = new();

        private static string FramesDir => Path.Combine(AssetManager.Dir, "frames");

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private const int MaxFrames = 30;

        /// <summary>Extract frames once (background thread at startup).</summary>
        public static void Prepare()
        {
            lock (_lock)
            {
                if (_prepared) return;
                _prepared = true;
            }
            try
            {
                string gif = Path.Combine(AssetManager.Dir, "Random_A-90.gif");
                if (!File.Exists(gif)) return;
                Directory.CreateDirectory(FramesDir);
                // Reuse existing extraction when the gif hasn't changed.
                string stamp = Path.Combine(FramesDir, "source.len");
                long len = new FileInfo(gif).Length;
                try
                {
                    if (File.Exists(stamp) && File.ReadAllText(stamp) == len.ToString())
                    {
                        var existing = Directory.GetFiles(FramesDir, "frame_*.bmp")
                            .OrderBy(f => f).ToList();
                        if (existing.Count > 0)
                        {
                            lock (_lock)
                            {
                                _frames.Clear();
                                foreach (var f in existing) _frames.Add((f, 120));
                            }
                            ReadDelays(gif);
                            FileLogger.Log($"[Wallpaper] Reused {existing.Count} cached frames");
                            return;
                        }
                    }
                }
                catch { }
                try
                {
                    foreach (var f in Directory.GetFiles(FramesDir, "frame_*.bmp"))
                        try { File.Delete(f); } catch { }
                }
                catch { }

                using var img = Image.FromFile(gif);
                int count;
                try { count = img.GetFrameCount(FrameDimension.Time); }
                catch { count = 1; }
                count = Math.Min(count, MaxFrames);
                if (count <= 0) return;

                int[] delays = ReadDelays(gif, count);
                var made = new List<(string, int)>(count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        img.SelectActiveFrame(FrameDimension.Time, i);
                        // Native gif resolution: no resize, no quality loss.
                        string out_ = Path.Combine(FramesDir, $"frame_{i:D3}.bmp");
                        img.Save(out_, ImageFormat.Bmp);
                        made.Add((out_, delays[i]));
                    }
                    catch { break; }
                }
                if (made.Count == 0) return;
                lock (_lock)
                {
                    _frames.Clear();
                    _frames.AddRange(made);
                }
                try { File.WriteAllText(stamp, len.ToString()); } catch { }
                FileLogger.Log($"[Wallpaper] Extracted {made.Count} frames");
            }
            catch (Exception ex) { FileLogger.Log($"[Wallpaper] prepare err: {ex.Message}"); }
        }

        private static int[] ReadDelays(string gif, int count = 0)
        {
            try
            {
                using var img = Image.FromFile(gif);
                int n = count > 0 ? count : Math.Min(img.GetFrameCount(FrameDimension.Time), MaxFrames);
                var delays = new int[n];
                for (int i = 0; i < n; i++) delays[i] = 120;
                try
                {
                    // PropertyTagFrameDelay = 0x5100: 4-byte 1/100s values.
                    var prop = img.GetPropertyItem(0x5100);
                    byte[] raw = prop?.Value ?? Array.Empty<byte>();
                    for (int i = 0; i < n && (i + 1) * 4 <= raw.Length; i++)
                    {
                        int cs = BitConverter.ToInt32(raw, i * 4); // centiseconds
                        delays[i] = Math.Clamp(cs * 10, 80, 1000);
                        if (delays[i] < 80) delays[i] = 120;
                    }
                }
                catch { }
                lock (_lock)
                {
                    for (int i = 0; i < Math.Min(n, _frames.Count); i++)
                        _frames[i] = (_frames[i].path, delays[i]);
                }
                return delays;
            }
            catch { return Enumerable.Repeat(120, Math.Max(count, 1)).ToArray(); }
        }

        public static bool IsRunning
        {
            get { lock (_lock) return _cts != null; }
        }

        public static int FrameCount
        {
            get { lock (_lock) return _frames.Count; }
        }

        /// <summary>Start cycling. Safe to call repeatedly; second call is a no-op.</summary>
        public static void Start()
        {
            lock (_lock)
            {
                if (_cts != null) return;
                if (_frames.Count == 0) return;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                var frames = _frames.ToList(); // snapshot: zero per-frame locking
                _loop = Task.Run(async () =>
                {
                    try
                    {
                        int i = 0;
                        while (!token.IsCancellationRequested)
                        {
                            var (path, delay) = frames[i % frames.Count];
                            i++;
                            try { SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE); }
                            catch { }
                            try { await Task.Delay(delay, token); } catch { break; }
                        }
                    }
                    catch { }
                }, token);
                FileLogger.Log($"[Wallpaper] Animation started ({frames.Count} frames)");
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;
            lock (_lock) { cts = _cts; _cts = null; }
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
            _loop = null;
            FileLogger.Log("[Wallpaper] Animation stopped");
        }
    }
}
