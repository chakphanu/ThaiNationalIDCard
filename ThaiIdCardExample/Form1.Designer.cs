namespace ThaiIdCardExample
{
    partial class frmMain
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
            this.components = new System.ComponentModel.Container();
            this.btnRead = new System.Windows.Forms.Button();
            this.txtBoxLog = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.lbl_cid = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.lbl_th_prefix = new System.Windows.Forms.Label();
            this.lbl_th_firstname = new System.Windows.Forms.Label();
            this.lbl_th_lastname = new System.Windows.Forms.Label();
            this.lbl_en_prefix = new System.Windows.Forms.Label();
            this.lbl_en_firstname = new System.Windows.Forms.Label();
            this.lbl_en_lastname = new System.Windows.Forms.Label();
            this.lbl_birthday = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.lbl_sex = new System.Windows.Forms.Label();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.btnReadWithPhoto = new System.Windows.Forms.Button();
            this.PhotoProgressBar1 = new System.Windows.Forms.ProgressBar();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // btnRead
            // 
            this.btnRead.Location = new System.Drawing.Point(197, 12);
            this.btnRead.Name = "btnRead";
            this.btnRead.Size = new System.Drawing.Size(98, 40);
            this.btnRead.TabIndex = 0;
            this.btnRead.Text = "Read";
            this.btnRead.UseVisualStyleBackColor = true;
            this.btnRead.Click += new System.EventHandler(this.btnRead_Click);
            // 
            // txtBoxLog
            // 
            this.txtBoxLog.Location = new System.Drawing.Point(2, 252);
            this.txtBoxLog.Multiline = true;
            this.txtBoxLog.Name = "txtBoxLog";
            this.txtBoxLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtBoxLog.Size = new System.Drawing.Size(530, 234);
            this.txtBoxLog.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(63, 55);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(113, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "รหัสประจำตัวประชาชน";
            // 
            // lbl_cid
            // 
            this.lbl_cid.AutoSize = true;
            this.lbl_cid.Location = new System.Drawing.Point(182, 55);
            this.lbl_cid.Name = "lbl_cid";
            this.lbl_cid.Size = new System.Drawing.Size(37, 13);
            this.lbl_cid.TabIndex = 3;
            this.lbl_cid.Text = "lbl_cid";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(144, 68);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(32, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "คำนำ";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(156, 81);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(20, 13);
            this.label3.TabIndex = 5;
            this.label3.Text = "ชื่อ";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(150, 94);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(26, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "สกุล";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(144, 117);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(32, 13);
            this.label5.TabIndex = 7;
            this.label5.Text = "prefix";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(127, 130);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(49, 13);
            this.label6.TabIndex = 8;
            this.label6.Text = "firstname";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(127, 143);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(49, 13);
            this.label7.TabIndex = 9;
            this.label7.Text = "lastname";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(141, 166);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(40, 13);
            this.label8.TabIndex = 10;
            this.label8.Text = "วันเกิด";
            // 
            // lbl_th_prefix
            // 
            this.lbl_th_prefix.AutoSize = true;
            this.lbl_th_prefix.Location = new System.Drawing.Point(182, 68);
            this.lbl_th_prefix.Name = "lbl_th_prefix";
            this.lbl_th_prefix.Size = new System.Drawing.Size(63, 13);
            this.lbl_th_prefix.TabIndex = 11;
            this.lbl_th_prefix.Text = "lbl_th_prefix";
            // 
            // lbl_th_firstname
            // 
            this.lbl_th_firstname.AutoSize = true;
            this.lbl_th_firstname.Location = new System.Drawing.Point(182, 81);
            this.lbl_th_firstname.Name = "lbl_th_firstname";
            this.lbl_th_firstname.Size = new System.Drawing.Size(80, 13);
            this.lbl_th_firstname.TabIndex = 12;
            this.lbl_th_firstname.Text = "lbl_th_firstname";
            // 
            // lbl_th_lastname
            // 
            this.lbl_th_lastname.AutoSize = true;
            this.lbl_th_lastname.Location = new System.Drawing.Point(182, 94);
            this.lbl_th_lastname.Name = "lbl_th_lastname";
            this.lbl_th_lastname.Size = new System.Drawing.Size(80, 13);
            this.lbl_th_lastname.TabIndex = 13;
            this.lbl_th_lastname.Text = "lbl_th_lastname";
            // 
            // lbl_en_prefix
            // 
            this.lbl_en_prefix.AutoSize = true;
            this.lbl_en_prefix.Location = new System.Drawing.Point(182, 117);
            this.lbl_en_prefix.Name = "lbl_en_prefix";
            this.lbl_en_prefix.Size = new System.Drawing.Size(66, 13);
            this.lbl_en_prefix.TabIndex = 14;
            this.lbl_en_prefix.Text = "lbl_en_prefix";
            // 
            // lbl_en_firstname
            // 
            this.lbl_en_firstname.AutoSize = true;
            this.lbl_en_firstname.Location = new System.Drawing.Point(182, 130);
            this.lbl_en_firstname.Name = "lbl_en_firstname";
            this.lbl_en_firstname.Size = new System.Drawing.Size(83, 13);
            this.lbl_en_firstname.TabIndex = 15;
            this.lbl_en_firstname.Text = "lbl_en_firstname";
            // 
            // lbl_en_lastname
            // 
            this.lbl_en_lastname.AutoSize = true;
            this.lbl_en_lastname.Location = new System.Drawing.Point(182, 143);
            this.lbl_en_lastname.Name = "lbl_en_lastname";
            this.lbl_en_lastname.Size = new System.Drawing.Size(83, 13);
            this.lbl_en_lastname.TabIndex = 16;
            this.lbl_en_lastname.Text = "lbl_en_lastname";
            // 
            // lbl_birthday
            // 
            this.lbl_birthday.AutoSize = true;
            this.lbl_birthday.Location = new System.Drawing.Point(182, 166);
            this.lbl_birthday.Name = "lbl_birthday";
            this.lbl_birthday.Size = new System.Drawing.Size(60, 13);
            this.lbl_birthday.TabIndex = 17;
            this.lbl_birthday.Text = "lbl_birthday";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(145, 184);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(28, 13);
            this.label9.TabIndex = 18;
            this.label9.Text = "เพศ";
            // 
            // lbl_sex
            // 
            this.lbl_sex.AutoSize = true;
            this.lbl_sex.Location = new System.Drawing.Point(182, 184);
            this.lbl_sex.Name = "lbl_sex";
            this.lbl_sex.Size = new System.Drawing.Size(39, 13);
            this.lbl_sex.TabIndex = 19;
            this.lbl_sex.Text = "lbl_sex";
            // 
            // imageList1
            // 
            this.imageList1.ColorDepth = System.Windows.Forms.ColorDepth.Depth8Bit;
            this.imageList1.ImageSize = new System.Drawing.Size(16, 16);
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(538, 12);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(297, 355);
            this.pictureBox1.TabIndex = 20;
            this.pictureBox1.TabStop = false;
            // 
            // btnReadWithPhoto
            // 
            this.btnReadWithPhoto.Location = new System.Drawing.Point(301, 12);
            this.btnReadWithPhoto.Name = "btnReadWithPhoto";
            this.btnReadWithPhoto.Size = new System.Drawing.Size(95, 40);
            this.btnReadWithPhoto.TabIndex = 21;
            this.btnReadWithPhoto.Text = "Read with Photo";
            this.btnReadWithPhoto.UseVisualStyleBackColor = true;
            this.btnReadWithPhoto.Click += new System.EventHandler(this.button1_Click);
            // 
            // PhotoProgressBar1
            // 
            this.PhotoProgressBar1.Location = new System.Drawing.Point(185, 215);
            this.PhotoProgressBar1.MarqueeAnimationSpeed = 0;
            this.PhotoProgressBar1.Name = "PhotoProgressBar1";
            this.PhotoProgressBar1.Size = new System.Drawing.Size(211, 23);
            this.PhotoProgressBar1.TabIndex = 22;
            // 
            // frmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(845, 498);
            this.Controls.Add(this.PhotoProgressBar1);
            this.Controls.Add(this.btnReadWithPhoto);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lbl_sex);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.lbl_birthday);
            this.Controls.Add(this.lbl_en_lastname);
            this.Controls.Add(this.lbl_en_firstname);
            this.Controls.Add(this.lbl_en_prefix);
            this.Controls.Add(this.lbl_th_lastname);
            this.Controls.Add(this.lbl_th_firstname);
            this.Controls.Add(this.lbl_th_prefix);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.lbl_cid);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtBoxLog);
            this.Controls.Add(this.btnRead);
            this.Name = "frmMain";
            this.Text = "Thai ID Smartcard Example";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnRead;
        private System.Windows.Forms.TextBox txtBoxLog;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lbl_cid;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label lbl_th_prefix;
        private System.Windows.Forms.Label lbl_th_firstname;
        private System.Windows.Forms.Label lbl_th_lastname;
        private System.Windows.Forms.Label lbl_en_prefix;
        private System.Windows.Forms.Label lbl_en_firstname;
        private System.Windows.Forms.Label lbl_en_lastname;
        private System.Windows.Forms.Label lbl_birthday;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label lbl_sex;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button btnReadWithPhoto;
        private System.Windows.Forms.ProgressBar PhotoProgressBar1;
    }
}

