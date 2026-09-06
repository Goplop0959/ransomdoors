using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rans0m
{
    /// <summary>
    /// Clickable gold coins rendered as small topmost windows that sit above
    /// desktop icons. Single click collects (no drag and drop). Each popup shows
    /// its Gold_X.png face; 5% are Honey_Pot.png which clears the full 500.
    /// </summary>
    public static class CoinOverlay
    {
        private static readonly List<CoinForm> _forms = new();
        private static readonly object _lock = new();
        private static readonly Dictionary<string, Image> _imgCache = new();
        private static readonly object _imgLock = new();

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;
            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        /// <summary>Load Gold_X.png / Honey_Pot.png from the exe dir, cached. Falls back to embedded Gold.</summary>
        public static Image CoinImage(int value, bool honeyPot)
        {
            string name = honeyPot ? "Honey_Pot.png" : $"Gold_{value}.png";
            lock (_imgLock)
            {
                if (_imgCache.TryGetValue(name, out var cached)) return cached;
                Image img;
                try
                {
                    string path = Path.Combine(AssetManager.Dir, name);
                    if (File.Exists(path))
                    {
                        // Load via bytes so the file isn't locked on disk
                        byte[] bytes = File.ReadAllBytes(path);
                        using var ms = new MemoryStream(bytes);
                        img = Image.FromStream(ms);
                    }
                    else img = Properties.Resources.Gold;
                }
                catch { try { img = Properties.Resources.Gold; } catch { img = new Bitmap(64, 64); } }
                _imgCache[name] = img;
                return img;
            }
        }

        private sealed class CoinForm : Form
        {
            private readonly PictureBox _pic;
            private readonly System.Windows.Forms.Timer _topTimer;
            private readonly Label? _valLabel;
            public string? BoundGoldPath;

            public CoinForm(Image coinImage, Point location, int value, bool honeyPot)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                ShowIcon = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                Location = location;
                Size = honeyPot ? new Size(96, 110) : new Size(80, 96);
                MinimumSize = Size;
                MaximumSize = Size;
                BackColor = Color.Magenta;
                TransparencyKey = Color.Magenta;
                ControlBox = false;
                MinimizeBox = false;
                MaximizeBox = false;
                Text = honeyPot ? "HONEY POT" : $"GOLD {value}";

                _pic = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = coinImage,
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Cursor = Cursors.Hand,
                    BackColor = Color.Transparent
                };
                // Click anywhere on form or picture collects
                _pic.Click += (_, __) => CollectAndClose();
                Click += (_, __) => CollectAndClose();
                Controls.Add(_pic);

                if (!honeyPot)
                {
                    _valLabel = new Label
                    {
                        Text = value.ToString(),
                        Dock = DockStyle.Bottom,
                        Height = 22,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Consolas", 12F, FontStyle.Bold),
                        ForeColor = Color.Gold,
                        BackColor = Color.Black,
                        Cursor = Cursors.Hand
                    };
                    _valLabel.Click += (_, __) => CollectAndClose();
                    Controls.Add(_valLabel);
                    _valLabel.BringToFront();
                }

                _topTimer = new System.Windows.Forms.Timer { Interval = 400 };
                _topTimer.Tick += (_, __) =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE); } catch { }
                };
                _topTimer.Start();
                FormClosed += (_, __) => { try { _topTimer.Stop(); _topTimer.Dispose(); } catch { } };
            }

            private void CollectAndClose()
            {
                try
                {
                    GoldCoinManager.CollectResult r;
                    // Prefer bound file, fallback to any desktop coin
                    if (!string.IsNullOrEmpty(BoundGoldPath) && File.Exists(BoundGoldPath))
                        r = GoldCoinManager.CollectCoinFile(BoundGoldPath);
                    else
                        r = GoldCoinManager.CollectAnyDesktopCoin();
                    FileLogger.Log($"[CoinOverlay] Click collected value={r.Value} honey={r.IsHoneyPot} saved={r.SavedCredit} remaining={Global.ransomLeft}");
                    try { SoundHelper.PlayOneShot(Properties.Resources.cash, 0.5f); } catch { }
                    // Notify any open Ransomed window to refresh + maybe win
                    try
                    {
                        foreach (var f in Application.OpenForms.OfType<Ransomed>().ToList())
                        {
                            try { f.Invoke(new Action(() => f.RefreshAfterCollect())); } catch { try { f.RefreshAfterCollect(); } catch { } }
                        }
                    }
                    catch { }
                }
                catch (Exception ex) { FileLogger.Log($"[CoinOverlay] collect err: {ex.Message}"); }
                finally
                {
                    try { Close(); } catch { }
                    try { Dispose(); } catch { }
                }
            }
        }

        public static void SpawnForCoins(List<GoldCoinManager.CoinDef> coins)
        {
            try
            {
                Clear();
                if (coins == null || coins.Count == 0) return;

                var bounds = Global.screenBounds;
                lock (_lock)
                {
                    foreach (var c in coins)
                    {
                        try
                        {
                            var img = CoinImage(c.Value, c.IsHoneyPot);
                            int w = c.IsHoneyPot ? 96 : 80;
                            int h = c.IsHoneyPot ? 110 : 96;
                            int x = bounds.X + Global.RngNext(0, Math.Max(1, bounds.Width - w - 10));
                            int y = bounds.Y + Global.RngNext(0, Math.Max(1, bounds.Height - h - 10));
                            var f = new CoinForm(img, new Point(x, y), c.Value, c.IsHoneyPot) { BoundGoldPath = c.Path };
                            _forms.Add(f);
                            try { f.Show(); } catch { }
                        }
                        catch { }
                    }
                }
                FileLogger.Log($"[CoinOverlay] Spawned {_forms.Count} clickable coins on top of desktop");
            }
            catch (Exception ex) { FileLogger.Log($"[CoinOverlay] spawn err: {ex.Message}"); }
        }

        public static void Clear()
        {
            List<CoinForm> snapshot;
            lock (_lock) { snapshot = _forms.ToList(); _forms.Clear(); }
            // Close outside the lock; skip Invoke when already on the UI thread.
            foreach (var f in snapshot)
            {
                try
                {
                    if (f.IsDisposed) continue;
                    if (f.InvokeRequired)
                    {
                        try { f.Invoke(new Action(() => { try { f.Close(); } catch { } })); } catch { try { f.Close(); } catch { } }
                    }
                    else { try { f.Close(); } catch { } }
                }
                catch { }
                try { f.Dispose(); } catch { }
            }
        }

        public static int ActiveCount
        {
            get { lock (_lock) return _forms.Count(f => !f.IsDisposed); }
        }
    }
}
