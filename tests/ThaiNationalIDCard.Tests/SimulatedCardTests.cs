using System.Linq;
using ThaiNationalIDCard.Hardware;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>The simulator itself, driven raw without the guard. It must behave before tests can trust it.</summary>
    public class SimulatedCardTests
    {
        private static SimulatedCard Connected(SimulatedCard? card = null)
        {
            card ??= new SimulatedCard();
            card.Connect(card.ReaderName);
            card.Transmit(ApduText.Select(SimulatedCard.MoiAidHex));
            return card;
        }

        [Fact]
        public void Image_is_5950_bytes_with_the_expected_field_contents()
        {
            byte[] image = new SimulatedCard().MoiImage;

            Assert.Equal(5950, image.Length);
            Assert.Equal(SimulatedCard.SyntheticCitizenId,
                SyntheticImage.FromTis(ApduText.Slice(image, SyntheticImage.CitizenId, 13)));
            Assert.Equal(SyntheticImage.IssueExpireText,
                SyntheticImage.FromTis(ApduText.Slice(image, SyntheticImage.IssueExpire, 16)));
            Assert.Equal(new byte[] { 0xFF, 0xD8 }, ApduText.Slice(image, SyntheticImage.Photo, 2));
            Assert.Equal(new byte[] { 0xFF, 0xD9 }, ApduText.Slice(image, SyntheticImage.Photo + 5116, 2));
            Assert.All(ApduText.Slice(image, SyntheticImage.Never1627, 5950 - SyntheticImage.Never1627),
                b => Assert.Equal(SyntheticImage.NeverReadMarker, b));
        }

        [Fact]
        public void Synthetic_citizen_id_satisfies_the_checksum_formula()
        {
            int[] d = SimulatedCard.SyntheticCitizenId.Select(c => c - '0').ToArray();
            int sum = Enumerable.Range(0, 12).Sum(i => d[i] * (13 - i));
            Assert.Equal((11 - sum % 11) % 10, d[12]);
        }

        [Fact]
        public void Get_response_mode_keeps_data_pending_until_get_response()
        {
            SimulatedCard card = Connected();

            Assert.Equal(new byte[] { 0x61, 0x0D }, card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
            Assert.Empty(card.Answered);

            byte[] response = card.Transmit(ApduText.GetResponse(0x00, 13));
            Assert.Equal(15, response.Length);
            Assert.Equal(new byte[] { 0x90, 0x00 }, response.Skip(13).ToArray());
            Assert.Equal(Enumerable.Range(SyntheticImage.CitizenId, 13), card.AnsweredOffsets().OrderBy(o => o));
        }

        [Fact]
        public void Partial_get_response_leaves_the_rest_pending()
        {
            SimulatedCard card = Connected();
            card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));

            byte[] first = card.Transmit(ApduText.GetResponse(0x00, 5));
            Assert.Equal(new byte[] { 0x61, 0x08 }, first.Skip(5).ToArray());
            Assert.Equal(5, card.AnsweredOffsets().Count);
        }

        [Fact]
        public void Another_command_discards_the_pending_response()
        {
            SimulatedCard card = Connected();
            card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));
            card.Transmit(ApduText.Select(SimulatedCard.MoiAidHex));

            Assert.Equal(new byte[] { 0x69, 0x85 }, card.Transmit(ApduText.GetResponse(0x00, 13)));
            Assert.Empty(card.Answered);
        }

        [Fact]
        public void Direct_answer_mode_returns_data_and_90_00_at_once()
        {
            SimulatedCard card = Connected(new SimulatedCard { Mode = SimReaderMode.DirectAnswer });

            byte[] response = card.Transmit(ApduText.Read(SyntheticImage.Photo, 255));

            Assert.Equal(257, response.Length);
            Assert.Equal(new byte[] { 0x90, 0x00 }, response.Skip(255).ToArray());
        }

        [Fact]
        public void Wrong_length_mode_answers_6c_until_le_is_exact()
        {
            SimulatedCard card = Connected(new SimulatedCard
            {
                Mode = SimReaderMode.WrongLength,
                ExactLength = (offset, le) => 12,
            });

            Assert.Equal(new byte[] { 0x6C, 0x0C }, card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
            Assert.Equal(14, card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 12)).Length);
        }

        [Theory]
        [InlineData(0, 0x90, 0x00)]
        [InlineData(1, 0x61, 0x0A)]
        [InlineData(2, 0x6A, 0x82)]
        public void Select_answers_as_configured(int answer, byte sw1, byte sw2)
        {
            var card = new SimulatedCard { SelectAnswer = (SimSelectAnswer)answer };
            card.Connect(card.ReaderName);

            Assert.Equal(new[] { sw1, sw2 }, card.Transmit(ApduText.Select(SimulatedCard.MoiAidHex)));
        }

        [Fact]
        public void Select_of_an_absent_applet_and_read_without_selection_fail()
        {
            var card = new SimulatedCard();
            card.Connect(card.ReaderName);

            Assert.Equal(new byte[] { 0x6A, 0x82 }, card.Transmit(ApduText.Select(SimulatedCard.NhsoAidHex)));
            Assert.Equal(new byte[] { 0x69, 0x85 }, card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
        }

        [Fact]
        public void Wrong_p2_answers_as_configured_and_can_lose_the_pending_response()
        {
            SimulatedCard card = Connected(new SimulatedCard { WrongP2LosesPending = true });
            card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));

            Assert.Equal(new byte[] { 0x6A, 0x86 }, card.Transmit(ApduText.GetResponse(0x01, 13)));
            Assert.Equal(new byte[] { 0x69, 0x85 }, card.Transmit(ApduText.GetResponse(0x00, 13)));
        }

        [Fact]
        public void Injected_failure_hits_the_given_position_and_is_logged()
        {
            SimulatedCard card = Connected();
            card.FailTransmitAt(2, CardFailureKind.CardRemoved);

            Assert.Throws<CardRemovedException>(() => card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));

            Assert.Equal(CardFailureKind.CardRemoved, card.Log.Last().Failure);
            Assert.False(card.IsConnected);
        }

        [Fact]
        public void Reset_clears_the_selection_and_the_pending_response()
        {
            SimulatedCard card = Connected();
            card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));
            card.FailTransmitAt(3, CardFailureKind.CardReset);

            var ex = Assert.Throws<CardCommunicationException>(() => card.Transmit(ApduText.GetResponse(0x00, 13)));

            Assert.Equal(CardFailureKind.CardReset, ex.Failure);
            Assert.Equal(new byte[] { 0x69, 0x85 }, card.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
        }

        [Fact]
        public void Applets_have_separate_memory()
        {
            var card = new SimulatedCard();
            card.AddApplet(SimulatedCard.NhsoAidHex, Enumerable.Repeat((byte)0x41, 0x200).ToArray());
            card.Connect(card.ReaderName);
            card.Transmit(ApduText.Select(SimulatedCard.NhsoAidHex));
            card.Transmit(ApduText.Read(0x0004, 4));
            card.Transmit(ApduText.GetResponse(0x00, 4));

            Assert.Equal(4, card.AnsweredOffsets(SimulatedCard.NhsoAidHex).Count);
            Assert.Empty(card.AnsweredOffsets());
        }
    }
}
