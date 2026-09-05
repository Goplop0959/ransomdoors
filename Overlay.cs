using NAudio.Wave;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rans0m
{
    public partial class Overlay : Form
    {
        private NotifyIcon? trayIcon;
        private System.Windows.Forms.Timer? topMostTimer;
        private CancellationTokenSource? ransomCts;
        private int _spawnGate = 0; // 0 idle, 1 running - ensures only 1 ransom at a time

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;

            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        public Overlay() { InitializeComponent(); }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                return cp;
            }
        }

        // ----------------------------- RANSOM PHASES -----------------------------

        public async Task<bool> RansomWarning()
        {
            bool result = false;
            WaveOut? spawnSound = null;
            try
            {
                FileLogger.Log("[RansomWarning] start");
                try { spawnSound = SoundHelper.Create(Properties.Resources.spawn); spawnSound.Play(); FileLogger.Log("[RansomWarning] spawn sound played"); } catch (Exception ex) { FileLogger.Log($"[RansomWarning] spawn sound fail: {ex.Message}"); }

                try { Global.RandomPosControl(pc_ransom); pc_ransom.Visible = true; FileLogger.Log($"[RansomWarning] face shown at {pc_ransom.Location}"); } catch (Exception ex) { FileLogger.Log($"[RansomWarning] face show fail: {ex.Message}"); }

                await Task.Delay(500);
                try { Global.lastRegisteredMousePos = MousePosition; Global.spyingMouse = true; FileLogger.Log($"[RansomWarning] spy start at {Global.lastRegisteredMousePos}"); } catch (Exception ex) { FileLogger.Log($"[RansomWarning] spy start fail: {ex.Message}"); }

                try { pc_stopsign.Visible = true; pc_ransom.Visible = false; FileLogger.Log("[RansomWarning] stop sign shown"); } catch (Exception ex) { FileLogger.Log($"[RansomWarning] stop show fail: {ex.Message}"); }

                await Task.Delay(500);

                try { Global.spyingMouse = false; FileLogger.Log("[RansomWarning] spy end"); } catch { }

                try { pc_stopsign.Visible = false; pc_ransom.Visible = true; Global.CenterControl(pc_ransom); this.BackColor = Color.DarkRed; FileLogger.Log($"[RansomWarning] face centered at {pc_ransom.Location} red"); } catch (Exception ex) { FileLogger.Log($"[RansomWarning] face centered fail: {ex.Message}"); }

                await Task.Delay(100);

                try
                {
                    if (!IsDisposed)
                    {
                        this.BackColor = this.TransparencyKey;
                        pc_ransom.Visible = false;
                        FileLogger.Log("[RansomWarning] face hidden, bg transparent");
                    }
                }
                catch (Exception ex) { FileLogger.Log($"[RansomWarning] hide fail: {ex.Message}"); }

                try
                {
                    var cur = MousePosition;
                    // Require at least 1cm movement (~38px at 96 DPI) to trigger - not just 1px jitter
                    // Use 40px threshold (~1.06cm) to avoid hypersensitivity
                    int dx = cur.X - Global.lastRegisteredMousePos.X;
                    int dy = cur.Y - Global.lastRegisteredMousePos.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    // If last was (-1,-1) from key press, always trigger (dist huge)
                    result = dist > 40;
                    FileLogger.Log($"[RansomWarning] last={Global.lastRegisteredMousePos} cur={cur} dist={dist:F1}px moved={result} (threshold 40px ~1cm)");
                }
                catch (Exception ex) { FileLogger.Log($"[RansomWarning] moved check fail: {ex.Message}"); result = false; }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[RansomWarning] exception: {ex.Message} {ex.StackTrace}");
                try { Global.spyingMouse = false; } catch { }
                try
                {
                    if (!IsDisposed)
                    {
                        pc_stopsign.Visible = false;
                        pc_ransom.Visible = false;
                        pc_attack.Visible = false;
                        this.BackColor = this.TransparencyKey;
                        this.Opacity = 1.0;
                    }
                }
                catch { }
                result = false;
            }
            finally
            {
                try { spawnSound?.Dispose(); } catch { }
            }
            return result;
        }

        public async Task DownloadJumpscare()
        {
            try
            {
                WaveOut? attackSound = null;
                try { attackSound = SoundHelper.Create(Properties.Resources.attack); attackSound.Play(); } catch { }

                if (IsDisposed) return;
                try { Global.CenterControl(pc_attack); } catch { }
                Point attack_center = pc_attack.Location;
                try { pc_attack.Visible = true; this.BackColor = Color.DarkRed; } catch { }

                // Shake effect
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i <= 25; i++)
                    {
                        await Task.Delay(20);
                        if (IsDisposed || !IsHandleCreated) break;
                        try { this.Invoke(() => { if (!IsDisposed) pc_attack.Location = new Point(attack_center.X + Global.RngNext(-40, 40), attack_center.Y + Global.RngNext(-40, 40)); }); }
                        catch { break; }
                    }
                });

                await Task.Delay(800);
                if (IsDisposed) return;

                try { pc_ransom.Visible = false; pc_attack.Visible = false; } catch { }

                WaveOut? installSound = null;
                try { installSound = SoundHelper.Create(Properties.Resources.install); installSound.Play(); } catch { }

                // Background signs effect
                _ = Task.Run(async () =>
                {
                    List<PictureBox> list = new();
                    try
                    {
                        for (int i = 0; i <= 70; i++)
                        {
                            await Task.Delay(10);
                            if (IsDisposed || !IsHandleCreated) break;
                            try
                            {
                                this.Invoke(() =>
                                {
                                    if (IsDisposed) return;
                                    PictureBox stopsigndup = new PictureBox();
                                    stopsigndup.Image = Properties.Resources.stop_sign;
                                    stopsigndup.Size = new Size(192, 192);
                                    Global.RandomPosControl(stopsigndup);
                                    stopsigndup.SizeMode = PictureBoxSizeMode.StretchImage;
                                    this.Controls.Add(stopsigndup);
                                    list.Add(stopsigndup);
                                });
                            }
                            catch { break; }
                        }
                    }
                    catch { }

                    await Task.Delay(800);
                    foreach (PictureBox item in list)
                    {
                        if (IsDisposed || !IsHandleCreated) break;
                        try { this.Invoke(() => { try { this.Controls.Remove(item); item.Dispose(); } catch { } }); }
                        catch { break; }
                    }
                });

                if (IsDisposed) return;
                try { Global.CenterControl(txt_download); Global.CenterControl(pb_download); pb_download.Location = new Point(pb_download.Location.X, pb_download.Location.Y + 50); } catch { }

                try { txt_download.Visible = true; pb_download.Visible = true; pb_download.Value = 100; } catch { }

                // Text shake
                _ = Task.Run(async () =>
                {
                    var origFont = txt_download.Font;
                    var origTxtLoc = txt_download.Location;
                    var origPbLoc = pb_download.Location;
                    for (int i = 0; i <= 40; i++)
                    {
                        await Task.Delay(40);
                        if (IsDisposed || !IsHandleCreated) break;
                        try
                        {
                            this.Invoke(() =>
                            {
                                if (IsDisposed) return;
                                try
                                {
                                    var newSize = Math.Max(8, txt_download.Font.Size + Global.RngNext(-2, 3));
                                    var old = txt_download.Font;
                                    if (Math.Abs(newSize - old.Size) > 0.5)
                                    {
                                        var nf = new Font(old.FontFamily, newSize, old.Style);
                                        txt_download.Font = nf;
                                        if (old != origFont) try { old.Dispose(); } catch { }
                                    }
                                    txt_download.Location = new Point(origTxtLoc.X + Global.RngNext(-5, 5), origTxtLoc.Y + Global.RngNext(-5, 5));
                                    pb_download.Location = new Point(origPbLoc.X + Global.RngNext(-5, 5), origPbLoc.Y + Global.RngNext(-5, 5));
                                }
                                catch { }
                            });
                        }
                        catch { break; }
                    }
                });

                await Task.Delay(1200);
                if (IsDisposed) return;

                try { pc_ransom.Visible = false; pc_attack.Visible = false; txt_download.Visible = false; pb_download.Visible = false; pb_download.Value = 0; } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DownloadJumpscare] exception: {ex.Message}");
                try
                {
                    if (!IsDisposed)
                    {
                        pc_ransom.Visible = false;
                        pc_attack.Visible = false;
                        txt_download.Visible = false;
                        pb_download.Visible = false;
                        try { pb_download.Value = 0; } catch { }
                        this.BackColor = this.TransparencyKey;
                    }
                }
                catch { }
            }
        }

        public async Task<bool> Ransomed()
        {
            this.BackColor = Color.Red;
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i <= 50; i++)
                    {
                        await Task.Delay(1);
                        if (IsDisposed || !IsHandleCreated) break;
                        try { this.Invoke(() => { if (!IsDisposed) this.Opacity = 1 - (i * 2 / 100.0); }); } catch { break; }
                    }
                    if (!IsDisposed && IsHandleCreated)
                    {
                        this.Invoke(() =>
                        {
                            if (!IsDisposed)
                            {
                                this.BackColor = this.TransparencyKey;
                                this.Opacity = 1.0;
                            }
                        });
                    }
                }
                catch { }
            });

            WaveOut? layer1 = null, layer2 = null, layer3 = null;
            try { layer1 = SoundHelper.Create(Properties.Resources.layer1); } catch { }
            try { layer2 = SoundHelper.Create(Properties.Resources.layer2); } catch { }
            try { layer3 = SoundHelper.Create(Properties.Resources.layer3); } catch { }

            Global.ransomLeft = 500;
            Global.underRansom = true;
            ransomCts?.Cancel(); ransomCts?.Dispose();
            ransomCts = new CancellationTokenSource();
            var token = ransomCts.Token;

            Global.RansomPayed = () =>
            {
                try { token.ThrowIfCancellationRequested(); } catch { return; }
                try
                {
                    layer1?.Stop(); layer2?.Stop(); layer3?.Stop();
                    try { layer1?.Dispose(); } catch { }
                    try { layer2?.Dispose(); } catch { }
                    try { layer3?.Dispose(); } catch { }
                }
                catch { }
                if (IsDisposed || !IsHandleCreated) return;
                try { this.Invoke((MethodInvoker)ResetRansom); } catch { try { ResetRansom(); } catch { } }
            };

            // Random flashing faces
            _ = Task.Run(async () =>
            {
                while (Global.underRansom && !token.IsCancellationRequested)
                {
                    try { await Task.Delay(Global.RngNext(2000, 5000), token); } catch { break; }
                    if (IsDisposed || !IsHandleCreated || token.IsCancellationRequested) break;
                    try
                    {
                        this.Invoke((MethodInvoker)async delegate
                        {
                            if (IsDisposed || token.IsCancellationRequested) return;
                            for (int i = 0; i <= Global.RngNext(1, 4); i++)
                            {
                                if (IsDisposed) break;
                                PictureBox ransomFace = new PictureBox();
                                ransomFace.Image = Properties.Resources.ransom_random;
                                int size = Global.RngNext(50, 400);
                                ransomFace.Size = new Size(size, size);
                                Global.RandomPosControl(ransomFace);
                                ransomFace.SizeMode = PictureBoxSizeMode.StretchImage;
                                this.Controls.Add(ransomFace);
                                await Task.Delay(25);
                                try { this.Controls.Remove(ransomFace); ransomFace.Dispose(); } catch { }
                            }
                        });
                    }
                    catch { break; }
                }
            }, token);

            Ransomed? ransomedForm = null;
            try
            {
                ransomedForm = new Ransomed();
                ransomedForm.Show();
            }
            catch (Exception ex) { Debug.WriteLine($"[Ransomed] form show failed: {ex.Message}"); }

            try { layer1?.Play(); } catch { }
            try { await Task.Delay(26000, token); } catch { }
            if (!Global.underRansom || token.IsCancellationRequested) { try { layer1?.Dispose(); layer2?.Dispose(); layer3?.Dispose(); } catch { } return false; }

            try { layer2?.Play(); } catch { }
            try { await Task.Delay(26000, token); } catch { }
            if (!Global.underRansom || token.IsCancellationRequested) { try { layer1?.Dispose(); layer2?.Dispose(); layer3?.Dispose(); } catch { } return false; }

            try { layer3?.Play(); } catch { }
            try { await Task.Delay(26000, token); } catch { }
            if (!Global.underRansom || token.IsCancellationRequested) { try { layer1?.Dispose(); layer2?.Dispose(); layer3?.Dispose(); } catch { } return false; }

            try { ransomedForm?.Close(); ransomedForm?.Dispose(); } catch { }
            try { layer1?.Dispose(); layer2?.Dispose(); layer3?.Dispose(); } catch { }
            return true;
        }

        public async Task LoseGame()
        {
            WaveOut? attackSound = null;
            try { attackSound = SoundHelper.Create(Properties.Resources.attack); attackSound.Play(); } catch { }

            if (!IsDisposed)
            {
                this.BackColor = Color.DarkRed;
                try { Global.CenterControl(pc_attack); } catch { }
                Point attack_center = pc_attack.Location;
                try { pc_attack.Visible = true; } catch { }
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i <= 25; i++)
                    {
                        await Task.Delay(10);
                        if (IsDisposed || !IsHandleCreated) break;
                        try { this.Invoke(() => { if (!IsDisposed) pc_attack.Location = new Point(attack_center.X + Global.RngNext(-40, 40), attack_center.Y + Global.RngNext(-40, 40)); }); }
                        catch { break; }
                    }
                });
            }

            await Task.Delay(1000);

            try { Global.OpenRickRoll(); } catch { }

            if (!IsDisposed)
            {
                try
                {
                    foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                    {
                        if (f is Ransomed || f is TauntWindow)
                        {
                            try { f.Invoke(() => f.Close()); } catch { try { f.Close(); } catch { } }
                        }
                    }
                }
                catch { }

                ResetRansom();
                try { pc_attack.Visible = false; this.BackColor = this.TransparencyKey; this.Opacity = 1.0; } catch { }
            }
        }

        public Task CrashJumpscare() => LoseGame();

        // ----------------------------- CORE -----------------------------

        public void ResetRansom()
        {
            try { ransomCts?.Cancel(); } catch { }
            try { GoldCoinManager.DeleteAllCoins(); } catch { }
            // Restore desktop files/icons if ransomed - handles force-stop case and win case
            try { DesktopRansomManager.TryRestore(); } catch { }
            Global.canAttack = true;
            Global.RansomPayed = null;
            Global.underRansom = false;
            Global.ransomLeft = 0;
            try { KonamiCodeDetector.Reset(); } catch { }

            try
            {
                var oldFont = txt_download.Font;
                var newFont = new Font("Consolas", 36F, FontStyle.Bold, GraphicsUnit.Point, 0);
                txt_download.Font = newFont;
                if (oldFont != null && oldFont != newFont)
                {
                    try { oldFont.Dispose(); } catch { }
                }
            }
            catch { }

            if (!IsDisposed)
            {
                try { pc_ransom.Visible = false; } catch { }
                try { pc_attack.Visible = false; } catch { }
                try { txt_download.Visible = false; } catch { }
                try { pb_download.Visible = false; } catch { }
                try { pc_stopsign.Visible = false; } catch { }
                try { pb_download.Value = 0; } catch { }
                try { this.BackColor = this.TransparencyKey; } catch { }
                try { this.Opacity = 1.0; } catch { }
                try
                {
                    var toRemove = this.Controls.OfType<PictureBox>().Where(p => p != pc_ransom && p != pc_attack && p != pc_stopsign).ToList();
                    foreach (var pb in toRemove) { try { this.Controls.Remove(pb); pb.Dispose(); } catch { } }
                }
                catch { }
                try { this.Invalidate(); this.Update(); } catch { }
            }
        }

        public async Task SpawnRansomAsync()
        {
            // Enforce single ransom at a time (atomic gate)
            if (Interlocked.CompareExchange(ref _spawnGate, 1, 0) != 0)
            {
                FileLogger.Log("[Spawn] skip - already running");
                return;
            }
            bool gateAcquired = true;
            try
            {
                if (!Global.canAttack || Global.underRansom)
                {
                    FileLogger.Log($"[Spawn] skip - canAttack={Global.canAttack} underRansom={Global.underRansom}");
                    return;
                }
                Global.canAttack = false;
                FileLogger.Log("[Spawn] gate acquired, canAttack=false");

                bool mouseMoved = false;
                try
                {
                    mouseMoved = await RansomWarning();
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"[Spawn] RansomWarning ex: {ex.Message}");
                    mouseMoved = false;
                }

                if (IsDisposed)
                {
                    Global.canAttack = true;
                    try { ResetRansom(); } catch { }
                    return;
                }

            if (mouseMoved)
            {
                // Desktop file/icon ransom (attempt for each file)
                try
                {
                    await Task.Run(() => DesktopRansomManager.TryRansomDesktop());
                }
                catch (Exception ex) { Debug.WriteLine($"[Spawn] DesktopRansom ex: {ex.Message}"); }

                try { GoldCoinManager.CreateRandomCoins(8); } catch (Exception ex) { Debug.WriteLine($"[Spawn] GoldCoin ex: {ex.Message}"); }

                try { await DownloadJumpscare(); } catch (Exception ex) { Debug.WriteLine($"[Spawn] DownloadJumpscare ex: {ex.Message}"); }

                if (IsDisposed)
                {
                    Global.underRansom = false;
                    try { ResetRansom(); } catch { }
                    return;
                }

                bool failed = false;
                try { failed = await Ransomed(); } catch (Exception ex) { Debug.WriteLine($"[Spawn] Ransomed ex: {ex.Message}"); failed = false; }

                if (failed)
                {
                    Global.underRansom = false;
                    try { await LoseGame(); } catch (Exception ex) { Debug.WriteLine($"[Spawn] LoseGame ex: {ex.Message}"); }
                    // Do not auto-restore on loss to keep prank until win or next launch; but ensure tray still works
                    Global.canAttack = true;
                }
                else
                {
                    // Win case already handled via RansomPayed -> ResetRansom which restores
                    if (!Global.underRansom)
                    {
                        // Ensure canAttack true (Reset already)
                        Global.canAttack = true;
                    }
                }
            }
            else
            {
                // Dodge success - ensure fully transparent, no leftover
                try { ResetRansom(); } catch (Exception ex) { FileLogger.Log($"[Spawn] Reset ex: {ex.Message}"); try { this.BackColor = this.TransparencyKey; pc_ransom.Visible = false; Global.canAttack = true; } catch { } }
            }
            }
            finally
            {
                if (gateAcquired) Interlocked.Exchange(ref _spawnGate, 0);
                FileLogger.Log("[Spawn] gate released");
            }
        }

        public async void SpawnRansom()
        {
            await SpawnRansomAsync();
        }

        private async void RansomLoop()
        {
            // Face hidden during idle - ensure initial delay before first attack
            FileLogger.Log("[RansomLoop] Started, face hidden during idle");
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    this.Invoke(new Action(() =>
                    {
                        try
                        {
                            pc_ransom.Visible = false;
                            pc_stopsign.Visible = false;
                            pc_attack.Visible = false;
                            txt_download.Visible = false;
                            pb_download.Visible = false;
                            this.BackColor = this.TransparencyKey;
                            this.Opacity = 1.0;
                            FileLogger.Log("[RansomLoop] Initial hide done");
                        }
                        catch (Exception ex) { FileLogger.Log($"[RansomLoop] Initial hide ex: {ex.Message}"); }
                    }));
                }
            }
            catch { }

            bool first = true;
            while (!this.IsDisposed)
            {
                int delay;
                if (first)
                {
                    delay = Global.RngNext(9*1000, 15*1000); // first ransom 9-15s per request
                    first = false;
                }
                else delay = Global.RngNext(Global.minRansomTime*1000, Global.maxRansomTime*1000);
                FileLogger.Log($"[RansomLoop] Next ransom in {delay/1000}s (face hidden) first={first}");
                try { await Task.Delay(delay); } catch { break; }

                if (IsDisposed) break;

                // Ensure still hidden before spawn (spawn will show face as part of warning)
                FileLogger.Log("[RansomLoop] Spawning ransom now");

                try
                {
                    if (IsHandleCreated)
                    {
                        try
                        {
                            // Fire-and-forget like original: Invoke async void, do not block UI (avoid deadlock)
                            this.Invoke(new Action(() => { _ = SpawnRansomAsync(); }));
                        }
                        catch (Exception ex) { FileLogger.Log($"[RansomLoop] Invoke ex: {ex.Message}"); _ = SpawnRansomAsync(); }
                    }
                    else _ = SpawnRansomAsync();
                }
                catch (Exception ex) { FileLogger.Log($"[RansomLoop] outer ex: {ex.Message}"); break; }

                // After spawn, if dodge, face should be hidden again; if attack, underRansom true
                // Loop will then delay again with face hidden (if not underRansom)
                try
                {
                    if (!Global.underRansom && IsHandleCreated && !IsDisposed)
                    {
                        this.Invoke(new Action(() =>
                        {
                            try
                            {
                                if (pc_ransom.Visible || pc_stopsign.Visible || pc_attack.Visible)
                                {
                                    FileLogger.Log("[RansomLoop] Post-spawn fixing stray visible face");
                                    pc_ransom.Visible = false;
                                    pc_stopsign.Visible = false;
                                    pc_attack.Visible = false;
                                    txt_download.Visible = false;
                                    pb_download.Visible = false;
                                    this.BackColor = this.TransparencyKey;
                                    this.Opacity = 1.0;
                                }
                            }
                            catch { }
                        }));
                    }
                }
                catch { }
            }
        }

        private void SetupTrayIcon()
        {
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            var closeItem = trayMenu.Items.Add("Close");
            closeItem.Click += (s, e) =>
            {
                if (!Global.canAttack) return;
                Close();
            };
            var restoreItem = trayMenu.Items.Add("Restore Desktop (if ransomed)");
            restoreItem.Click += (s, e) =>
            {
                try { DesktopRansomManager.TryRestore(); MessageBox.Show("Restore attempted. Check Desktop.", "RANS0M"); } catch { }
            };
            var hintItem = trayMenu.Items.Add("RANS0M - Konami+Shift = Win");
            hintItem.Enabled = false;

            trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Text = "RANS0M - Up Up Down Down Left Right Left Right B A Shift",
                Visible = true
            };

            this.FormClosed += (s, e) =>
            {
                try { trayIcon.Visible = false; } catch { }
                try { trayIcon.Dispose(); } catch { }
                try { topMostTimer?.Stop(); topMostTimer?.Dispose(); } catch { }
                try { ransomCts?.Cancel(); ransomCts?.Dispose(); } catch { }
            };
        }

        private void Overlay_Load(object sender, EventArgs e)
        {
            this.Bounds = SystemInformation.VirtualScreen;
            this.Location = new Point(0, 0);

            try { FileTypeRegister.RegisterIconForExtension(".gold", Properties.Resources.GoldIco, "GoldFile"); } catch { }
            // Auto-restore if previous run was force-stopped
            try { DesktopRansomManager.TryRestoreIfNeeded(); } catch { }
            ResetRansom();
            SetupTrayIcon();
            SetupTopMostTimer();

            _ = Task.Run(() => RansomLoop());
        }

        private void SetupTopMostTimer()
        {
            topMostTimer = new System.Windows.Forms.Timer { Interval = 500 };
            topMostTimer.Tick += (s, e) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                // Don't cover Ransomed window - let it stay on top
                try
                {
                    if (Application.OpenForms.OfType<Ransomed>().Any(f => f.Visible)) return;
                    NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                }
                catch { }
            };
            topMostTimer.Start();

            this.FormClosed += (s, e) => { try { topMostTimer.Stop(); } catch { } };
        }

        private void Overlay_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Global.underRansom)
            {
                e.Cancel = true;
                return;
            }
            try { GoldCoinManager.DeleteAllCoins(); } catch { }
            // Don't delete restore json here; leave for next launch if ransomed
        }
    }
}
