using System;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class PersonalTests
    {
        private static Personal Sample()
        {
            return new Personal
            {
                Citizenid = CardScripts.SyntheticCitizenId,
                Info = CardScripts.PersonInfoText(),
                Address = CardScripts.AddressText.PadRight(100),
                Issue_Expire = CardScripts.IssueExpireText(),
                Issuer = "Test Issuer"
            };
        }

        private static Personal WithBirth(string birthBe)
        {
            return new Personal { Info = CardScripts.PersonInfoText(birthBe) };
        }

        private static Personal WithIssueExpire(string issueBe, string expireBe)
        {
            return new Personal { Issue_Expire = CardScripts.IssueExpireText(issueBe, expireBe) };
        }

        [Fact]
        public void Names_are_split_and_trimmed()
        {
            var p = Sample();
            Assert.Equal(CardScripts.ThPrefix, p.Th_Prefix);
            Assert.Equal(CardScripts.ThFirst, p.Th_Firstname);
            Assert.Equal(string.Empty, p.Th_Middlename);
            Assert.Equal(CardScripts.ThLast, p.Th_Lastname);
            Assert.Equal("Mr.", p.En_Prefix);
            Assert.Equal("Test", p.En_Firstname);
            Assert.Equal(string.Empty, p.En_Middlename);
            Assert.Equal("Sample", p.En_Lastname);
        }

        [Fact]
        public void Sex_is_the_char_after_the_birth_date()
        {
            Assert.Equal("1", Sample().Sex);
            Assert.Equal("2", new Personal { Info = CardScripts.PersonInfoText(sex: '2') }.Sex);
        }

        [Fact]
        public void Address_parts_are_split_and_trimmed()
        {
            var p = Sample();
            Assert.Equal("99/9", p.addrHouseNo);
            Assert.Equal("5", p.addrVillageNo);
            Assert.Equal("Soi Test", p.addrLane);
            Assert.Equal("Test Road", p.addrRoad);
            Assert.Equal("Test Tambol", p.addrTambol);
            Assert.Equal("Test Amphur", p.addrAmphur);
            Assert.Equal("Test Province", p.addrProvince);
            Assert.Equal("99/9 5 Soi Test Test Road  Test Tambol Test Amphur Test Province", p.Address);
        }

        [Fact]
        public void Address_getters_give_the_same_value_when_called_repeatedly()
        {
            var p = Sample();
            Assert.Equal(p.addrProvince, p.addrProvince);
            Assert.Equal("Test Amphur", p.addrAmphur);
            Assert.Equal("Test Amphur", p.addrAmphur);
        }

        [Fact]
        public void Setting_Address_again_invalidates_the_split_cache()
        {
            var p = Sample();
            Assert.Equal("99/9", p.addrHouseNo); // fills the cache
            Assert.Equal("Test Province", p.addrProvince);

            p.Address = "1#2#3#4#5#6#7#8";

            Assert.Equal("1", p.addrHouseNo);
            Assert.Equal("2", p.addrVillageNo);
            Assert.Equal("8", p.addrProvince);
            Assert.Equal("1 2 3 4 5 6 7 8", p.Address);
        }

        [Fact]
        public void Setting_Info_again_replaces_the_names()
        {
            var p = Sample();
            Assert.Equal("Test", p.En_Firstname);
            p.Info = (CardScripts.ThPrefix + "#Abc##Def").PadRight(100) + "Ms.#Xyz##Uvw".PadRight(100) + "25400131" + "2";
            Assert.Equal("Abc", p.Th_Firstname);
            Assert.Equal("Xyz", p.En_Firstname);
            Assert.Equal("2", p.Sex);
        }

        [Fact]
        public void Birthday_converts_buddhist_year()
        {
            Assert.Equal(new DateTime(1997, 1, 31), WithBirth("25400131").Birthday);
            Assert.Equal("1997", WithBirth("25400131").BirthdayYearString);
        }

        // ---- Birthday behavior pinned as it is (year 0 -> current year was added deliberately in PR #22) ----

        [Fact]
        public void Birthday_year_0_becomes_current_year()
        {
            var b = WithBirth("00000131").Birthday;
            Assert.Equal(DateTime.Now.Year, b.Year);
            Assert.Equal(1, b.Month);
            Assert.Equal(31, b.Day);
        }

        [Fact]
        public void Birthday_month_0_becomes_1()
        {
            Assert.Equal(new DateTime(1997, 1, 15), WithBirth("25400015").Birthday);
        }

        [Fact]
        public void Birthday_day_0_becomes_1()
        {
            Assert.Equal(new DateTime(1997, 3, 1), WithBirth("25400300").Birthday);
        }

        [Fact]
        public void Birthday_all_unknown_is_january_first_of_current_year()
        {
            Assert.Equal(new DateTime(DateTime.Now.Year, 1, 1), WithBirth("00000000").Birthday);
        }

        [Fact]
        public void Birthday_month_13_still_throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WithBirth("25401301").Birthday);
        }

        [Fact]
        public void BirthdayYearString_for_year_0_is_unchanged()
        {
            Assert.Equal("-543", WithBirth("00000131").BirthdayYearString);
        }

        // ---- Issue / Expire ----

        [Fact]
        public void Issue_and_Expire_convert_buddhist_year()
        {
            var p = Sample();
            Assert.Equal(new DateTime(2017, 1, 31), p.Issue);
            Assert.Equal(new DateTime(2026, 1, 30), p.Expire);
            Assert.Equal("20260130", p.ExpireString);
        }

        [Fact]
        public void Issue_month_and_day_00_do_not_throw()
        {
            Assert.Equal(new DateTime(2017, 1, 1), WithIssueExpire("25600000", "25690130").Issue);
            Assert.Equal(new DateTime(2017, 1, 15), WithIssueExpire("25600015", "25690130").Issue);
            Assert.Equal(new DateTime(2017, 3, 1), WithIssueExpire("25600300", "25690130").Issue);
        }

        [Fact]
        public void Expire_month_and_day_00_do_not_throw()
        {
            Assert.Equal(new DateTime(2026, 1, 1), WithIssueExpire("25600131", "25690000").Expire);
            Assert.Equal(new DateTime(2026, 1, 15), WithIssueExpire("25600131", "25690015").Expire);
            Assert.Equal(new DateTime(2026, 3, 1), WithIssueExpire("25600131", "25690300").Expire);
            Assert.Equal("20260101", WithIssueExpire("25600131", "25690000").ExpireString);
        }

        [Fact]
        public void Issue_and_Expire_clamp_values_above_range()
        {
            Assert.Equal(new DateTime(2017, 12, 31), WithIssueExpire("25601399", "25691399").Issue);
            Assert.Equal(new DateTime(2026, 12, 31), WithIssueExpire("25601399", "25691399").Expire);
        }

        [Fact]
        public void Day_31_in_a_30_day_month_is_clamped_to_month_end()
        {
            Assert.Equal(new DateTime(2017, 4, 30), WithIssueExpire("25600431", "25690431").Issue);
            Assert.Equal(new DateTime(2026, 4, 30), WithIssueExpire("25600431", "25690431").Expire);
            Assert.Equal(new DateTime(2018, 2, 28), WithIssueExpire("25610230", "25690131").Issue);
        }

        [Fact]
        public void Lifetime_card_expire_9999_still_works()
        {
            Assert.Equal(new DateTime(9456, 12, 31), WithIssueExpire("25600131", "99991231").Expire);
        }

        [Fact]
        public void Lifetime_card_raw_expire_99999999_clamps_to_the_last_day_of_year_9456()
        {
            // Issue #3: lifetime cards store 99999999. Month and day are clamped; the year is still 9999 - 543.
            var p = WithIssueExpire("25600131", "99999999");
            Assert.Equal(new DateTime(9456, 12, 31), p.Expire);
            Assert.Equal("94561231", p.ExpireString);
        }

        [Fact]
        public void Issue_and_Expire_without_data_throw_as_before()
        {
            var empty = new Personal();
            Assert.Throws<NullReferenceException>(() => empty.Issue);
            Assert.Throws<NullReferenceException>(() => empty.Expire);

            var shortData = new Personal { Issue_Expire = "2560" };
            Assert.Throws<ArgumentOutOfRangeException>(() => shortData.Issue);
            Assert.Throws<ArgumentOutOfRangeException>(() => shortData.Expire);
        }

        [Fact]
        public void Issue_with_year_0_still_throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WithIssueExpire("00000131", "25690130").Issue);
        }
    }
}
