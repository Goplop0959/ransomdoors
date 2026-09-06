namespace rans0m
{
    partial class Ransomed
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            pictureBox1 = new PictureBox();
            label1 = new Label();
            label2 = new Label();
            lbl_time = new Label();
            txt_cashToPay = new Label();
            timer1 = new System.Windows.Forms.Timer(components);
            pictureBox2 = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Image = Properties.Resources.ransom_idle;
            pictureBox1.Location = new Point(28, 12);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(188, 185);
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // label1
            // 
            label1.Font = new Font("Consolas", 33F, FontStyle.Bold);
            label1.ForeColor = Color.White;
            label1.Location = new Point(233, 9);
            label1.Name = "label1";
            label1.Size = new Size(267, 154);
            label1.TabIndex = 1;
            label1.Text = "YOUR FILES HAVE BEEN ENCRYPTED";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // label2
            // 
            label2.BackColor = Color.Black;
            label2.BorderStyle = BorderStyle.Fixed3D;
            label2.Font = new Font("Consolas", 15.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label2.ForeColor = Color.White;
            label2.Location = new Point(12, 166);
            label2.Name = "label2";
            label2.Size = new Size(520, 76);
            label2.TabIndex = 2;
            label2.Text = "IF YOU DO NOT PAY THIS RANSOM BY THE END OF THE TIMER, YOUR FILES WILL BE UNRECOVERABLE BY ANY MEANS.";
            label2.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // lbl_time
            // 
            lbl_time.BackColor = Color.Red;
            lbl_time.BorderStyle = BorderStyle.Fixed3D;
            lbl_time.Font = new Font("Consolas", 36F, FontStyle.Bold, GraphicsUnit.Point, 0);
            lbl_time.ForeColor = Color.Black;
            lbl_time.Location = new Point(206, 245);
            lbl_time.Name = "lbl_time";
            lbl_time.Size = new Size(326, 61);
            lbl_time.TabIndex = 3;
            lbl_time.Text = "TIME: 00:00";
            lbl_time.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // txt_cashToPay
            // 
            txt_cashToPay.BackColor = Color.Black;
            txt_cashToPay.Font = new Font("Consolas", 36F, FontStyle.Bold, GraphicsUnit.Point, 0);
            txt_cashToPay.ForeColor = Color.Gold;
            txt_cashToPay.Location = new Point(12, 246);
            txt_cashToPay.Name = "txt_cashToPay";
            txt_cashToPay.Size = new Size(118, 61);
            txt_cashToPay.TabIndex = 4;
            txt_cashToPay.Text = "500";
            txt_cashToPay.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // timer1
            // 
            timer1.Interval = 1000;
            timer1.Tick += timer1_Tick;
            // 
            // pictureBox2
            // 
            pictureBox2.BackColor = Color.Black;
            pictureBox2.Image = Properties.Resources.Gold;
            pictureBox2.Location = new Point(134, 246);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(66, 61);
            pictureBox2.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox2.TabIndex = 5;
            pictureBox2.TabStop = false;
            // 
            // Ransomed
            // 
            AllowDrop = false;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.Red;
            ClientSize = new Size(544, 315);
            ControlBox = false;
            Controls.Add(pictureBox2);
            Controls.Add(txt_cashToPay);
            Controls.Add(lbl_time);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(pictureBox1);
            MaximizeBox = false;
            MaximumSize = new Size(560, 354);
            MdiChildrenMinimizedAnchorBottom = false;
            MinimizeBox = false;
            MinimumSize = new Size(560, 354);
            Name = "Ransomed";
            ShowIcon = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.Manual;
            Text = "RANS0M";
            TopMost = true;
            Load += Ransomed_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox1;
        private Label label1;
        private Label label2;
        private Label lbl_time;
        private Label txt_cashToPay;
        private System.Windows.Forms.Timer timer1;
        private PictureBox pictureBox2;
    }
}