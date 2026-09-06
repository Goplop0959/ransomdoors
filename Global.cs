using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace rans0m
{
    public class Global
    {
        // ----------------------------- CONFIGURATION -----------------------------
        public static readonly int minRansomTime = 26*3; // 78s original - restored after bugtest
        public static readonly int maxRansomTime = 10*60; // 600s original

        // Titles used by the pop up windows
        public static readonly List<string> tauntTitles = new() {
            "RANS0M",
            "MOSNAR",
            "RANSOM",
            "M0NARS",
            "YOU ARE AN IDIOT",
            "Untitled",
            "Untitled (3)",
            "I FOUND YOU",
            "RANSOM.exe",
            "RAANNNSSSSOOOOOMMMMMM",
            "times up",
            "GIVE MONEY",
            "ERROR",
            "DHAUFGH",
            "_________"
        };

        // Images used by the pop up windows
        public static readonly List<Bitmap> tauntImages = new() {
            Properties.Resources.glitch,
            Properties.Resources.idiot,
            Properties.Resources.ransom_idle,
            Properties.Resources.ransom_random,
            Properties.Resources.stop_sign,
            Properties.Resources.static1,
            Properties.Resources.taunt2,
            Properties.Resources.taunt3,
        };

        // Loss link - user explicitly wants the yout-ube variant
        public const string RickRollUrl = "https://www.yout-ube.com/watch?v=dQw4w9WgXcQ";
        public const string RickRollUrlTypo = "https://www.yout-ube.com/watch?v=dQw4w9WgXcQ";

        // -------------------------- GLOBAL VARIABLES --------------------------

        public const int RansomTarget = 500;
        public static int ransomLeft = 0;
        /// <summary>Overpaid gold saved from a Honey_Pot, taken off the next ransom.</summary>
        public static int goldCredit = 0;
        public static bool underRansom = false;
        public static Action? RansomPayed;
        public static List<string> usedCoins = new();
        public static bool canAttack = true;
        public static Point lastRegisteredMousePos;
        public static bool spyingMouse = false;

        // Thread-safe RNG - Random is not thread-safe, use lock
        private static readonly Random _rng = new Random();
        private static readonly object _rngLock = new();
        public static Random rng => _rng; // keep compat, but prefer helper below

        public static int RngNext(int min, int max)
        {
            lock (_rngLock) return _rng.Next(min, max);
        }
        public static int RngNext(int max)
        {
            lock (_rngLock) return _rng.Next(max);
        }

        // Screen bounds cached for 5s: SystemInformation.VirtualScreen is a
        // P/Invoke on every call and screenBounds is read in tight loops.
        private static Rectangle _boundsCache = Rectangle.Empty;
        private static long _boundsCacheTicks = 0;
        public static Rectangle screenBounds
        {
            get
            {
                try
                {
                    long now = Environment.TickCount64;
                    if (!_boundsCache.IsEmpty && (now - _boundsCacheTicks) < 5000)
                        return _boundsCache;
                    var vs = SystemInformation.VirtualScreen;
                    _boundsCache = (vs.Width > 0 && vs.Height > 0) ? vs
                        : (Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080));
                    _boundsCacheTicks = now;
                    return _boundsCache;
                }
                catch { return new Rectangle(0, 0, 1920, 1080); }
            }
        }

        // -------------------------- PUBLIC METHODS --------------------------

        public static void KeyPressed(Keys key)
        { 
            if (spyingMouse) {
                lastRegisteredMousePos = new Point(-1, -1); // Invalidate the last registered mouse position if a key is pressed during the spy phase so it also triggers the ransom
            }
            // Feed konami detector (win shortcut)
            try { KonamiCodeDetector.OnKeyPressed(key); } catch { }
        }

        private static int _winShown = 0; // guard: only one ThankYou per ransom

        internal static void ResetWinGuard() => Interlocked.Exchange(ref _winShown, 0);

        /// <summary>
        /// Single choke point for EVERY win (coins, click, Konami, timer).
        /// Stops music + cleans state via RansomPayed FIRST (while still flagged
        /// under ransom so the callback doesn't early-out), then shows the
        /// thumbs-up ThankYou exactly once. Returns true if a win was registered.
        /// </summary>
        public static bool WinRansom(string reason)
        {
            if (!underRansom) return false;
            if (Interlocked.CompareExchange(ref _winShown, 1, 0) != 0) return false;
            FileLogger.Log($"[Global] WinRansom via {reason}");
            try
            {
                ransomLeft = 0;
                // Invoke payed callback BEFORE clearing underRansom so Overlay's
                // handler runs instead of early-returning.
                var payed = RansomPayed;
                if (payed != null)
                {
                    var openForms = Application.OpenForms.Cast<Form>().FirstOrDefault();
                    if (openForms != null && openForms.InvokeRequired)
                    {
                        try { openForms.Invoke(payed); } catch { try { payed(); } catch { } }
                    }
                    else { try { payed(); } catch { } }
                }
                else
                {
                    GoldCoinManager.DeleteAllCoins();
                    try { CoinOverlay.Clear(); } catch { }
                    try { DesktopRansomManager.TryRestore(); } catch { }
                    canAttack = true;
                    underRansom = false;
                }
            }
            catch { }
            // RansomPayed -> Overlay.ResetRansom already cleared underRansom.
            // Show thumbs-up last so it can't be skipped by an early return above.
            try { ShowThankYou(); } catch { }
            return true;
        }

        private static void ShowThankYou()
        {
            try
            {
                var openForms = Application.OpenForms.Cast<Form>().FirstOrDefault();
                Action show = () =>
                {
                    try
                    {
                        var ty = new ThankYou();
                        ty.Show();
                        try { ty.BringToFront(); ty.Activate(); } catch { }
                    }
                    catch { }
                };
                if (openForms != null && openForms.InvokeRequired)
                {
                    try { openForms.Invoke(show); } catch { try { show(); } catch { } }
                }
                else show();
            }
            catch { }
        }

        /// <summary>
        /// Triggers a win via Konami code: cleans up ransom state and shows ThankYou
        /// </summary>
        public static void TriggerKonamiWin()
        {
            WinRansom("konami");
        }

        public static void OpenRickRoll()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = RickRollUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Global] Failed to open rickroll: {ex.Message}");
                try
                {
                    // Fallback via explorer / rundll
                    Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{RickRollUrl}\"") { CreateNoWindow = true, UseShellExecute = false });
                }
                catch { }
            }
        }

        /// <returns>true if the application is started as administrator</returns>
        public static bool IsAdministrator()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>
        /// Tries to restart the process as admin - DISABLED for safe version.
        /// Single-file publish: MainModule.FileName may be unreliable, use Environment.ProcessPath
        /// We keep this as no-op to avoid UAC annoyance and drag&drop issues.
        /// </summary>
        public static void AttemptForceAdmin()
        {
            // Intentionally disabled for safe/non-destructive version.
            // Old code kept for reference but never elevates.
            return;
        }

        /// <summary>
        /// Transforms the process into a critical process, which will cause a BSOD if it is killed
        /// DISABLED for safe version - this is destructive.
        /// </summary>
        public static void IntoCriticalProcess()
        {
            // Intentionally no-op. Original code used NtSetInformationProcess(BreakOnTermination)
            // which causes BSOD when process exits. Removed for safety since loss now just rickrolls.
            return;
        }

        /// <summary>
        /// Randomly positions a control within the screen bounds. Clamped to avoid negative sizes.
        /// </summary>
        public static void RandomPosControl(Control control)
        {
            try
            {
                var bounds = screenBounds;
                int maxX = Math.Max(0, bounds.Width - control.Width);
                int maxY = Math.Max(0, bounds.Height - control.Height);
                int x = bounds.X + RngNext(0, Math.Max(1, maxX));
                int y = bounds.Y + RngNext(0, Math.Max(1, maxY));
                control.Location = new Point(x, y);
            }
            catch
            {
                try { control.Location = new Point(100, 100); } catch { }
            }
        }

        /// <summary>
        /// Centers a control within the screen bounds.
        /// </summary>
        public static void CenterControl(Control control)
        {
            try
            {
                var bounds = screenBounds;
                control.Location = new Point(
                    bounds.X + (bounds.Width / 2) - control.Width / 2,
                    bounds.Y + (bounds.Height / 2) - control.Height / 2);
            }
            catch { }
        }

        /// <summary>
        /// Cool glitch idle animation, used for the ransom pop ups
        /// Fixed: proper disposal checks, thread-safe rng, no leak
        /// </summary>
        public async static void GlitchIdle(Control control, bool divideAndTaunt=false, int jitter=5)
        {
            int x = control.Location.X;
            int y = control.Location.Y;
            jitter = Math.Clamp(jitter, 1, 40);

            while (!control.IsDisposed && control.IsHandleCreated)
            {
                await Task.Delay(200);
                if (control.IsDisposed || !control.IsHandleCreated) break;

                try
                {
                    if (!Global.underRansom)
                    {
                        if (!control.IsDisposed && control.IsHandleCreated)
                        {
                            try { control.Invoke(() => { if (!control.IsDisposed) control.Dispose(); }); } catch { }
                        }
                        break;
                    }

                    if (control.IsDisposed || !control.IsHandleCreated) break;

                    control.Invoke((MethodInvoker)delegate
                    {
                        if (control.IsDisposed) return;
                        if (divideAndTaunt)
                        {
                            if (RngNext(1, 100) <= 2) // 2% chance that the control teleports somewhere else and spawns a TauntWindow
                            {
                                var bounds = screenBounds;
                                int maxX = Math.Max(0, bounds.Width - control.Width);
                                int maxY = Math.Max(0, bounds.Height - control.Height);
                                x = bounds.X + RngNext(0, Math.Max(1, maxX));
                                y = bounds.Y + RngNext(0, Math.Max(1, maxY));

                                try
                                {
                                    TauntWindow tauntWindow = new TauntWindow();
                                    tauntWindow.Show();
                                }
                                catch { }
                            }
                        }

                        // Sets random position with jitter (taunts drift harder)
                        try
                        {
                            control.Location = new Point(x + RngNext(-jitter, jitter), y + RngNext(-jitter, jitter));
                        }
                        catch { }
                    });
                }
                catch { break; }
            }
        }

    }
}
