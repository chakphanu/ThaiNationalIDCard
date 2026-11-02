using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>SW1 state machine against the simulator, always through the real guard.</summary>
    public class ApduExchangeTests
    {
        private static readonly byte[] Cid = ApduText.Read(SyntheticImage.CitizenId, 13);
        private static readonly byte[] SelectMoi = ApduText.Select(SimulatedCard.MoiAidHex);

        private static (ApduExchange Exchange, GuardedCardConnection Guard) Open(SimulatedCard card, CommandPolicy? policy = null, bool select = true)
        {
            var guard = new GuardedCardConnection(card, policy ?? CommandPolicy.Public);
            guard.Connect(card.ReaderName);
            var exchange = new ApduExchange(guard);
            if (select)
            {
                exchange.Exchange(SelectMoi, 0x00);
            }

            return (exchange, guard);
        }

        private static byte[] Image(SimulatedCard card, int offset, int length) => ApduText.Slice(card.MoiImage, offset, length);

        /// <summary>Plays the logged traffic against a fresh guard: every command that was sent must pass it.</summary>
        internal static void AssertEveryLoggedCommandPassesTheGuard(SimulatedCard card, CommandPolicy policy)
        {
            using var replay = new GuardedCardConnection(new ReplayChannel(card.Log), policy);
            foreach (SimExchange entry in card.Log)
            {
                try
                {
                    replay.Transmit(entry.Command);
                }
                catch (ThaiIdCardException ex) when (ex is not CommandRejectedException)
                {
                    Assert.Equal(entry.Failure, ex.Failure);
                }
            }
        }

        // ---- SELECT ----

        [Fact]
        public void Select_9000_finishes_without_get_response()
        {
            var card = new SimulatedCard();
            (ApduExchange exchange, _) = Open(card, select: false);

            ApduResponse response = exchange.Exchange(SelectMoi, 0x00);

            Assert.Equal(0x9000, response.StatusWord);
            Assert.Empty(response.Data);
            Assert.Equal(1, card.TransmitCount);
        }

        [Fact]
        public void Select_610a_sends_get_response_discards_the_bytes_and_reports_61_0a()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };
            (ApduExchange exchange, _) = Open(card, select: false);

            ApduResponse response = exchange.Exchange(SelectMoi, 0x00);

            Assert.Equal(new[] { ApduText.Hex(SelectMoi), "00c000000a" }, card.ReceivedHex);
            Assert.Equal(0x610A, response.StatusWord);
            Assert.Empty(response.Data);
            Assert.Equal(new[] { 0, 10 }, exchange.Trace.Select(t => t.ReceivedLength));
            Assert.Empty(card.Answered);
        }

        [Fact]
        public void Select_610a_stays_usable_when_the_drain_gets_a_bad_status()
        {
            // The drain result does not decide anything: only the SELECT status does.
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A, GetResponseP2 = 0x01 };
            (ApduExchange exchange, _) = Open(card, select: false);

            ApduResponse response = exchange.Exchange(SelectMoi, 0x00);

            Assert.Equal(0x610A, response.StatusWord);
            Assert.Equal(0x6A86, exchange.Trace.Last().StatusWord);
            exchange.Exchange(Cid, 0x01); // no CommandRejectedException: the applet counts as selected
            Assert.Contains(ApduText.Hex(Cid), card.ReceivedHex);
        }

        [Fact]
        public void Select_6a82_returns_the_status_and_no_read_can_follow()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.NotFound6A82 };
            (ApduExchange exchange, _) = Open(card, select: false);

            ApduResponse response = exchange.Exchange(SelectMoi, 0x00);

            Assert.Equal(0x6A82, response.StatusWord);
            Assert.Equal(1, card.TransmitCount);
            var ex = Assert.Throws<CommandRejectedException>(() => exchange.Exchange(Cid, 0x00));
            Assert.Equal(CommandVerdict.NoAppletSelected, ex.Verdict);
            Assert.Equal(1, card.TransmitCount);
        }

        [Fact]
        public void Select_6c_xx_is_not_resent_because_select_has_no_le()
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => new byte[] { 0x6C, 0x0A } };
            (ApduExchange exchange, _) = Open(card, select: false);

            ApduResponse response = exchange.Exchange(SelectMoi, 0x00);

            Assert.Equal(0x6C0A, response.StatusWord);
            Assert.Equal(1, card.TransmitCount);
        }

        // ---- 61 xx ----

        [Fact]
        public void Read_61_xx_sends_get_response_with_sw2_and_strips_the_status_word()
        {
            var card = new SimulatedCard();
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(new[] { "80b0000402000d", "00c000000d" }, card.ReceivedHex.Skip(1));
            Assert.Equal(0x9000, response.StatusWord);
            Assert.Equal(Image(card, SyntheticImage.CitizenId, 13), response.Data);
            Assert.Equal(ExchangeFlags.None, response.Flags);
        }

        [Fact]
        public void Get_response_uses_the_p2_it_is_given()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x01);

            Assert.Equal("00c000010d", card.ReceivedHex.Last());
            Assert.True(response.IsSuccess);
        }

        [Fact]
        public void Sw_61_00_means_256_bytes_and_get_response_le_00()
        {
            var card = new SimulatedCard();
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.Photo, 0), 0x00);

            Assert.Equal(new[] { "80b0017b020000", "00c0000000" }, card.ReceivedHex.Skip(1));
            Assert.Equal(256, response.Data.Length);
            Assert.Equal(Image(card, SyntheticImage.Photo, 256), response.Data);
            Assert.True(response.IsSuccess);
        }

        [Fact]
        public void Chained_61_xx_collects_every_block_in_order()
        {
            var card = new SimulatedCard { ChainSize = 0x40 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.PersonInfo, 0xD1), 0x00);

            Assert.Equal(Image(card, SyntheticImage.PersonInfo, 0xD1), response.Data);
            Assert.True(response.IsSuccess);
            Assert.Equal(4, card.ReceivedHex.Count(h => h.StartsWith("00c0", StringComparison.Ordinal)));
            Assert.Equal(new[] { 1, 2, 3, 4 }, exchange.Trace.Where(t => t.Kind == TraceCommandKind.GetResponse).Select(t => t.Round));
        }

        [Fact]
        public void Round_limit_stops_a_card_that_keeps_announcing_data_without_sending_it()
        {
            var card = new SimulatedCard
            {
                AnswerOverride = (position, command) => position == 1 ? null : new byte[] { 0x61, 0x01 },
            };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(1 + 1 + ApduExchange.MaxGetResponseRounds, card.TransmitCount);
            Assert.Equal(0x6101, response.StatusWord);
            Assert.Empty(response.Data);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.RoundLimitReached));
        }

        // ---- 6C xx ----

        [Fact]
        public void Read_6c_smaller_resends_once_fixing_the_last_byte_of_02_00_le()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => 12 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(new[] { "80b0000402000d", "80b0000402000c" }, card.ReceivedHex.Skip(1));
            Assert.True(response.IsSuccess);
            Assert.Equal(Image(card, SyntheticImage.CitizenId, 12), response.Data);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ResentAfterWrongLength));
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ShorterThanRequested));
        }

        [Fact]
        public void Read_6c_larger_inside_the_range_resends_and_keeps_only_what_was_asked()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => 0x20 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.Photo, 0x10), 0x00);

            Assert.Equal(new[] { "80b0017b020010", "80b0017b020020" }, card.ReceivedHex.Skip(1));
            Assert.Equal(Image(card, SyntheticImage.Photo, 0x10), response.Data);
            Assert.True(response.IsSuccess);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ExcessDropped));
        }

        [Fact]
        public void Read_6c_00_means_256_and_is_sent_as_le_00()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => 256 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.Photo, 0xFF), 0x00);

            Assert.Equal(new[] { "80b0017b0200ff", "80b0017b020000" }, card.ReceivedHex.Skip(1));
            Assert.Equal(Image(card, SyntheticImage.Photo, 0xFF), response.Data);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ExcessDropped));
        }

        [Theory]
        [InlineData(SyntheticImage.IssueExpire, 0x10, 0x12)] // would reach 0177-0178
        [InlineData(0x00F6, 0x64, 0x65)]                     // would reach 015A
        [InlineData(0x1579, 0xA0, 0xA1)]                     // would reach 1619
        [InlineData(0x0004, 0x0D, 0x0E)]                     // would cross into the next declared range
        public void Read_6c_larger_past_the_declared_range_is_not_sent_and_returns_the_status(int offset, int le, int exact)
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (o, l) => exact };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(offset, le), 0x00);

            Assert.Equal(new[] { ApduText.Hex(ApduText.Read(offset, le)) }, card.ReceivedHex.Skip(1));
            Assert.Equal(0x6C00 | exact, response.StatusWord);
            Assert.Empty(response.Data);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.FollowUpRejected));
            Assert.Empty(card.Answered);
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, exchange.Trace.Last().Verdict);
        }

        [Fact]
        public void Read_second_6c_is_not_followed_by_a_third_transmit()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => le - 1 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(new[] { "80b0000402000d", "80b0000402000c" }, card.ReceivedHex.Skip(1));
            Assert.Equal(0x6C0B, response.StatusWord);
            Assert.Empty(response.Data);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.WrongLengthRepeated));
        }

        [Fact]
        public void Get_response_6c_is_resent_once_with_the_length_fixed_in_p3()
        {
            var card = new SimulatedCard
            {
                AnswerOverride = (position, command) => position == 3 ? new byte[] { 0x6C, 0x0D } : null,
            };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.CitizenId, 13), 0x00);

            Assert.Equal(new[] { "80b0000402000d", "00c000000d", "00c000000d" }, card.ReceivedHex.Skip(1));
            Assert.True(response.IsSuccess);
        }

        [Fact]
        public void Get_response_6c_offering_more_stays_limited_to_the_request()
        {
            var card = new SimulatedCard
            {
                AnswerOverride = (position, command) => position == 3 ? new byte[] { 0x6C, 0xFF } : null,
            };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.CitizenId, 13), 0x00);

            Assert.Equal(new[] { "80b0000402000d", "00c000000d", "00c000000d" }, card.ReceivedHex.Skip(1));
            Assert.True(response.IsSuccess);
            Assert.Equal(13, response.Data.Length);
        }

        // ---- other status words ----

        [Theory]
        [InlineData(0x6A, 0x82)]
        [InlineData(0x6B, 0x00)]
        [InlineData(0x6D, 0x00)]
        [InlineData(0x6F, 0x00)]
        [InlineData(0x67, 0x00)]
        [InlineData(0x69, 0x85)]
        [InlineData(0x62, 0x83)]
        public void Other_status_words_return_with_no_data_and_no_follow_up(byte sw1, byte sw2)
        {
            var card = new SimulatedCard
            {
                AnswerOverride = (position, command) => position == 2 ? new byte[] { 0x31, 0x32, sw1, sw2 } : null,
            };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(sw1, response.Sw1);
            Assert.Equal(sw2, response.Sw2);
            Assert.Empty(response.Data);
            Assert.False(response.IsSuccess);
            Assert.Equal(2, card.TransmitCount);
            Assert.Equal(2, exchange.Trace.Last().ReceivedLength);
        }

        [Fact]
        public void Bad_status_in_the_middle_of_a_chain_returns_no_partial_data()
        {
            var card = new SimulatedCard
            {
                ChainSize = 5,
                AnswerOverride = (position, command) => position == 4 ? new byte[] { 0x6F, 0x00 } : null,
            };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(0x6F00, response.StatusWord);
            Assert.Empty(response.Data);
        }

        [Fact]
        public void Status_word_alone_gives_empty_data_and_a_short_flag()
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => position == 2 ? new byte[] { 0x90, 0x00 } : null };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.True(response.IsSuccess);
            Assert.Empty(response.Data);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ShorterThanRequested));
        }

        [Fact]
        public void One_byte_response_is_a_communication_failure_with_a_trace_entry()
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => position == 2 ? new byte[] { 0x90 } : null };
            (ApduExchange exchange, _) = Open(card);

            var ex = Assert.Throws<CardCommunicationException>(() => exchange.Exchange(Cid, 0x00));

            Assert.Equal(CardFailureKind.MalformedResponse, ex.Failure);
            Assert.Equal(CardFailureKind.MalformedResponse, exchange.Trace.Last().Failure);
        }

        // ---- excess data ----

        [Fact]
        public void Direct_answer_with_extra_bytes_keeps_what_was_asked_and_flags_it()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer, OverAnswerBytes = 2 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.IssueExpire, 0x10), 0x00);

            Assert.Equal(Image(card, SyntheticImage.IssueExpire, 0x10), response.Data);
            Assert.True(response.IsSuccess);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ExcessDropped));
            Assert.Equal(0x12, exchange.Trace.Last().ReceivedLength);
        }

        [Fact]
        public void Card_announcing_more_than_asked_is_clamped_so_0177_never_leaves_the_card()
        {
            var card = new SimulatedCard { OverAnswerBytes = 2 };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.IssueExpire, 0x10), 0x00);

            Assert.Equal(new[] { "80b00167020010", "00c0000010" }, card.ReceivedHex.Skip(1));
            Assert.Equal(Image(card, SyntheticImage.IssueExpire, 0x10), response.Data);
            Assert.Equal(0x6102, response.StatusWord);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.ExcessDropped));
            Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
        }

        [Fact]
        public void Sw_61_00_on_a_shorter_read_clamps_get_response_to_the_request()
        {
            var card = new SimulatedCard
            {
                AnswerOverride = (position, command) => position == 2 ? new byte[] { 0x61, 0x00 } : null,
            };
            (ApduExchange exchange, _) = Open(card);

            exchange.Exchange(Cid, 0x00);

            Assert.Equal("00c000000d", ApduText.Hex(card.Log[2].Command));
        }

        // ---- wrong P2: this layer returns what the card said, the CID gate decides in step 5 ----

        [Theory]
        [InlineData(0x6A, 0x86)]
        [InlineData(0x6B, 0x00)]
        [InlineData(0x6D, 0x00)]
        [InlineData(0x6F, 0x00)]
        [InlineData(0x67, 0x00)]
        public void Wrong_p2_status_comes_back_without_data(byte sw1, byte sw2)
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01, WrongP2Response = new[] { sw1, sw2 } };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(sw1, response.Sw1);
            Assert.Equal(sw2, response.Sw2);
            Assert.Empty(response.Data);
        }

        [Fact]
        public void Wrong_p2_garbage_with_90_00_is_returned_as_data_for_the_cid_gate_to_judge()
        {
            byte[] garbage = Enumerable.Repeat((byte)0x3F, 13).Concat(new byte[] { 0x90, 0x00 }).ToArray();
            var card = new SimulatedCard { GetResponseP2 = 0x01, WrongP2Response = garbage };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.True(response.IsSuccess);
            Assert.Equal(garbage.Take(13), response.Data);
        }

        [Fact]
        public void Pending_lost_after_wrong_p2_is_not_retried_with_get_response()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01, WrongP2LosesPending = true };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.Equal(0x6A86, response.StatusWord);
            Assert.Equal(1, card.ReceivedHex.Count(h => h.StartsWith("00c0", StringComparison.Ordinal)));
        }

        // ---- transport failures ----

        public static IEnumerable<object[]> TransmitPositions() =>
            Enumerable.Range(1, 6).Select(p => new object[] { p });

        private static void RunFlow(ApduExchange exchange)
        {
            exchange.Exchange(SelectMoi, 0x00);                                   // 1 SELECT, 2 GET RESPONSE
            exchange.Exchange(Cid, 0x00);                                         // 3 READ, 4 GET RESPONSE
            exchange.Exchange(ApduText.Read(SyntheticImage.IssueExpire, 16), 0x00); // 5 READ, 6 GET RESPONSE
        }

        [Theory]
        [MemberData(nameof(TransmitPositions))]
        public void Removed_card_at_each_transmit_position_stops_at_once(int position)
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };
            card.FailTransmitAt(position, CardFailureKind.CardRemoved);
            (ApduExchange exchange, GuardedCardConnection guard) = Open(card, select: false);

            Assert.Throws<CardRemovedException>(() => RunFlow(exchange));

            Assert.Equal(position, card.TransmitCount);
            Assert.Equal(position, exchange.Trace.Count);
            Assert.Equal(CardFailureKind.CardRemoved, exchange.Trace.Last().Failure);
            Assert.Equal(TraceEntry.NoValue, exchange.Trace.Last().StatusWord);
            Assert.Throws<CommandRejectedException>(() => guard.Transmit(Cid));
            AssertEveryLoggedCommandPassesTheGuard(card, CommandPolicy.Public);
        }

        [Fact]
        public void Full_flow_without_failures_takes_exactly_six_transmits()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };
            (ApduExchange exchange, _) = Open(card, select: false);

            RunFlow(exchange);

            Assert.Equal(6, card.TransmitCount);
            AssertEveryLoggedCommandPassesTheGuard(card, CommandPolicy.Public);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void Reset_card_midway_surfaces_its_category_and_requires_a_new_select(int position)
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };
            card.FailTransmitAt(position, CardFailureKind.CardReset);
            (ApduExchange exchange, _) = Open(card, select: false);

            var ex = Assert.Throws<CardCommunicationException>(() => RunFlow(exchange));

            Assert.Equal(CardFailureKind.CardReset, ex.Failure);
            Assert.Equal(CardFailureKind.CardReset, exchange.Trace.Last().Failure);
            Assert.Equal(CommandVerdict.NoAppletSelected,
                Assert.Throws<CommandRejectedException>(() => exchange.Exchange(Cid, 0x00)).Verdict);

            // A retry that starts with SELECT works again; the retry policy itself belongs to step 5.
            Assert.Equal(0x610A, exchange.Exchange(SelectMoi, 0x00).StatusWord);
            Assert.True(exchange.Exchange(Cid, 0x00).IsSuccess);
        }

        [Fact]
        public void Sharing_violation_on_transmit_surfaces_its_category()
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(2, CardFailureKind.SharingViolation);
            (ApduExchange exchange, _) = Open(card);

            var ex = Assert.Throws<CardCommunicationException>(() => exchange.Exchange(Cid, 0x00));

            Assert.Equal(CardFailureKind.SharingViolation, ex.Failure);
            Assert.Equal(2, card.TransmitCount);
        }

        [Fact]
        public void Exchange_never_retries_by_itself_after_a_timeout()
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(2, CardFailureKind.Timeout);
            (ApduExchange exchange, _) = Open(card);

            var ex = Assert.Throws<CardCommunicationException>(() => exchange.Exchange(Cid, 0x00));

            Assert.Equal(CardFailureKind.Timeout, ex.Failure);
            Assert.Equal(2, card.TransmitCount);
        }

        // ---- guard and trace ----

        [Fact]
        public void A_forbidden_first_command_throws_and_is_traced_but_not_sent()
        {
            var card = new SimulatedCard();
            (ApduExchange exchange, _) = Open(card);
            int before = card.TransmitCount;

            var ex = Assert.Throws<CommandRejectedException>(() => exchange.Exchange(ApduText.Bytes("802001002000000000"), 0x00));

            Assert.Equal(CommandVerdict.AlwaysForbidden, ex.Verdict);
            Assert.Equal(before, card.TransmitCount);
            Assert.Equal(CommandVerdict.AlwaysForbidden, exchange.Trace.Last().Verdict);
            Assert.Equal(TraceEntry.NoValue, exchange.Trace.Last().StatusWord);
        }

        [Fact]
        public void A_bad_p2_argument_is_stopped_by_the_guard_as_a_follow_up()
        {
            var card = new SimulatedCard();
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x02);

            Assert.Equal(0x610D, response.StatusWord);
            Assert.True(response.Flags.HasFlag(ExchangeFlags.FollowUpRejected));
            Assert.DoesNotContain(card.ReceivedHex, h => h.StartsWith("00c0", StringComparison.Ordinal));
        }

        [Fact]
        public void Every_transmit_emits_one_trace_entry_with_lengths_and_status()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A, ChainSize = 5 };
            (ApduExchange exchange, _) = Open(card, select: false);

            exchange.Exchange(SelectMoi, 0x00);
            exchange.Exchange(Cid, 0x00);

            Assert.Equal(card.TransmitCount, exchange.Trace.Count);
            TraceEntry read = exchange.Trace.First(t => t.Kind == TraceCommandKind.Read);
            Assert.Equal(SyntheticImage.CitizenId, read.Offset);
            Assert.Equal(13, read.RequestedLength);
            Assert.Equal(0, read.ReceivedLength);
            Assert.Equal(0x6105, read.StatusWord);
            TraceEntry last = exchange.Trace.Last();
            Assert.Equal(TraceCommandKind.GetResponse, last.Kind);
            Assert.Equal(0, last.GetResponseP2);
            Assert.Equal(0x9000, last.StatusWord);
        }

        [Fact]
        public void Every_command_of_every_mode_passed_the_guard()
        {
            foreach (SimReaderMode mode in Enum.GetValues<SimReaderMode>())
            {
                foreach (CommandPolicy policy in new[] { CommandPolicy.Public, CommandPolicy.PublicAndPrivate })
                {
                    var card = new SimulatedCard { Mode = mode, SelectAnswer = SimSelectAnswer.Pending610A, ExactLength = (offset, le) => Math.Max(1, le - 1) };
                    (ApduExchange exchange, _) = Open(card, policy, select: false);

                    exchange.Exchange(SelectMoi, 0x00);
                    foreach ((int offset, int le) in GoldenSequence.V3Reads())
                    {
                        exchange.Exchange(ApduText.Read(offset, le), 0x00);
                    }

                    Assert.True(card.TransmitCount > 26);
                    AssertEveryLoggedCommandPassesTheGuard(card, policy);
                    Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
                    Assert.DoesNotContain(SyntheticImage.Never015A, card.AnsweredOffsets());
                    Assert.DoesNotContain(SyntheticImage.Never1627, card.AnsweredOffsets());
                }
            }
        }

        [Fact]
        public void Nothing_written_about_an_exchange_holds_card_data()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            (ApduExchange exchange, _) = Open(card);

            ApduResponse response = exchange.Exchange(Cid, 0x00);

            Assert.DoesNotContain(SimulatedCard.SyntheticCitizenId, response.ToString(), StringComparison.Ordinal);
            Assert.All(exchange.Trace, t => Assert.DoesNotContain(SimulatedCard.SyntheticCitizenId, t.ToString(), StringComparison.Ordinal));
        }
    }

    /// <summary>Answers from a recorded log, so a fresh guard can re-check what a test sent.</summary>
    internal sealed class ReplayChannel : IRawCardChannel
    {
        private readonly IReadOnlyList<SimExchange> _log;
        private int _next;

        public ReplayChannel(IReadOnlyList<SimExchange> log)
        {
            _log = log;
        }

        public byte[] Transmit(byte[] command)
        {
            SimExchange entry = _log[_next++];
            Assert.Equal(entry.Command, command);
            if (entry.Response == null)
            {
                throw CardFailures.ToException(entry.Failure);
            }

            return entry.Response;
        }

        public IReadOnlyList<string> ListReaders() => Array.Empty<string>();

        public IReadOnlyList<ReaderStatus> GetReaderStatuses() => Array.Empty<ReaderStatus>();

        public CardConnectionInfo Connect(string readerName) => new CardConnectionInfo(readerName, null, CardTransportProtocol.T0);

        public void Disconnect()
        {
        }

        public void BeginTransaction()
        {
        }

        public void EndTransaction()
        {
        }

        public IReaderMonitor CreateMonitor() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
