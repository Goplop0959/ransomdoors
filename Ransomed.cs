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
            // Drag-and-drop retired: coins are click-only overlay popups now.
        }

        private static int _popupIndex = 0;

        private void Ransomed_Load(object sender, EventArgs e)
        {
            // Fix time-left not showing: force visible, front, repaint, and mirror in title
            try
            {
                lbl_time.Visible = true;
                lbl_time.Enabled = true;
                lbl_time.BringToFront();
                lbl_time.Text = $"TIME: {remainingTime / 60:D2}:{remainingTime % 60:D2}";
                lbl_time.Refresh();
                Text = $"RANS0M - {lbl_time.Text}";
            }
            catch { }
            txt_cashToPay.Text = Global.ransomLeft.ToString();
            try { txt_cashToPay.Visible = true; txt_cashToPay.BringToFront(); } catch { }
            timer1.Start();
            Global.RandomPosControl(this);
            FileLogger.Log($"[Ransomed] Load at {Location} time {lbl_time.Text} cash {txt_cashToPay.Text}");

            // Single popup at a time, cycling to a different one when it closes
            SpawnNextPopup();

            // Click-to-collect: single click on gold icon collects one coin (no drag needed)
            try
            {
                pictureBox2.Cursor = Cursors.Hand;
                pictureBox2.Click += (_, __) =>
                {
                    try
                    {
                        var r = GoldCoinManager.CollectAnyDesktopCoin();
                        FileLogger.Log($"[Ransomed] Gold picture click collected {r.Value} honey={r.IsHoneyPot}, remaining {Global.ransomLeft}");
                        if (r.Value <= 0)
                        {
                            // Still give feedback even if no file (e.g. already collected via overlay)
                            txt_cashToPay.Text = Global.ransomLeft.ToString();
                        }
                        RefreshAfterCollect();
                    }
                    catch { }
                };
            }
            catch { }

            _ = Task.Run(() => Global.GlitchIdle(this, true));
            SetupTopMost();
        }

        private const int PopupTarget = 7; // keep 7 taunts on screen at once

        private void SpawnNextPopup()
        {
            try
            {
                if (!Global.underRansom || IsDisposed) return;
                int alive = Application.OpenForms.OfType<TauntWindow>().Count(f => !f.IsDisposed);
                for (int i = alive; i < PopupTarget; i++)
                {
                    try
                    {
                        var w = new TauntWindow(_popupIndex++);
                        w.FormClosed += (_, __) =>
                        {
                            try
                            {
                                if (Global.underRansom && !IsDisposed && IsHandleCreated)
                                    BeginInvoke(new Action(SpawnNextPopup));
                            }
                            catch { }
                        };
                        w.Show();
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Called after a click-collect so time + cash repaint immediately.</summary>
        public void RefreshAfterCollect()
        {
            try
            {
                if (IsDisposed) return;
                txt_cashToPay.Text = Global.ransomLeft.ToString();
                lbl_time.Text = $"TIME: {remainingTime / 60:D2}:{remainingTime % 60:D2}";
                try { lbl_time.Refresh(); txt_cashToPay.Refresh(); } catch { }
                Text = $"RANS0M - {lbl_time.Text} - {Global.ransomLeft} left";
            }
            catch { }
            try { CheckWin(); } catch { }
        }

        private void CheckWin()
        {
            if (Global.ransomLeft <= 0 && Global.underRansom)
            {
                try { timer1.Stop(); } catch { }
                Global.WinRansom("coins");
                try { Dispose(); } catch { }
            }
        }

        private void SetupTopMost()
        {
            try
            {
                // Labels are fronted once in Load; the tick only re-pins topmost
                // (per-tick BringToFront restacks all 7 popups and causes lag).
                topMostTimer = new System.Windows.Forms.Timer { Interval = 500 };
                topMostTimer.Tick += (s, e) =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE); } catch { }
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
            try { lbl_time.Visible = true; lbl_time.BringToFront(); lbl_time.Refresh(); } catch { }
            try { Text = $"RANS0M - {lbl_time.Text} - {Global.ransomLeft} left"; } catch { }

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
                timer1.Stop();
                Global.WinRansom("coins-external");
                try { Dispose(); } catch { }
            }
        }


        // ------------ CLICK-ONLY ------------------------------------------
        // Drag-and-drop retired: coins are collected by clicking the on-screen
        // coin popups (CoinOverlay) or the gold icon below. Kept intentionally
        // empty so old .gold drops can't be used.

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { timer1.Stop(); } catch { }
            try { topMostTimer?.Stop(); topMostTimer?.Dispose(); } catch { }
            FileLogger.Log("[Ransomed] Closing");
            base.OnFormClosing(e);
        }
    }
}
