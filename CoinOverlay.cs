using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rans0m
{
    /// <summary>
    /// Clickable gold coins rendered as small topmost windows that sit above
    /// desktop icons. Single click collects (no drag and drop). Each popup shows
    /// its Gold_X.png face; 5% are Honey_Pot.png which clears the full 500.
    ///
    /// Placement rules: a coin never spawns under a popup (taunt / ransom /
    /// thank-you / another coin). One shared 1.5s UI timer watches for overlaps
    /// - if a popup later covers a coin, that coin is deleted (file discarded,
    /// no credit) - and regenerates replacements at new free positions, so the
    /// coin count stays constant while the ransom is live.
    /// </summary>
    public static class CoinOverlay
    {
        private static readonly List<CoinForm> _forms = new();
        private static readonly object _lock = new();
        private static readonly Dictionary<string, Image> _imgCache = new();
        private static readonly object _imgLock = new();
        private static System.Windows.Forms.Timer? _watcher;
        private static int _targetCount;

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;
            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        /// <summary>Load Gold_X.png / Honey_Pot.png from the unpack dir, cached. Falls back to embedded Gold.</summary>
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

                // Enforcement only; 1.5s keeps timer traffic negligible.
                _topTimer = new System.Windows.Forms.Timer { Interval = 1500 };
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
                    RemoveForm(this);
                    try { Close(); } catch { }
                    try { Dispose(); } catch { }
                    // Watcher refills the set at a new free spot on its next tick.
                }
            }
        }

        /// <summary>Screen rects of popups that coins must avoid. UI thread only.</summary>
        /// <param name="includeCoins">Coins never kill each other, only real popups do.</param>
        private static List<Rectangle> PopupRects(bool includeCoins = true)
        {
            var rects = new List<Rectangle>(16);
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    try
                    {
                        if (f.IsDisposed || !f.IsHandleCreated || !f.Visible) continue;
                        if (f is Overlay) continue; // click-through fullscreen layer, not a blocker
                        if (f is TauntWindow || f is Ransomed || f is ThankYou)
                            rects.Add(f.Bounds);
                        else if (includeCoins && f is CoinForm)
                            rects.Add(f.Bounds);
                    }
                    catch { }
                }
            }
            catch { }
            return rects;
        }

        /// <summary>Random spot that overlaps no popup (10px margin). Null if crowded.</summary>
        private static Point? FindFreeSpot(int w, int h)
        {
            try
            {
                var bounds = Global.screenBounds;
                var blockers = PopupRects();
                for (int i = 0; i < 12; i++)
                {
                    int x = bounds.X + Global.RngNext(0, Math.Max(1, bounds.Width - w - 10));
                    int y = bounds.Y + Global.RngNext(0, Math.Max(1, bounds.Height - h - 10));
                    var r = new Rectangle(x, y, w, h);
                    r.Inflate(10, 10);
                    bool hit = false;
                    foreach (var b in blockers)
                    {
                        if (r.IntersectsWith(b)) { hit = true; break; }
                    }
                    if (!hit) return new Point(x, y);
                }
            }
            catch { }
            return null;
        }

        private static void SpawnOne(GoldCoinManager.CoinDef c)
        {
            try
            {
                var img = CoinImage(c.Value, c.IsHoneyPot);
                int w = c.IsHoneyPot ? 96 : 80;
                int h = c.IsHoneyPot ? 110 : 96;
                var spot = FindFreeSpot(w, h);
                if (spot == null)
                {
                    // Nowhere free right now: drop the coin file so supply stays
                    // consistent, the watcher retries when space opens up.
                    GoldCoinManager.DiscardCoinFile(c.Path);
                    return;
                }
                var f = new CoinForm(img, spot.Value, c.Value, c.IsHoneyPot) { BoundGoldPath = c.Path };
                lock (_lock) { _forms.Add(f); }
                try { f.Show(); } catch { lock (_lock) { _forms.Remove(f); } }
            }
            catch { }
        }

        private static void RemoveForm(CoinForm f)
        {
            lock (_lock) { _forms.Remove(f); }
        }

        private static void EnsureWatcher()
        {
            try
            {
                if (_watcher != null && !_watcher.Enabled) { try { _watcher.Start(); } catch { } return; }
                if (_watcher != null) return;
                _watcher = new System.Windows.Forms.Timer { Interval = 1500 };
                _watcher.Tick += (_, __) => OnWatchTick();
                _watcher.Start();
            }
            catch { }
        }

        private static void OnWatchTick()
        {
            try
            {
                if (!Global.underRansom) return;
                List<CoinForm> snapshot;
                lock (_lock) { snapshot = _forms.ToList(); }
                if (snapshot.Count == 0 && _targetCount <= 0) return;

                // 1) Delete any coin a popup has appeared over (no credit).
                foreach (var coin in snapshot)
                {
                    bool dead = false;
                    try
                    {
                        if (coin.IsDisposed || !coin.IsHandleCreated || !coin.Visible) continue;
                        var r = coin.Bounds;
                        r.Inflate(6, 6);
                        foreach (var b in PopupRects(includeCoins: false))
                        {
                            if (r.IntersectsWith(b)) { dead = true; break; }
                        }
                    }
                    catch { continue; }
                    if (!dead) continue;
                    try
                    {
                        FileLogger.Log("[CoinOverlay] Popup covered a coin - deleting it");
                        GoldCoinManager.DiscardCoinFile(coin.BoundGoldPath);
                        RemoveForm(coin);
                        try { coin.Close(); } catch { }
                        try { coin.Dispose(); } catch { }
                    }
                    catch { }
                }

                // 2) Regenerate back up to the target count at new free spots.
                // Capped per tick so file IO never hitches the UI thread.
                int alive;
                lock (_lock) { alive = _forms.Count(f => !f.IsDisposed); }
                int need = Math.Min(2, Math.Max(0, _targetCount - alive));
                for (int i = 0; i < need; i++)
                {
                    if (!Global.underRansom) break;
                    List<GoldCoinManager.CoinDef> fresh = new();
                    try { fresh = GoldCoinManager.CreateRandomCoins(1); } catch { }
                    if (fresh.Count == 0) break;
                    SpawnOne(fresh[0]);
                }
            }
            catch { }
        }

        public static void SpawnForCoins(List<GoldCoinManager.CoinDef> coins)
        {
            try
            {
                Clear();
                if (coins == null || coins.Count == 0) return;
                _targetCount = coins.Count;

                foreach (var c in coins)
                {
                    if (!Global.underRansom) break;
                    SpawnOne(c);
                }
                EnsureWatcher();
                FileLogger.Log($"[CoinOverlay] Spawned {ActiveCount} clickable coins on top of desktop");
            }
            catch (Exception ex) { FileLogger.Log($"[CoinOverlay] spawn err: {ex.Message}"); }
        }

        public static void Clear()
        {
            List<CoinForm> snapshot;
            lock (_lock) { snapshot = _forms.ToList(); _forms.Clear(); }
            _targetCount = 0;
            // Stop the watcher (may be called off-UI; guard accordingly).
            var w = _watcher;
            _watcher = null;
            if (w != null)
            {
                try
                {
                    // WinForms timers must be touched on their thread; best effort.
                    try { w.Stop(); } catch { }
                    try { w.Dispose(); } catch { }
                }
                catch { }
            }
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
