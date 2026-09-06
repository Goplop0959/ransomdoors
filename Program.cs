using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rans0m
{
    internal static class Program
    {
        private static KeyboardHook keyboardHook = new KeyboardHook();

        [STAThread]
        static void Main()
        {
            FileLogger.Clear();
            FileLogger.Log("=== RANSOM START ===");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) =>
            {
                FileLogger.Log($"[Program] ThreadException: {e.Exception}");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                FileLogger.Log($"[Program] Unhandled: {e.ExceptionObject}");
            };

            ApplicationConfiguration.Initialize();
            // Global.AttemptForceAdmin(); Disabled - causes drag&drop issues and UAC

            // Unpack embedded assets to the created %TEMP%\Ransom_A-90 folder
            try { AssetManager.EnsureAssets(); } catch { }
            // Warm everything in the background so the first ransom has no hitches:
            // audio bytes decoded, wallpaper frames pre-extracted at native res.
            _ = Task.Run(() =>
            {
                try
                {
                    AudioEngine.Preload(
                        ("spawn", Properties.Resources.spawn),
                        ("attack", Properties.Resources.attack),
                        ("install", Properties.Resources.install),
                        ("cash", Properties.Resources.cash),
                        ("layer1", Properties.Resources.layer1),
                        ("layer2", Properties.Resources.layer2),
                        ("layer3", Properties.Resources.layer3));
                }
                catch { }
                try { WallpaperAnimator.Prepare(); } catch { }
            });
            // Auto-restore if previous run was force-stopped and left restore.json (file ops revert, icons, wallpaper)
            try { DesktopRansomManager.TryRestoreIfNeeded(); } catch (Exception ex) { Debug.WriteLine($"[Program] RestoreIfNeeded fail: {ex.Message}"); }
            // Ensure gif placeholder exists for future ransom
            try { DesktopRansomManager.EnsureGifExists(); } catch { }

            // Hook global keys for movement detection and Konami code
            keyboardHook.KeyPressed += Global.KeyPressed;
            keyboardHook.KeyPressed += (k) => FileLogger.Log($"[Key] {k}");
            // Konami+Shift (Up Up Down Down Left Right Left Right B A Shift):
            // - ransom active -> thumbs-up win first, THEN end the process
            // - idle (can spawn, nothing active) -> just end the process
            KonamiCodeDetector.CodeEntered += () =>
            {
                try
                {
                    bool active = Global.underRansom;
                    FileLogger.Log($"[Program] Konami code detected (active={active})");
                    if (active)
                    {
                        // WinRansom shows the ThankYou thumbs-up and cleans state.
                        // Wait until it has been visible (~4s, it auto-closes at 3.5s)
                        // BEFORE exiting, otherwise the process dies with it unseen.
                        var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Overlay);
                        try
                        {
                            if (form != null && form.InvokeRequired)
                                form.Invoke(() => Global.WinRansom("konami"));
                            else Global.WinRansom("konami");
                        }
                        catch { try { Global.WinRansom("konami"); } catch { } }
                        Task.Delay(4000).ContinueWith(_ =>
                        {
                            FileLogger.Log("[Konami] Stopping exe after thumbs-up");
                            try { DesktopRansomManager.TryRestore(); } catch { }
                            try { GoldCoinManager.DeleteAllCoins(); } catch { }
                            try { CoinOverlay.Clear(); } catch { }
                            Environment.Exit(0);
                        });
                    }
                    else
                    {
                        FileLogger.Log("[Konami] Idle - ending process");
                        try { DesktopRansomManager.TryRestore(); } catch { }
                        try { GoldCoinManager.DeleteAllCoins(); } catch { }
                        try { CoinOverlay.Clear(); } catch { }
                        Environment.Exit(0);
                    }
                }
                catch (Exception ex) { FileLogger.Log($"[Konami] win trigger err: {ex.Message}"); try { Environment.Exit(0); } catch { } }
            };

            try { keyboardHook.Hook(); FileLogger.Log("[Program] Hook installed"); } catch (Exception ex) { FileLogger.Log($"[Program] Hook failed: {ex.Message}"); }

            // Cleanup orphaned gold files from previous crash (if any) - but don't delete if currently under ransom?
            // Only clean if not already tracking coins? We'll clean on startup to avoid leaks from killed process
            // But safe version: don't auto-clean on startup if registry already has coins - could be mid-game after explorer restart
            // So we skip auto-clean; Delete will happen on next ResetRansom

            Application.Run(new Overlay());

            try { keyboardHook.Unhook(); } catch { }
            KonamiCodeDetector.CodeEntered -= () => Global.TriggerKonamiWin();
            keyboardHook.Dispose();
        }
    }
}
