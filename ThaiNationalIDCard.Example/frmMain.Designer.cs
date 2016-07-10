namespace ThaiNationalIDCard.Example
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
            this.label13 = new System.Windows.Forms.Label();
            this.lbl_issue = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.btnRefreshReaderList = new System.Windows.Forms.Button();
            this.chkBoxMonitor = new System.Windows.Forms.CheckBox();
            this.cbxReaderList = new System.Windows.Forms.ComboBox();
            this.btnReadWithPhoto = new System.Windows.Forms.Button();
            this.PhotoProgressBar1 = new System.Windows.Forms.ProgressBar();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.lbl_expire = new System.Windows.Forms.Label();
            this.lbl_sex = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.lbl_birthday = new System.Windows.Forms.Label();
            this.lbl_en_lastname = new System.Windows.Forms.Label();
            this.lbl_en_firstname = new System.Windows.Forms.Label();
            this.lbl_en_prefix = new System.Windows.Forms.Label();
            this.lbl_th_lastname = new System.Windows.Forms.Label();
            this.lbl_th_firstname = new System.Windows.Forms.Label();
            this.lbl_th_prefix = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lbl_cid = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.txtBoxLog = new System.Windows.Forms.TextBox();
            this.btnRead = new System.Windows.Forms.Button();
            this.lbLibVersion = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(141, 205);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(60, 13);
            this.label13.TabIndex = 59;
            this.label13.Text = "วันหมดอายุ";
            // 
            // lbl_issue
            // 
            this.lbl_issue.AutoSize = true;
            this.lbl_issue.Location = new System.Drawing.Point(210, 192);
            this.lbl_issue.Name = "lbl_issue";
            this.lbl_issue.Size = new System.Drawing.Size(47, 13);
            this.lbl_issue.TabIndex = 58;
            this.lbl_issue.Text = "lbl_issue";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(141, 192);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(60, 13);
            this.label11.TabIndex = 57;
            this.label11.Text = "วันออกบัตร";
            // 
            // btnRefreshReaderList
            // 
            this.btnRefreshReaderList.Location = new System.Drawing.Point(700, 9);
            this.btnRefreshReaderList.Name = "btnRefreshReaderList";
            this.btnRefreshReaderList.Size = new System.Drawing.Size(124, 23);
            this.btnRefreshReaderList.TabIndex = 56;
            this.btnRefreshReaderList.Text = "Refresh reader list";
            this.btnRefreshReaderList.UseVisualStyleBackColor = true;
            this.btnRefreshReaderList.Click += new System.EventHandler(this.btnRefreshReaderList_Click_1);
            // 
            // chkBoxMonitor
            // 
            this.chkBoxMonitor.AutoSize = true;
            this.chkBoxMonitor.Location = new System.Drawing.Point(564, 13);
            this.chkBoxMonitor.Name = "chkBoxMonitor";
            this.chkBoxMonitor.Size = new System.Drawing.Size(88, 17);
            this.chkBoxMonitor.TabIndex = 55;
            this.chkBoxMonitor.Text = "Auto/Monitor";
            this.chkBoxMonitor.UseVisualStyleBackColor = true;
            this.chkBoxMonitor.CheckedChanged += new System.EventHandler(this.chkBoxMonitor_CheckedChanged_1);
            // 
            // cbxReaderList
            // 
            this.cbxReaderList.FormattingEnabled = true;
            this.cbxReaderList.Location = new System.Drawing.Point(564, 41);
            this.cbxReaderList.Name = "cbxReaderList";
            this.cbxReaderList.Size = new System.Drawing.Size(297, 21);
            this.cbxReaderList.TabIndex = 54;
            // 
            // btnReadWithPhoto
            // 
            this.btnReadWithPhoto.Location = new System.Drawing.Point(329, 12);
            this.btnReadWithPhoto.Name = "btnReadWithPhoto";
            this.btnReadWithPhoto.Size = new System.Drawing.Size(95, 40);
            this.btnReadWithPhoto.TabIndex = 53;
            this.btnReadWithPhoto.Text = "Read with Photo";
            this.btnReadWithPhoto.UseVisualStyleBackColor = true;
            this.btnReadWithPhoto.Click += new System.EventHandler(this.btnReadWithPhoto_Click_1);
            // 
            // PhotoProgressBar1
            // 
            this.PhotoProgressBar1.Location = new System.Drawing.Point(213, 223);
            this.PhotoProgressBar1.MarqueeAnimationSpeed = 0;
            this.PhotoProgressBar1.Name = "PhotoProgressBar1";
            this.PhotoProgressBar1.Size = new System.Drawing.Size(211, 23);
            this.PhotoProgressBar1.TabIndex = 52;
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(564, 130);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(297, 355);
            this.pictureBox1.TabIndex = 51;
            this.pictureBox1.TabStop = false;
            // 
            // imageList1
            // 
            this.imageList1.ColorDepth = System.Windows.Forms.ColorDepth.Depth8Bit;
            this.imageList1.ImageSize = new System.Drawing.Size(16, 16);
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // lbl_expire
            // 
            this.lbl_expire.AutoSize = true;
            this.lbl_expire.Location = new System.Drawing.Point(210, 205);
            this.lbl_expire.Name = "lbl_expire";
            this.lbl_expire.Size = new System.Drawing.Size(51, 13);
            this.lbl_expire.TabIndex = 60;
            this.lbl_expire.Text = "lbl_expire";
            // 
            // lbl_sex
            // 
            this.lbl_sex.AutoSize = true;
            this.lbl_sex.Location = new System.Drawing.Point(210, 179);
            this.lbl_sex.Name = "lbl_sex";
            this.lbl_sex.Size = new System.Drawing.Size(39, 13);
            this.lbl_sex.TabIndex = 50;
            this.lbl_sex.Text = "lbl_sex";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(173, 179);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(28, 13);
            this.label9.TabIndex = 49;
            this.label9.Text = "เพศ";
            // 
            // lbl_birthday
            // 
            this.lbl_birthday.AutoSize = true;
            this.lbl_birthday.Location = new System.Drawing.Point(210, 166);
            this.lbl_birthday.Name = "lbl_birthday";
            this.lbl_birthday.Size = new System.Drawing.Size(60, 13);
            this.lbl_birthday.TabIndex = 48;
            this.lbl_birthday.Text = "lbl_birthday";
            // 
            // lbl_en_lastname
            // 
            this.lbl_en_lastname.AutoSize = true;
            this.lbl_en_lastname.Location = new System.Drawing.Point(210, 143);
            this.lbl_en_lastname.Name = "lbl_en_lastname";
            this.lbl_en_lastname.Size = new System.Drawing.Size(83, 13);
            this.lbl_en_lastname.TabIndex = 47;
            this.lbl_en_lastname.Text = "lbl_en_lastname";
            // 
            // lbl_en_firstname
            // 
            this.lbl_en_firstname.AutoSize = true;
            this.lbl_en_firstname.Location = new System.Drawing.Point(210, 130);
            this.lbl_en_firstname.Name = "lbl_en_firstname";
            this.lbl_en_firstname.Size = new System.Drawing.Size(83, 13);
            this.lbl_en_firstname.TabIndex = 46;
            this.lbl_en_firstname.Text = "lbl_en_firstname";
            // 
            // lbl_en_prefix
            // 
            this.lbl_en_prefix.AutoSize = true;
            this.lbl_en_prefix.Location = new System.Drawing.Point(210, 117);
            this.lbl_en_prefix.Name = "lbl_en_prefix";
            this.lbl_en_prefix.Size = new System.Drawing.Size(66, 13);
            this.lbl_en_prefix.TabIndex = 45;
            this.lbl_en_prefix.Text = "lbl_en_prefix";
            // 
            // lbl_th_lastname
            // 
            this.lbl_th_lastname.AutoSize = true;
            this.lbl_th_lastname.Location = new System.Drawing.Point(210, 94);
            this.lbl_th_lastname.Name = "lbl_th_lastname";
            this.lbl_th_lastname.Size = new System.Drawing.Size(80, 13);
            this.lbl_th_lastname.TabIndex = 44;
            this.lbl_th_lastname.Text = "lbl_th_lastname";
            // 
            // lbl_th_firstname
            // 
            this.lbl_th_firstname.AutoSize = true;
            this.lbl_th_firstname.Location = new System.Drawing.Point(210, 81);
            this.lbl_th_firstname.Name = "lbl_th_firstname";
            this.lbl_th_firstname.Size = new System.Drawing.Size(80, 13);
            this.lbl_th_firstname.TabIndex = 43;
            this.lbl_th_firstname.Text = "lbl_th_firstname";
            // 
            // lbl_th_prefix
            // 
            this.lbl_th_prefix.AutoSize = true;
            this.lbl_th_prefix.Location = new System.Drawing.Point(210, 68);
            this.lbl_th_prefix.Name = "lbl_th_prefix";
            this.lbl_th_prefix.Size = new System.Drawing.Size(63, 13);
            this.lbl_th_prefix.TabIndex = 42;
            this.lbl_th_prefix.Text = "lbl_th_prefix";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(169, 166);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(40, 13);
            this.label8.TabIndex = 41;
            this.label8.Text = "วันเกิด";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(155, 143);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(49, 13);
            this.label7.TabIndex = 40;
            this.label7.Text = "lastname";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(155, 130);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(49, 13);
            this.label6.TabIndex = 39;
            this.label6.Text = "firstname";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(172, 117);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(32, 13);
            this.label5.TabIndex = 38;
            this.label5.Text = "prefix";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(178, 94);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(26, 13);
            this.label4.TabIndex = 37;
            this.label4.Text = "สกุล";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(184, 81);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(20, 13);
            this.label3.TabIndex = 36;
            this.label3.Text = "ชื่อ";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(172, 68);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(32, 13);
            this.label2.TabIndex = 35;
            this.label2.Text = "คำนำ";
            // 
            // lbl_cid
            // 
            this.lbl_cid.AutoSize = true;
            this.lbl_cid.Location = new System.Drawing.Point(210, 55);
            this.lbl_cid.Name = "lbl_cid";
            this.lbl_cid.Size = new System.Drawing.Size(37, 13);
            this.lbl_cid.TabIndex = 34;
            this.lbl_cid.Text = "lbl_cid";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(91, 55);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(113, 13);
            this.label1.TabIndex = 33;
            this.label1.Text = "รหัสประจำตัวประชาชน";
            // 
            // txtBoxLog
            // 
            this.txtBoxLog.Location = new System.Drawing.Point(30, 252);
            this.txtBoxLog.Multiline = true;
            this.txtBoxLog.Name = "txtBoxLog";
            this.txtBoxLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtBoxLog.Size = new System.Drawing.Size(530, 234);
            this.txtBoxLog.TabIndex = 32;
            // 
            // btnRead
            // 
            this.btnRead.Location = new System.Drawing.Point(225, 12);
            this.btnRead.Name = "btnRead";
            this.btnRead.Size = new System.Drawing.Size(98, 40);
            this.btnRead.TabIndex = 31;
            this.btnRead.Text = "Read";
            this.btnRead.UseVisualStyleBackColor = true;
            this.btnRead.Click += new System.EventHandler(this.btnRead_Click);
            // 
            // lbLibVersion
            // 
            this.lbLibVersion.AutoSize = true;
            this.lbLibVersion.Location = new System.Drawing.Point(9, 11);
            this.lbLibVersion.Name = "lbLibVersion";
            this.lbLibVersion.Size = new System.Drawing.Size(41, 13);
            this.lbLibVersion.TabIndex = 61;
            this.lbLibVersion.Text = "label10";
            // 
            // frmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(891, 495);
            this.Controls.Add(this.lbLibVersion);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.lbl_issue);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.btnRefreshReaderList);
            this.Controls.Add(this.chkBoxMonitor);
            this.Controls.Add(this.cbxReaderList);
            this.Controls.Add(this.btnReadWithPhoto);
            this.Controls.Add(this.PhotoProgressBar1);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lbl_expire);
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
            this.Text = "frmMain";
            this.Load += new System.EventHandler(this.frmMain_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label lbl_issue;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Button btnRefreshReaderList;
        private System.Windows.Forms.CheckBox chkBoxMonitor;
        private System.Windows.Forms.ComboBox cbxReaderList;
        private System.Windows.Forms.Button btnReadWithPhoto;
        private System.Windows.Forms.ProgressBar PhotoProgressBar1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.Label lbl_expire;
        private System.Windows.Forms.Label lbl_sex;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label lbl_birthday;
        private System.Windows.Forms.Label lbl_en_lastname;
        private System.Windows.Forms.Label lbl_en_firstname;
        private System.Windows.Forms.Label lbl_en_prefix;
        private System.Windows.Forms.Label lbl_th_lastname;
        private System.Windows.Forms.Label lbl_th_firstname;
        private System.Windows.Forms.Label lbl_th_prefix;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lbl_cid;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtBoxLog;
        private System.Windows.Forms.Button btnRead;
        private System.Windows.Forms.Label lbLibVersion;
    }
}