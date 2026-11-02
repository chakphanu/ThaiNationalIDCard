/* BSD license
 * Credit:  APDU Command from  Mr.Manoi http://hosxp.net/index.php?option=com_smf&topic=22496
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// TODO(phase 4): PhotoBitmap was removed from netstandard2.0/net8.0 because it needs WPF and System.Drawing.
// The ThaiNationalIDCard.Windows package will provide a replacement helper. The old code is kept
// below behind the LEGACY_PHOTO_BITMAP symbol, which is never defined.
#if LEGACY_PHOTO_BITMAP
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
#endif
using System.IO;

namespace ThaiNationalIDCard
{
    public class Personal
    {
        private string _personal;
        private string _address;
        private string _issue_expire;
        private string _issuer;
        private string[] _th_personal;
        private string[] _en_personal;

        private byte[] _photo_jpeg;

        public string Citizenid { get; set; }

        public byte[] PhotoRaw
        {
            get
            {
                return _photo_jpeg;
            }
            set
            {
                _photo_jpeg = value;
            }
        }

#if LEGACY_PHOTO_BITMAP
        public System.Drawing.Bitmap PhotoBitmap
        {
            get
            {
                if (_photo_jpeg == null)
                    return null;
                JpegBitmapDecoder decoder = new JpegBitmapDecoder(new MemoryStream(_photo_jpeg), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                BitmapSource bitmapSource = decoder.Frames[0];
                using (System.IO.MemoryStream outStream = new System.IO.MemoryStream())
                {
                    BitmapEncoder enc = new BmpBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bitmapSource));
                    enc.Save(outStream);
                    return new System.Drawing.Bitmap(outStream);
                }
            }
        }
#endif

        public string Info
        {
            set
            {
                _personal = value;
                _th_personal = _personal.Substring(CardConstants.ThaiNameOffset, CardConstants.NameFieldLength).Split('#');
                _en_personal = _personal.Substring(CardConstants.EnglishNameOffset, CardConstants.NameFieldLength).Split('#');
            }
        }

        public string Address
        {
            set
            {
                _address = value.Trim();
            }
            get
            {
                return _address.Replace('#', ' ');
            }
        }

        public string addrHouseNo
        {
            get
            {
                return _address.Split('#')[0].Trim();
            }
        }


        public string addrVillageNo
        {
            get
            {
                return _address.Split('#')[1].Trim();
            }
        }


        public string addrLane
        {
            get
            {
                return _address.Split('#')[2].Trim();
            }
        }

        public string addrRoad
        {
            get
            {
                return _address.Split('#')[3].Trim();
            }
        }

        public string addrTambol
        {
            get
            {
                return _address.Split('#')[5].Trim();
            }
        }

        public string addrAmphur
        {
            get
            {
                return _address.Split('#')[6].Trim();
            }
        }

        public string addrProvince
        {
            get
            {
                return _address.Split('#')[7].Trim();
            }
        }

        public string Issue_Expire
        {
            set
            {
                _issue_expire = value;
            }
        }

        // KNOWN BUG (phase 3): (e) Issue/Expire/Birthday date getters: Issue has no month/day guard,
        // Expire only clamps month/day upward (00 throws), Birthday maps year 0 to the current year.
        public DateTime Issue
        {
            get
            {
                return new DateTime(
                    Convert.ToInt32(_issue_expire.Substring(0, 4)) - 543,
                    Convert.ToInt32(_issue_expire.Substring(4, 2)),
                    Convert.ToInt32(_issue_expire.Substring(6, 2))
                    );
            }
        }

        public DateTime Expire
        {
            get
            {
                var year = Convert.ToInt32(_issue_expire.Substring(8, 4)) - 543;
                var month = Convert.ToInt32(_issue_expire.Substring(12, 2));
                var day = Convert.ToInt32(_issue_expire.Substring(14, 2));

                return new DateTime(year, month > 12 ? 12 : month, day > 31 ? 31 : day);
            }
        }

        public string ExpireString
        {
            get
            {
                return Expire.ToString("yyyyMMdd");
            }
        }

        public DateTime Birthday 
        {
            get
            {
                var dateNow = DateTime.Now;
                int year, month, day;
                // KNOWN BUG (phase 3): (e) year 0000 silently becomes the current year (wrong data).
                year = Convert.ToInt32(_personal.Substring(CardConstants.BirthYearOffset, CardConstants.BirthYearLength));
                year = year > 0 ? year - 543 : dateNow.Year;
                month = Convert.ToInt32(_personal.Substring(CardConstants.BirthMonthOffset, CardConstants.DatePartLength));
                month = month > 0 ? month : 1;
                day = Convert.ToInt32(_personal.Substring(CardConstants.BirthDayOffset, CardConstants.DatePartLength));
                day = day > 0 ? day : 1;

                return new DateTime(year, month, day);
            }
        }
        
        public string BirthdayYearString
        {
            get
            {
                return (Convert.ToInt32(_personal.Substring(CardConstants.BirthYearOffset, CardConstants.BirthYearLength)) - 543).ToString();
            }
        }

        public string Sex
        {
            get
            {
                return _personal.Substring(CardConstants.SexOffset, CardConstants.SexLength);
            }
        }

        public string Th_Prefix
        {
            get
            {
                return _th_personal[0].Trim();
            }
        }

        public string Th_Firstname
        {
            get
            {
                return _th_personal[1].Trim();
            }
        }

        public string Th_Middlename
        {
            get
            {
                return _th_personal[2].Trim();
            }
        }

        public string Th_Lastname
        {
            get
            {
                return _th_personal[3].Trim();
            }
        }

        public string En_Prefix
        {
            get
            {
                return _en_personal[0].Trim();
            }
        }

        public string En_Firstname
        {
            get
            {
                return _en_personal[1].Trim();
            }
        }
        public string En_Middlename
        {
            get
            {
                return _en_personal[2].Trim();
            }
        }

        public string En_Lastname
        {
            get
            {
                return _en_personal[3].Trim();
            }
        }

        public string Issuer
        {
            get
            {
                return _issuer;
            }
            set
            {
                _issuer = value;
            }
        }


    }
}
