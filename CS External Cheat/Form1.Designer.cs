namespace CS_External_Cheat
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblIsConnected = new Label();
            label1 = new Label();
            SuspendLayout();
            // 
            // lblIsConnected
            // 
            lblIsConnected.AutoSize = true;
            lblIsConnected.Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point);
            lblIsConnected.ForeColor = SystemColors.ButtonHighlight;
            lblIsConnected.Location = new Point(49, 30);
            lblIsConnected.Name = "lblIsConnected";
            lblIsConnected.Size = new Size(142, 28);
            lblIsConnected.TabIndex = 0;
            lblIsConnected.Text = "Connected: No";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = Color.Snow;
            label1.Location = new Point(72, 136);
            label1.Name = "label1";
            label1.Size = new Size(38, 15);
            label1.TabIndex = 1;
            label1.Text = "label1";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(17, 19, 21);
            ClientSize = new Size(800, 450);
            Controls.Add(label1);
            Controls.Add(lblIsConnected);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblIsConnected;
        private Label label1;
    }
}