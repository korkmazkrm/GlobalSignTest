namespace GlobalSignTest
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
            button1 = new Button();
            buttonSelectPdf = new Button();
            labelSelectedFile = new Label();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Location = new Point(12, 47);
            button1.Name = "button1";
            button1.Size = new Size(153, 29);
            button1.TabIndex = 0;
            button1.Text = "İmzala";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // buttonSelectPdf
            // 
            buttonSelectPdf.Location = new Point(12, 12);
            buttonSelectPdf.Name = "buttonSelectPdf";
            buttonSelectPdf.Size = new Size(153, 29);
            buttonSelectPdf.TabIndex = 1;
            buttonSelectPdf.Text = "PDF Seç";
            buttonSelectPdf.UseVisualStyleBackColor = true;
            buttonSelectPdf.Click += buttonSelectPdf_Click;
            // 
            // labelSelectedFile
            // 
            labelSelectedFile.AutoEllipsis = true;
            labelSelectedFile.Location = new Point(171, 12);
            labelSelectedFile.Name = "labelSelectedFile";
            labelSelectedFile.Size = new Size(426, 29);
            labelSelectedFile.TabIndex = 2;
            labelSelectedFile.Text = "Seçilen dosya yok";
            labelSelectedFile.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1045, 450);
            Controls.Add(labelSelectedFile);
            Controls.Add(buttonSelectPdf);
            Controls.Add(button1);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private Button button1;
        private Button buttonSelectPdf;
        private Label labelSelectedFile;
    }
}
