using System;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>All dates are made up.</summary>
    public class ThaiCardDateTests
    {
        [Fact]
        public void A_complete_date_converts_the_year_and_keeps_the_raw_text()
        {
            var date = new ThaiCardDate("25400131");

            Assert.Equal("25400131", date.Raw);
            Assert.Equal(1997, date.Year);
            Assert.Equal(1, date.Month);
            Assert.Equal(31, date.Day);
            Assert.False(date.IsLifetime);
            Assert.Equal(new DateTime(1997, 1, 31), date.ToDateTime());
            Assert.Equal(DateTimeKind.Unspecified, date.ToDateTime()!.Value.Kind);
            Assert.Equal(new DateOnly(1997, 1, 31), date.ToDateOnly());
            Assert.Equal(new DateTime(1997, 1, 31), date.AgeReferenceDate);
            Assert.True(date.IsWellFormed);
        }

        [Fact]
        public void Day_00_is_null_and_no_date_is_made_up()
        {
            var date = new ThaiCardDate("25400300");

            Assert.Equal(1997, date.Year);
            Assert.Equal(3, date.Month);
            Assert.Null(date.Day);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.ToDateOnly());
            Assert.True(date.IsWellFormed);
        }

        [Theory]
        [InlineData("25400300", 1997, 3, 1)]  // month known, day unknown: first of the month
        [InlineData("25400000", 1997, 1, 1)]  // only the year known: start of the year
        [InlineData("25400031", 1997, 1, 1)]  // month unknown: a known day does not help
        public void Age_reference_date_follows_the_civil_code_rule_and_stays_separate(string raw, int year, int month, int day)
        {
            var date = new ThaiCardDate(raw);

            Assert.Equal(new DateTime(year, month, day), date.AgeReferenceDate);
            Assert.Null(date.ToDateTime());
        }

        [Theory]
        [InlineData("00000000")]
        [InlineData("00000131")]
        public void Year_0000_is_null_and_nothing_is_derived_from_it(string raw)
        {
            var date = new ThaiCardDate(raw);

            Assert.Null(date.Year);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.ToDateOnly());
            Assert.Null(date.AgeReferenceDate);
            Assert.False(date.IsWellFormed);
            Assert.Equal(raw, date.Raw);
        }

        [Fact]
        public void All_nines_is_lifetime_with_no_date_parts()
        {
            var date = new ThaiCardDate("99999999");

            Assert.True(date.IsLifetime);
            Assert.Null(date.Year);
            Assert.Null(date.Month);
            Assert.Null(date.Day);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.ToDateOnly());
            Assert.Null(date.AgeReferenceDate);
            Assert.Equal("99999999", date.Raw);
        }

        [Fact]
        public void A_year_of_9999_with_a_real_month_and_day_is_not_lifetime()
        {
            var date = new ThaiCardDate("99991231");

            Assert.False(date.IsLifetime);
            Assert.Equal(9456, date.Year);
            Assert.Equal(new DateTime(9456, 12, 31), date.ToDateTime());
        }

        [Theory]
        [InlineData("25401331")] // month 13
        [InlineData("25409931")] // month 99
        public void An_impossible_month_is_null_and_not_clamped(string raw)
        {
            var date = new ThaiCardDate(raw);

            Assert.Null(date.Month);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.AgeReferenceDate);
            Assert.False(date.IsWellFormed);
        }

        [Fact]
        public void An_impossible_day_is_null_and_not_clamped()
        {
            var date = new ThaiCardDate("25400132");

            Assert.Equal(1, date.Month);
            Assert.Null(date.Day);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.AgeReferenceDate);
            Assert.False(date.IsWellFormed);
        }

        [Theory]
        [InlineData("25400231")] // no 31 February
        [InlineData("25400431")] // no 31 April
        [InlineData("25440229")] // 2001 is not a leap year
        public void A_day_that_does_not_exist_in_that_month_gives_no_date(string raw)
        {
            var date = new ThaiCardDate(raw);

            Assert.NotNull(date.Month);
            Assert.NotNull(date.Day);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.ToDateOnly());
            Assert.Null(date.AgeReferenceDate);
            Assert.False(date.IsWellFormed);
        }

        [Fact]
        public void Leap_day_is_judged_on_the_gregorian_year()
        {
            Assert.Equal(new DateTime(2000, 2, 29), new ThaiCardDate("25430229").ToDateTime());
        }

        [Theory]
        [InlineData("05430101")] // would be year 0
        [InlineData("01000101")] // would be negative
        public void A_year_before_gregorian_year_1_is_null(string raw)
        {
            var date = new ThaiCardDate(raw);

            Assert.Null(date.Year);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.AgeReferenceDate);
            Assert.False(date.IsWellFormed);
        }

        [Fact]
        public void The_earliest_year_datetime_can_hold_works()
        {
            Assert.Equal(new DateTime(1, 1, 1), new ThaiCardDate("05440101").ToDateTime());
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("2540", "2540")]
        [InlineData("2540013", "2540013")]
        [InlineData("254001311", "254001311")]
        [InlineData("2540013X", "2540013X")]
        [InlineData("        ", "        ")]
        [InlineData("-5400131", "-5400131")]
        [InlineData("\u0E52\u0E55\u0E54\u0E50\u0E50\u0E51\u0E53\u0E51", "\u0E52\u0E55\u0E54\u0E50\u0E50\u0E51\u0E53\u0E51")]
        public void Anything_but_8_ascii_digits_keeps_the_raw_text_and_gives_no_parts(string? raw, string expectedRaw)
        {
            var date = new ThaiCardDate(raw);

            Assert.Equal(expectedRaw, date.Raw);
            Assert.Null(date.Year);
            Assert.Null(date.Month);
            Assert.Null(date.Day);
            Assert.False(date.IsLifetime);
            Assert.Null(date.ToDateTime());
            Assert.Null(date.AgeReferenceDate);
            Assert.False(date.IsWellFormed);
        }

        [Fact]
        public void There_is_no_expiry_judgement_on_the_type()
        {
            Assert.Null(typeof(ThaiCardDate).GetProperty("IsExpired"));
            Assert.Null(typeof(ThaiIdCardData).GetProperty("IsExpired"));
        }

        [Fact]
        public void To_string_holds_no_date()
        {
            Assert.DoesNotContain("2540", new ThaiCardDate("25400131").ToString(), StringComparison.Ordinal);
        }
    }
}
