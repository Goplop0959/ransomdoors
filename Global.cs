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

        // Rickroll URL - opened on loss instead of shutdown/BSOD
        // User requested https://www.yout-ube.com/watch?v=dQw4w9WgXcQ (typo variant)
        // We normalize to the valid youtube domain
        public const string RickRollUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        public const string RickRollUrlTypo = "https://www.yout-ube.com/watch?v=dQw4w9WgXcQ";

        // -------------------------- GLOBAL VARIABLES --------------------------

        public static int ransomLeft = 0;
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

        // Dynamically compute screen bounds to support multi-monitor and DPI changes
        public static Rectangle screenBounds
        {
            get
            {
                try
                {
                    var vs = SystemInformation.VirtualScreen;
                    if (vs.Width > 0 && vs.Height > 0) return vs;
                    var ps = Screen.PrimaryScreen;
                    if (ps != null) return ps.WorkingArea;
                    return new Rectangle(0, 0, 1920, 1080);
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

        /// <summary>
        /// Triggers a win via Konami code: cleans up ransom state and shows ThankYou
        /// </summary>
        public static void TriggerKonamiWin()
        {
            if (!underRansom) return; // only meaningful during ransom
            // Prevent re-entrance
            if (ransomLeft <= 0) return;
            Debug.WriteLine("[Global] Konami win triggered!");
            // Simulate paying ransom instantly
            ransomLeft = 0;
            // Invoke payed callback on UI thread if needed - caller should handle invoke
            // But we try direct
            try
            {
                // Clear used coins and delete files via overlay reset will handle
                // Duplicate logic from Ransomed drag drop win
                underRansom = false;
                // RansomPayed will be invoked by overlay or we invoke now if on UI thread
                // We'll let overlay handle showing ThankYou; but ensure coins cleaned
                // Use a helper to find overlay and invoke
                // If RansomPayed is set, invoke it
                var payed = RansomPayed;
                if (payed != null)
                {
                    // Try to invoke via any open form's Invoke
                    var openForms = Application.OpenForms.Cast<Form>().FirstOrDefault();
                    if (openForms != null && openForms.InvokeRequired)
                    {
                        try { openForms.Invoke(payed); } catch { payed(); }
                    }
                    else payed();
                }
                else
                {
                    // Fallback: just clean and show thank you if possible
                    GoldCoinManager.DeleteAllCoins();
                    usedCoins.Clear();
                    canAttack = true;
                    underRansom = false;
                }
            }
            catch { }
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
        public async static void GlitchIdle(Control control, bool divideAndTaunt=false)
        {
            int x = control.Location.X;
            int y = control.Location.Y;

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

                        // Sets random position with small jitter
                        try
                        {
                            control.Location = new Point(x + RngNext(-5, 5), y + RngNext(-5, 5));
                        }
                        catch { }
                    });
                }
                catch { break; }
            }
        }

    }
}
