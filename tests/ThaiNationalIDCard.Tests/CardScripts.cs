using System;
using System.Linq;
using System.Text;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Builds synthetic (made up) card data and scripts for FakeReader. No real personal data.</summary>
    internal static class CardScripts
    {
        public const string SelectApplet = "00a4040008a000000054480001";
        public const string Cid = "80b0000402000d";
        public const string PersonInfo = "80b000110200d1";
        public const string Address = "80b01579020064";
        public const string IssueExpire = "80b00167020010";
        public const string Issuer = "80b000f6020064";

        public const string SyntheticCitizenId = "1234567890123";

        // Thai text is written with \u escapes so this source file stays ASCII.
        public const string ThPrefix = "\u0E19\u0E32\u0E22";                        // title
        public const string ThFirst = "\u0E17\u0E14\u0E2A\u0E2D\u0E1A";             // first name
        public const string ThLast = "\u0E19\u0E32\u0E21\u0E2A\u0E21\u0E21\u0E15\u0E34"; // last name

        public static byte[] Tis(string text) => Encoding.GetEncoding("TIS-620").GetBytes(text);

        public static byte[] Sw(byte sw1, byte sw2) => new[] { sw1, sw2 };

        public static byte[] WithOk(byte[] data) => data.Concat(new byte[] { 0x90, 0x00 }).ToArray();

        /// <summary>GET RESPONSE command as sent by ThaiIDCard: 00 C0 00 P2 + SW2 (P2 = 01 for Type01, 00 for Type02).</summary>
        public static string GetResponse(bool type01, byte sw2) =>
            "00c000" + (type01 ? "01" : "00") + sw2.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Script one command that answers "61 xx" and then the data via GET RESPONSE, or data + 90 00 on the first
        /// transmit when fake.DirectAnswer is set (GET RESPONSE then stays unscripted and would fail loudly).
        /// </summary>
        public static FakeReader OnData(this FakeReader fake, string commandHex, byte[] data, bool type01 = false)
        {
            if (fake.DirectAnswer)
                return fake.On(commandHex, WithOk(data));

            byte sw2 = (byte)data.Length;
            fake.On(commandHex, Sw(0x61, sw2));
            fake.On(GetResponse(type01, sw2), WithOk(data));
            return fake;
        }

        /// <summary>Synthetic EF_PERSON_INFO text: 100 chars Thai name, 100 chars English name, birth date (BE), sex.</summary>
        public static string PersonInfoText(string birthBe = "25400131", char sex = '1')
        {
            string th = ThPrefix + "#" + ThFirst + "##" + ThLast;
            string en = "Mr.#Test##Sample";
            return th.PadRight(100) + en.PadRight(100) + birthBe + sex;
        }

        public const string AddressText = "99/9#5#Soi Test#Test Road##Test Tambol#Test Amphur#Test Province";

        /// <summary>Synthetic EF_CARD_ISSUE_EXPIRE text: issue + expire (BE yyyyMMdd), the 16 bytes Le 0x10 returns.</summary>
        public static string IssueExpireText(string issueBe = "25600131", string expireBe = "25690130") =>
            issueBe + expireBe;

        /// <summary>
        /// Scripts a complete card: select + the five text files. Photo chunks are scripted when withPhoto is true.
        /// The ATR decides the command set the library will use (3B 67 = Type01).
        /// </summary>
        public static FakeReader NewCard(bool type01 = false, bool withPhoto = false, bool directAnswer = false,
            string issueBe = "25600131", string expireBe = "25690130")
        {
            var fake = new FakeReader { DirectAnswer = directAnswer };
            fake.Atr = type01 ? new byte[] { 0x3B, 0x67, 0x00, 0x00 } : new byte[] { 0x3B, 0x79, 0x18, 0x00 };
            fake.On(SelectApplet, Sw(0x90, 0x00));
            fake.OnData(Cid, Tis(SyntheticCitizenId), type01);
            fake.OnData(PersonInfo, Tis(PersonInfoText()), type01);
            fake.OnData(Address, Tis(AddressText.PadRight(100)), type01);
            fake.OnData(IssueExpire, Tis(IssueExpireText(issueBe, expireBe)), type01);
            fake.OnData(Issuer, Tis("Test Issuer".PadRight(100)), type01);
            if (withPhoto)
            {
                var cmds = PhotoCommands();
                for (int i = 0; i < cmds.Length; i++)
                    fake.OnData(cmds[i], PhotoChunk(i), type01);
            }
            return fake;
        }

        public static string[] PhotoCommands() =>
            new APDU_THAILAND_IDCARD_TYPE_02().EF_CARD_PHOTO.Select(FakeReader.Hex).ToArray();

        /// <summary>255 bytes of a recognisable pattern, different for each chunk.</summary>
        public static byte[] PhotoChunk(int index) =>
            Enumerable.Range(0, 255).Select(k => (byte)((index * 7 + k) & 0xFF)).ToArray();
    }
}
