using System;
using System.Collections.Generic;
using System.Linq;
using PCSC;
using PCSC.Exceptions;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Hardware.Pcsc;
using Xunit;
using ReaderStatus = ThaiNationalIDCard.Hardware.ReaderStatus;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>The adapter against fake PC/SC types. No reader or card is involved in any of these tests.</summary>
    public class PcscRawCardChannelTests
    {
        private sealed class Rig
        {
            public FakePcscContext Context { get; } = new FakePcscContext();
            public FakePcscReader Reader { get; } = new FakePcscReader();
            public FakePcscMonitor Monitor { get; } = new FakePcscMonitor();
            public FakeContextFactory Factory { get; }
            public PcscRawCardChannel Channel { get; }

            public Rig()
            {
                Factory = new FakeContextFactory(Context);
                Channel = new PcscRawCardChannel(() => Factory, context => Reader, factory => Monitor);
            }

            public Rig Connected()
            {
                Channel.Connect("Fake Reader 0");
                return this;
            }
        }

        public static IEnumerable<object[]> ErrorMap()
        {
            yield return new object[] { SCardError.NoService, typeof(SmartCardServiceUnavailableException), CardFailureKind.ServiceUnavailable };
            yield return new object[] { SCardError.ServiceStopped, typeof(SmartCardServiceUnavailableException), CardFailureKind.ServiceUnavailable };
            yield return new object[] { SCardError.NoReadersAvailable, typeof(ReaderNotFoundException), CardFailureKind.ReaderNotFound };
            yield return new object[] { SCardError.UnknownReader, typeof(ReaderNotFoundException), CardFailureKind.ReaderNotFound };
            yield return new object[] { SCardError.ReaderUnavailable, typeof(ReaderNotFoundException), CardFailureKind.ReaderNotFound };
            yield return new object[] { SCardError.NoSmartcard, typeof(CardNotPresentException), CardFailureKind.CardNotPresent };
            yield return new object[] { SCardError.RemovedCard, typeof(CardRemovedException), CardFailureKind.CardRemoved };
            yield return new object[] { SCardError.ResetCard, typeof(CardCommunicationException), CardFailureKind.CardReset };
            yield return new object[] { SCardError.SharingViolation, typeof(CardCommunicationException), CardFailureKind.SharingViolation };
            yield return new object[] { SCardError.Timeout, typeof(CardCommunicationException), CardFailureKind.Timeout };
            yield return new object[] { SCardError.UnsupportedCard, typeof(UnsupportedCardException), CardFailureKind.UnsupportedCard };
            yield return new object[] { SCardError.ProtocolMismatch, typeof(UnsupportedCardException), CardFailureKind.UnsupportedCard };
            yield return new object[] { SCardError.CommunicationError, typeof(CardCommunicationException), CardFailureKind.Communication };
            yield return new object[] { SCardError.InsufficientBuffer, typeof(CardCommunicationException), CardFailureKind.Communication };
        }

        [Theory]
        [MemberData(nameof(ErrorMap))]
        public void Transmit_return_code_maps_to_the_library_exception(SCardError error, Type exceptionType, object failure)
        {
            Rig rig = new Rig().Connected();
            rig.Reader.TransmitResult = error;

            var ex = Assert.IsAssignableFrom<ThaiIdCardException>(Record.Exception(() => rig.Channel.Transmit(new byte[] { 0x00 })));

            Assert.IsType(exceptionType, ex);
            Assert.Equal((CardFailureKind)failure, ex.Failure);
        }

        [Theory]
        [MemberData(nameof(ErrorMap))]
        public void Thrown_pcsc_exception_maps_the_same_way(SCardError error, Type exceptionType, object failure)
        {
            Rig rig = new Rig().Connected();
            rig.Reader.TransmitThrows = new PCSCException(error, "fake");

            var ex = Assert.IsAssignableFrom<ThaiIdCardException>(Record.Exception(() => rig.Channel.Transmit(new byte[] { 0x00 })));

            Assert.IsType(exceptionType, ex);
            Assert.Equal((CardFailureKind)failure, ex.Failure);
        }

        [Fact]
        public void Dedicated_pcsc_exception_types_map_by_their_error_code()
        {
            Rig rig = new Rig().Connected();

            rig.Reader.TransmitThrows = new RemovedCardException(SCardError.RemovedCard);
            Assert.Throws<CardRemovedException>(() => rig.Channel.Transmit(new byte[] { 0x00 }));

            rig.Reader.TransmitThrows = new SharingViolationException(SCardError.SharingViolation);
            Assert.Equal(CardFailureKind.SharingViolation,
                Assert.Throws<CardCommunicationException>(() => rig.Channel.Transmit(new byte[] { 0x00 })).Failure);

            rig.Reader.TransmitThrows = new UserTimeoutException(SCardError.Timeout);
            Assert.Equal(CardFailureKind.Timeout,
                Assert.Throws<CardCommunicationException>(() => rig.Channel.Transmit(new byte[] { 0x00 })).Failure);

            // No dedicated type exists for ResetCard: only .SCardError tells.
            rig.Reader.TransmitThrows = new PCSCException(SCardError.ResetCard);
            Assert.Equal(CardFailureKind.CardReset,
                Assert.Throws<CardCommunicationException>(() => rig.Channel.Transmit(new byte[] { 0x00 })).Failure);
        }

        [Fact]
        public void Missing_native_library_and_no_service_map_to_service_unavailable()
        {
            foreach (Exception failure in new Exception[]
            {
                new DllNotFoundException("fake"),
                new NoServiceException(SCardError.NoService),
                new TypeInitializationException("Fake", new DllNotFoundException("fake")),
            })
            {
                var rig = new Rig();
                rig.Factory.EstablishFailure = failure;

                Assert.Throws<SmartCardServiceUnavailableException>(() => rig.Channel.ListReaders());
            }
        }

        [Fact]
        public void A_throwing_context_factory_getter_also_maps_to_service_unavailable()
        {
            var channel = new PcscRawCardChannel(() => throw new DllNotFoundException("fake"), c => new FakePcscReader(), f => new FakePcscMonitor());

            Assert.Throws<SmartCardServiceUnavailableException>(() => channel.ListReaders());
        }

        [Fact]
        public void Unrelated_exceptions_are_not_swallowed_or_renamed()
        {
            var rig = new Rig();
            rig.Factory.EstablishFailure = new ArgumentOutOfRangeException("fake");

            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Channel.ListReaders());
        }

        [Fact]
        public void Transaction_when_not_connected_maps_invalid_operation_to_not_connected()
        {
            Rig rig = new Rig().Connected();
            rig.Reader.BeginThrows = new InvalidOperationException("fake");

            Assert.Equal(CardFailureKind.NotConnected,
                Assert.Throws<CardCommunicationException>(() => rig.Channel.BeginTransaction()).Failure);
        }

        [Fact]
        public void Operations_before_connect_report_not_connected()
        {
            var rig = new Rig();

            Assert.Equal(CardFailureKind.NotConnected, Assert.Throws<CardCommunicationException>(() => rig.Channel.BeginTransaction()).Failure);
            Assert.Equal(CardFailureKind.NotConnected, Assert.Throws<CardCommunicationException>(() => rig.Channel.EndTransaction()).Failure);
            Assert.Equal(CardFailureKind.NotConnected, Assert.Throws<CardCommunicationException>(() => rig.Channel.Transmit(new byte[] { 0 })).Failure);
        }

        [Fact]
        public void No_pcsc_type_leaves_the_adapter_except_as_inner_exception()
        {
            Rig rig = new Rig().Connected();
            var original = new RemovedCardException(SCardError.RemovedCard);
            rig.Reader.TransmitThrows = original;

            var ex = Assert.Throws<CardRemovedException>(() => rig.Channel.Transmit(new byte[] { 0 }));

            Assert.Same(original, ex.InnerException);
        }

        [Fact]
        public void Connect_uses_shared_mode_with_t0_or_t1_and_reports_the_atr()
        {
            var rig = new Rig();

            CardConnectionInfo info = rig.Channel.Connect("Fake Reader 0");

            Assert.Equal("Fake Reader 0", rig.Reader.ConnectedReader);
            Assert.Equal(SCardShareMode.Shared, rig.Reader.ShareMode);
            Assert.Equal(SCardProtocol.T0 | SCardProtocol.T1, rig.Reader.PreferredProtocol);
            Assert.Equal(new byte[] { 0x3B, 0x79 }, info.Atr);
            Assert.Equal(CardTransportProtocol.T1, info.Protocol);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 0x3B })]
        public void Short_or_missing_atr_is_not_a_failure(byte[]? atr)
        {
            var rig = new Rig();
            rig.Reader.Atr = atr;

            Assert.Equal(atr ?? Array.Empty<byte>(), rig.Channel.Connect("Fake Reader 0").Atr);
        }

        [Fact]
        public void Failed_status_after_connect_is_mapped_and_disconnects()
        {
            var rig = new Rig();
            rig.Reader.StatusResult = SCardError.RemovedCard;

            Assert.Throws<CardRemovedException>(() => rig.Channel.Connect("Fake Reader 0"));

            Assert.Single(rig.Reader.Disconnects);
        }

        [Fact]
        public void Unknown_protocol_is_unsupported_and_disconnects()
        {
            var rig = new Rig();
            rig.Reader.Protocol = SCardProtocol.Unset;

            Assert.Throws<UnsupportedCardException>(() => rig.Channel.Connect("Fake Reader 0"));

            Assert.Single(rig.Reader.Disconnects);
        }

        [Fact]
        public void Transmit_uses_the_explicit_length_overload_and_returns_the_exact_length()
        {
            Rig rig = new Rig().Connected();
            rig.Reader.Answer = new byte[] { 1, 2, 3, 0x90, 0x00 };

            byte[] response = rig.Channel.Transmit(new byte[] { 0x80, 0xB0, 0x00, 0x04, 0x02, 0x00, 0x03 });

            Assert.Equal(new byte[] { 1, 2, 3, 0x90, 0x00 }, response);
            Assert.Equal(7, rig.Reader.LastSendLength);
            Assert.Equal(258, rig.Reader.LastReceiveCapacity);
        }

        [Fact]
        public void Transmit_accepts_256_data_bytes_plus_the_status_word()
        {
            Rig rig = new Rig().Connected();
            rig.Reader.Answer = new byte[258];

            Assert.Equal(258, rig.Channel.Transmit(new byte[] { 0x00 }).Length);
        }

        [Fact]
        public void Transactions_begin_and_end_with_leave()
        {
            Rig rig = new Rig().Connected();

            rig.Channel.BeginTransaction();
            rig.Channel.EndTransaction();

            Assert.Equal(1, rig.Reader.BeginCalls);
            Assert.Equal(new[] { SCardReaderDisposition.Leave }, rig.Reader.Ends);
        }

        [Fact]
        public void Transaction_error_codes_map()
        {
            Rig rig = new Rig().Connected();
            rig.Reader.BeginResult = SCardError.SharingViolation;
            rig.Reader.EndResult = SCardError.RemovedCard;

            Assert.Equal(CardFailureKind.SharingViolation,
                Assert.Throws<CardCommunicationException>(() => rig.Channel.BeginTransaction()).Failure);
            Assert.Throws<CardRemovedException>(() => rig.Channel.EndTransaction());
        }

        [Fact]
        public void List_readers_with_none_is_empty_not_an_error()
        {
            var rig = new Rig();
            rig.Context.Readers = Array.Empty<string>();
            Assert.Empty(rig.Channel.ListReaders());

            rig.Context.GetReadersFailure = new NoReadersAvailableException(SCardError.NoReadersAvailable);
            Assert.Empty(rig.Channel.ListReaders());
        }

        [Fact]
        public void Reader_statuses_come_from_get_status_change_without_blocking()
        {
            var rig = new Rig();
            rig.Context.Readers = new[] { "R0", "R1", "R2", "R3" };
            SCRState[] seenCurrent = Array.Empty<SCRState>();
            rig.Context.StatusChange = (timeout, states) =>
            {
                seenCurrent = states.Select(s => s.CurrentState).ToArray();
                states[0].EventState = SCRState.Changed | SCRState.Empty;
                states[1].EventState = SCRState.Changed | SCRState.Present | SCRState.InUse;
                states[2].EventState = SCRState.Changed | SCRState.Present | SCRState.Mute;
                states[3].EventState = SCRState.Changed | SCRState.Unavailable;
                return SCardError.Success;
            };

            IReadOnlyList<ReaderStatus> statuses = rig.Channel.GetReaderStatuses();

            Assert.Equal(IntPtr.Zero, rig.Context.LastStatusChangeTimeout);
            Assert.All(seenCurrent, s => Assert.Equal(SCRState.Unaware, s));
            Assert.Equal(new[] { "R0", "R1", "R2", "R3" }, statuses.Select(s => s.Name));
            Assert.Equal(
                new[] { ReaderCardState.Empty, ReaderCardState.Present, ReaderCardState.Mute, ReaderCardState.Unavailable },
                statuses.Select(s => s.State));
        }

        [Fact]
        public void Reader_statuses_treat_timeout_as_no_change_but_map_other_errors()
        {
            var rig = new Rig();
            rig.Context.StatusChange = (timeout, states) =>
            {
                states[0].EventState = SCRState.Present;
                return SCardError.Timeout;
            };
            Assert.Equal(ReaderCardState.Present, Assert.Single(rig.Channel.GetReaderStatuses()).State);

            rig.Context.StatusChange = (timeout, states) => SCardError.NoService;
            Assert.Throws<SmartCardServiceUnavailableException>(() => rig.Channel.GetReaderStatuses());
        }

        [Fact]
        public void Reader_statuses_treat_a_thrown_timeout_as_no_change()
        {
            var rig = new Rig();
            rig.Context.StatusChange = (timeout, states) =>
            {
                states[0].EventState = SCRState.Empty;
                throw new UserTimeoutException(SCardError.Timeout);
            };

            Assert.Equal(ReaderCardState.Empty, Assert.Single(rig.Channel.GetReaderStatuses()).State);
        }

        [Fact]
        public void Reader_statuses_with_no_readers_do_not_call_get_status_change()
        {
            var rig = new Rig();
            rig.Context.Readers = Array.Empty<string>();
            rig.Context.StatusChange = (timeout, states) => throw new InvalidOperationException("must not be called");

            Assert.Empty(rig.Channel.GetReaderStatuses());
        }

        [Fact]
        public void Guard_selects_a_reader_through_the_adapter()
        {
            var rig = new Rig();
            rig.Context.Readers = new[] { "R0", "R1" };
            rig.Context.StatusChange = (timeout, states) =>
            {
                states[0].EventState = SCRState.Empty;
                states[1].EventState = SCRState.Present;
                return SCardError.Success;
            };
            using var guard = new GuardedCardConnection(rig.Channel, CommandPolicy.Public);

            Assert.Equal("R1", guard.SelectReader(null));
        }

        [Fact]
        public void Context_is_established_once_and_dispose_releases_it()
        {
            Rig rig = new Rig().Connected();
            rig.Channel.ListReaders();
            rig.Channel.GetReaderStatuses();

            rig.Channel.Dispose();
            rig.Channel.Dispose();

            Assert.Equal(1, rig.Factory.EstablishCalls);
            Assert.Equal(1, rig.Context.ReleaseCalls);
            Assert.Equal(1, rig.Context.DisposeCalls);
            Assert.Equal(new[] { SCardReaderDisposition.Leave }, rig.Reader.Disconnects);
            Assert.Equal(1, rig.Reader.DisposeCalls);
        }

        [Fact]
        public void Dispose_releases_the_context_even_when_disconnect_fails()
        {
            Rig rig = new Rig().Connected();
            rig.Reader.DisconnectThrows = new PCSCException(SCardError.InvalidHandle, "fake");

            rig.Channel.Dispose();

            Assert.Equal(1, rig.Context.ReleaseCalls);
        }

        [Fact]
        public void Disconnect_keeps_the_context_for_a_retry_and_a_new_connect_works()
        {
            Rig rig = new Rig().Connected();

            rig.Channel.Disconnect();
            rig.Channel.Connect("Fake Reader 0");

            Assert.Equal(1, rig.Factory.EstablishCalls);
            Assert.Equal(0, rig.Context.ReleaseCalls);
        }

        [Fact]
        public void Disconnect_when_not_connected_does_nothing()
        {
            var rig = new Rig();

            rig.Channel.Disconnect();

            Assert.Empty(rig.Reader.Disconnects);
        }

        [Fact]
        public void Failed_connect_can_be_retried_on_the_same_channel()
        {
            var rig = new Rig();
            rig.Reader.ConnectResult = SCardError.SharingViolation;
            Assert.Throws<CardCommunicationException>(() => rig.Channel.Connect("Fake Reader 0"));

            rig.Reader.ConnectResult = SCardError.Success;
            rig.Channel.Connect("Fake Reader 0");

            rig.Channel.BeginTransaction();
            Assert.Equal(1, rig.Reader.BeginCalls);
        }

        [Fact]
        public void Use_after_dispose_throws_object_disposed()
        {
            var rig = new Rig();
            rig.Channel.Dispose();

            Assert.Throws<ObjectDisposedException>(() => rig.Channel.ListReaders());
            Assert.Throws<ObjectDisposedException>(() => rig.Channel.Connect("Fake Reader 0"));
        }

        // ---- monitor ----

        [Fact]
        public void Monitor_forwards_reader_name_atr_and_failure()
        {
            var rig = new Rig();
            using IReaderMonitor monitor = rig.Channel.CreateMonitor();
            var inserted = new List<ReaderCardEventArgs>();
            var removed = new List<ReaderCardEventArgs>();
            var failed = new List<CardFailureKind>();
            monitor.CardInserted += (s, e) => inserted.Add(e);
            monitor.CardRemoved += (s, e) => removed.Add(e);
            monitor.Failed += (s, e) => failed.Add(e.Failure);

            monitor.Start(new[] { "R0", "R1" });
            rig.Monitor.RaiseInserted("R1", new byte[] { 0x3B, 0x67 });
            rig.Monitor.RaiseRemoved("R1");
            rig.Monitor.RaiseFailure(new NoServiceException(SCardError.NoService));

            Assert.Equal(new[] { "R0", "R1" }, rig.Monitor.Started);
            Assert.Equal("R1", Assert.Single(inserted).ReaderName);
            Assert.Equal(new byte[] { 0x3B, 0x67 }, inserted[0].Atr);
            Assert.Equal("R1", Assert.Single(removed).ReaderName);
            Assert.Equal(CardFailureKind.ServiceUnavailable, Assert.Single(failed));
        }

        [Fact]
        public void Monitor_start_failure_is_a_library_exception_and_stop_and_dispose_clean_up()
        {
            var rig = new Rig();
            rig.Monitor.StartThrows = new NoServiceException(SCardError.NoService);
            IReaderMonitor monitor = rig.Channel.CreateMonitor();

            Assert.Throws<SmartCardServiceUnavailableException>(() => monitor.Start(new[] { "R0" }));

            monitor.Stop();
            Assert.Equal(1, rig.Monitor.CancelCalls);

            monitor.Dispose();
            monitor.Dispose();
            Assert.Equal(1, rig.Monitor.DisposeCalls);
            Assert.Equal(0, rig.Monitor.SubscriberCount);
        }

        // ---- real PC/SC library, no hardware required ----

        [Fact]
        public void Real_pcsc_library_only_gives_library_exceptions_on_a_machine_without_hardware_assumptions()
        {
            // Holds with or without a service, native library or reader: the point is the exception type. This
            // proves the wiring to the real package only. It says nothing about any reader or card.
            using var channel = new PcscRawCardChannel();
            try
            {
                IReadOnlyList<string> readers = channel.ListReaders();
                IReadOnlyList<ReaderStatus> statuses = channel.GetReaderStatuses();
                Assert.Equal(readers.Count, statuses.Count);
            }
            catch (ThaiIdCardException)
            {
                // Expected on machines without pcscd or the native library.
            }
        }
    }
}
