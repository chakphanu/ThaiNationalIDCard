using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ThaiNationalIDCard
{
    class APDU_THAILAND_IDCARD
    {
        // Select/Reset
        public byte[][] CMD_SELECT()
        {
            return new byte[][] {
                new byte[] { 0x00, 0xA4, 0x04, 0x00, 0x08, 0xA0, 0x00, 0x00, 0x00, 0x54, 0x48, 0x00, 0x01 }
            };
        }

        // photo
        public CMD_PAIR[] GET_CMD_CARD_PHOTO()
        {
            CMD_PAIR[] cmds = new CMD_PAIR[21];
            for (int i = 0; i <= 20; i++)
            {
                int xwd;
                int xof = i * 254 + 379;
                if (i == 20)
                    xwd = 38;
                else
                    xwd = 254;

                int sp2 = (xof >> 8) & 0xff;
                int sp3 = xof & 0xff;
                int sp6 = xwd & 0xff;
                int spx = xwd & 0xff;

                cmds[i].CMD1 = new byte[] { 0x80, 0xb0, (byte)sp2, (byte)sp3, 0x02, 0x00, (byte)sp6 };
                cmds[i].CMD2 = new byte[] { 0x00, 0xc0, 0x00, 0x00, (byte)spx };
            }

            return cmds;
        }
    }
}
