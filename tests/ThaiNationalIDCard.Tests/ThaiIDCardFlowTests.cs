using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PCSC;
using PCSC.Exceptions;
using PCSC.Monitoring;
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
