using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThaiNationalIDCard
{
    public struct CMD_PAIR
    {
        public byte[] CMD1;
        public byte[] CMD2;
    }

    interface IAPDU_THAILAND_IDCARD
    {
        

        // Select/Reset
        byte[][] CMD_SELECT();

        // Citizen ID
        byte[][] CMD_CID();

        // Fullname Thai + Eng + BirthDate + Sex
        byte[][] CMD_PERSON_INFO();

        // Address
        byte[][] CMD_ADDRESS();

        // issue/expire
        byte[][] CMD_CARD_ISSUE_EXPIRE();

        // photo
        CMD_PAIR[] GET_CMD_CARD_PHOTO();
       
    }
}
