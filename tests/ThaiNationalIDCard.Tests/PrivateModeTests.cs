using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// The private mode of the public reader over the simulator. All data is made up, and the answers below are assumptions:
    /// passing here shows the code handles them, and says nothing about what a real card answers.
    /// </summary>
    public class PrivateModeTests
    {
        private const string Value = "AB1234567890";

        private static readonly byte[] Ok = { 0x90, 0x00 };

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static SimulatedCard Card()
        {
            var card = new SimulatedCard();
            card.AddApplet(SimulatedCard.NhsoAidHex, Enumerable.Range(0, 0x200).Select(i => (byte)(i & 0xFF)).ToArray());
            card.AddApplet(SimulatedCard.AdmAidHex, SyntheticImage.AdmImage(Value));
            return card;
        }

        private static byte[] Header() => Enumerable.Repeat((byte)0x78, 7).ToArray();

        private static byte[] Slot(string text)
        {
            var slot = new byte[16];
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, slot, bytes.Length);
            return slot;
        }

        private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

#pragma warning disable xUnit1051 // The helper passes the test token itself.
        private static Task<ThaiIdCardData> Read(SimulatedCard card, CardReadMode mode = CardReadMode.PublicAndPrivate) =>
            new ThaiIdCardReader(() => card, null, new DelaySpy(card).Wait).ReadAsync(card.ReaderName, new ReadOptions { Mode = mode }, Ct);
