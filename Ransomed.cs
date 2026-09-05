using NAudio.Wave;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace rans0m
{
    public partial class Ransomed : Form
    {
        private int remainingTime = 3 * 26; // 78 seconds - matches 3*26s music layers
        private System.Windows.Forms.Timer? topMostTimer;

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;
            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        public Ransomed() { InitializeComponent(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { DragDropFix.Allow(this.Handle); } catch { }
        }

        private void Ransomed_Load(object sender, EventArgs e)
        {
            lbl_time.Text = $"TIME: {remainingTime / 60:D2}:{remainingTime % 60:D2}";
            txt_cashToPay.Text = Global.ransomLeft.ToString();
            timer1.Start();
            Global.RandomPosControl(this);
            FileLogger.Log($"[Ransomed] Load at {Location} time {lbl_time.Text} cash {txt_cashToPay.Text}");

            for (int i = 0; i < 6; i++)
            {
                try { var w = new TauntWindow(); w.Show(); } catch { }
            }

            _ = Task.Run(() => Global.GlitchIdle(this, true));
            SetupTopMost();
        }

        private void SetupTopMost()
        {
            try
            {
                topMostTimer = new System.Windows.Forms.Timer { Interval = 200 };
                topMostTimer.Tick += (s, e) =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE); } catch { }
                    // Ensure labels are visible and on top
                    try { lbl_time.BringToFront(); txt_cashToPay.BringToFront(); } catch { }
                };
                topMostTimer.Start();
                FileLogger.Log("[Ransomed] TopMost timer started");
            }
            catch { }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            remainingTime--;
            if (remainingTime < 0) remainingTime = 0;
            txt_cashToPay.Text = Global.ransomLeft.ToString();
            lbl_time.Text = $"TIME: {remainingTime / 60:D2}:{remainingTime % 60:D2}";

            if (remainingTime <= 0)
            {
                timer1.Stop();
                // Time up -> trigger loss if still under ransom and not already won
                if (Global.underRansom && Global.ransomLeft > 0)
                {
                    // Prevent multiple triggers
                    Global.underRansom = false;
                    try
                    {
                        var overlay = Application.OpenForms.OfType<Overlay>().FirstOrDefault();
                        if (overlay != null)
                        {
                            overlay.BeginInvoke(async () => await overlay.LoseGame());
                        }
                        else
                        {
                            Global.OpenRickRoll();
                            Close();
                        }
                    }
                    catch { try { Global.OpenRickRoll(); } catch { } }
                    try { Close(); } catch { }
                }
            }

            // Also update cash even if ransomLeft changed externally (konami)
            if (Global.ransomLeft <= 0 && Global.underRansom)
            {
                // Should have been handled by dragdrop but handle konami win
                timer1.Stop();
                Global.underRansom = false;
                try { new ThankYou().Show(); } catch { }
                try { Dispose(); } catch { }
            }
        }


        // ------------ EVENT HANDLERS ------------------------------------------
        private void Ransomed_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Any(f => f.EndsWith(".gold", StringComparison.OrdinalIgnoreCase)))
                    e.Effect = DragDropEffects.Link;
                else
                    e.Effect = DragDropEffects.None;
            }
        }

        private void Ransomed_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null) return;
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;

            bool sfxPlayed = false;
            foreach (string file in files)
            {
                if (!file.EndsWith(".gold", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (!File.Exists(file)) continue;
                    Dictionary<string, string> goldFileData = GoldCoinManager.DecryptCoinFile(file);

                    if (!goldFileData.TryGetValue("RANSOM_COIN", out var coinId)) continue;
                    if (Global.usedCoins.Contains(coinId)) continue;

                    // Get coin value (25/30/50/75/100) - default 100 for old files
                    int coinValue = 100;
                    if (goldFileData.TryGetValue("VALUE", out var valStr) && int.TryParse(valStr, out var parsed))
                        coinValue = parsed;
                    else if (goldFileData.TryGetValue("COIN_VALUE", out var valStr2) && int.TryParse(valStr2, out var parsed2))
                        coinValue = parsed2;

                    if (!sfxPlayed)
                    {
                        try
                        {
                            WaveOut cashSfx = SoundHelper.Create(Properties.Resources.cash);
                            cashSfx.Volume = 0.5f;
                            cashSfx.Play();
                        }
                        catch { }
                        sfxPlayed = true;
                    }

                    Global.usedCoins.Add(coinId);
                    Global.ransomLeft -= coinValue;
                    if (Global.ransomLeft < 0) Global.ransomLeft = 0;
                    txt_cashToPay.Text = Global.ransomLeft.ToString();
                    FileLogger.Log($"[Ransomed] Coin {coinId} value {coinValue} -> remaining {Global.ransomLeft}");

                    try { File.Delete(file); } catch { }
                } 
                catch (Exception ex) { Debug.WriteLine($"[Ransomed] coin error {ex.Message}"); }
            }

            if (Global.ransomLeft <= 0)
            {
                timer1.Stop();
                Global.underRansom = false;
                try { new ThankYou().Show(); } catch { }
                try { Dispose(); } catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { timer1.Stop(); } catch { }
            try { topMostTimer?.Stop(); topMostTimer?.Dispose(); } catch { }
            FileLogger.Log("[Ransomed] Closing");
            base.OnFormClosing(e);
        }
    }
}
