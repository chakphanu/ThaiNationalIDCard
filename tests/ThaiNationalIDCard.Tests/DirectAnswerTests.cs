using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Direct-answer tests ported from release/2.x (ThaiIDCardFlowTests): the reader answers data + 90 00 on the
    /// first transmit. That reader mode is inferred from issue #25 and was not observed on hardware here.
    /// Differences from 2.x: the photo is 5118 bytes (21 chunks), and a status other than 90 00 gives empty data
    /// instead of the raw bytes.
    /// </summary>
    public class DirectAnswerTests
    {
        private static List<(int Offset, int Le)> PhotoChunks() =>
            GoldenSequence.V3Reads().Where(r => r.Offset >= SyntheticImage.Photo && r.Offset < SyntheticImage.Address).ToList();

        private static (SimulatedCard Card, ApduExchange Exchange) Open(Action<SimulatedCard>? configure = null)
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            configure?.Invoke(card);
            var guard = new GuardedCardConnection(card, CommandPolicy.Public);
            guard.Connect(card.ReaderName);
            var exchange = new ApduExchange(guard);
            Assert.True(exchange.Exchange(ApduText.Select(SimulatedCard.MoiAidHex), 0x00).IsSuccess);
            return (card, exchange);
        }

        private static byte[] ReadPhoto(ApduExchange exchange) =>
            PhotoChunks().SelectMany(c => exchange.Exchange(ApduText.Read(c.Offset, c.Le), 0x00).Data).ToArray();

        private static string ReadText(ApduExchange exchange, int offset, int le) =>
            SyntheticImage.FromTis(exchange.Exchange(ApduText.Read(offset, le), 0x00).Data);

        [Fact]
        public void Photo_read_gives_the_exact_bytes()
        {
            (SimulatedCard card, ApduExchange exchange) = Open();

            byte[] photo = ReadPhoto(exchange);

            Assert.Equal(ApduText.Slice(card.MoiImage, SyntheticImage.Photo, SyntheticImage.PhotoLength), photo);
        }

        [Fact]
        public void Photo_has_no_status_word_inserted_at_chunk_boundaries()
        {
            (SimulatedCard card, ApduExchange exchange) = Open(c =>
            {
                // Without 90 00 in the image itself, any 90 00 found at a boundary can only be a status word.
                byte[] image = c.MoiImage;
                for (int i = 0; i < SyntheticImage.PhotoLength; i++)
                {
                    image[SyntheticImage.Photo + i] = 0x41;
                }
            });

            byte[] photo = ReadPhoto(exchange);

            Assert.Equal(SyntheticImage.PhotoLength, photo.Length);
            Assert.DoesNotContain((byte)0x90, photo);
        }

        [Fact]
        public void Photo_read_transmits_once_per_chunk_without_get_response()
        {
            (SimulatedCard card, ApduExchange exchange) = Open();

            ReadPhoto(exchange);

            Assert.Equal(1 + 21, card.TransmitCount);
            foreach ((int offset, int le) in PhotoChunks())
            {
                Assert.Equal(1, card.ReceivedHex.Count(h => h == ApduText.Hex(ApduText.Read(offset, le))));
            }

            Assert.DoesNotContain(card.ReceivedHex, h => h.StartsWith("00c0", StringComparison.Ordinal));
        }

        [Fact]
        public void Text_fields_have_no_trailing_status_word()
        {
            (_, ApduExchange exchange) = Open();

            Assert.Equal(SimulatedCard.SyntheticCitizenId, ReadText(exchange, SyntheticImage.CitizenId, 13));
            Assert.Equal(SyntheticImage.IssuerText.PadRight(100), ReadText(exchange, SyntheticImage.Issuer, 100));
            Assert.Equal(SyntheticImage.IssueExpireText, ReadText(exchange, SyntheticImage.IssueExpire, 16));
            Assert.Equal(SyntheticImage.AddressText.PadRight(160), ReadText(exchange, SyntheticImage.Address, 160));
        }

        [Fact]
        public void Only_the_status_word_is_stripped_not_90_00_inside_the_data()
        {
            byte[] data = { 0x31, 0x90, 0x00, 0x32, 0x90, 0x00 };
            (_, ApduExchange exchange) = Open(c => Array.Copy(data, 0, c.MoiImage, SyntheticImage.Photo, data.Length));

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.Photo, data.Length), 0x00);

            Assert.Equal(data, response.Data);
        }

        [Fact]
        public void Accepts_256_data_bytes_plus_the_status_word()
        {
            (SimulatedCard card, ApduExchange exchange) = Open();

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.Photo, 0), 0x00);

            Assert.True(response.IsSuccess);
            Assert.Equal(ApduText.Slice(card.MoiImage, SyntheticImage.Photo, 256), response.Data);
        }

        [Theory]
        [InlineData(new byte[] { 0x6A, 0x82 })]             // error status alone
        [InlineData(new byte[] { 0x31, 0x32, 0x62, 0x83 })] // data + warning status
        public void Other_status_words_are_not_turned_into_text(byte[] answer)
        {
            (_, ApduExchange exchange) = Open(c => c.AnswerOverride = (position, command) => position == 2 ? answer : null);

            ApduResponse response = exchange.Exchange(ApduText.Read(SyntheticImage.CitizenId, 13), 0x00);

            Assert.Empty(response.Data);
            Assert.False(response.IsSuccess);
        }
    }
}
