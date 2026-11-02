using System.Linq;
using ThaiNationalIDCard.Hardware;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>The name shapes come from how pcsc-lite and WinSCard are documented. None came from a reader here.</summary>
    public class ReaderNamesTests
    {
        [Theory]
        [InlineData("Vendor Model (A1B2C3D4E5) 00 00", "Vendor Model 00 00")]
        [InlineData("Vendor Model (12345678) 01 00", "Vendor Model 01 00")]
        [InlineData("Vendor Model (unclosed 123 00 00", "Vendor Model")]
        [InlineData("Vendor Model SN:ABC12345 00 00", "Vendor Model 00 00")]
        [InlineData("Vendor Model S/N 99887 0", "Vendor Model 0")]
        [InlineData("Vendor Model serial=X1Y2Z3 0", "Vendor Model 0")]
        [InlineData("Vendor ACR122U123456 0", "Vendor 0")]
        [InlineData("Vendor DEADBEEF01 0", "Vendor 0")]
        [InlineData("Vendor 8751C4F2 0", "Vendor 0")]
        [InlineData("Vendor [ABCD1234EF] 00 00", "Vendor 00 00")]
        public void Serial_like_parts_are_removed(string name, string expected)
        {
            Assert.Equal(expected, ReaderNames.RemoveSerial(name));
        }

        [Theory]
        [InlineData("ACS ACR1252 1S CL Reader PICC 0")]
        [InlineData("Alcor Micro AU9540 00 00")]
        [InlineData("HID Global OMNIKEY 3x21 Smart Card Reader [OMNIKEY 3x21 Smart Card Reader] 00 00")]
        [InlineData("ACS ACR122U PICC Interface [ACR122U PICC Interface] 00 00")]
        [InlineData("Synthetic Reader 0")]
        public void Model_names_and_slot_numbers_stay(string name)
        {
            Assert.Equal(name, ReaderNames.RemoveSerial(name));
        }

        [Fact]
        public void A_word_sn_without_a_digit_is_not_a_serial()
        {
            Assert.Equal("HID SN Reader 0", ReaderNames.RemoveSerial("HID SN Reader 0"));
        }

        [Fact]
        public void Result_is_ascii_without_control_characters()
        {
            string result = ReaderNames.RemoveSerial("Vendor\u0E01\n\tModel\u0007 0");

            Assert.All(result, c => Assert.InRange(c, ' ', '~'));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("(ABC12345)")]
        public void Nothing_left_is_a_fixed_text(string? name)
        {
            Assert.Equal("(unnamed reader)", ReaderNames.RemoveSerial(name));
        }

        [Fact]
        public void A_reader_can_be_chosen_by_the_name_diagnostics_print()
        {
            ReaderStatus[] statuses =
            {
                new ReaderStatus("Vendor Model (AAAA111122) 00 00", ReaderCardState.Empty),
                new ReaderStatus("Vendor Model (BBBB222233) 01 00", ReaderCardState.Present),
            };

            Assert.Equal(statuses[1].Name, ReaderSelector.Select(statuses, "Vendor Model 01 00"));
            Assert.Equal(statuses[1].Name, ReaderSelector.Select(statuses, statuses[1].Name));
            Assert.Throws<CardNotPresentException>(() => ReaderSelector.Select(statuses, "Vendor Model 00 00"));
            Assert.Throws<ReaderNotFoundException>(() => ReaderSelector.Select(statuses, "Vendor Model"));
            Assert.Equal(2, statuses.Count(s => ReaderNames.RemoveSerial(s.Name).StartsWith("Vendor Model", System.StringComparison.Ordinal)));
        }
    }
}