#pragma warning restore xUnit1051

        /// <summary>The applet data command gets the first answer, the GET RESPONSE right after it the second.</summary>
        private static void AnswerAdm(SimulatedCard card, byte[] first, byte[]? second = null)
        {
            bool armed = false;
            card.AnswerOverride = (position, command) =>
            {
                if (ApduText.Hex(command) == "8000000007")
                {
                    armed = true;
                    return first;
                }

                if (armed && Flow.IsGetResponse(command) && second != null)
                {
                    armed = false;
                    return second;
                }

                return null;
            };
        }

        private static void AssertPublicIsWhole(ThaiIdCardData data)
        {
            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
            Assert.Equal(FieldQuality.Ok, data.Quality.CitizenId);
            Assert.Equal(FieldQuality.Ok, data.Quality.ThaiName);
            Assert.Equal(FieldQuality.Ok, data.Quality.Address);
            Assert.Equal(FieldQuality.Ok, data.Quality.IssueDate);
            Assert.Equal(FieldQuality.Ok, data.Quality.Issuer);
        }

        private static CardRawValue[] NhsoValues(ThaiIdCardNhsoData nhso) => new[]
        {
            nhso.Raw0004, nhso.Raw0040, nhso.Raw00A4, nhso.Raw00F4, nhso.Raw0144, nhso.Raw0145, nhso.Raw014D, nhso.Raw0155, nhso.Raw015D,
        };

        // ---- what each mode gives ----

        [Fact]
        public async Task Public_mode_has_no_private_model_and_sends_nothing_for_it()
        {
            SimulatedCard card = Card();

            ThaiIdCardData data = await Read(card, CardReadMode.Public);

            Assert.Null(data.Private);
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.AdmAidHex));
            Assert.DoesNotContain("8000000007", card.ReceivedHex);
        }

        [Fact]
        public async Task Data_built_by_hand_has_no_private_model_and_empty_values_are_not_read()
        {
            var data = new ThaiIdCardData();
            var extra = new ThaiIdCardPrivateData();

            Assert.Null(data.Private);
            Assert.Null(extra.LaserId);
            Assert.Equal(FieldQuality.NotRead, extra.LaserIdQuality);
            Assert.All(NhsoValues(extra.Nhso), v => Assert.Equal(FieldQuality.NotRead, v.Quality));
            Assert.Null(extra.CardVersionRaw.GetBytes());
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Private_mode_gives_the_public_fields_and_every_private_field_with_its_quality()
        {
            SimulatedCard card = Card();

            ThaiIdCardData data = await Read(card);
            ThaiIdCardPrivateData extra = data.Private!;

            AssertPublicIsWhole(data);
            Assert.Equal(CardReadMode.PublicAndPrivate, data.Diagnostics.Mode);

            Assert.Equal(Enumerable.Repeat((byte)0x71, 4), extra.CardVersionRaw.GetBytes());
            Assert.Equal(Enumerable.Repeat((byte)0x72, 20), extra.MoiRaw00E2.GetBytes());
            Assert.Equal(Enumerable.Repeat((byte)0x73, 2), extra.MoiRaw0177.GetBytes());
            Assert.Equal(Enumerable.Repeat((byte)0x75, 14), extra.MoiRaw1619.GetBytes());
            Assert.All(new[] { extra.CardVersionRaw, extra.MoiRaw00E2, extra.MoiRaw0177, extra.MoiRaw1619 }, v => Assert.Equal(FieldQuality.Ok, v.Quality));

            (int Offset, int Length)[] nhso =
            {
                (0x0004, 60), (0x0040, 100), (0x00A4, 80), (0x00F4, 80), (0x0144, 1), (0x0145, 8), (0x014D, 8), (0x0155, 8), (0x015D, 1),
            };
            CardRawValue[] values = NhsoValues(extra.Nhso);
            byte[] image = Enumerable.Range(0, 0x200).Select(i => (byte)(i & 0xFF)).ToArray();
            for (int i = 0; i < nhso.Length; i++)
            {
                Assert.Equal(ApduText.Slice(image, nhso[i].Offset, nhso[i].Length), values[i].GetBytes());
                Assert.Equal(FieldQuality.Ok, values[i].Quality);
            }

            Assert.Equal(Value, extra.LaserId);
            Assert.Equal(FieldQuality.Ok, extra.LaserIdQuality);
            Assert.Equal(23, extra.AdmAppletDataRaw.GetBytes()!.Length);
            Assert.Equal(FieldQuality.Ok, extra.AdmAppletDataRaw.Quality);
        }

        [Fact]
        public async Task Raw_values_are_copies_of_what_the_card_gave()
        {
            ThaiIdCardData data = await Read(Card());
            CardRawValue value = data.Private!.MoiRaw0177;

            byte[] first = value.GetBytes()!;
            first[0] = 0x00;

            Assert.Equal(new byte[] { 0x73, 0x73 }, value.GetBytes());
        }

        [Fact]
        public void No_private_member_name_claims_a_meaning_nobody_confirmed()
        {
            string[] claims = { "Religion", "Blood", "CardType", "Serial", "Marital", "Bp1", "Request", "Reference", "Title", "ChipId" };
            IEnumerable<string> names = new[] { typeof(ThaiIdCardPrivateData), typeof(ThaiIdCardNhsoData), typeof(CardRawValue) }
                .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Select(m => m.Name);

            Assert.DoesNotContain(names, n => claims.Any(c => n.Contains(c, StringComparison.OrdinalIgnoreCase)));
        }

        // ---- the Laser ID path ----

        [Fact]
        public async Task The_laser_path_sends_exactly_select_data_command_and_get_response_of_16()
        {
            SimulatedCard card = Card();

            await Read(card);

            string[] wire = card.ReceivedHex.ToArray();
            int select = Array.IndexOf(wire, "00a4040008a000000084060002");
            Assert.True(select > 0);
            Assert.Equal(new[] { "00a4040008a000000084060002", "8000000007", "00c0000010" }, wire.Skip(select));
        }

        [Fact]
        public async Task A_direct_answer_of_the_whole_shape_is_good()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, Join(Header(), Slot(Value), Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(Value, extra.LaserId);
            Assert.Equal(FieldQuality.Ok, extra.LaserIdQuality);
        }

        [Fact]
        public async Task A_card_announcing_all_23_bytes_at_once_gets_one_get_response_of_23()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, new byte[] { 0x61, 0x17 }, Join(Header(), Slot(Value), Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(Value, extra.LaserId);
            Assert.Equal(FieldQuality.Ok, extra.LaserIdQuality);
            Assert.Equal("00c0000017", card.ReceivedHex.Last());
        }

        [Fact]
        public async Task A_card_offering_more_never_gets_a_longer_get_response_and_the_shape_is_suspect()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, Join(Header(), new byte[] { 0x61, 0x20 }), Join(Slot(Value), Ok));

            ThaiIdCardData data = await Read(card);

            Assert.Equal("00c0000010", card.ReceivedHex.Last());
            Assert.Equal(FieldQuality.Suspect, data.Private!.LaserIdQuality);
            Assert.Equal(23, data.Private.AdmAppletDataRaw.GetBytes()!.Length);
            AssertPublicIsWhole(data);
        }

        [Fact]
        public async Task Announcing_256_bytes_still_asks_for_at_most_23()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, new byte[] { 0x61, 0x00 }, Join(Header(), Slot(Value), Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal("00c0000017", card.ReceivedHex.Last());
            Assert.Equal(FieldQuality.Suspect, extra.LaserIdQuality);
        }

        [Fact]
        public async Task A_direct_answer_longer_than_the_shape_is_cut_and_suspect()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, Join(Header(), Slot(Value), new byte[] { 0x01, 0x02 }, Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(23, extra.AdmAppletDataRaw.GetBytes()!.Length);
            Assert.Equal(FieldQuality.Suspect, extra.LaserIdQuality);
            Assert.Equal(FieldQuality.Suspect, extra.AdmAppletDataRaw.Quality);
        }

        [Fact]
        public async Task Only_the_header_keeps_the_raw_bytes_gives_no_value_and_is_suspect()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, Join(Header(), Ok));

            ThaiIdCardData data = await Read(card);

            Assert.Null(data.Private!.LaserId);
            Assert.Equal(FieldQuality.Suspect, data.Private.LaserIdQuality);
            Assert.Equal(Header(), data.Private.AdmAppletDataRaw.GetBytes());
            AssertPublicIsWhole(data);
        }

        [Fact]
        public async Task Only_the_16_bytes_keeps_the_raw_bytes_and_does_not_guess_where_the_value_is()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, new byte[] { 0x61, 0x10 }, Join(Slot(Value), Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Null(extra.LaserId);
            Assert.Equal(FieldQuality.Suspect, extra.LaserIdQuality);
            Assert.Equal(Slot(Value), extra.AdmAppletDataRaw.GetBytes());
        }

        [Theory]
        [InlineData("AB123456789")]
        [InlineData("AB12345678901")]
        [InlineData("AB1-234567-89")]
        [InlineData("AB1234567890ABCD")]
        public async Task A_readable_value_of_another_format_is_kept_and_suspect(string text)
        {
            SimulatedCard card = Card();
            AnswerAdm(card, Join(Header(), new byte[] { 0x61, 0x10 }), Join(Slot(text), Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(text, extra.LaserId);
            Assert.Equal(FieldQuality.Suspect, extra.LaserIdQuality);
        }

        [Theory]
        [InlineData("0000000000000000")]
        [InlineData("41420041424344454647484950510000")]
        [InlineData("00414243444546474849505152000000")]
        [InlineData("E0E1E2E3E4E5E6E7E8E9EAEB00000000")]
        [InlineData("41424320444546474849505100000000")]
        public async Task A_slot_that_is_not_readable_text_gives_no_value_and_keeps_the_bytes(string slotHex)
        {
            byte[] slot = ApduText.Bytes(slotHex);
            SimulatedCard card = Card();
            AnswerAdm(card, Join(Header(), new byte[] { 0x61, 0x10 }), Join(slot, Ok));

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Null(extra.LaserId);
            Assert.Equal(FieldQuality.Suspect, extra.LaserIdQuality);
            Assert.Equal(Join(Header(), slot), extra.AdmAppletDataRaw.GetBytes());
        }

        [Fact]
        public async Task A_6c_answer_cannot_widen_the_data_command_and_fails_this_field_only()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, new byte[] { 0x6C, 0x17 });

            ThaiIdCardData data = await Read(card);

            Assert.DoesNotContain("8000000017", card.ReceivedHex);
            Assert.Null(data.Private!.LaserId);
            Assert.Equal(FieldQuality.Failed, data.Private.LaserIdQuality);
            Assert.Null(data.Private.AdmAppletDataRaw.GetBytes());
            FieldDiagnostics info = data.Diagnostics.Fields.Single(f => f.Id == CardFieldId.AdmAppletData);
            Assert.Equal(0x6C17, info.StatusWord);
            Assert.True(info.Flags.HasFlag(ExchangeNotes.FollowUpRejected));
            AssertPublicIsWhole(data);
        }

        [Fact]
        public async Task A_bad_status_on_the_data_command_fails_this_field_only()
        {
            SimulatedCard card = Card();
            AnswerAdm(card, new byte[] { 0x69, 0x82 });

            ThaiIdCardData data = await Read(card);

            Assert.Null(data.Private!.LaserId);
            Assert.Equal(FieldQuality.Failed, data.Private.LaserIdQuality);
            Assert.Equal(FieldQuality.Failed, data.Private.AdmAppletDataRaw.Quality);
            Assert.Equal(FieldQuality.Ok, data.Private.Nhso.Raw0004.Quality);
            AssertPublicIsWhole(data);
        }

        [Fact]
        public async Task Private_get_response_uses_the_p2_the_citizen_id_locked()
        {
            SimulatedCard card = Card();
            card.GetResponseP2 = 0x01;

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(Value, extra.LaserId);
            Assert.Equal("00c0000110", card.ReceivedHex.Last());
        }

        // ---- chip data ----

        [Fact]
        public async Task Chip_data_is_not_read_and_says_so_with_no_command_sent_for_it()
        {
            SimulatedCard card = Card();

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(FieldQuality.NotRead, extra.ChipDataRaw.Quality);
            Assert.Null(extra.ChipDataRaw.GetBytes());
            Assert.DoesNotContain(card.ReceivedHex, h => h.StartsWith("80ca", StringComparison.Ordinal));
            Assert.DoesNotContain(card.Log, e => Flow.IsSelect(e.Command) && e.Command.Length <= 5);
        }

        // ---- failures stay per field ----

        [Fact]
        public async Task A_card_with_neither_private_applet_keeps_every_public_field()
        {
            var card = new SimulatedCard();

            ThaiIdCardData data = await Read(card);
            ThaiIdCardPrivateData extra = data.Private!;

            AssertPublicIsWhole(data);
            Assert.Equal(FieldQuality.Ok, extra.MoiRaw0177.Quality);
            Assert.All(NhsoValues(extra.Nhso), v =>
            {
                Assert.Equal(FieldQuality.Failed, v.Quality);
                Assert.Null(v.GetBytes());
            });
            Assert.Null(extra.LaserId);
            Assert.Equal(FieldQuality.Failed, extra.LaserIdQuality);
            Assert.Equal(FieldQuality.Failed, extra.AdmAppletDataRaw.Quality);
        }

        [Theory]
        [InlineData("80b0000002" + "0004")]
        [InlineData("80b000e2020014")]
        [InlineData("80b001770200" + "02")]
        [InlineData("80b016190200" + "0e")]
        public async Task A_bad_status_on_a_moi_private_range_fails_that_one_with_no_bytes(string command)
        {
            SimulatedCard card = Card();
            card.AnswerOverride = (position, sent) => ApduText.Hex(sent) == command ? new byte[] { 0x6B, 0x00 } : null;

            ThaiIdCardData data = await Read(card);
            ThaiIdCardPrivateData extra = data.Private!;

            CardRawValue[] values = { extra.CardVersionRaw, extra.MoiRaw00E2, extra.MoiRaw0177, extra.MoiRaw1619 };
            CardRawValue failed = Assert.Single(values, v => v.Quality == FieldQuality.Failed);
            Assert.Null(failed.GetBytes());
            Assert.Equal(3, values.Count(v => v.Quality == FieldQuality.Ok));
            Assert.Equal(Value, extra.LaserId);
            AssertPublicIsWhole(data);
        }

        [Fact]
        public async Task A_short_range_is_kept_and_suspect()
        {
            SimulatedCard card = Card();
            card.AnswerOverride = (position, sent) => ApduText.Hex(sent) == "80b000e2020014" ? new byte[] { 0x72, 0x72, 0x90, 0x00 } : null;

            ThaiIdCardPrivateData extra = (await Read(card)).Private!;

            Assert.Equal(FieldQuality.Suspect, extra.MoiRaw00E2.Quality);
            Assert.Equal(new byte[] { 0x72, 0x72 }, extra.MoiRaw00E2.GetBytes());
        }

        [Theory]
        [InlineData((int)CardFailureKind.CardRemoved)]
        [InlineData((int)CardFailureKind.CardReset)]
        [InlineData((int)CardFailureKind.Timeout)]
        public async Task A_connection_failure_in_the_private_stage_marks_the_rest_not_read(int failureValue)
        {
            SimulatedCard card = Card();
            int publicCommands = 1 + (6 * 2) + 40;
            card.FailTransmitAt(publicCommands + 1, (CardFailureKind)failureValue);

            ThaiIdCardData data = await new ThaiIdCardReader(() => card, null, new DelaySpy(card).Wait)
                .ReadAsync(card.ReaderName, new ReadOptions { Mode = CardReadMode.PublicAndPrivate, IncludePhoto = true }, Ct);

            AssertPublicIsWhole(data);
            Assert.NotNull(data.GetPhoto());
            Assert.Equal(FieldQuality.NotRead, data.Private!.LaserIdQuality);
            Assert.Null(data.Private.LaserId);
            Assert.Equal(FieldQuality.NotRead, data.Private.Nhso.Raw015D.Quality);
        }

        // ---- no data in any text ----

        private const string Sentinel = "ZQXSENTINELZQX";

        /// <summary>The sentinel in every byte of every range, the citizen ID aside, and a value slot that holds it too.</summary>
        private static SimulatedCard SentinelCard()
        {
            var card = new SimulatedCard();
            byte[] marker = Encoding.ASCII.GetBytes(Sentinel);
            byte[] image = card.MoiImage;
            for (int i = 0; i < image.Length; i++)
            {
                if (i < SyntheticImage.CitizenId || i >= SyntheticImage.CitizenId + 13)
                {
                    image[i] = marker[i % marker.Length];
                }
            }

            card.AddApplet(SimulatedCard.NhsoAidHex, Enumerable.Range(0, 0x200).Select(i => marker[i % marker.Length]).ToArray());
            byte[] adm = SyntheticImage.AdmImage("SENTINELZQX0");
            card.AddApplet(SimulatedCard.AdmAidHex, adm);
            return card;
        }

        private static void AssertClean(string text)
        {
            Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
            Assert.DoesNotContain("53454E54", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.ASCII.GetBytes("SENTINEL")).TrimEnd('='), text, StringComparison.Ordinal);
            Assert.DoesNotContain(SimulatedCard.SyntheticCitizenId, text, StringComparison.Ordinal);
        }

        private static IEnumerable<object> AllPrivateParts(ThaiIdCardPrivateData extra)
        {
            yield return extra;
            yield return extra.Nhso;
            foreach (CardRawValue value in NhsoValues(extra.Nhso))
            {
                yield return value;
            }

            yield return extra.AdmAppletDataRaw;
            yield return extra.CardVersionRaw;
            yield return extra.MoiRaw00E2;
            yield return extra.MoiRaw0177;
            yield return extra.MoiRaw1619;
            yield return extra.ChipDataRaw;
        }

        private static string Everything(ReadDiagnostics diagnostics) =>
            diagnostics + "\n" + string.Join("\n", diagnostics.Trace) + "\n" + string.Join("\n", diagnostics.Fields) + "\n" + diagnostics.AtrHex;

        [Fact]
        public async Task Control_the_sentinel_really_is_in_every_private_field()
        {
            ThaiIdCardPrivateData extra = (await Read(SentinelCard())).Private!;

            Assert.Contains("SENTINEL", extra.LaserId!, StringComparison.Ordinal);
            string joined = Encoding.ASCII.GetString(new[]
            {
                extra.Nhso.Raw0040, extra.MoiRaw00E2, extra.MoiRaw1619,
            }.SelectMany(v => v.GetBytes()!).ToArray());
            Assert.Contains("SENTINEL", joined, StringComparison.Ordinal);

            byte[] marker = Encoding.ASCII.GetBytes(Sentinel);
            Assert.All(new[] { extra.Nhso.Raw0144, extra.MoiRaw0177, extra.CardVersionRaw }, v =>
                Assert.All(v.GetBytes()!, b => Assert.Contains(b, marker)));
        }

        [Fact]
        public async Task To_string_of_every_private_part_holds_no_card_data()
        {
            ThaiIdCardData data = await Read(SentinelCard());

            AssertClean(data.ToString());
            AssertClean(Everything(data.Diagnostics));
            Assert.All(AllPrivateParts(data.Private!), p => AssertClean(p.ToString()!));
            Assert.All(AllPrivateParts(data.Private!), p => Assert.EndsWith("(data hidden)", p.ToString()!, StringComparison.Ordinal));
        }

        [Fact]
        public void The_walk_above_covers_every_private_property()
        {
            var extra = new ThaiIdCardPrivateData();
            object[] parts = AllPrivateParts(extra).ToArray();

            int values = typeof(ThaiIdCardPrivateData).GetProperties().Count(p => p.PropertyType == typeof(CardRawValue)) +
                typeof(ThaiIdCardNhsoData).GetProperties().Count(p => p.PropertyType == typeof(CardRawValue));

            Assert.Equal(15, values);
            Assert.Equal(values + 2, parts.Length);
        }

        [Fact]
        public async Task A_failure_at_any_point_of_a_private_read_holds_no_card_data()
        {
            for (int position = 1; position <= 80; position++)
            {
                SimulatedCard card = SentinelCard();
                card.FailTransmitAt(position, CardFailureKind.Communication);

                try
                {
                    ThaiIdCardData data = await Read(card);
                    AssertClean(data.ToString());
                    AssertClean(Everything(data.Diagnostics));
                    Assert.All(AllPrivateParts(data.Private!), p => AssertClean(p.ToString()!));
                }
                catch (ThaiIdCardException ex)
                {
                    AssertClean(ex.ToString());
                    AssertClean(ex.Message);
                    AssertClean(Everything(ex.Diagnostics!));
                    Assert.Empty(ex.Data.Keys.Cast<object>());
                }
            }
        }

        [Fact]
        public async Task Private_mode_touches_no_never_read_range_and_no_forbidden_command()
        {
            SimulatedCard card = SentinelCard();

            await new ThaiIdCardReader(() => card, null, new DelaySpy(card).Wait)
                .ReadAsync(card.ReaderName, new ReadOptions { Mode = CardReadMode.PublicAndPrivate, IncludePhoto = true }, Ct);

            HashSet<int> offsets = card.AnsweredOffsets();
            Assert.DoesNotContain(offsets, o => o >= 0x015A && o <= 0x0166);
            Assert.DoesNotContain(offsets, o => o >= 0x1627 && o <= 0x173D);
            Assert.DoesNotContain(card.Log, e => (e.Command[0] == 0x80 && (e.Command[1] == 0x20 || e.Command[1] == 0xB4)) || (e.Command[0] == 0xB0 && e.Command[1] == 0x34));
        }
    }
}
