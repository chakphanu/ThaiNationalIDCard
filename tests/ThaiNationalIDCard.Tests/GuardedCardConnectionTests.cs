using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class GuardedCardConnectionTests
    {
        private static (SimulatedCard Card, GuardedCardConnection Guard) Open(CommandPolicy? policy = null, SimulatedCard? card = null)
        {
            card ??= new SimulatedCard();
            var guard = new GuardedCardConnection(card, policy ?? CommandPolicy.Public);
            guard.Connect(card.ReaderName);
            return (card, guard);
        }

        private static void SelectMoi(GuardedCardConnection guard) => guard.Transmit(ApduText.Select(SimulatedCard.MoiAidHex));

        [Fact]
        public void Guard_is_sealed_and_never_exposes_the_raw_channel()
        {
            Type type = typeof(GuardedCardConnection);
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            Assert.True(type.IsSealed);
            Assert.DoesNotContain(type.GetProperties(All), p => typeof(IRawCardChannel).IsAssignableFrom(p.PropertyType));
            Assert.DoesNotContain(type.GetMethods(All), m => typeof(IRawCardChannel).IsAssignableFrom(m.ReturnType));
            Assert.DoesNotContain(type.GetFields(All), f => typeof(IRawCardChannel).IsAssignableFrom(f.FieldType) && !f.IsPrivate);
        }

        [Fact]
        public void Exchange_can_only_be_built_on_the_guard()
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            ConstructorInfo ctor = Assert.Single(typeof(ApduExchange).GetConstructors(All));
            ParameterInfo parameter = Assert.Single(ctor.GetParameters());
            Assert.Equal(typeof(GuardedCardConnection), parameter.ParameterType);
            Assert.DoesNotContain(typeof(ApduExchange).GetFields(All), f => typeof(IRawCardChannel).IsAssignableFrom(f.FieldType));
        }

        [Theory]
        [InlineData("802001002000000000")]
        [InlineData("80b400000000")]
        [InlineData("b03400760000")]
        [InlineData("00d6000004aabbccdd")]
        [InlineData("80b0015a02000d")] // off limits range
        [InlineData("80b01627020010")] // off limits range
        [InlineData("80b00167020012")] // the old 18 byte read
        [InlineData("80ca9f7f2d")]     // chip data, not in the policy yet
        public void Forbidden_commands_never_reach_the_channel(string hex)
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();
            SelectMoi(guard);
            int before = card.TransmitCount;

            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Bytes(hex)));

            Assert.Equal(before, card.TransmitCount);
        }

        [Fact]
        public void Property_random_commands_only_reach_the_channel_when_the_policy_allows_them()
        {
            // Fixed seed: a failure reproduces. Commands use the known headers often, so both branches get exercised.
            var random = new Random(20261102);
            byte[][] headers =
            {
                new byte[] { 0x80, 0xB0 }, new byte[] { 0x00, 0xC0 }, new byte[] { 0x00, 0xA4 }, new byte[] { 0x80, 0x20 },
                new byte[] { 0x80, 0xB4 }, new byte[] { 0xB0, 0x34 }, new byte[] { 0x00, 0xD6 }, new byte[] { 0x80, 0xCA },
                new byte[] { 0x80, 0x00 },
            };

            foreach (CommandPolicy policy in new[] { CommandPolicy.Public, CommandPolicy.PublicAndPrivate })
            {
                (SimulatedCard card, GuardedCardConnection guard) = Open(policy);
                SelectMoi(guard);
                int allowed = 0;
                int rejected = 0;

                for (int i = 0; i < 20000; i++)
                {
                    byte[] command;
                    if (random.Next(10) == 0)
                    {
                        command = new byte[random.Next(0, 16)];
                        random.NextBytes(command);
                    }
                    else
                    {
                        byte[] head = headers[random.Next(headers.Length)];
                        command = new byte[] { head[0], head[1], (byte)random.Next(0x18), (byte)random.Next(256), 0x02, 0x00, (byte)random.Next(256) };
                    }

                    int before = card.TransmitCount;
                    try
                    {
                        guard.Transmit(command);
                        Assert.Equal(before + 1, card.TransmitCount);
                        allowed++;
                    }
                    catch (CommandRejectedException)
                    {
                        Assert.Equal(before, card.TransmitCount);
                        rejected++;
                    }
                }

                Assert.True(allowed > 0, "No command was allowed, so the property proves nothing.");
                Assert.True(rejected > 0, "No command was rejected, so the property proves nothing.");
                Assert.DoesNotContain(card.Log, e => e.Command.Length >= 2 && e.Command[0] == 0x80 && e.Command[1] == 0x20);
                Assert.DoesNotContain(card.Log, e => e.Command.Length >= 2 && e.Command[0] == 0x80 && e.Command[1] == 0xB4);
                Assert.DoesNotContain(card.Log, e => e.Command.Length >= 2 && e.Command[0] == 0xB0 && e.Command[1] == 0x34);
                Assert.DoesNotContain(SyntheticImage.Never015A, card.AnsweredOffsets());
                Assert.DoesNotContain(SyntheticImage.Never1627, card.AnsweredOffsets());
            }
        }

        [Fact]
        public void Read_before_select_is_rejected()
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();

            var ex = Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));

            Assert.Equal(CommandVerdict.NoAppletSelected, ex.Verdict);
            Assert.Equal(0, card.TransmitCount);
        }

        [Fact]
        public void Failed_select_clears_the_selection()
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open(CommandPolicy.PublicAndPrivate);
            SelectMoi(guard);

            // NHSO is absent from this card, so SELECT answers 6A 82.
            guard.Transmit(ApduText.Select(SimulatedCard.NhsoAidHex));

            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
            SelectMoi(guard);
            guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));
            Assert.Equal(3, card.ReceivedHex.Count(h => h.StartsWith("00a4", StringComparison.Ordinal)));
        }

        [Fact]
        public void Selecting_nhso_switches_the_ranges_to_that_applet()
        {
            var image = Enumerable.Repeat((byte)0x41, 0x200).ToArray();
            var card = new SimulatedCard();
            card.AddApplet(SimulatedCard.NhsoAidHex, image);
            (_, GuardedCardConnection guard) = Open(CommandPolicy.PublicAndPrivate, card);

            SelectMoi(guard);
            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(0x015D, 1)));

            guard.Transmit(ApduText.Select(SimulatedCard.NhsoAidHex));
            guard.Transmit(ApduText.Read(0x015D, 1));

            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.Photo, 0xFF)));
        }

        [Fact]
        public void Get_response_is_only_allowed_right_after_61_xx()
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();
            SelectMoi(guard);
            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.GetResponse(0, 13)));

            guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));
            guard.Transmit(ApduText.GetResponse(0, 13));

            int before = card.TransmitCount;
            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.GetResponse(0, 13)));
            Assert.Equal(before, card.TransmitCount);
        }

        [Theory]
        [InlineData((int)CardFailureKind.CardRemoved)]
        [InlineData((int)CardFailureKind.CardReset)]
        [InlineData((int)CardFailureKind.SharingViolation)]
        [InlineData((int)CardFailureKind.Timeout)]
        public void A_transport_failure_forgets_the_selection(int failure)
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();
            SelectMoi(guard);
            card.FailTransmitAt(2, (CardFailureKind)failure);

            Assert.ThrowsAny<ThaiIdCardException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));

            var ex = Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
            Assert.Equal(CommandVerdict.NoAppletSelected, ex.Verdict);
        }

        [Fact]
        public void Transaction_boundaries_forget_the_selection_because_another_application_may_select()
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();
            SelectMoi(guard);
            guard.BeginTransaction();
            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));

            SelectMoi(guard);
            guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13));
            guard.EndTransaction();
            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));

            Assert.Equal(1, card.BeginTransactionCalls);
            Assert.Equal(1, card.EndTransactionCalls);
        }

        [Fact]
        public void Connect_and_disconnect_forget_the_selection()
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();
            SelectMoi(guard);

            guard.Disconnect();
            guard.Connect(card.ReaderName);

            Assert.Throws<CommandRejectedException>(() => guard.Transmit(ApduText.Read(SyntheticImage.CitizenId, 13)));
            Assert.Equal(1, card.DisconnectCalls);
            Assert.Equal(2, card.ConnectCalls);
        }

        [Fact]
        public void Dispose_disposes_the_channel_once()
        {
            (SimulatedCard card, GuardedCardConnection guard) = Open();

            guard.Dispose();
            guard.Dispose();

            Assert.Equal(1, card.DisposeCalls);
            Assert.Throws<ObjectDisposedException>(() => guard.Transmit(ApduText.Select(SimulatedCard.MoiAidHex)));
        }

        [Fact]
        public void Connect_reports_atr_and_failures_from_the_seam()
        {
            var card = new SimulatedCard { Atr = new byte[] { 0x3B } };
            using var guard = new GuardedCardConnection(card, CommandPolicy.Public);

            Assert.Equal(new byte[] { 0x3B }, guard.Connect(card.ReaderName).Atr);

            card.ConnectFailure = CardFailureKind.SharingViolation;
            var ex = Assert.Throws<CardCommunicationException>(() => guard.Connect(card.ReaderName));
            Assert.Equal(CardFailureKind.SharingViolation, ex.Failure);
        }

        [Fact]
        public void Begin_transaction_failure_comes_through_as_a_library_exception()
        {
            var card = new SimulatedCard { BeginTransactionFailure = CardFailureKind.SharingViolation };
            (_, GuardedCardConnection guard) = Open(card: card);

            var ex = Assert.Throws<CardCommunicationException>(() => guard.BeginTransaction());
            Assert.Equal(CardFailureKind.SharingViolation, ex.Failure);
        }

        // ---- reader selection ----

        private static SimulatedCard ThreeReaders(params ReaderCardState[] states)
        {
            var card = new SimulatedCard();
            card.Statuses.Clear();
            for (int i = 0; i < states.Length; i++)
            {
                card.Statuses.Add(new ReaderStatus("Synthetic Reader " + i, states[i]));
            }

            return card;
        }

        [Fact]
        public void Select_reader_takes_the_first_reader_holding_a_card()
        {
            SimulatedCard card = ThreeReaders(ReaderCardState.Empty, ReaderCardState.Present, ReaderCardState.Present);
            using var guard = new GuardedCardConnection(card, CommandPolicy.Public);

            Assert.Equal("Synthetic Reader 1", guard.SelectReader(null));
            Assert.Equal("Synthetic Reader 1", guard.SelectReader(string.Empty));
        }

        [Fact]
        public void Select_reader_with_a_name_never_moves_to_another_reader()
        {
            SimulatedCard card = ThreeReaders(ReaderCardState.Empty, ReaderCardState.Present);
            using var guard = new GuardedCardConnection(card, CommandPolicy.Public);

            Assert.Equal("Synthetic Reader 1", guard.SelectReader("Synthetic Reader 1"));
            Assert.Throws<CardNotPresentException>(() => guard.SelectReader("Synthetic Reader 0"));
            Assert.Throws<ReaderNotFoundException>(() => guard.SelectReader("Other Reader"));
        }

        [Fact]
        public void Select_reader_without_readers_or_cards_reports_which()
        {
            using var none = new GuardedCardConnection(ThreeReaders(), CommandPolicy.Public);
            Assert.Throws<ReaderNotFoundException>(() => none.SelectReader(null));

            using var empty = new GuardedCardConnection(ThreeReaders(ReaderCardState.Empty, ReaderCardState.Mute, ReaderCardState.Unavailable), CommandPolicy.Public);
            Assert.Throws<CardNotPresentException>(() => empty.SelectReader(null));
        }

        [Fact]
        public void List_readers_passes_through()
        {
            using var guard = new GuardedCardConnection(ThreeReaders(ReaderCardState.Empty, ReaderCardState.Empty), CommandPolicy.Public);

            Assert.Equal(new[] { "Synthetic Reader 0", "Synthetic Reader 1" }, guard.ListReaders());
        }

        [Fact]
        public void Monitor_seam_carries_reader_name_atr_and_failure()
        {
            var card = new SimulatedCard();
            using IReaderMonitor monitor = card.CreateMonitor();
            var inserted = new List<ReaderCardEventArgs>();
            var failed = new List<CardFailureKind>();
            monitor.CardInserted += (s, e) => inserted.Add(e);
            monitor.Failed += (s, e) => failed.Add(e.Failure);
            monitor.Start(new[] { "Synthetic Reader 0" });

            card.LastMonitor!.RaiseInserted("Synthetic Reader 0", new byte[] { 0x3B });
            card.LastMonitor.RaiseFailed(CardFailureKind.ServiceUnavailable);

            Assert.Equal("Synthetic Reader 0", Assert.Single(inserted).ReaderName);
            Assert.Equal(CardFailureKind.ServiceUnavailable, Assert.Single(failed));
        }
    }
}
