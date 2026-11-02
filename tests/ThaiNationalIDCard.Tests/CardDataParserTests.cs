using System;
using System.Linq;
using System.Text;
using ThaiNationalIDCard.Parsing;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Every value is made up. Thai text is written as escapes so that sources stay ASCII.</summary>
    public class CardDataParserTests
    {
        // Label words, written separately from the parser so a typo there shows up here.
        private const string Moo = "\u0E2B\u0E21\u0E39\u0E48\u0E17\u0E35\u0E48";
        private const string Trok = "\u0E15\u0E23\u0E2D\u0E01";
        private const string Soi = "\u0E0B\u0E2D\u0E22";
        private const string Road = "\u0E16\u0E19\u0E19";
        private const string Tambon = "\u0E15\u0E33\u0E1A\u0E25";
        private const string Amphoe = "\u0E2D\u0E33\u0E40\u0E20\u0E2D";
        private const string Province = "\u0E08\u0E31\u0E07\u0E2B\u0E27\u0E31\u0E14";
        private const string Khwaeng = "\u0E41\u0E02\u0E27\u0E07";
        private const string Khet = "\u0E40\u0E02\u0E15";

        // Made up words.
        private const string NameA = "\u0E17\u0E14\u0E2A\u0E2D\u0E1A";
        private const string NameB = "\u0E2A\u0E21\u0E21\u0E15\u0E34";
        private const string MrPrefix = "\u0E19\u0E32\u0E22";
        private const string MrsPrefix = "\u0E19\u0E32\u0E07";

        private static byte[] Tis(string text) => SyntheticImage.Tis(text);

        private static RawCardField Ok(byte[] data) => new RawCardField(data, FieldQuality.Ok);

        private static byte[] Person(string thai, string english, string birth = "25400131", string sex = "1") =>
            Tis(thai.PadRight(100) + english.PadRight(100) + birth + sex);

        private static string ThaiName => MrPrefix + "#" + NameA + "##" + NameB;

        private static ThaiIdCardData ParsePerson(byte[] data) =>
            CardDataParser.Parse(new RawCardFields { PersonInfo = Ok(data) });

        private static ThaiIdCardData ParseAddress(string text) =>
            CardDataParser.Parse(new RawCardFields { Address = Ok(Tis(text.PadRight(160))) });

        private static ThaiIdCardData ParseIssueExpire(string text) =>
            CardDataParser.Parse(new RawCardFields { IssueExpire = Ok(Tis(text)) });

        private static ThaiIdCardData ParsePhoto(byte[] data, FieldQuality quality = FieldQuality.Ok) =>
            CardDataParser.Parse(new RawCardFields { Photo = new RawCardField(data, quality) });

        // ---- whole card ----

        [Fact]
        public void A_whole_card_parses_field_by_field()
        {
            string address = "99/9#" + Moo + " 5###" + Road + NameA + "#" + Tambon + NameB + "#" + Amphoe + NameA + "#" + Province + NameB;
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields
            {
                CitizenId = Ok(Tis(SimulatedCard.SyntheticCitizenId)),
                PersonInfo = Ok(Person(ThaiName, "Mr.#Synthetic##Sample")),
                Address = Ok(Tis(address.PadRight(160))),
                IssueExpire = Ok(Tis("2560013125690130")),
                Issuer = Ok(Tis(SyntheticImage.IssuerText.PadRight(100))),
                Photo = Ok(MiniJpeg.Build(0x11).Concat(new byte[] { 0x20, 0x20 }).ToArray()),
            });

            Assert.Equal(SimulatedCard.SyntheticCitizenId, card.CitizenId);
            Assert.Equal(MrPrefix, card.ThaiName.Prefix);
            Assert.Equal(NameA, card.ThaiName.FirstName);
            Assert.Equal(string.Empty, card.ThaiName.MiddleName);
            Assert.Equal(NameB, card.ThaiName.LastName);
            Assert.Equal("Mr.", card.EnglishName.Prefix);
            Assert.Equal("Synthetic", card.EnglishName.FirstName);
            Assert.Equal("Sample", card.EnglishName.LastName);
            Assert.Equal(new DateTime(1997, 1, 31), card.BirthDate.ToDateTime());
            Assert.Equal(CardSex.Male, card.Sex);
            Assert.Equal(new DateTime(2017, 1, 31), card.IssueDate.ToDateTime());
            Assert.Equal(new DateTime(2026, 1, 30), card.ExpiryDate.ToDateTime());
            Assert.Equal(SyntheticImage.IssuerText, card.Issuer);
            Assert.Equal(MiniJpeg.Build(0x11), card.GetPhoto());
            Assert.Equal(PhotoFailureReason.None, card.PhotoFailure);

            ThaiIdCardQuality q = card.Quality;
            FieldQuality[] all = { q.CitizenId, q.ThaiName, q.EnglishName, q.BirthDate, q.Sex, q.Address, q.IssueDate, q.ExpiryDate, q.Issuer, q.Photo };
            Assert.All(all, quality => Assert.Equal(FieldQuality.Ok, quality));
        }

        [Fact]
        public void Nothing_read_gives_empty_values_and_not_read_quality()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields());

            Assert.Null(card.CitizenId);
            Assert.Equal(string.Empty, card.ThaiName.FirstName);
            Assert.Equal(string.Empty, card.BirthDate.Raw);
            Assert.Empty(card.Address.RawParts);
            Assert.Null(card.Issuer);
            Assert.Null(card.GetPhoto());
            Assert.Null(card.SexRaw);
            Assert.Equal(CardSex.Unknown, card.Sex);
            Assert.Equal(PhotoFailureReason.None, card.PhotoFailure);
            Assert.Equal(FieldQuality.NotRead, card.Quality.CitizenId);
            Assert.Equal(FieldQuality.NotRead, card.Quality.BirthDate);
            Assert.Equal(FieldQuality.NotRead, card.Quality.Sex);
            Assert.Equal(FieldQuality.NotRead, card.Quality.Photo);
        }

        [Fact]
        public void A_failed_read_is_not_decoded_as_text_even_with_bytes_present()
        {
            byte[] bytes = Tis(SimulatedCard.SyntheticCitizenId);

            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields
            {
                CitizenId = new RawCardField(bytes, FieldQuality.Failed),
                PersonInfo = new RawCardField(Person(ThaiName, "Mr.#A##B"), FieldQuality.Failed),
                Address = new RawCardField(Tis("99/9#a#b"), FieldQuality.Failed),
                IssueExpire = new RawCardField(Tis("2560013125690130"), FieldQuality.Failed),
                Issuer = new RawCardField(Tis("x"), FieldQuality.Failed),
            });

            Assert.Null(card.CitizenId);
            Assert.Equal(string.Empty, card.ThaiName.Raw);
            Assert.Empty(card.Address.RawParts);
            Assert.Equal(string.Empty, card.IssueDate.Raw);
            Assert.Null(card.Issuer);
            Assert.Null(card.SexRaw);
            Assert.Equal(FieldQuality.Failed, card.Quality.CitizenId);
            Assert.Equal(FieldQuality.Failed, card.Quality.ThaiName);
            Assert.Equal(FieldQuality.Failed, card.Quality.BirthDate);
            Assert.Equal(FieldQuality.Failed, card.Quality.Sex);
            Assert.Equal(FieldQuality.Failed, card.Quality.Address);
            Assert.Equal(FieldQuality.Failed, card.Quality.ExpiryDate);
            Assert.Equal(FieldQuality.Failed, card.Quality.Issuer);
        }

        // ---- names ----

        [Fact]
        public void An_empty_middle_segment_does_not_shift_the_last_name()
        {
            ThaiIdCardName name = ParsePerson(Person("a#b##d", "Mr.#A##B")).ThaiName;

            Assert.Equal("a", name.Prefix);
            Assert.Equal("b", name.FirstName);
            Assert.Equal(string.Empty, name.MiddleName);
            Assert.Equal("d", name.LastName);
        }

        [Fact]
        public void All_four_segments_are_kept_in_both_languages()
        {
            ThaiIdCardData card = ParsePerson(Person(MrPrefix + "#" + NameA + "#" + NameB + "#" + NameA, "Mr.#John#Middle#Doe"));

            Assert.Equal(NameB, card.ThaiName.MiddleName);
            Assert.Equal("Middle", card.EnglishName.MiddleName);
            Assert.Equal("Doe", card.EnglishName.LastName);
        }

        [Theory]
        [InlineData("a", "a", "", "", "")]
        [InlineData("a#b", "a", "b", "", "")]
        [InlineData("a#b#c", "a", "b", "c", "")]
        public void Missing_segments_are_empty_and_marked(string text, string prefix, string first, string middle, string last)
        {
            ThaiIdCardData card = ParsePerson(Person(text, "Mr.#A##B"));

            Assert.Equal(prefix, card.ThaiName.Prefix);
            Assert.Equal(first, card.ThaiName.FirstName);
            Assert.Equal(middle, card.ThaiName.MiddleName);
            Assert.Equal(last, card.ThaiName.LastName);
            Assert.Equal(text, card.ThaiName.Raw);
            Assert.Equal(FieldQuality.Suspect, card.Quality.ThaiName);
            Assert.Equal(FieldQuality.Ok, card.Quality.EnglishName);
        }

        [Fact]
        public void Extra_segments_stay_in_raw_and_are_marked()
        {
            ThaiIdCardData card = ParsePerson(Person("a#b#c#d#e", "Mr.#A##B"));

            Assert.Equal("d", card.ThaiName.LastName);
            Assert.Equal("a#b#c#d#e", card.ThaiName.Raw);
            Assert.Equal(FieldQuality.Suspect, card.Quality.ThaiName);
        }

        [Fact]
        public void An_empty_english_name_is_tolerated_and_does_not_touch_the_other_fields()
        {
            ThaiIdCardData card = ParsePerson(Person(ThaiName, string.Empty));

            Assert.Equal(string.Empty, card.EnglishName.Prefix);
            Assert.Equal(string.Empty, card.EnglishName.FirstName);
            Assert.Equal(string.Empty, card.EnglishName.LastName);
            Assert.Equal(FieldQuality.Suspect, card.Quality.EnglishName);
            Assert.Equal(NameA, card.ThaiName.FirstName);
            Assert.Equal(FieldQuality.Ok, card.Quality.ThaiName);
            Assert.Equal(1997, card.BirthDate.Year);
            Assert.Equal(CardSex.Male, card.Sex);
        }

        [Fact]
        public void Separators_with_nothing_between_them_are_marked()
        {
            Assert.Equal(FieldQuality.Suspect, ParsePerson(Person("###", "Mr.#A##B")).Quality.ThaiName);
        }

        [Fact]
        public void Nul_padding_inside_a_segment_is_trimmed()
        {
            byte[] data = Person("a#b  ##d", "Mr.#A##B");
            data[3] = 0x00;

            ThaiIdCardName name = ParsePerson(data).ThaiName;

            Assert.Equal("b", name.FirstName);
            Assert.Equal("d", name.LastName);
        }

        [Fact]
        public void A_full_thai_slot_does_not_run_into_the_english_slot()
        {
            string thai = "a#b##" + new string('x', 95);

            ThaiIdCardData card = ParsePerson(Person(thai, "Mr.#A##B"));

            Assert.Equal(new string('x', 95), card.ThaiName.LastName);
            Assert.Equal("Mr.", card.EnglishName.Prefix);
        }

        [Fact]
        public void A_byte_outside_tis620_marks_only_the_name_it_sits_in()
        {
            byte[] data = Person(ThaiName, "Mr.#A##B");
            data[50] = 0xFF;

            ThaiIdCardData card = ParsePerson(data);

            Assert.Equal(FieldQuality.Suspect, card.Quality.ThaiName);
            Assert.Equal(FieldQuality.Ok, card.Quality.EnglishName);
        }

        // ---- sex ----

        [Theory]
        [InlineData((byte)'1', (int)CardSex.Male, (int)FieldQuality.Ok)]
        [InlineData((byte)'2', (int)CardSex.Female, (int)FieldQuality.Ok)]
        [InlineData((byte)'0', (int)CardSex.Unknown, (int)FieldQuality.Suspect)]
        [InlineData((byte)'3', (int)CardSex.Unknown, (int)FieldQuality.Suspect)]
        [InlineData((byte)' ', (int)CardSex.Unknown, (int)FieldQuality.Suspect)]
        [InlineData((byte)0x00, (int)CardSex.Unknown, (int)FieldQuality.Suspect)]
        [InlineData((byte)0x01, (int)CardSex.Unknown, (int)FieldQuality.Suspect)]
        [InlineData((byte)0xFF, (int)CardSex.Unknown, (int)FieldQuality.Suspect)]
        public void Sex_comes_from_the_byte_at_its_offset_and_keeps_it(byte raw, int sex, int quality)
        {
            byte[] data = Person(ThaiName, "Mr.#A##B");
            data[208] = raw;

            ThaiIdCardData card = ParsePerson(data);

            Assert.Equal((CardSex)sex, card.Sex);
            Assert.Equal(raw, card.SexRaw);
            Assert.Equal((FieldQuality)quality, card.Quality.Sex);
            Assert.Equal(1997, card.BirthDate.Year);
        }

        [Theory]
        [InlineData(MrPrefix, "1", (int)CardSex.Male)]
        [InlineData(MrsPrefix, "1", (int)CardSex.Male)]    // the prefix does not overrule the byte
        [InlineData(MrPrefix, "2", (int)CardSex.Female)]
        [InlineData(MrsPrefix, "3", (int)CardSex.Unknown)] // and does not fill in for it
        public void Sex_is_never_inferred_from_the_prefix(string prefix, string code, int expected)
        {
            ThaiIdCardData card = ParsePerson(Person(prefix + "#" + NameA + "##" + NameB, "Mr.#A##B", sex: code));

            Assert.Equal((CardSex)expected, card.Sex);
        }

        // ---- truncated person info ----

        [Fact]
        public void A_short_person_info_marks_each_slice_on_its_own()
        {
            ThaiIdCardData card = ParsePerson(Person(ThaiName, "Mr.#A##B").Take(106).ToArray());

            Assert.Equal(FieldQuality.Ok, card.Quality.ThaiName);
            Assert.Equal(FieldQuality.Suspect, card.Quality.EnglishName);
            Assert.Equal("Mr.#A#", card.EnglishName.Raw);
            Assert.Equal(FieldQuality.Failed, card.Quality.BirthDate);
            Assert.Equal(FieldQuality.Failed, card.Quality.Sex);
            Assert.Null(card.SexRaw);
        }

        [Fact]
        public void A_cut_off_birth_date_keeps_what_arrived()
        {
            ThaiIdCardData card = ParsePerson(Person(ThaiName, "Mr.#A##B").Take(204).ToArray());

            Assert.Equal("2540", card.BirthDate.Raw);
            Assert.Null(card.BirthDate.Year);
            Assert.Equal(FieldQuality.Suspect, card.Quality.BirthDate);
        }

        [Fact]
        public void Every_length_of_person_info_parses_without_throwing()
        {
            byte[] whole = Person(ThaiName, "Mr.#A##B");
            for (int length = 0; length <= whole.Length; length++)
            {
                Assert.NotNull(ParsePerson(whole.Take(length).ToArray()));
            }
        }

        // ---- dates in person info and issue / expiry ----

        [Theory]
        [InlineData("25400131", (int)FieldQuality.Ok)]
        [InlineData("25400100", (int)FieldQuality.Ok)]      // unknown day exists on real cards according to public code
        [InlineData("25400000", (int)FieldQuality.Ok)]
        [InlineData("00000000", (int)FieldQuality.Suspect)] // year 0000 has no documentation
        [InlineData("25401399", (int)FieldQuality.Suspect)]
        [InlineData("99999999", (int)FieldQuality.Suspect)] // lifetime means nothing on a birth date
        [InlineData("2540013X", (int)FieldQuality.Suspect)]
        public void Birth_date_quality(string raw, int quality)
        {
            ThaiIdCardData card = ParsePerson(Person(ThaiName, "Mr.#A##B", birth: raw));

            Assert.Equal((FieldQuality)quality, card.Quality.BirthDate);
            Assert.Equal(raw, card.BirthDate.Raw);
        }

        [Fact]
        public void Lifetime_expiry_is_ok_and_has_no_date()
        {
            ThaiIdCardData card = ParseIssueExpire("2560013199999999");

            Assert.True(card.ExpiryDate.IsLifetime);
            Assert.Null(card.ExpiryDate.ToDateTime());
            Assert.Equal(FieldQuality.Ok, card.Quality.ExpiryDate);
            Assert.Equal(new DateTime(2017, 1, 31), card.IssueDate.ToDateTime());
            Assert.Equal(FieldQuality.Ok, card.Quality.IssueDate);
        }

        [Theory]
        [InlineData("2560013125690100", (int)FieldQuality.Ok, (int)FieldQuality.Suspect)]
        [InlineData("2560010025690130", (int)FieldQuality.Suspect, (int)FieldQuality.Ok)]
        [InlineData("0000000000000000", (int)FieldQuality.Suspect, (int)FieldQuality.Suspect)]
        [InlineData("9999999925690130", (int)FieldQuality.Suspect, (int)FieldQuality.Ok)]
        [InlineData("25600131", (int)FieldQuality.Ok, (int)FieldQuality.Failed)]
        [InlineData("256001312569", (int)FieldQuality.Ok, (int)FieldQuality.Suspect)]
        [InlineData("2560", (int)FieldQuality.Suspect, (int)FieldQuality.Failed)]
        public void Issue_and_expiry_need_a_whole_date_and_are_judged_apart(string text, int issue, int expiry)
        {
            ThaiIdCardData card = ParseIssueExpire(text);

            Assert.Equal((FieldQuality)issue, card.Quality.IssueDate);
            Assert.Equal((FieldQuality)expiry, card.Quality.ExpiryDate);
        }

        // ---- address ----

        [Fact]
        public void Labelled_parts_are_found_whatever_their_position_and_the_label_is_removed()
        {
            string text = "99/9#" + Province + "P#" + Moo + " 5#" + Road + "R#" + Trok + "T#" + Soi + "S#" + Tambon + "B#" + Amphoe + "A";

            ThaiIdCardAddress address = ParseAddress(text).Address;

            Assert.Equal("5", address.Moo);
            Assert.Equal("T", address.Trok);
            Assert.Equal("S", address.Soi);
            Assert.Equal("R", address.Road);
            Assert.Equal("B", address.Tambon);
            Assert.Equal("A", address.Amphoe);
            Assert.Equal("P", address.Province);
            Assert.Null(address.Khwaeng);
            Assert.Null(address.Khet);
        }

        [Fact]
        public void Bangkok_labels_are_their_own_properties()
        {
            ThaiIdCardAddress address = ParseAddress("1######" + Khwaeng + "K#" + Khet + "E").Address;

            Assert.Equal("K", address.Khwaeng);
            Assert.Equal("E", address.Khet);
            Assert.Null(address.Tambon);
            Assert.Null(address.Amphoe);
        }

        [Fact]
        public void Unlabelled_parts_are_only_in_raw_parts_and_never_guessed_as_lane_or_road()
        {
            // The positions 2 and 3 are what 2.x read as lane and road.
            ThaiIdCardAddress address = ParseAddress("99/9#x#LaneLike#RoadLike##y#z#w").Address;

            Assert.Equal(new[] { "99/9", "x", "LaneLike", "RoadLike", string.Empty, "y", "z", "w" }, address.RawParts);
            Assert.Null(address.Moo);
            Assert.Null(address.Trok);
            Assert.Null(address.Soi);
            Assert.Null(address.Road);
            Assert.Null(address.Tambon);
            Assert.Null(address.Amphoe);
            Assert.Null(address.Province);
        }

        [Fact]
        public void A_label_word_in_the_middle_of_a_part_is_not_a_label()
        {
            ThaiIdCardAddress address = ParseAddress("1#" + Soi + NameA + Khet + "#" + Road + Khet + NameB + "#x" + Province + "#y" + Amphoe + "#z#w#v").Address;

            Assert.Equal(NameA + Khet, address.Soi);
            Assert.Equal(Khet + NameB, address.Road);
            Assert.Null(address.Khet);
            Assert.Null(address.Province);
            Assert.Null(address.Amphoe);
        }

        [Fact]
        public void A_village_name_starting_like_moo_is_not_moo()
        {
            string village = "\u0E2B\u0E21\u0E39\u0E48\u0E1A\u0E49\u0E32\u0E19" + NameA;

            ThaiIdCardAddress address = ParseAddress("1#" + village + "#a#b#c#d#e#f").Address;

            Assert.Null(address.Moo);
            Assert.Contains(village, address.RawParts);
        }

        [Fact]
        public void Two_parts_with_one_label_set_neither_and_are_marked()
        {
            ThaiIdCardData card = ParseAddress("1#" + Soi + "A#" + Soi + "B#b#c#d#e#f");

            Assert.Null(card.Address.Soi);
            Assert.Contains(Soi + "A", card.Address.RawParts);
            Assert.Contains(Soi + "B", card.Address.RawParts);
            Assert.Equal(FieldQuality.Suspect, card.Quality.Address);
        }

        [Fact]
        public void Full_text_joins_the_parts_that_have_text()
        {
            ThaiIdCardData card = ParseAddress("99/9#" + Moo + " 5###" + Road + "R#b#c#d");

            Assert.Equal("99/9 " + Moo + " 5 " + Road + "R b c d", card.Address.Full);
            Assert.Equal(FieldQuality.Ok, card.Quality.Address);
        }

        [Theory]
        [InlineData("")]
        [InlineData("no separators")]
        [InlineData("a#b#c")]
        [InlineData("a#b#c#d#e#f#g#h#i#j")]
        public void Any_other_part_count_is_kept_and_marked(string text)
        {
            ThaiIdCardData card = ParseAddress(text);

            Assert.Equal(FieldQuality.Suspect, card.Quality.Address);
            Assert.Equal(text.Replace('#', ' ').Trim(), card.Address.Full);
        }

        [Fact]
        public void A_label_alone_is_an_empty_value_and_not_missing()
        {
            Assert.Equal(string.Empty, ParseAddress("1#" + Soi + "#a#b#c#d#e#f").Address.Soi);
        }

        // ---- citizen id ----

        [Fact]
        public void The_synthetic_fixture_passes_the_checksum_and_the_old_one_does_not()
        {
            Assert.Equal("1234567890121", SimulatedCard.SyntheticCitizenId);
            Assert.True(new ThaiIdCardData(citizenId: "1234567890121").IsValidCitizenId);
            Assert.False(new ThaiIdCardData(citizenId: "1234567890123").IsValidCitizenId);
        }

        [Fact]
        public void A_bad_checksum_keeps_the_value_and_is_marked()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { CitizenId = Ok(Tis("1234567890123")) });

            Assert.Equal("1234567890123", card.CitizenId);
            Assert.False(card.IsValidCitizenId);
            Assert.Equal("1", card.PersonCategoryCode);
            Assert.Equal(FieldQuality.Suspect, card.Quality.CitizenId);
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("12345678901abc")]
        [InlineData("1234567890 21")]
        public void A_malformed_citizen_id_keeps_the_value_and_has_no_category(string text)
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { CitizenId = Ok(Tis(text)) });

            Assert.Equal(text, card.CitizenId);
            Assert.False(card.IsValidCitizenId);
            Assert.Null(card.PersonCategoryCode);
            Assert.NotEqual(FieldQuality.Ok, card.Quality.CitizenId);
        }

        [Fact]
        public void Empty_bytes_count_as_a_failed_field()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { CitizenId = Ok(Array.Empty<byte>()) });

            Assert.Null(card.CitizenId);
            Assert.Equal(FieldQuality.Failed, card.Quality.CitizenId);
        }

        [Theory]
        [InlineData("0123456789016", "0")] // the first digit is not judged
        [InlineData("1234567890121", "1")]
        public void Category_code_is_the_raw_first_digit(string text, string code)
        {
            var card = new ThaiIdCardData(citizenId: text);

            Assert.True(card.IsValidCitizenId);
            Assert.Equal(code, card.PersonCategoryCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("\u0E51234567890121")] // Thai digit one
        [InlineData("12345678901210")]
        public void Is_valid_is_false_for_anything_else_and_does_not_throw(string? text)
        {
            var card = new ThaiIdCardData(citizenId: text);

            Assert.False(card.IsValidCitizenId);
            Assert.Null(card.PersonCategoryCode);
        }

        [Fact]
        public void No_public_member_claims_verification()
        {
            string[] forbidden = { "IsVerified", "IsGenuine", "IsAuthentic", "IsExpired" };
            var members = typeof(ThaiIdCardData).Assembly.GetExportedTypes().SelectMany(t => t.GetMembers()).Select(m => m.Name);

            Assert.Empty(members.Intersect(forbidden));
        }

        // ---- issuer ----

        [Fact]
        public void Issuer_is_trimmed_of_padding_and_keeps_inner_text()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { Issuer = Ok(Tis(NameA + " " + NameB + new string(' ', 60))) });

            Assert.Equal(NameA + " " + NameB, card.Issuer);
            Assert.Equal(FieldQuality.Ok, card.Quality.Issuer);
        }

        [Fact]
        public void Status_word_bytes_at_the_end_of_data_are_data_and_get_marked()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { Issuer = Ok(new byte[] { 0x41, 0x42, 0x90, 0x00 }) });

            Assert.StartsWith("AB", card.Issuer, StringComparison.Ordinal);
            Assert.True(card.Issuer!.Length > 2);
            Assert.Equal(FieldQuality.Suspect, card.Quality.Issuer);
        }

        [Fact]
        public void Blank_issuer_is_marked()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { Issuer = Ok(Tis(new string(' ', 100))) });

            Assert.Equal(string.Empty, card.Issuer);
            Assert.Equal(FieldQuality.Suspect, card.Quality.Issuer);
        }

        // ---- photo ----

        [Fact]
        public void Photo_is_cut_at_the_end_marker_by_position_and_0x20_inside_stays()
        {
            byte[] jpeg = MiniJpeg.Build(0x20, 0x11, 0x20, 0x20);
            byte[] padded = jpeg.Concat(Enumerable.Repeat((byte)0x20, 50)).ToArray();

            ThaiIdCardData card = ParsePhoto(padded);

            Assert.Equal(jpeg, card.GetPhoto());
            Assert.Equal(FieldQuality.Ok, card.Quality.Photo);
            Assert.Equal(PhotoFailureReason.None, card.PhotoFailure);
        }

        [Fact]
        public void No_start_marker_means_no_photo_and_a_reason()
        {
            ThaiIdCardData card = ParsePhoto(new byte[] { 0x20, 0x20, 0xFF, 0xD8, 0xFF, 0xD9 });

            Assert.Null(card.GetPhoto());
            Assert.Equal(PhotoFailureReason.NoStartMarker, card.PhotoFailure);
            Assert.Equal(FieldQuality.Failed, card.Quality.Photo);
        }

        [Fact]
        public void No_end_marker_means_no_photo_and_a_reason()
        {
            ThaiIdCardData card = ParsePhoto(MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0x11, 0xFF, 0x00, 0xD9 }));

            Assert.Null(card.GetPhoto());
            Assert.Equal(PhotoFailureReason.NoEndMarker, card.PhotoFailure);
            Assert.Equal(FieldQuality.Failed, card.Quality.Photo);
        }

        [Fact]
        public void A_photo_not_asked_for_has_no_reason()
        {
            ThaiIdCardData card = CardDataParser.Parse(new RawCardFields { Photo = new RawCardField(null, FieldQuality.NotRead) });

            Assert.Null(card.GetPhoto());
            Assert.Equal(PhotoFailureReason.None, card.PhotoFailure);
            Assert.Equal(FieldQuality.NotRead, card.Quality.Photo);
        }

        [Fact]
        public void A_failed_read_with_no_end_marker_is_a_read_failure()
        {
            ThaiIdCardData card = ParsePhoto(MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0x11 }), FieldQuality.Failed);

            Assert.Null(card.GetPhoto());
            Assert.Equal(PhotoFailureReason.ReadFailed, card.PhotoFailure);
            Assert.Equal(FieldQuality.Failed, card.Quality.Photo);
        }

        [Fact]
        public void A_failed_read_with_no_bytes_is_a_read_failure()
        {
            ThaiIdCardData card = ParsePhoto(Array.Empty<byte>(), FieldQuality.Failed);

            Assert.Equal(PhotoFailureReason.ReadFailed, card.PhotoFailure);
            Assert.Equal(FieldQuality.Failed, card.Quality.Photo);
        }

        [Theory]
        [InlineData((int)FieldQuality.Failed)]
        [InlineData((int)FieldQuality.Suspect)]
        public void A_whole_image_before_a_later_gap_is_kept_and_marked(int readQuality)
        {
            byte[] jpeg = MiniJpeg.Build(0x11);

            ThaiIdCardData card = ParsePhoto(jpeg, (FieldQuality)readQuality);

            Assert.Equal(jpeg, card.GetPhoto());
            Assert.Equal(FieldQuality.Suspect, card.Quality.Photo);
            Assert.Equal(PhotoFailureReason.None, card.PhotoFailure);
        }

        [Fact]
        public void The_photo_cannot_be_changed_from_outside()
        {
            byte[] source = MiniJpeg.Build(0x11);
            var card = new ThaiIdCardData(photo: source);

            source[2] = 0x00;
            card.GetPhoto()![3] = 0x00;

            Assert.Equal(MiniJpeg.Build(0x11), card.GetPhoto());
        }

        // ---- malformed data in general ----

        [Fact]
        public void Random_bytes_in_every_field_never_throw()
        {
            var random = new Random(20261102);
            for (int i = 0; i < 2000; i++)
            {
                ThaiIdCardData card = CardDataParser.Parse(new RawCardFields
                {
                    CitizenId = Ok(RandomBytes(random, 20)),
                    PersonInfo = Ok(RandomBytes(random, 260)),
                    Address = Ok(RandomBytes(random, 200)),
                    IssueExpire = Ok(RandomBytes(random, 24)),
                    Issuer = Ok(RandomBytes(random, 120)),
                    Photo = Ok(RandomBytes(random, 300)),
                });

                Assert.NotNull(card.ThaiName);
                _ = card.BirthDate.ToDateTime();
                _ = card.BirthDate.AgeReferenceDate;
            }
        }

        private static byte[] RandomBytes(Random random, int max)
        {
            var data = new byte[random.Next(0, max)];
            random.NextBytes(data);
            return data;
        }

        // ---- text of the models ----

        [Fact]
        public void To_string_of_the_models_shows_no_card_data()
        {
            const string Sentinel = "ZQXSENTINEL";
            var name = new ThaiIdCardName(Sentinel, Sentinel, Sentinel, Sentinel, Sentinel);
            var address = new ThaiIdCardAddress(Sentinel, new[] { Sentinel }, Sentinel, Sentinel, Sentinel, Sentinel, Sentinel, Sentinel, Sentinel, Sentinel, Sentinel);
            var date = new ThaiCardDate(Sentinel);
            var card = new ThaiIdCardData(Sentinel, name, name, date, CardSex.Male, 0x31, address, date, date, Sentinel, Encoding.ASCII.GetBytes(Sentinel));

            object[] all = { name, address, date, card, card.Quality, card.Diagnostics };

            Assert.All(all, o => Assert.DoesNotContain(Sentinel, o.ToString()!, StringComparison.Ordinal));
        }
    }
}
