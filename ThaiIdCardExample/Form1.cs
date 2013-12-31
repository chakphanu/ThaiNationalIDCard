using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.IO;
using ThaiNationalIDCard;


namespace ThaiIdCardExample
{

    public partial class frmMain : Form
    {







        public frmMain()
        {
            InitializeComponent();
        }




        public void Log(string text = "")
        {
            if (txtBoxLog.InvokeRequired)
            {
                txtBoxLog.BeginInvoke(new MethodInvoker(delegate { txtBoxLog.AppendText(text); }));
            }
            else
            {
                txtBoxLog.AppendText(text);
            }
        }

        public void LogLine(string text = "")
        {
            if (txtBoxLog.InvokeRequired)
            {
                txtBoxLog.BeginInvoke(new MethodInvoker(delegate { txtBoxLog.AppendText(text + Environment.NewLine); }));
            }
            else
            {
                txtBoxLog.AppendText(text + Environment.NewLine);
            }
        }



        private void btnRead_Click(object sender, EventArgs e)
        {
            ThaiIDCard idcard = new ThaiIDCard();
            Personal personal = idcard.readAll();
            if (personal != null)
            {
                lbl_cid.Text = personal.Citizenid;
                lbl_birthday.Text = personal.Birthday.ToString("dd/MM/yyyy");
                lbl_sex.Text = personal.Sex;
                lbl_th_prefix.Text = personal.Th_Prefix;
                lbl_th_firstname.Text = personal.Th_Firstname;
                lbl_th_lastname.Text = personal.Th_Lastname;
                lbl_en_prefix.Text = personal.En_Prefix;
                lbl_en_firstname.Text = personal.En_Firstname;
                lbl_en_lastname.Text = personal.En_Lastname;

                // ขี้เกรียจวาด label แล้ว
                LogLine(personal.Issue.ToString());
                LogLine(personal.Expire.ToString());

                LogLine(personal.Address);
                LogLine(personal.addrHouseNo); // บ้านเลขที่ 
                LogLine(personal.addrVillageNo); // หมู่ที่
                LogLine(personal.addrLane); // ซอย
                LogLine(personal.addrRoad); // ถนน
                LogLine(personal.addrTambol); 
                LogLine(personal.addrAmphur);
                LogLine(personal.addrProvince);
            }
            else if (idcard.ErrorCode() > 0)
            {
                MessageBox.Show(idcard.Error());
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ThaiIDCard idcard = new ThaiIDCard();
            idcard.eventPhotoProgress += new handlePhotoProgress(photoProgress);
            Personal personal = idcard.readAllPhoto();
            if (personal != null)
            {
                lbl_cid.Text = personal.Citizenid;
                lbl_birthday.Text = personal.Birthday.ToString("dd/MM/yyyy");
                lbl_sex.Text = personal.Sex;
                lbl_th_prefix.Text = personal.Th_Prefix;
                lbl_th_firstname.Text = personal.Th_Firstname;
                lbl_th_lastname.Text = personal.Th_Lastname;
                lbl_en_prefix.Text = personal.En_Prefix;
                lbl_en_firstname.Text = personal.En_Firstname;
                lbl_en_lastname.Text = personal.En_Lastname;
                pictureBox1.Image = personal.PhotoBitmap;
            }
            else if (idcard.ErrorCode() > 0)
            {
                MessageBox.Show(idcard.Error());
            }
        }

        private void photoProgress(int value, int maximum)
        {
            if (PhotoProgressBar1.Maximum != maximum)
                PhotoProgressBar1.Maximum = maximum;
            
            // fix progress bar sync.
            if (PhotoProgressBar1.Maximum > value)
                PhotoProgressBar1.Value = value + 1;
            
            PhotoProgressBar1.Value = value;
            //LogLine(String.Format("{0} of {1}", value, maximum));
        }
    }
}
