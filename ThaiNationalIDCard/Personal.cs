/* BSD license
 * Credit:  APDU Command from  Mr.Manoi http://hosxp.net/index.php?option=com_smf&topic=22496
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
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
        private string[] _address_parts; // lazy cache of the trimmed '#' fields of _address; reset by the Address setter

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

        public string Info
        {
            set
            {
                _personal = value;
                _th_personal = _personal.Substring(0, 100).Split('#');
                _en_personal = _personal.Substring(100, 100).Split('#');
            }
        }

        public string Address
        {
            set
            {
                _address = value.Trim();
                _address_parts = null; // invalidate the split cache
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
                return AddressPart(0);
            }
        }


        public string addrVillageNo
        {
            get
            {
                return AddressPart(1);
            }
        }


        public string addrLane
        {
            get
            {
                return AddressPart(2);
            }
        }

        public string addrRoad
        {
            get
            {
                return AddressPart(3);
            }
        }

        public string addrTambol
        {
            get
            {
                return AddressPart(5);
            }
        }

        public string addrAmphur
        {
            get
            {
                return AddressPart(6);
            }
        }

        public string addrProvince
        {
            get
            {
                return AddressPart(7);
            }
        }

        // Splits _address once and caches the trimmed parts (was: Split on every getter call).
        private string AddressPart(int index)
        {
            string[] parts = _address_parts;
            if (parts == null)
            {
                parts = _address.Split('#');
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = parts[i].Trim();
                _address_parts = parts;
            }
            return parts[index];
        }

        // Parses yyyyMMdd (Buddhist year) at offset; month/day outside range are clamped instead of throwing.
        private static DateTime ClampedDate(string value, int offset)
        {
            var year = Convert.ToInt32(value.Substring(offset, 4)) - 543;
            var month = Convert.ToInt32(value.Substring(offset + 4, 2));
            var day = Convert.ToInt32(value.Substring(offset + 6, 2));

            // Month/day 00 would throw in DateTime; use the same lower clamp as Birthday and the upper clamp Expire had.
            month = month < 1 ? 1 : (month > 12 ? 12 : month);
            day = day < 1 ? 1 : (day > 31 ? 31 : day);
            // 31 in a 30-day month (or 29/30 in February) would throw too; clamp to the month length.
            int daysInMonth = DateTime.DaysInMonth(year, month);
            day = day > daysInMonth ? daysInMonth : day;

            return new DateTime(year, month, day);
        }

        public string Issue_Expire
        {
            set
            {
                _issue_expire = value;
            }
        }

        public DateTime Issue
        {
            get
            {
                return ClampedDate(_issue_expire, 0);
            }
        }

        public DateTime Expire
        {
            get
            {
                return ClampedDate(_issue_expire, 8);
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
                year = Convert.ToInt32(_personal.Substring(200, 4));
                year = year > 0 ? year - 543 : dateNow.Year;
                month = Convert.ToInt32(_personal.Substring(204, 2));
                month = month > 0 ? month : 1;
                day = Convert.ToInt32(_personal.Substring(206, 2));
                day = day > 0 ? day : 1;

                return new DateTime(year, month, day);
            }
        }
        
        public string BirthdayYearString
        {
            get
            {
                return (Convert.ToInt32(_personal.Substring(200, 4)) - 543).ToString();
            }
        }

        public string Sex
        {
            get
            {
                return _personal.Substring(208, 1);
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
