using System;
using System.Linq;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Every status and data combination ApduExchange can return for a READ, each produced by the real state machine,
    /// and what the workflow makes of it. These pin the logic to assumptions; none of the answers was seen on a card.
    /// </summary>
    public class ExchangeOutcomeTests
    {
        private static ApduExchange Open(SimulatedCard card)
        {
            var guard = new GuardedCardConnection(card, CommandPolicy.Public);
            guard.Connect(card.ReaderName);
            var exchange = new ApduExchange(guard);
            Assert.True(exchange.Exchange(ApduText.Select(SimulatedCard.MoiAidHex), 0x00).IsSuccess);
            return exchange;
        }

        private static byte[] Answer(byte sw1, byte sw2, int dataLength = 0) =>
            Enumerable.Repeat((byte)0x31, dataLength).Concat(new[] { sw1, sw2 }).ToArray();

        [Fact]
        public void Sw_61_xx_with_all_requested_bytes_is_complete_and_flagged_more_offered()
        {
            // Why this is not a failure: READ 0167 asks Le 10 while a card may hold 12 bytes there and announce 61 12.
            // The state machine asks GET RESPONSE only for the 10 bytes it wants, so the card still has 2 bytes and
            // ends with 61 02. Asking for those would read 0177-0178, which public mode must not touch. So 61 xx here
            // means "you got all you asked for and I have more", and the field is good.
            var card = new SimulatedCard { OverAnswerBytes = 2 };
            ApduExchange exchange = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.IssueExpire, 0x10), 0x00);

            Assert.Equal(new byte[] { 0x61, 0x12 }, card.Log[1].Response);
            Assert.Equal(0x6102, response.StatusWord);
            Assert.False(response.IsSuccess);
            Assert.Equal(0x10, response.Data.Length);
            Assert.Equal(ReadOutcomeKind.CompleteMoreOffered, ExchangeOutcome.OfRead(response, 0x10));
            Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
        }

        [Fact]
        public void Direct_answer_with_all_bytes_and_61_xx_on_the_first_transmit_is_complete_too()
        {
            var card = new SimulatedCard
            {
                AnswerOverride = (position, command) => position == 2 ? Answer(0x61, 0x02, 0x10) : null,
            };
            ApduExchange exchange = Open(card);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.IssueExpire, 0x10), 0x00);

            Assert.Equal(2, card.TransmitCount);
            Assert.Equal(ReadOutcomeKind.CompleteMoreOffered, ExchangeOutcome.OfRead(response, 0x10));
        }

        [Fact]
        public void Sw_9000_with_all_bytes_is_complete()
        {
            ApduResponse response = Open(new SimulatedCard()).Exchange(ApduText.Read(0x0004, 13), 0x00);

            Assert.Equal(ReadOutcomeKind.Complete, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void Sw_9000_after_excess_was_dropped_is_complete_because_90_00_says_nothing_is_left()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer, OverAnswerBytes = 2 };

            ApduResponse response = Open(card).Exchange(ApduText.Read(SyntheticImage.IssueExpire, 0x10), 0x00);

            Assert.True(response.Flags.HasFlag(ExchangeNotes.ExcessDropped));
            Assert.Equal(ReadOutcomeKind.Complete, ExchangeOutcome.OfRead(response, 0x10));
        }

        [Fact]
        public void Sw_9000_after_6c_larger_is_complete()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => 0x20 };

            ApduResponse response = Open(card).Exchange(ApduText.Read(SyntheticImage.Photo, 0x10), 0x00);

            Assert.True(response.Flags.HasFlag(ExchangeNotes.ResentAfterWrongLength));
            Assert.Equal(ReadOutcomeKind.Complete, ExchangeOutcome.OfRead(response, 0x10));
        }

        [Fact]
        public void Sw_9000_with_fewer_bytes_is_truncated_also_after_6c_smaller()
        {
            var plain = new SimulatedCard { AnswerOverride = (position, command) => position == 2 ? Answer(0x90, 0x00, 5) : null };
            var viaResend = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => 12 };

            Assert.Equal(ReadOutcomeKind.Truncated, ExchangeOutcome.OfRead(Open(plain).Exchange(ApduText.Read(0x0004, 13), 0x00), 13));
            Assert.Equal(ReadOutcomeKind.Truncated, ExchangeOutcome.OfRead(Open(viaResend).Exchange(ApduText.Read(0x0004, 13), 0x00), 13));
        }

        [Fact]
        public void Sw_9000_with_no_bytes_is_a_failure_because_there_is_nothing_to_keep()
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => position == 2 ? Answer(0x90, 0x00) : null };

            ApduResponse response = Open(card).Exchange(ApduText.Read(0x0004, 13), 0x00);

            Assert.True(response.IsSuccess);
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void Round_limit_keeps_61_xx_but_has_no_data_so_it_is_a_failure()
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => position == 1 ? null : Answer(0x61, 0x01) };

            ApduResponse response = Open(card).Exchange(ApduText.Read(0x0004, 13), 0x00);

            Assert.Equal(0x6101, response.StatusWord);
            Assert.True(response.Flags.HasFlag(ExchangeNotes.RoundLimitReached));
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void Refused_6c_follow_up_is_a_failure()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => 0x12 };

            ApduResponse response = Open(card).Exchange(ApduText.Read(SyntheticImage.IssueExpire, 0x10), 0x00);

            Assert.Equal(0x6C12, response.StatusWord);
            Assert.True(response.Flags.HasFlag(ExchangeNotes.FollowUpRejected));
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 0x10));
        }

        [Fact]
        public void Refused_get_response_keeps_61_xx_with_no_data_so_it_is_a_failure()
        {
            var card = new SimulatedCard();

            // P2 02 is off the allowlist, so the guard refuses the follow-up the card asked for.
            ApduResponse response = Open(card).Exchange(ApduText.Read(0x0004, 13), 0x02);

            Assert.Equal(0x610D, response.StatusWord);
            Assert.True(response.Flags.HasFlag(ExchangeNotes.FollowUpRejected));
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void Second_6c_is_a_failure()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => le - 1 };

            ApduResponse response = Open(card).Exchange(ApduText.Read(0x0004, 13), 0x00);

            Assert.True(response.Flags.HasFlag(ExchangeNotes.WrongLengthRepeated));
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Theory]
        [InlineData(0x6A, 0x82)]
        [InlineData(0x62, 0x82)]
        [InlineData(0x63, 0x00)]
        [InlineData(0x90, 0x01)]
        public void Any_other_status_word_is_a_failure_even_with_bytes_on_the_wire(byte sw1, byte sw2)
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => position == 2 ? Answer(sw1, sw2, 13) : null };

            ApduResponse response = Open(card).Exchange(ApduText.Read(0x0004, 13), 0x00);

            Assert.Empty(response.Data);
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void Partial_chain_that_ends_badly_is_a_failure()
        {
            var card = new SimulatedCard
            {
                ChainSize = 5,
                AnswerOverride = (position, command) => position == 4 ? Answer(0x6F, 0x00) : null,
            };

            ApduResponse response = Open(card).Exchange(ApduText.Read(0x0004, 13), 0x00);

            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void A_61_xx_answer_shorter_than_requested_cannot_be_called_complete()
        {
            // The state machine does not produce this today. The rule stays strict in case that ever changes.
            var response = new ApduResponse(new byte[5], 0x61, 0x02);

            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(response, 13));
        }

        [Fact]
        public void Flags_for_a_stopped_exchange_outrank_full_length_data()
        {
            var limited = new ApduResponse(new byte[13], 0x61, 0x02, ExchangeNotes.RoundLimitReached);
            var refused = new ApduResponse(new byte[13], 0x61, 0x02, ExchangeNotes.FollowUpRejected);

            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(limited, 13));
            Assert.Equal(ReadOutcomeKind.Failed, ExchangeOutcome.OfRead(refused, 13));
        }

        // ---- drain switch ----

        [Fact]
        public void Select_61_xx_with_drain_off_sends_no_get_response_and_reports_the_select_status()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };
            var guard = new GuardedCardConnection(card, CommandPolicy.Public);
            guard.Connect(card.ReaderName);
            var exchange = new ApduExchange(guard);

            ApduResponse response = exchange.Exchange(MoiCardCommands.Select(MoiProfile.Public), 0x00, drainSelectResponse: false);

            Assert.Equal(1, card.TransmitCount);
            Assert.Equal(0x610A, response.StatusWord);
            Assert.Empty(response.Data);
        }
    }
}
