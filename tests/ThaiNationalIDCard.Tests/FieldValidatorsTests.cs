using System;
using System.Linq;
using System.Text;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Parsing;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// All values are made up. A validator marks data as suspect; passing proves the shape is right, not that a value
    /// is real, and that a real card has these shapes is still an assumption.
    /// </summary>
    public class FieldValidatorsTests
    {
        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        private static byte[] PersonInfo(string thai, string english, string birth, string sex) =>
            SyntheticImage.Tis(thai.PadRight(100) + english.PadRight(100) + birth + sex);

        private static byte[] GoodPersonInfo() =>
            PersonInfo(SyntheticImage.PersonInfoText, SyntheticImage.EnglishInfoText, "25400131", "1");

        // ---- citizen id ----

        [Theory]
        [InlineData("1234567890121")]
        [InlineData("1100000000008")]
        [InlineData("3101234567893")]
        public void Citizen_id_formula_accepts_a_matching_check_digit(string id)
        {
            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.CitizenId, Ascii(id)));
        }

        [Fact]
        public void Check_digit_uses_weights_13_down_to_2_over_the_first_12_digits()
        {
            // 1*13 + 2*12 + 3*11 + 4*10 + 5*9 + 6*8 + 7*7 + 8*6 + 9*5 + 0*4 + 1*3 + 2*2 = 352, 352 mod 11 = 0, (11 - 0) mod 10 = 1.
            Assert.Equal(1, FieldValidators.CitizenIdCheckDigit(Ascii("123456789012")));
        }

        [Fact]
        public void Check_digit_10_and_11_results_wrap_with_mod_10()
        {
            // sum mod 11 = 1 gives 10, so check digit 0: 000000000001 has sum 2, and 000000000006 has sum 12.
            Assert.Equal(0, FieldValidators.CitizenIdCheckDigit(Ascii("000000000006")));
            // sum mod 11 = 0 gives 11, so check digit 1.
            Assert.Equal(1, FieldValidators.CitizenIdCheckDigit(Ascii("000000000000")));
        }

        [Fact]
        public void Every_wrong_last_digit_is_a_checksum_mismatch_and_not_a_shape_failure()
        {
            for (char digit = '0'; digit <= '9'; digit++)
            {
                ValidationFailures result = FieldValidators.Check(FieldCheck.CitizenId, Ascii("123456789012" + digit));
                Assert.Equal(digit == '1' ? ValidationFailures.None : ValidationFailures.ChecksumMismatch, result);
            }
        }

        [Fact]
        public void The_old_fixture_1234567890123_fails_the_formula()
        {
            Assert.Equal(ValidationFailures.ChecksumMismatch, FieldValidators.Check(FieldCheck.CitizenId, Ascii("1234567890123")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("123456789012")]
        [InlineData("12345678901210")]
        [InlineData("12345678901a1")]
        [InlineData("1234567890 21")]
        public void Anything_but_13_ascii_digits_is_a_shape_failure(string text)
        {
            Assert.Equal(ValidationFailures.NotThirteenDigits, FieldValidators.Check(FieldCheck.CitizenId, Ascii(text)));
        }

        [Fact]
        public void Thai_digits_are_not_digits_here()
        {
            byte[] data = Ascii("1234567890121");
            data[0] = 0xF1; // TIS-620 Thai digit one.

            Assert.Equal(ValidationFailures.NotThirteenDigits, FieldValidators.Check(FieldCheck.CitizenId, data));
        }

        [Fact]
        public void First_digit_is_not_judged_because_zero_ids_are_not_rejected()
        {
            // 0 + weights: sum of 012345678901 = 0*13+1*12+2*11+3*10+4*9+5*8+6*7+7*6+8*5+9*4+0*3+1*2 = 302, 302 mod 11 = 5, 6 mod 10 = 6.
            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.CitizenId, Ascii("0123456789016")));
        }

        // ---- person info ----

        [Fact]
        public void Person_info_with_every_part_in_shape_passes()
        {
            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.PersonInfo, GoodPersonInfo()));
        }

        [Theory]
        [InlineData("25400000", (int)ValidationFailures.None)]   // month and day 00 are known to exist, so digits are enough
        [InlineData("00000000", (int)ValidationFailures.None)]
        [InlineData("2540013X", (int)ValidationFailures.BirthDateNotDigits)]
        [InlineData("        ", (int)ValidationFailures.BirthDateNotDigits)]
        public void Birth_date_is_a_weak_signal_and_only_non_digits_mark_it(string birth, int expectedValue)
        {
            var expected = (ValidationFailures)expectedValue;
            byte[] data = PersonInfo(SyntheticImage.PersonInfoText, SyntheticImage.EnglishInfoText, birth, "2");

            Assert.Equal(expected, FieldValidators.Check(FieldCheck.PersonInfo, data));
        }

        [Theory]
        [InlineData("1", (int)ValidationFailures.None)]
        [InlineData("2", (int)ValidationFailures.None)]
        [InlineData("3", (int)ValidationFailures.SexCode)]
        [InlineData("0", (int)ValidationFailures.SexCode)]
        [InlineData(" ", (int)ValidationFailures.SexCode)]
        public void Sex_byte_outside_1_and_2_is_marked(string sex, int expectedValue)
        {
            var expected = (ValidationFailures)expectedValue;
            byte[] data = PersonInfo(SyntheticImage.PersonInfoText, SyntheticImage.EnglishInfoText, "25400131", sex);

            Assert.Equal(expected, FieldValidators.Check(FieldCheck.PersonInfo, data));
        }

        [Fact]
        public void Name_shape_is_judged_on_the_slice_so_a_nul_sex_byte_cannot_shift_anything()
        {
            byte[] data = GoodPersonInfo();
            data[208] = 0x00;

            Assert.Equal(ValidationFailures.SexCode, FieldValidators.Check(FieldCheck.PersonInfo, data));
        }

        [Fact]
        public void Name_without_three_separators_is_marked_per_language()
        {
            byte[] noThai = PersonInfo("no separators", SyntheticImage.EnglishInfoText, "25400131", "1");
            byte[] noEnglish = PersonInfo(SyntheticImage.PersonInfoText, new string(' ', 10), "25400131", "1");

            Assert.Equal(ValidationFailures.ThaiNameShape, FieldValidators.Check(FieldCheck.PersonInfo, noThai));
            Assert.Equal(ValidationFailures.EnglishNameShape, FieldValidators.Check(FieldCheck.PersonInfo, noEnglish));
        }

        [Fact]
        public void Truncated_person_info_marks_what_is_missing_and_never_throws()
        {
            byte[] data = GoodPersonInfo().Take(106).ToArray();

            ValidationFailures result = FieldValidators.Check(FieldCheck.PersonInfo, data);

            Assert.True(result.HasFlag(ValidationFailures.EnglishNameShape));
            Assert.True(result.HasFlag(ValidationFailures.BirthDateNotDigits));
            Assert.True(result.HasFlag(ValidationFailures.SexCode));
            Assert.False(result.HasFlag(ValidationFailures.ThaiNameShape));
        }

        // ---- encoding ----

        [Theory]
        [InlineData(0x80)]
        [InlineData(0xA0)]
        [InlineData(0xDB)]
        [InlineData(0xDE)]
        [InlineData(0xFC)]
        [InlineData(0xFF)]
        [InlineData(0x01)]
        [InlineData(0x7F)]
        public void Bytes_outside_tis620_mark_text_fields(byte bad)
        {
            byte[] issuer = SyntheticImage.Tis(SyntheticImage.IssuerText.PadRight(100));
            issuer[3] = bad;
            byte[] person = GoodPersonInfo();
            person[50] = bad;

            Assert.Equal(ValidationFailures.TextEncoding, FieldValidators.Check(FieldCheck.Issuer, issuer));
            Assert.Equal(ValidationFailures.TextEncoding, FieldValidators.Check(FieldCheck.PersonInfo, person));
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0x20)]
        [InlineData(0xA1)]
        [InlineData(0xDA)]
        [InlineData(0xDF)]
        [InlineData(0xFB)]
        public void Padding_and_thai_range_edges_are_valid_text(byte good)
        {
            byte[] issuer = SyntheticImage.Tis(SyntheticImage.IssuerText.PadRight(100));
            issuer[3] = good;

            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.Issuer, issuer));
        }

        // ---- address ----

        [Fact]
        public void Address_with_eight_parts_passes()
        {
            byte[] data = SyntheticImage.Tis(SyntheticImage.AddressText.PadRight(SyntheticImage.AddressLength));

            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.Address, data));
        }

        [Theory]
        [InlineData("no separators at all")]
        [InlineData("a#b#c")]
        [InlineData("a#b#c#d#e#f#g#h#i")]
        public void Address_with_another_part_count_is_marked_and_not_rejected(string text)
        {
            byte[] data = SyntheticImage.Tis(text.PadRight(SyntheticImage.AddressLength));

            Assert.Equal(ValidationFailures.AddressShape, FieldValidators.Check(FieldCheck.Address, data));
        }

        // ---- dates ----

        [Theory]
        [InlineData("2560013125690130", (int)ValidationFailures.None)]
        [InlineData("2560013199999999", (int)ValidationFailures.None)]
        [InlineData("256001312569013X", (int)ValidationFailures.ExpiryDateNotDigits)]
        [InlineData("256001X125690130", (int)ValidationFailures.IssueDateNotDigits)]
        [InlineData("25600131", (int)ValidationFailures.ExpiryDateNotDigits)]
        public void Issue_and_expiry_need_8_digits_each_and_99999999_counts(string text, int expectedValue)
        {
            var expected = (ValidationFailures)expectedValue;
            Assert.Equal(expected, FieldValidators.Check(FieldCheck.IssueExpire, Ascii(text)));
        }

        // ---- photo ----

        [Fact]
        public void Jpeg_needs_ff_d8_at_the_start_and_ff_d9_somewhere_after_it()
        {
            var good = new byte[] { 0xFF, 0xD8, 0x20, 0x20, 0xFF, 0xD9, 0x20 };
            var noEnd = new byte[] { 0xFF, 0xD8, 0x20, 0x20 };
            var noStart = new byte[] { 0x20, 0xD8, 0x20, 0xFF, 0xD9 };

            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.Jpeg, good));
            Assert.Equal(ValidationFailures.JpegEnd, FieldValidators.Check(FieldCheck.Jpeg, noEnd));
            Assert.Equal(ValidationFailures.JpegStart, FieldValidators.Check(FieldCheck.Jpeg, noStart));
            Assert.Equal(ValidationFailures.JpegStart | ValidationFailures.JpegEnd, FieldValidators.Check(FieldCheck.Jpeg, Array.Empty<byte>()));
        }

        [Fact]
        public void None_check_passes_anything()
        {
            Assert.Equal(ValidationFailures.None, FieldValidators.Check(FieldCheck.None, new byte[] { 0xFF, 0x00 }));
        }
    }
}
