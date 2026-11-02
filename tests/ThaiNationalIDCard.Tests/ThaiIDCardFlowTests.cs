using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PCSC;
using PCSC.Exceptions;
using PCSC.Iso7816;
using PCSC.Monitoring;
using PCSC.Utils;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class ThaiIDCardFlowTests
    {
        private static ThaiIDCard NewReader(FakeReader reader, out FakeContext context)
        {
            context = new FakeContext();
            return new ThaiIDCard(context, reader, 0); // no 1.5 s ATR delay in tests
        }

        private static ThaiIDCard NewReader(FakeReader reader) => NewReader(reader, out _);

        /// <summary>Calls the private monitor callback exactly like SCardMonitor would.</summary>
        private static void RaiseInserted(ThaiIDCard card, string readerName = "Fake Reader 0")
        {
            var method = typeof(ThaiIDCard).GetMethod("_Inserted", BindingFlags.NonPublic | BindingFlags.Instance);
            var args = new CardStatusEventArgs(readerName, SCRState.Present, new byte[] { 0x3B, 0x79 });
            method.Invoke(card, new object[] { args });
        }

        // ---- ATR / command set selection ----

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Atr_decides_the_get_response_command_set(bool type01)
        {
            // 3B 67 -> Type01 (GET RESPONSE P2 = 01), anything else -> Type02 (P2 = 00).
            var reader = CardScripts.NewCard(type01);
            var card = NewReader(reader);

            Personal p = card.readCitizenid();

            Assert.NotNull(p);
            Assert.Equal(CardScripts.SyntheticCitizenId, p.Citizenid);
            Assert.Equal(1, reader.CountOf(CardScripts.GetResponse(type01, 13)));
            Assert.Equal(0, reader.CountOf(CardScripts.GetResponse(!type01, 13)));
        }

        [Fact]
        public void Open_selects_the_applet_then_connects_and_close_releases()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader, out var context);

            Assert.True(card.Open());
            Assert.True(reader.IsConnected);
            Assert.Equal("Fake Reader 0", reader.ConnectedReaderName);
            Assert.Equal(CardScripts.SelectApplet, reader.SentHex.First());
            Assert.Equal(0, context.ReleaseCalls); // still open

            Assert.True(card.Close());
            Assert.False(reader.IsConnected);
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void Open_uses_the_given_reader_name()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader);

            Assert.True(card.Open("Other Reader"));
            Assert.Equal("Other Reader", reader.ConnectedReaderName);
        }

        // ---- full read / GET RESPONSE chaining ----

        [Fact]
        public void ReadAll_decodes_all_fields_through_get_response()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader);

            Personal p = card.readAll();

            Assert.NotNull(p);
            Assert.Equal(CardScripts.SyntheticCitizenId, p.Citizenid);
            Assert.Equal(CardScripts.ThFirst, p.Th_Firstname);
            Assert.Equal("Sample", p.En_Lastname);
            Assert.Equal(new DateTime(1997, 1, 31), p.Birthday);
            Assert.Equal("99/9", p.addrHouseNo);
            Assert.Equal("Test Province", p.addrProvince);
            Assert.Equal(new DateTime(2017, 1, 31), p.Issue);
            Assert.Equal(new DateTime(2026, 1, 30), p.Expire);
            Assert.StartsWith("Test Issuer", p.Issuer);
            Assert.Null(p.PhotoRaw);

            // select + 5 files, each followed by one GET RESPONSE
            Assert.Equal(1 + 5 * 2, reader.Sent.Count);
            Assert.False(reader.IsConnected); // readAll closes the card
        }

        [Fact]
        public void Select_failure_is_reported_as_unsupported_card_and_cleans_up()
        {
            var reader = new FakeReader();
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x6A, 0x82));
            var card = NewReader(reader, out var context);

            Assert.False(card.Open());

            Assert.Equal(1, card.ErrorCode());
            Assert.Contains("not support", card.Error());
            Assert.False(reader.IsConnected);
            Assert.Equal(1, reader.DisconnectCalls);
            Assert.Equal(1, context.ReleaseCalls);
            Assert.Null(card.readAll());
        }

        // ---- photo ----

        [Fact]
        public void Photo_read_transmits_exactly_one_command_per_chunk()
        {
            var reader = CardScripts.NewCard(withPhoto: true);
            var card = NewReader(reader);
            var cmds = CardScripts.PhotoCommands();
            Assert.Equal(20, cmds.Length);

            Personal p = card.readAll(true);

            Assert.NotNull(p);
            foreach (string cmd in cmds)
                Assert.Equal(1, reader.CountOf(cmd));

            // select + 5 files + 20 photo chunks, each followed by one GET RESPONSE
            Assert.Equal(1 + 5 * 2 + 20 * 2, reader.Sent.Count);
        }

        [Fact]
        public void Photo_bytes_are_the_chunks_in_order()
        {
            var reader = CardScripts.NewCard(withPhoto: true);
            var card = NewReader(reader);

            Personal p = card.readAllPhoto();

            var expected = Enumerable.Range(0, 20).SelectMany(CardScripts.PhotoChunk).ToArray();
            Assert.Equal(expected, p.PhotoRaw);
        }

        [Fact]
        public void Photo_progress_event_fires_once_per_chunk()
        {
            var reader = CardScripts.NewCard(withPhoto: true);
            var card = NewReader(reader);
            var events = new List<Tuple<int, int>>();
            card.eventPhotoProgress += (value, maximum) => events.Add(Tuple.Create(value, maximum));

            card.readAll(true);

            Assert.Equal(20, events.Count);
            for (int i = 0; i < 20; i++)
                Assert.Equal(Tuple.Create(i + 1, 20), events[i]);
        }

        [Fact]
        public void Photo_commands_use_the_expected_apdu_layout()
        {
            var cmds = CardScripts.PhotoCommands();
            Assert.Equal("80b0017b0200ff", cmds[0]);
            Assert.Equal("80b0146802" + "00ff", cmds[19]);
        }

        // ---- direct-answer reader: first transmit returns data + 90 00 (inferred from issue #25) ----

        private static readonly byte[] ExpectedPhoto = Enumerable.Range(0, 20).SelectMany(CardScripts.PhotoChunk).ToArray();

        [Fact]
        public void Fake_reports_insufficient_buffer_when_the_receive_buffer_is_too_small()
        {
            var reader = new FakeReader();
            reader.On("00", new byte[257]);

            var small = new byte[256];
            Assert.Equal(SCardError.InsufficientBuffer, reader.Transmit(IntPtr.Zero, new byte[] { 0x00 }, ref small));

            var enough = new byte[258];
            Assert.Equal(SCardError.Success, reader.Transmit(IntPtr.Zero, new byte[] { 0x00 }, ref enough));
            Assert.Equal(257, enough.Length);
        }

        [Fact]
        public void Direct_answer_photo_read_gives_the_exact_bytes()
        {
            var reader = CardScripts.NewCard(withPhoto: true, directAnswer: true);
            var card = NewReader(reader);

            Personal p = card.readAllPhoto();

            Assert.Equal(ExpectedPhoto, p.PhotoRaw);
        }

        [Fact]
        public void Direct_answer_photo_has_no_status_words_at_chunk_boundaries()
        {
            var reader = CardScripts.NewCard(withPhoto: true, directAnswer: true);
            var card = NewReader(reader);

            byte[] photo = card.readAllPhoto().PhotoRaw;

            Assert.Equal(20 * 255, photo.Length);
            for (int chunk = 1; chunk < 20; chunk++)
            {
                int boundary = chunk * 255;
                Assert.False(photo[boundary] == 0x90 && photo[boundary + 1] == 0x00, "90 00 after chunk " + chunk);
            }
        }

        [Fact]
        public void Direct_answer_photo_read_transmits_once_per_chunk_without_get_response()
        {
            var reader = CardScripts.NewCard(withPhoto: true, directAnswer: true);
            var card = NewReader(reader);

            card.readAllPhoto();

            foreach (string cmd in CardScripts.PhotoCommands())
                Assert.Equal(1, reader.CountOf(cmd));
            Assert.Equal(1 + 5 + 20, reader.Sent.Count); // select + 5 files + 20 chunks, no GET RESPONSE
            Assert.DoesNotContain(reader.SentHex, h => h.StartsWith("00c0", StringComparison.Ordinal));
        }

        [Fact]
        public void Direct_answer_text_fields_have_no_trailing_status_word_garbage()
        {
            var reader = CardScripts.NewCard(directAnswer: true);
            var card = NewReader(reader);

            Personal p = card.readAll();

            Assert.Equal(CardScripts.SyntheticCitizenId, p.Citizenid);
            Assert.Equal("Test Issuer".PadRight(100), p.Issuer);
            Assert.Equal("Test Province", p.addrProvince);
            Assert.Equal(new DateTime(2026, 1, 30), p.Expire);
            Assert.Equal(new DateTime(1997, 1, 31), p.Birthday);
        }

        [Fact]
        public void Direct_answer_strips_only_the_status_word_not_90_00_inside_the_data()
        {
            var data = new byte[] { 0x31, 0x90, 0x00, 0x32, 0x90, 0x00 };
            var reader = new FakeReader { Atr = CardScripts.NewCard().Atr, DirectAnswer = true };
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x90, 0x00));
            reader.OnData(CardScripts.Cid, data);
            var card = NewReader(reader);

            Personal p = card.readCitizenid();

            Assert.Equal(Decode(data), p.Citizenid);
        }

        [Fact]
        public void Direct_answer_accepts_256_data_bytes_plus_the_status_word()
        {
            var data = Enumerable.Range(0, 256).Select(k => (byte)0x41).ToArray();
            var reader = new FakeReader { Atr = CardScripts.NewCard().Atr, DirectAnswer = true };
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x90, 0x00));
            reader.OnData(CardScripts.Cid, data);
            var card = NewReader(reader);

            Assert.Equal(new string('A', 256), card.readCitizenid().Citizenid);
        }

        [Fact]
        public void Status_word_alone_on_first_transmit_gives_an_empty_result()
        {
            var reader = new FakeReader { Atr = CardScripts.NewCard().Atr };
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x90, 0x00));
            reader.On(CardScripts.Cid, CardScripts.Sw(0x90, 0x00));
            var card = NewReader(reader);

            Assert.Equal(string.Empty, card.readCitizenid().Citizenid);
        }

        [Theory]
        [InlineData(new byte[] { 0x6A, 0x82 })]             // error status alone
        [InlineData(new byte[] { 0x31, 0x32, 0x62, 0x83 })] // data + warning status
        [InlineData(new byte[] { 0x31, 0x32, 0x6C, 0x0D })] // data + wrong-length status
        public void Other_status_words_on_first_transmit_are_returned_as_before(byte[] response)
        {
            var reader = new FakeReader { Atr = CardScripts.NewCard().Atr };
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x90, 0x00));
            reader.On(CardScripts.Cid, response);
            var card = NewReader(reader);

            Assert.Equal(Decode(response), card.readCitizenid().Citizenid);
        }

        [Fact]
        public void One_byte_response_still_throws_as_before()
        {
            var reader = new FakeReader { Atr = CardScripts.NewCard().Atr };
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x90, 0x00));
            reader.On(CardScripts.Cid, new byte[] { 0x90 });
            var card = NewReader(reader);

            Assert.Throws<InvalidApduException>(() => card.readCitizenid());
            Assert.False(reader.IsConnected);
        }

        private static string Decode(byte[] bytes) => System.Text.Encoding.GetEncoding("TIS-620").GetString(bytes);

        // ---- EF_CARD_ISSUE_EXPIRE reads issue + expire only, never card bytes 0177-0178 ----

        private const string OldIssueExpire = "80b00167020012";

        /// <summary>Also answers the old 18-byte command, so going back to it fails an assert, not as an unscripted command.</summary>
        private static FakeReader AlsoAnswerOldIssueExpire(FakeReader reader, bool type01 = false)
        {
            return reader.OnData(OldIssueExpire, CardScripts.Tis(("25600131" + "25690130").PadRight(18)), type01);
        }

        /// <summary>First and last card offset of every READ BINARY sent (80 B0 P1 P2 02 00 Le).</summary>
        private static List<int[]> CardReads(FakeReader reader)
        {
            return reader.Sent
                .Where(c => c[0] == 0x80 && c[1] == 0xB0)
                .Select(c => new[] { (c[2] << 8) | c[3], ((c[2] << 8) | c[3]) + c[6] - 1 })
                .ToList();
        }

        [Fact]
        public void Issue_expire_command_asks_for_16_bytes_in_both_command_sets()
        {
            byte[] expected = { 0x80, 0xB0, 0x01, 0x67, 0x02, 0x00, 0x10 };
            Assert.Equal(expected, new APDU_THAILAND_IDCARD_TYPE_01().EF_CARD_ISSUE_EXPIRE);
            Assert.Equal(expected, new APDU_THAILAND_IDCARD_TYPE_02().EF_CARD_ISSUE_EXPIRE);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Card_flow_sends_the_16_byte_issue_expire_command_once(bool type01, bool directAnswer)
        {
            var reader = AlsoAnswerOldIssueExpire(CardScripts.NewCard(type01, directAnswer: directAnswer), type01);

            NewReader(reader).readAll();

            Assert.Equal(1, reader.CountOf("80b00167020010"));
            Assert.Equal(0, reader.CountOf(OldIssueExpire));
            Assert.Equal(directAnswer ? 0 : 1, reader.CountOf(CardScripts.GetResponse(type01, 0x10)));
            Assert.Equal(0, reader.CountOf(CardScripts.GetResponse(type01, 0x12)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void No_read_in_a_full_card_flow_reaches_past_offset_0176(bool directAnswer)
        {
            var reader = AlsoAnswerOldIssueExpire(CardScripts.NewCard(withPhoto: true, directAnswer: directAnswer));

            NewReader(reader).readAll(true);

            List<int[]> reads = CardReads(reader);
            Assert.Equal(5 + 20, reads.Count); // 5 files + 20 photo chunks
            Assert.DoesNotContain(reads, r => r[0] <= 0x0178 && r[1] >= 0x0177);
            Assert.Contains(reads, r => r[0] == 0x0167 && r[1] == 0x0176);
        }

        public static IEnumerable<object[]> IssueExpireFlowCases()
        {
            var inputs = new[]
            {
                new object[] { "25600131", "25690130", new DateTime(2017, 1, 31), new DateTime(2026, 1, 30), "20260130" }, // normal
                new object[] { "25600000", "25690000", new DateTime(2017, 1, 1), new DateTime(2026, 1, 1), "20260101" },   // 00 month and day
                new object[] { "25600015", "25690300", new DateTime(2017, 1, 15), new DateTime(2026, 3, 1), "20260301" },  // issue 00 month, expire 00 day
                new object[] { "25600131", "99999999", new DateTime(2017, 1, 31), new DateTime(9456, 12, 31), "94561231" }, // lifetime card
            };
            var modes = new[]
            {
                new object[] { false, false }, // Type02, 61 xx + GET RESPONSE
                new object[] { true, false },  // Type01, 61 xx + GET RESPONSE
                new object[] { false, true },  // direct answer
            };
            return inputs.SelectMany(input => modes.Select(mode => input.Concat(mode).ToArray()));
        }

        [Theory]
        [MemberData(nameof(IssueExpireFlowCases))]
        public void Issue_expire_values_through_the_full_card_flow(string issueBe, string expireBe,
            DateTime issue, DateTime expire, string expireString, bool type01, bool directAnswer)
        {
            var reader = CardScripts.NewCard(type01, directAnswer: directAnswer, issueBe: issueBe, expireBe: expireBe);

            Personal p = NewReader(reader).readAll();

            Assert.Equal(issue, p.Issue);
            Assert.Equal(expire, p.Expire);
            Assert.Equal(expireString, p.ExpireString);
        }

        // ---- Open without a reader name tries the readers in order (issue #14) ----

        private const string Reader0 = "Fake Reader 0";
        private const string Reader1 = "Fake Reader 1";
        private const string Reader2 = "Fake Reader 2";

        private static ThaiIDCard NewMultiReader(FakeReader reader, out FakeContext context)
        {
            ThaiIDCard card = NewReader(reader, out context);
            context.Readers = new[] { Reader0, Reader1, Reader2 };
            return card;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Open_without_name_uses_the_second_reader_when_the_first_connect_fails(string name)
        {
            var reader = CardScripts.NewCard();
            reader.ConnectErrorByReader[Reader0] = SCardError.RemovedCard;
            var card = NewMultiReader(reader, out var context);

            Assert.True(card.Open(name));

            Assert.Equal(new[] { Reader0, Reader1 }, reader.ConnectAttempts);
            Assert.Equal(Reader1, reader.ConnectedReaderName);
            Assert.Equal(1, reader.CountOf(CardScripts.SelectApplet));
            Assert.Equal(0, context.ReleaseCalls);
            Assert.Equal(0, card.ErrorCode());
            Assert.Null(card.Error());
        }

        [Fact]
        public void ReadAll_without_name_reads_through_the_second_reader()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectErrorByReader[Reader0] = SCardError.NoSmartcard;
            var card = NewMultiReader(reader, out _);

            Personal p = card.readAll();

            Assert.NotNull(p);
            Assert.Equal(CardScripts.SyntheticCitizenId, p.Citizenid);
        }

        [Fact]
        public void Open_without_name_stops_at_the_first_reader_that_connects()
        {
            var reader = CardScripts.NewCard();
            var card = NewMultiReader(reader, out _);

            Assert.True(card.Open());

            Assert.Equal(new[] { Reader0 }, reader.ConnectAttempts);
        }

        [Fact]
        public void All_readers_failing_reports_the_first_error_after_trying_each()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectErrorByReader[Reader0] = SCardError.RemovedCard;
            reader.ConnectErrorByReader[Reader1] = SCardError.NoSmartcard;
            reader.ConnectErrorByReader[Reader2] = SCardError.SharingViolation;
            var card = NewMultiReader(reader, out _);

            Assert.False(card.Open());

            Assert.Equal(new[] { Reader0, Reader1, Reader2 }, reader.ConnectAttempts);
            Assert.Equal(256, card.ErrorCode());
            Assert.Equal("Open Err: " + SCardHelper.StringifyError(SCardError.RemovedCard) + " (RemovedCard)", card.Error());
        }

        [Fact]
        public void All_readers_failing_releases_the_context()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectError = SCardError.NoSmartcard;
            var card = NewMultiReader(reader, out var context);

            Assert.False(card.Open());

            Assert.Equal(1, context.ReleaseCalls);
            Assert.False(reader.IsConnected);
        }

        [Fact]
        public void Single_reader_connect_failure_reports_the_same_error_as_before()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectError = SCardError.NoSmartcard;
            var card = NewReader(reader);

            Assert.False(card.Open());

            Assert.Equal(new[] { Reader0 }, reader.ConnectAttempts);
            Assert.Equal(256, card.ErrorCode());
            Assert.Equal("Open Err: " + SCardHelper.StringifyError(SCardError.NoSmartcard) + " (NoSmartcard)", card.Error());
        }

        [Fact]
        public void Open_with_explicit_name_never_tries_another_reader()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectErrorByReader[Reader1] = SCardError.RemovedCard;
            var card = NewMultiReader(reader, out var context);

            Assert.False(card.Open(Reader1));

            Assert.Equal(new[] { Reader1 }, reader.ConnectAttempts);
            Assert.Contains("RemovedCard", card.Error());
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void Monitor_callback_reads_only_the_reader_that_raised_it()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectErrorByReader[Reader1] = SCardError.RemovedCard;
            var card = NewMultiReader(reader, out _);
            var received = new List<Personal>();
            card.eventCardInserted += received.Add;

            RaiseInserted(card, Reader1);

            Assert.Equal(new[] { Reader1 }, reader.ConnectAttempts);
            Assert.Null(Assert.Single(received));
        }

        [Fact]
        public void Failed_applet_select_on_a_connected_reader_does_not_move_on()
        {
            var reader = new FakeReader { Atr = CardScripts.NewCard().Atr };
            reader.On(CardScripts.SelectApplet, CardScripts.Sw(0x6A, 0x82));
            var card = NewMultiReader(reader, out var context);

            Assert.False(card.Open());

            Assert.Equal(new[] { Reader0 }, reader.ConnectAttempts);
            Assert.Equal(1, card.ErrorCode());
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void Select_transmit_error_on_a_connected_reader_does_not_move_on()
        {
            var reader = CardScripts.NewCard();
            reader.OnError(CardScripts.SelectApplet, SCardError.RemovedCard);
            var card = NewMultiReader(reader, out _);

            Assert.False(card.Open());

            Assert.Equal(new[] { Reader0 }, reader.ConnectAttempts);
            Assert.Contains("RemovedCard", card.Error());
        }

        // ---- second Transmit (GET RESPONSE) error handling ----

        [Fact]
        public void Failed_get_response_throws_instead_of_returning_a_zero_filled_buffer()
        {
            var reader = CardScripts.NewCard();
            reader.OnError(CardScripts.GetResponse(false, (byte)CardScripts.Tis(CardScripts.SyntheticCitizenId).Length), SCardError.CommunicationDataLost);
            var card = NewReader(reader);

            var ex = Assert.Throws<PCSCException>(() => card.readAll());
            Assert.Equal(SCardError.CommunicationDataLost, ex.SCardError);
            Assert.False(reader.IsConnected); // the card is not left connected
        }

        [Fact]
        public void Failed_get_response_surfaces_as_error_state_on_the_monitor_path()
        {
            var reader = CardScripts.NewCard();
            reader.OnError(CardScripts.GetResponse(false, 13), SCardError.CommunicationDataLost);
            var card = NewReader(reader);
            int calls = 0;
            card.eventCardInserted += p => calls++;

            RaiseInserted(card);

            Assert.Equal(0, calls); // no half-read, corrupt Personal reaches the handler
            Assert.Equal(256, card.ErrorCode());
            Assert.Contains("_Inserted Err", card.Error());
            Assert.Contains("CommunicationDataLost", card.Error());
        }

        // ---- Open failure cleanup ----

        [Fact]
        public void Connect_failure_releases_the_context()
        {
            var reader = CardScripts.NewCard();
            reader.ConnectError = SCardError.NoSmartcard;
            var card = NewReader(reader, out var context);

            Assert.False(card.Open());

            Assert.Equal(256, card.ErrorCode());
            Assert.Contains("Open Err", card.Error());
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void No_readers_releases_the_context()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader, out var context);
            context.Readers = new string[0];

            Assert.False(card.Open());

            Assert.Equal(256, card.ErrorCode());
            Assert.Equal(0, reader.ConnectCalls);
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void Empty_atr_disconnects_and_releases()
        {
            var reader = CardScripts.NewCard();
            reader.Atr = new byte[0];
            var card = NewReader(reader, out var context);

            Assert.False(card.Open());

            Assert.False(reader.IsConnected);
            Assert.Equal(1, reader.DisconnectCalls);
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void Select_transmit_error_disconnects_and_releases()
        {
            var reader = CardScripts.NewCard();
            reader.OnError(CardScripts.SelectApplet, SCardError.RemovedCard);
            var card = NewReader(reader, out var context);

            Assert.False(card.Open());

            Assert.Equal(256, card.ErrorCode());
            Assert.Contains("RemovedCard", card.Error());
            Assert.False(reader.IsConnected);
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void Successful_open_does_not_clean_up()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader, out var context);

            Assert.True(card.Open());

            Assert.True(reader.IsConnected);
            Assert.Equal(0, reader.DisconnectCalls);
            Assert.Equal(0, context.ReleaseCalls);
        }

        // ---- GetReaders ----

        [Fact]
        public void GetReaders_returns_the_names_and_releases_the_context()
        {
            var card = NewReader(new FakeReader(), out var context);

            Assert.Equal(new[] { "Fake Reader 0" }, card.GetReaders());
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void GetReaders_without_readers_throws_and_sets_error_state()
        {
            var card = NewReader(new FakeReader(), out var context);
            context.Readers = new string[0];

            var ex = Assert.Throws<PCSCException>(() => card.GetReaders());

            Assert.Equal(SCardError.NoReadersAvailable, ex.SCardError);
            Assert.Equal(256, card.ErrorCode());
            Assert.Contains("GetReaders Err", card.Error());
            Assert.Equal(1, context.ReleaseCalls);
        }

        [Fact]
        public void GetReaders_rethrows_without_losing_the_original_stack_trace()
        {
            var card = NewReader(new FakeReader(), out var context);
            context.GetReadersFailure = new PCSCException(SCardError.ServiceStopped, "fake failure");

            var ex = Assert.Throws<PCSCException>(() => card.GetReaders());

            Assert.Same(context.GetReadersFailure, ex);
            // "throw ex;" would restart the trace at ThaiIDCard.GetReaders and drop the frame that raised it.
            Assert.Contains("FakeContext.GetReaders", ex.StackTrace);
            Assert.Equal(1, context.ReleaseCalls); // released even though GetReaders failed
        }

        // ---- card inserted monitor callback ----

        [Fact]
        public void Both_events_subscribed_read_the_card_once_and_share_the_result()
        {
            var reader = CardScripts.NewCard(withPhoto: true);
            var card = NewReader(reader);
            Personal first = null, second = null;
            card.eventCardInserted += p => first = p;
            card.eventCardInsertedWithPhoto += p => second = p;

            RaiseInserted(card);

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.NotNull(second.PhotoRaw);
            Assert.Equal(1, reader.CountOf(CardScripts.SelectApplet)); // one Open, one read
            Assert.Equal(1, reader.CountOf(CardScripts.Cid));
        }

        [Fact]
        public void Only_plain_event_subscribed_does_not_read_the_photo()
        {
            var reader = CardScripts.NewCard(withPhoto: true);
            var card = NewReader(reader);
            Personal got = null;
            card.eventCardInserted += p => got = p;

            RaiseInserted(card);

            Assert.NotNull(got);
            Assert.Null(got.PhotoRaw);
            Assert.Equal(0, reader.CountOf(CardScripts.PhotoCommands()[0]));
        }

        [Fact]
        public void Only_photo_event_subscribed_reads_the_photo_once()
        {
            var reader = CardScripts.NewCard(withPhoto: true);
            var card = NewReader(reader);
            Personal got = null;
            card.eventCardInsertedWithPhoto += p => got = p;

            RaiseInserted(card);

            Assert.NotNull(got.PhotoRaw);
            Assert.Equal(1, reader.CountOf(CardScripts.SelectApplet));
            Assert.Equal(1, reader.CountOf(CardScripts.PhotoCommands()[0]));
        }

        [Fact]
        public void No_subscribers_means_no_card_access()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader);

            RaiseInserted(card);

            Assert.Empty(reader.Sent);
            Assert.Equal(0, reader.ConnectCalls);
        }

        [Fact]
        public void Handlers_still_receive_null_when_open_fails_like_before()
        {
            // Backward compatibility: existing applications test the argument for null to report a failed read.
            var reader = CardScripts.NewCard();
            reader.ConnectError = SCardError.NoSmartcard;
            var card = NewReader(reader);
            var received = new List<Personal>();
            card.eventCardInserted += received.Add;
            card.eventCardInsertedWithPhoto += received.Add;

            RaiseInserted(card);

            Assert.Equal(2, received.Count);
            Assert.All(received, p => Assert.Null(p));
            Assert.Equal(256, card.ErrorCode()); // the reason is also available
        }

        [Fact]
        public void Handler_exception_does_not_escape_the_monitor_thread()
        {
            var reader = CardScripts.NewCard();
            var card = NewReader(reader);
            card.eventCardInserted += p => throw new InvalidOperationException("handler boom");

            RaiseInserted(card); // must not throw

            Assert.NotEqual(0, card.ErrorCode());
            Assert.Contains("_Inserted Err", card.Error());
            Assert.Contains("handler boom", card.Error());
        }

        [Fact]
        public void Malformed_card_data_does_not_escape_the_monitor_thread()
        {
            var reader = CardScripts.NewCard();
            // EF_PERSON_INFO shorter than 200 chars makes the Personal.Info setter throw.
            var broken = new FakeReader { Atr = reader.Atr };
            broken.On(CardScripts.SelectApplet, CardScripts.Sw(0x90, 0x00));
            broken.OnData(CardScripts.Cid, CardScripts.Tis(CardScripts.SyntheticCitizenId));
            broken.OnData(CardScripts.PersonInfo, CardScripts.Tis("short"));
            var card = NewReader(broken);
            card.eventCardInserted += p => { };

            RaiseInserted(card); // must not throw

            Assert.NotEqual(0, card.ErrorCode());
            Assert.Contains("_Inserted Err", card.Error());
            Assert.False(broken.IsConnected);
        }
    }
}
