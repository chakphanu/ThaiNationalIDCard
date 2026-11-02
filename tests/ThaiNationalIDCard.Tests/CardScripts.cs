using System;
using System.Linq;
using System.Text;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Builds synthetic (made up) card responses for the fake transport. No real personal data.</summary>
    internal static class CardScripts
    {
        public const string SelectApplet = "00a4040008a000000054480001";
        public const string Cid = "80b0000402000d";
        public const string PersonInfo = "80b000110200d1";
        public const string Address = "80b01579020064";
        public const string IssueExpire = "80b00167020012";
        public const string Issuer = "80b000f6020064";

        public const string SyntheticCitizenId = "1234567890123";

        static CardScripts()
        {
            // Needed to build TIS-620 test data on any runtime.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static byte[] Tis(string text) => Encoding.GetEncoding("TIS-620").GetBytes(text);

        public static byte[] Sw(byte sw1, byte sw2) => new[] { sw1, sw2 };

        public static byte[] WithOk(byte[] data) => data.Concat(new byte[] { 0x90, 0x00 }).ToArray();

        /// <summary>GET RESPONSE command as sent by ThaiIDCard: 00 C0 00 P2 + SW2 (P2 = 01 for Type01, 00 for Type02).</summary>
        public static string GetResponse(bool type01, byte sw2) =>
            "00c000" + (type01 ? "01" : "00") + sw2.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Script one command that answers "61 xx" and then the data via GET RESPONSE.</summary>
        public static FakeCardTransport OnData(this FakeCardTransport fake, string commandHex, byte[] data, bool type01 = false)
        {
            byte sw2 = (byte)data.Length;
            fake.On(commandHex, Sw(0x61, sw2));
            fake.On(GetResponse(type01, sw2), WithOk(data));
            return fake;
        }

        public static FakeCardTransport ScriptSelectOk(this FakeCardTransport fake) =>
            fake.On(SelectApplet, Sw(0x90, 0x00));

        /// <summary>Synthetic EF_PERSON_INFO text: 100 chars Thai name, 100 chars English name, birth date (BE), sex.</summary>
        public static string PersonInfoText(string birthBe = "25400131", char sex = '1')
        {
            string th = "\u0E19\u0E32\u0E22#\u0E17\u0E14\u0E2A\u0E2D\u0E1A##\u0E19\u0E32\u0E21\u0E2A\u0E21\u0E21\u0E15\u0E34";
            string en = "Mr.#Test##Sample";
            return th.PadRight(100) + en.PadRight(100) + birthBe + sex;
        }
    }
}
