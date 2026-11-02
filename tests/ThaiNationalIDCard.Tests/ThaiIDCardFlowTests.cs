using System;
using System.Collections.Generic;
using System.Linq;
using PCSC;
using PCSC.Monitoring;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Hardware free smoke tests of the APDU flow through FakeCardTransport (synthetic data only).
    /// They characterize the current behavior, including known bugs that phase 3 fixes test-first.
    /// </summary>
    public class ThaiIDCardFlowTests
    {
        private static readonly byte[] AtrType01 = { 0x3B, 0x67, 0x00, 0x00, 0x00, 0xFF };

        private static ThaiIDCard NewCard(FakeCardTransport fake) => new ThaiIDCard(fake, 0);

        private static FakeCardTransport CitizenIdCard(bool type01 = false)
        {
            var fake = new FakeCardTransport();
            if (type01) fake.Atr = AtrType01;
            fake.ScriptSelectOk();
            fake.OnData(CardScripts.Cid, CardScripts.Tis(CardScripts.SyntheticCitizenId), type01);
            return fake;
        }

        private static FakeCardTransport FullCard()
        {
            var fake = new FakeCardTransport().ScriptSelectOk();
            fake.OnData(CardScripts.Cid, CardScripts.Tis(CardScripts.SyntheticCitizenId));
            fake.OnData(CardScripts.PersonInfo, CardScripts.Tis(CardScripts.PersonInfoText()));
            fake.OnData(CardScripts.Address, CardScripts.Tis(
                "99/9#\u0E2B\u0E21\u0E39\u0E48 1#Soi Test#Road Test##Tambol#Amphur#Province".PadRight(100)));
            fake.OnData(CardScripts.IssueExpire, CardScripts.Tis("2560010125700101  "));
            fake.OnData(CardScripts.Issuer, CardScripts.Tis("Test Issuer".PadRight(100)));
            return fake;
        }

        [Fact]
        public void ReadCitizenid_Type02_UsesGetResponseP2Zero()
        {
            var fake = CitizenIdCard();

            var personal = NewCard(fake).readCitizenid();

            Assert.NotNull(personal);
            Assert.Equal(CardScripts.SyntheticCitizenId, personal!.Citizenid);
            Assert.Equal(
                new[] { CardScripts.SelectApplet, CardScripts.Cid, CardScripts.GetResponse(false, 13) },
                fake.SentHex);
            Assert.Equal(1, fake.ConnectCalls);
            Assert.Equal(1, fake.DisconnectCalls);
        }

        [Fact]
        public void ReadCitizenid_Type01Atr_UsesGetResponseP2One()
        {
            var fake = CitizenIdCard(type01: true);

            var personal = NewCard(fake).readCitizenid();

            Assert.Equal(CardScripts.SyntheticCitizenId, personal!.Citizenid);
            Assert.Contains(CardScripts.GetResponse(true, 13), fake.SentHex);
            Assert.DoesNotContain(CardScripts.GetResponse(false, 13), fake.SentHex);
        }

        [Fact]
        public void ReadCitizenid_PassesReaderNameToTransport()
        {
            var fake = CitizenIdCard();
            NewCard(fake).readCitizenid("Synthetic Reader 0");
            Assert.Equal("Synthetic Reader 0", fake.ConnectedReaderName);
        }

        [Fact]
        public void Open_EmptyAtr_ReturnsFalseWithoutSelect()
        {
            var fake = new FakeCardTransport { Atr = new byte[] { 0x3B } };
            var card = NewCard(fake);

            Assert.False(card.Open());
            Assert.Empty(fake.Sent);
        }

        [Fact]
        public void Open_SelectRejected_ReturnsFalseWithUnsupportedCardError()
        {
            var fake = new FakeCardTransport().On(CardScripts.SelectApplet, CardScripts.Sw(0x6A, 0x82));
            var card = NewCard(fake);

            Assert.False(card.Open());
            Assert.Equal(1, card.ErrorCode());
            Assert.Contains("not support", card.Error());
        }

        [Fact]
        public void Open_ConnectFails_ReturnsFalseWithScardError()
        {
            var fake = new FakeCardTransport { ConnectError = SCardError.NoSmartcard };
            var card = NewCard(fake);

            Assert.False(card.Open());
            Assert.Equal(256, card.ErrorCode());
            Assert.StartsWith("Open Err:", card.Error());
        }

        [Fact]
        public void ReadAll_ParsesSyntheticCard()
        {
            var fake = FullCard();

            var personal = NewCard(fake).readAll();

            Assert.NotNull(personal);
            Assert.Equal(CardScripts.SyntheticCitizenId, personal!.Citizenid);
            Assert.Equal("\u0E19\u0E32\u0E22", personal.Th_Prefix);
            Assert.Equal("\u0E17\u0E14\u0E2A\u0E2D\u0E1A", personal.Th_Firstname);
            Assert.Equal("Mr.", personal.En_Prefix);
            Assert.Equal("Test", personal.En_Firstname);
            Assert.Equal("Sample", personal.En_Lastname);
            Assert.Equal("1", personal.Sex);
            Assert.Equal(new System.DateTime(1997, 1, 31), personal.Birthday);
            Assert.Equal(new System.DateTime(2017, 1, 1), personal.Issue);
            Assert.Equal(new System.DateTime(2027, 1, 1), personal.Expire);
            Assert.Equal("99/9", personal.addrHouseNo);
            Assert.Equal("Province", personal.addrProvince);
            Assert.Equal("Test Issuer", personal.Issuer.Trim());
            Assert.Null(personal.PhotoRaw);
            Assert.Equal(1, fake.DisconnectCalls);
        }

        [Fact]
        public void ReadAll_Failure_ReturnsNull()
        {
            var fake = new FakeCardTransport { Atr = Array.Empty<byte>() };
            Assert.Null(NewCard(fake).readAll());
        }

        [Fact]
        public void ReadAllPhoto_CharacterizesCurrentChunkTransmitCount()
        {
            // Current (buggy) behavior: each of the 20 photo chunks is requested twice.
            // KNOWN BUG (phase 3): flip this to 20 chunk reads when the double SendCommand is fixed.
            var fake = FullCard();

            var chunkCommands = new APDU_THAILAND_IDCARD_TYPE_02().EF_CARD_PHOTO;
            var chunk = Enumerable.Range(0, 255).Select(i => (byte)i).ToArray();
            foreach (var cmd in chunkCommands)
                fake.On(FakeCardTransport.Hex(cmd), CardScripts.Sw(0x61, 0xFF));
            fake.On(CardScripts.GetResponse(false, 0xFF), CardScripts.WithOk(chunk));

            var progress = new List<(int Value, int Max)>();
            var card = NewCard(fake);
            card.eventPhotoProgress += (v, m) => progress.Add((v, m));

            var personal = card.readAllPhoto();

            Assert.NotNull(personal);
            Assert.Equal(20, progress.Count);
            Assert.Equal((20, 20), progress.Last());
            foreach (var cmd in chunkCommands)
                Assert.Equal(2, fake.CountOf(FakeCardTransport.Hex(cmd)));
            Assert.Equal(20 * 255, personal!.PhotoRaw!.Length);
        }

        [Fact]
        public void Inserted_RaisesCardInsertedWithReaderName()
        {
            // Drives the handler the SCardMonitor would call. The monitor itself needs a real PC/SC context.
            var fake = FullCard();
            var card = NewCard(fake);
            Personal? received = null;
            card.eventCardInserted += p => received = p;

            card._Inserted(new CardStatusEventArgs("Synthetic Reader 0", SCRState.Present, new byte[] { 0x3B, 0x79 }));

            Assert.NotNull(received);
            Assert.Equal(CardScripts.SyntheticCitizenId, received!.Citizenid);
            Assert.Equal("Synthetic Reader 0", fake.ConnectedReaderName);
        }
    }
}
