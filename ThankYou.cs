using NAudio.Wave;

namespace rans0m
{
    public partial class ThankYou : Form
    {
        public ThankYou()
        {
            InitializeComponent();
        }

        private void ThankYou_Load(object sender, EventArgs e)
        {
            try { Global.RansomPayed?.Invoke(); } catch { }
            try
            {
                WaveOut thankYouSfx = SoundHelper.Create(Properties.Resources.cash);
                thankYouSfx.Play();
            }
            catch { }

            Global.CenterControl(this);

            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                if (IsDisposed || !IsHandleCreated) return;
                try { this.Invoke(() => { if (!IsDisposed) Dispose(); }); } catch { }
            });
        }
    }
}
