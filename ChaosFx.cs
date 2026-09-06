using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace rans0m
{
    /// <summary>
    /// Gif metadata helper: total play-once duration of an animated gif.
    /// </summary>
    public static class GifInfo
    {
        public static int TotalDurationMs(string path, int minMs = 400, int maxMs = 8000)
        {
            try
            {
                using var img = Image.FromFile(path);
                int n;
                try { n = img.GetFrameCount(FrameDimension.Time); }
                catch { n = 1; }
                n = Math.Clamp(n, 1, 120);
                int total = 0;
                try
                {
                    var prop = img.GetPropertyItem(0x5100); // PropertyTagFrameDelay
                    byte[] raw = prop?.Value ?? Array.Empty<byte>();
                    for (int i = 0; i < n; i++)
                    {
                        int cs = 10; // 100ms default
                        if ((i + 1) * 4 <= raw.Length)
                            cs = Math.Max(2, BitConverter.ToInt32(raw, i * 4));
                        total += Math.Clamp(cs * 10, 80, 1000);
                    }
                }
                catch { total = n * 120; }
                return Math.Clamp(total, minMs, maxMs);
            }
            catch { return 1000; }
        }
    }

    /// <summary>
    /// Fullscreen click-through chaos layer, ported from the Python reference
    /// sim (ransom border frame + red static noise):
    ///  - 4 animated red pixel-dot corner clouds (4 pre-rendered cels each,
    ///    deterministic hash noise, cycled every ~67ms).
    ///  - Fullscreen red static streaks (4 pre-rendered half-res frames,
    ///    cycled every ~90ms).
    /// Everything is pre-rendered once; per-frame work is just PictureBox
    /// image swaps (zero allocation). The window is fully click-through and
    /// mostly transparent, so coins and popups beneath stay visible/clickable.
    /// Must be driven from the UI thread.
    /// </summary>
    public static class ChaosFx
    {
        private sealed class FxForm : Form
        {
            public readonly PictureBox NoiseBox = new();
            public readonly PictureBox[] Corners = new PictureBox[4];
            public readonly System.Windows.Forms.Timer BorderTimer = new() { Interval = 67 };
            public readonly System.Windows.Forms.Timer NoiseTimer = new() { Interval = 90 };
            public int Tick;

            public FxForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                ShowIcon = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                var bounds = Global.screenBounds;
                Bounds = bounds;
                BackColor = Color.Magenta;
                TransparencyKey = Color.Magenta;
                ControlBox = false;
                MinimizeBox = false;
                MaximizeBox = false;
                Text = "CHAOS";

                NoiseBox.Dock = DockStyle.Fill;
                NoiseBox.BackColor = Color.Transparent;
                NoiseBox.SizeMode = PictureBoxSizeMode.StretchImage;
                NoiseBox.TabStop = false;
                Controls.Add(NoiseBox);

                for (int i = 0; i < 4; i++)
                {
                    var pb = new PictureBox
                    {
                        BackColor = Color.Transparent,
                        SizeMode = PictureBoxSizeMode.Normal,
                        TabStop = false
                    };
                    Corners[i] = pb;
                    Controls.Add(pb);
                    pb.BringToFront();
                }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT (click-through)
                    cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    return cp;
                }
            }
        }

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;
            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        private static readonly object _lock = new();
        private static FxForm? _form;
        private static Bitmap[]? _noiseFrames;
        private static Bitmap[,]? _cornerCels; // [corner, frame]
        private static int _cornerSize;

        private static readonly Color[] DotColors = new[]
        {
            Color.FromArgb(0xFF, 0x06, 0x1D),
            Color.FromArgb(0xE0, 0x00, 0x18),
            Color.FromArgb(0xC0, 0x00, 0x16),
            Color.FromArgb(0x92, 0x00, 0x11),
            Color.FromArgb(0xFF, 0x30, 0x40),
        };

        private static readonly Color[] StreakColors = new[]
        {
            Color.FromArgb(0x31, 0x00, 0x00),
            Color.FromArgb(0x69, 0x00, 0x00),
            Color.FromArgb(0xA9, 0x00, 0x00),
            Color.FromArgb(0xFF, 0x11, 0x11),
            Color.FromArgb(0x17, 0x00, 0x00),
        };

        private static uint HashNoise(int x, int y, int seed)
        {
            unchecked
            {
                uint v = (uint)((x * 73856093) ^ (y * 19349663) ^ (seed * 83492791));
                v = (v ^ (v >> 13)) * 1274126177u;
                return v;
            }
        }

        private static Bitmap RenderDotCorner(int size, string corner, int frame, int salt)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            const int dot = 4, spacing = 4;
            for (int gy = 0; gy < size; gy += spacing)
            {
                for (int gx = 0; gx < size; gx += spacing)
                {
                    int dx = corner.Contains("left") ? gx : size - dot - gx;
                    int dy = corner.Contains("top") ? gy : size - dot - gy;
                    double depthRatio = Math.Min(1.0, Math.Min(dx, dy) / Math.Max(1.0, size * 0.46));
                    double alongRatio = Math.Min(1.0, Math.Max(dx, dy) / Math.Max(1, size - dot));
                    double prob = 0.94 * Math.Pow(1.0 - depthRatio, 1.65) * Math.Pow(1.0 - alongRatio, 0.92) + 0.012;
                    int cx = gx / spacing, cy = gy / spacing;
                    uint baseV = HashNoise(cx, cy, salt);
                    uint varV = HashNoise(cx, cy, salt + 101 * (frame + 1));
                    uint sel = HashNoise(cx, cy, salt + 10003);
                    uint v = ((sel & 0xFFFF) < 38010) ? baseV : varV;
                    if (v % 65536 / 65535.0 >= prob) continue;
                    Color c = DotColors[(v >> 24) % (uint)DotColors.Length];
                    for (int py = gy; py < Math.Min(gy + dot, size); py++)
                        for (int px = gx; px < Math.Min(gx + dot, size); px++)
                            bmp.SetPixel(px, py, c);
                }
            }
            return bmp;
        }

        private static Bitmap RenderNoiseFrame(int w, int h)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            int count = Math.Clamp(w * h / 8000, 40, 130); // ~115 at 1080p
            for (int i = 0; i < count; i++)
            {
                int y = Global.RngNext(0, Math.Max(1, h));
                int x1 = Global.RngNext(0, Math.Max(1, w - 10));
                int len = Global.RngNext(7, Math.Max(15, w / 3));
                int hh = Global.RngNext(1, 4);
                using var brush = new SolidBrush(StreakColors[Global.RngNext(StreakColors.Length)]);
                g.FillRectangle(brush, x1, y, Math.Min(len, w - x1), hh);
            }
            return bmp;
        }

        private static void EnsurePrepared()
        {
            lock (_lock)
            {
                if (_noiseFrames != null && _cornerCels != null) return;
                var bounds = Global.screenBounds;
                int size = Math.Clamp(Math.Min(bounds.Width, bounds.Height) / 5, 170, 300);
                _cornerSize = size;
                string[] corners = new[] { "top_left", "top_right", "bottom_left", "bottom_right" };
                int[] salts = new[] { 11, 23, 37, 53 };
                var cels = new Bitmap[4, 4];
                for (int c = 0; c < 4; c++)
                    for (int f = 0; f < 4; f++)
                        cels[c, f] = RenderDotCorner(size, corners[c], f, salts[c]);
                _cornerCels = cels;
                int nw = Math.Max(320, bounds.Width / 2), nh = Math.Max(200, bounds.Height / 2);
                var noise = new Bitmap[4];
                for (int f = 0; f < 4; f++) noise[f] = RenderNoiseFrame(nw, nh);
                _noiseFrames = noise;
            }
        }

        /// <summary>Show the fullscreen FX layer. UI thread only. Idempotent.</summary>
        public static void Show()
        {
            try
            {
                lock (_lock)
                {
                    if (_form != null && !_form.IsDisposed) return;
                }
                EnsurePrepared();
                var form = new FxForm();
                var bounds = Global.screenBounds;
                int s = _cornerSize;
                form.Corners[0].Location = new Point(bounds.X, bounds.Y);
                form.Corners[1].Location = new Point(bounds.Right - s, bounds.Y);
                form.Corners[2].Location = new Point(bounds.X, bounds.Bottom - s);
                form.Corners[3].Location = new Point(bounds.Right - s, bounds.Bottom - s);
                foreach (var c in form.Corners) c.Size = new Size(s, s);
                var noise = _noiseFrames;
                var cels = _cornerCels;
                if (noise == null || cels == null) { try { form.Dispose(); } catch { } return; }
                form.NoiseBox.Image = noise[0];
                for (int i = 0; i < 4; i++) form.Corners[i].Image = cels[i, 0];
                form.BorderTimer.Tick += (_, __) =>
                {
                    if (form.IsDisposed || !form.IsHandleCreated) return;
                    try
                    {
                        form.Tick++;
                        int f = form.Tick % 4;
                        for (int i = 0; i < 4; i++) form.Corners[i].Image = cels[i, f];
                        if (form.Tick % 8 == 0)
                            NativeMethods.SetWindowPos(form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                    catch { }
                };
                form.NoiseTimer.Tick += (_, __) =>
                {
                    if (form.IsDisposed || !form.IsHandleCreated) return;
                    try { form.NoiseBox.Image = noise[form.Tick % 4]; } catch { }
                };
                form.FormClosed += (_, __) =>
                {
                    try { form.BorderTimer.Stop(); form.BorderTimer.Dispose(); } catch { }
                    try { form.NoiseTimer.Stop(); form.NoiseTimer.Dispose(); } catch { }
                };
                lock (_lock) { _form = form; }
                form.Show();
                form.BorderTimer.Start();
                form.NoiseTimer.Start();
                FileLogger.Log("[ChaosFx] Shown fullscreen");
            }
            catch (Exception ex) { FileLogger.Log($"[ChaosFx] show err: {ex.Message}"); }
        }

        public static void Hide()
        {
            FxForm? form;
            lock (_lock) { form = _form; _form = null; }
            if (form == null) return;
            try
            {
                if (!form.IsDisposed)
                {
                    if (form.InvokeRequired)
                    {
                        try { form.Invoke(new Action(() => { try { form.Close(); } catch { } })); }
                        catch { try { form.Close(); } catch { } }
                    }
                    else { try { form.Close(); } catch { } }
                }
            }
            catch { }
            try { form.Dispose(); } catch { }
            FileLogger.Log("[ChaosFx] Hidden");
        }

        public static bool IsVisible
        {
            get { lock (_lock) return _form != null && !_form.IsDisposed; }
        }
    }
}
