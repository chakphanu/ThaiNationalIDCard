using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThaiNationalIDCard.Parsing;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class ThaiCardTextDecoderTests
    {
        [Fact]
        public void DecodesThaiLetters()
        {
            // TIS-620: 0xA1 = KO KAI (U+0E01), 0xD2 = SARA AA (U+0E32).
            string text = ThaiCardTextDecoder.DecodeTis620(new byte[] { 0xA1, 0xD2 });
            Assert.Equal("\u0E01\u0E32", text);
        }

        [Fact]
        public void DecodesMixedAsciiAndThai()
        {
            byte[] data = SyntheticImage.Tis("Mr.#\u0E19\u0E32\u0E22");
            Assert.Equal("Mr.#\u0E19\u0E32\u0E22", ThaiCardTextDecoder.DecodeTis620(data));
        }

        [Fact]
        public void TisEncodingNameResolvesToCodePage874()
        {
            ThaiCardTextDecoder.DecodeTis620(new byte[] { 0x41 });
            Assert.Equal(874, Encoding.GetEncoding("TIS-620").CodePage);
        }

        [Fact]
        public void TrimsTrailingSpaceAndNulPadding()
        {
            byte[] data = new byte[] { 0x41, 0x42, 0x20, 0x20, 0x00, 0x20, 0x00 };
            Assert.Equal("AB", ThaiCardTextDecoder.DecodeTis620(data));
        }

        [Fact]
        public void KeepsLeadingAndInnerSpaces()
        {
            byte[] data = Encoding.ASCII.GetBytes("  A B  ");
            Assert.Equal("  A B", ThaiCardTextDecoder.DecodeTis620(data));
        }

        [Fact]
        public void Bytes_that_look_like_a_status_word_are_data_because_the_protocol_layer_stripped_it()
        {
            string text = ThaiCardTextDecoder.DecodeTis620(new byte[] { 0x41, 0x42, 0x90, 0x00 });

            Assert.StartsWith("AB", text, StringComparison.Ordinal);
            Assert.True(text.Length > 2);
        }

        [Fact]
        public void EmptyInputGivesEmptyString()
        {
            Assert.Equal(string.Empty, ThaiCardTextDecoder.DecodeTis620(Array.Empty<byte>()));
        }

        [Fact]
        public void NullInputThrows()
        {
            Assert.Throws<System.ArgumentNullException>(() => ThaiCardTextDecoder.DecodeTis620(null!));
        }

        [Fact]
        public void RepeatedAndConcurrentCallsAreSafe()
        {
            byte[] data = new byte[] { 0xA1, 0xD2 };
            for (int i = 0; i < 100; i++)
                Assert.Equal("\u0E01\u0E32", ThaiCardTextDecoder.DecodeTis620(data));

            var results = new string[200];
            Parallel.For(0, results.Length, i => results[i] = ThaiCardTextDecoder.DecodeTis620(data));
            Assert.All(results, r => Assert.Equal("\u0E01\u0E32", r));
        }
    }
}
