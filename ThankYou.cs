using NAudio.Wave;
using System.Runtime.InteropServices;

namespace rans0m
{
    public partial class ThankYou : Form
    {
        private System.Windows.Forms.Timer? _topTimer;

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOACTIVATE = 0x0010;
            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        }

        public ThankYou()
        {
            InitializeComponent();
        }

        private void ThankYou_Load(object sender, EventArgs e)
        {
            // Music already stopped by WinRansom -> RansomPayed -> ResetRansom.
            // Just play the cash sfx (fire-and-forget, no early dispose) and stay
            // visible long enough to actually be seen.
            try { SoundHelper.PlayOneShot(Properties.Resources.cash, 0.5f); } catch { }

            try { Global.CenterControl(this); } catch { }
            try { TopMost = true; BringToFront(); Activate(); } catch { }

            try
            {
                _topTimer = new System.Windows.Forms.Timer { Interval = 200 };
                _topTimer.Tick += (_, __) =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE); } catch { }
                    try { BringToFront(); } catch { }
                };
                _topTimer.Start();
            }
            catch { }
            FileLogger.Log("[ThankYou] thumbs-up shown");

            _ = Task.Run(async () =>
            {
                await Task.Delay(3500);
                if (IsDisposed || !IsHandleCreated) return;
                try { this.Invoke(() => { if (!IsDisposed) { try { _topTimer?.Stop(); } catch { } Dispose(); } }); } catch { }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _topTimer?.Stop(); _topTimer?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
