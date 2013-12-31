using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ThaiNationalIDCard
{
    class APDU_THAILAND_IDCARD_3B67 : APDU_THAILAND_IDCARD, IAPDU_THAILAND_IDCARD
    {
        // Citizen ID
        public byte[][] CMD_CID()
        {
            return new byte[][] {
                new byte[]{ 0x80, 0xb0, 0x00, 0x04, 0x02, 0x00, 0x0d }, 
                new byte[]{ 0x00, 0xc0, 0x00, 0x01, 0x0d } // 00 c0 00 01 0d
            };
        }

        // Fullname Thai + Eng + BirthDate + Sex
        public byte[][] CMD_PERSON_INFO()
        {
            return new byte[][]{
                new byte[]{ 0x80, 0xb0, 0x00, 0x11, 0x02, 0x00, 0xd1 }, 
                new byte[]{ 0x00, 0xc0, 0x00, 0x01, 0xd1 }  // 00 c0 00 01 d1
            };
        }

        // Address
        public byte[][] CMD_ADDRESS()
        {
            return new byte[][]{
                new byte[]{ 0x80, 0xb0, 0x15, 0x79, 0x02, 0x00, 0x64 }, 
                new byte[]{ 0x00, 0xc0, 0x00, 0x01, 0x64 } // 00 c0 00 01 64
            };
        }

        // issue/expire
        public byte[][] CMD_CARD_ISSUE_EXPIRE()
        {
            return new byte[][]{
                new byte[]{ 0x80, 0xb0, 0x01, 0x67, 0x02, 0x00, 0x12 }, 
                new byte[]{ 0x00, 0xc0, 0x00, 0x01, 0x12 } // 00 c0 00 01 12
            };
        }
    }
}
