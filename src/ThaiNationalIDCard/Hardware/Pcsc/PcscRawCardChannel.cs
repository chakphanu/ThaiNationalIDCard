using System;
using System.Collections.Generic;
using System.Linq;
using PCSC;
using PCSC.Exceptions;
using PCSC.Monitoring;

namespace ThaiNationalIDCard.Hardware.Pcsc
{
    /// <summary>
    /// PC/SC side of the seam, and with <see cref="PcscReaderMonitor"/> the only new code that uses PCSC types.
    /// Every failure leaves as a library exception, whether PCSC reported it as a return code or as an exception.
    /// </summary>
    internal sealed class PcscRawCardChannel : IRawCardChannel
    {
        // Largest short response: 256 data bytes plus SW1 and SW2.
        private const int ReceiveBufferSize = 258;

        private readonly Func<IContextFactory> _contextFactory;
        private readonly Func<ISCardContext, ISCardReader> _readerFactory;
        private readonly Func<IContextFactory, ISCardMonitor> _monitorFactory;

        private ISCardContext? _context;
        private ISCardReader? _reader;
        private bool _disposed;

        public PcscRawCardChannel()
            : this(() => ContextFactory.Instance, context => new SCardReader(context), factory => new SCardMonitor(factory, SCardScope.System))
        {
        }

        /// <summary>Test seam. The factories run lazily, so a missing native library is mapped, not thrown here.</summary>
        internal PcscRawCardChannel(Func<IContextFactory> contextFactory, Func<ISCardContext, ISCardReader> readerFactory,
            Func<IContextFactory, ISCardMonitor> monitorFactory)
        {
            _contextFactory = contextFactory;
            _readerFactory = readerFactory;
            _monitorFactory = monitorFactory;
        }

        public IReadOnlyList<string> ListReaders()
        {
            ThrowIfDisposed();
            return PcscErrors.Run(() =>
            {
                ISCardContext context = EnsureContext();
                try
                {
                    return (IReadOnlyList<string>)(context.GetReaders() ?? Array.Empty<string>());
                }
                catch (PCSCException ex) when (ex.SCardError == SCardError.NoReadersAvailable)
                {
                    return Array.Empty<string>();
                }
            });
        }

        public IReadOnlyList<ReaderStatus> GetReaderStatuses()
        {
            IReadOnlyList<string> names = ListReaders();
            if (names.Count == 0)
            {
                return Array.Empty<ReaderStatus>();
            }

            return PcscErrors.Run(() =>
            {
                SCardReaderState[] states = names
                    .Select(name => new SCardReaderState { ReaderName = name, CurrentState = SCRState.Unaware, EventState = SCRState.Unknown })
                    .ToArray();

                try
                {
                    QueryStatus(EnsureContext(), states);
                    return (IReadOnlyList<ReaderStatus>)states.Select(s => new ReaderStatus(s.ReaderName, ToCardState(s.EventState))).ToArray();
                }
                finally
                {
                    foreach (SCardReaderState state in states)
                    {
                        state.Dispose();
                    }
                }
            });
        }

        public CardConnectionInfo Connect(string readerName)
        {
            ThrowIfDisposed();
            ReleaseReader();

            return PcscErrors.Run(() =>
            {
                ISCardReader reader = _readerFactory(EnsureContext());
                try
                {
                    PcscErrors.Check(reader.Connect(readerName, SCardShareMode.Shared, SCardProtocol.T0 | SCardProtocol.T1));
                    CardTransportProtocol protocol = ToProtocol(reader.ActiveProtocol);
                    PcscErrors.Check(reader.Status(out string[] _, out SCardState _, out SCardProtocol _, out byte[] atr));

                    _reader = reader;
                    return new CardConnectionInfo(readerName, atr, protocol);
                }
                catch
                {
                    ReleaseQuietly(reader);
                    throw;
                }
            });
        }

        public void Disconnect()
        {
            ThrowIfDisposed();
            ReleaseReader();
        }

        public void BeginTransaction()
        {
            ThrowIfDisposed();
            ISCardReader reader = RequireReader();
            PcscErrors.RunConnected(() => PcscErrors.Check(reader.BeginTransaction()));
        }

        public void EndTransaction()
        {
            ThrowIfDisposed();
            ISCardReader reader = RequireReader();
            PcscErrors.RunConnected(() => PcscErrors.Check(reader.EndTransaction(SCardReaderDisposition.Leave)));
        }

        public byte[] Transmit(byte[] command)
        {
            ThrowIfDisposed();
            ISCardReader reader = RequireReader();

            return PcscErrors.RunConnected(() =>
            {
                // Explicit length: the real response length comes back in receiveLength and is copied exactly.
                var buffer = new byte[ReceiveBufferSize];
                int receiveLength = buffer.Length;
                PcscErrors.Check(reader.Transmit(command, command.Length, buffer, ref receiveLength));

                var response = new byte[receiveLength];
                Array.Copy(buffer, response, receiveLength);
                return response;
            });
        }

        public IReaderMonitor CreateMonitor()
        {
            ThrowIfDisposed();
            return PcscErrors.Run(() => (IReaderMonitor)new PcscReaderMonitor(_monitorFactory(_contextFactory())));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseReader();

            ISCardContext? context = _context;
            _context = null;
            if (context == null)
            {
                return;
            }

            // Both run even if the first fails: nothing here may hide the error a caller is handling.
            try
            {
                context.Release();
            }
            catch (PCSCException)
            {
            }

            try
            {
                context.Dispose();
            }
            catch (PCSCException)
            {
            }
        }

        /// <summary>
        /// Zero timeout: this must not block. A timeout only means "no change", so the event states still count.
        /// That reading of Unaware with a zero timeout has not been confirmed on pcsc-lite or WinSCard.
        /// </summary>
        private static void QueryStatus(ISCardContext context, SCardReaderState[] states)
        {
            SCardError error;
            try
            {
                error = context.GetStatusChange(IntPtr.Zero, states);
            }
            catch (PCSCException ex) when (PcscErrors.Classify(ex) == CardFailureKind.Timeout)
            {
                return;
            }

            if (error != SCardError.Timeout)
            {
                PcscErrors.Check(error);
            }
        }

        private static CardTransportProtocol ToProtocol(SCardProtocol protocol)
        {
            switch (protocol)
            {
                case SCardProtocol.T0:
                    return CardTransportProtocol.T0;
                case SCardProtocol.T1:
                    return CardTransportProtocol.T1;
                case SCardProtocol.Raw:
                    return CardTransportProtocol.Raw;
                default:
                    throw new PCSCException(SCardError.ProtocolMismatch);
            }
        }

        private static ReaderCardState ToCardState(SCRState state)
        {
            if ((state & SCRState.Unavailable) != 0)
            {
                return ReaderCardState.Unavailable;
            }

            if ((state & SCRState.Mute) != 0)
            {
                return ReaderCardState.Mute;
            }

            if ((state & SCRState.Present) != 0)
            {
                return ReaderCardState.Present;
            }

            return (state & SCRState.Empty) != 0 ? ReaderCardState.Empty : ReaderCardState.Unknown;
        }

        private static void ReleaseQuietly(ISCardReader reader)
        {
            try
            {
                reader.Disconnect(SCardReaderDisposition.Leave);
            }
            catch (Exception ex) when (ex is PCSCException || ex is InvalidOperationException)
            {
            }

            reader.Dispose();
        }

        private ISCardContext EnsureContext() => _context ??= _contextFactory().Establish(SCardScope.System);

        private ISCardReader RequireReader() => _reader ?? throw new CardCommunicationException(CardFailureKind.NotConnected);

        private void ReleaseReader()
        {
            ISCardReader? reader = _reader;
            _reader = null;
            if (reader != null)
            {
                ReleaseQuietly(reader);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PcscRawCardChannel));
            }
        }
    }

    /// <summary>PC/SC monitor behind the seam. PCSC event arguments and errors do not pass through it.</summary>
    internal sealed class PcscReaderMonitor : IReaderMonitor
    {
        private readonly ISCardMonitor _monitor;
        private bool _disposed;

        public PcscReaderMonitor(ISCardMonitor monitor)
        {
            _monitor = monitor;
            _monitor.CardInserted += OnInserted;
            _monitor.CardRemoved += OnRemoved;
            _monitor.MonitorException += OnException;
        }

        public event EventHandler<ReaderCardEventArgs>? CardInserted;

        public event EventHandler<ReaderCardEventArgs>? CardRemoved;

        public event EventHandler<ReaderMonitorFailureEventArgs>? Failed;

        public void Start(IReadOnlyList<string> readerNames)
        {
            PcscErrors.Run(() =>
            {
                _monitor.Start(readerNames.ToArray());
                return true;
            });
        }

        public void Stop()
        {
            PcscErrors.Run(() =>
            {
                _monitor.Cancel();
                return true;
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _monitor.CardInserted -= OnInserted;
            _monitor.CardRemoved -= OnRemoved;
            _monitor.MonitorException -= OnException;
            _monitor.Dispose();
        }

        private void OnInserted(object sender, CardStatusEventArgs e) =>
            CardInserted?.Invoke(this, new ReaderCardEventArgs(e.ReaderName, e.Atr));

        private void OnRemoved(object sender, CardStatusEventArgs e) =>
            CardRemoved?.Invoke(this, new ReaderCardEventArgs(e.ReaderName, null));

        private void OnException(object sender, PCSCException exception) =>
            Failed?.Invoke(this, new ReaderMonitorFailureEventArgs(PcscErrors.Classify(exception)));
    }

    /// <summary>The only place that maps PCSC errors to library failures.</summary>
    internal static class PcscErrors
    {
        public static CardFailureKind Classify(SCardError error)
        {
            switch (error)
            {
                case SCardError.NoService:
                case SCardError.ServiceStopped:
                case SCardError.Shutdown:
                    return CardFailureKind.ServiceUnavailable;
                case SCardError.NoReadersAvailable:
                case SCardError.UnknownReader:
                case SCardError.ReaderUnavailable:
                case SCardError.ReaderUnsupported:
                case SCardError.DuplicateReader:
                    return CardFailureKind.ReaderNotFound;
                case SCardError.NoSmartcard:
                    return CardFailureKind.CardNotPresent;
                case SCardError.RemovedCard:
                    return CardFailureKind.CardRemoved;
                case SCardError.ResetCard:
                    return CardFailureKind.CardReset;
                case SCardError.SharingViolation:
                    return CardFailureKind.SharingViolation;
                case SCardError.Timeout:
                case SCardError.WaitedTooLong:
                    return CardFailureKind.Timeout;
                case SCardError.UnsupportedCard:
                case SCardError.CardUnsupported:
                case SCardError.UnknownCard:
                case SCardError.ProtocolMismatch:
                case SCardError.InvalidAtr:
                    return CardFailureKind.UnsupportedCard;
                default:
                    return CardFailureKind.Communication;
            }
        }

        /// <summary>A thrown error. The timeout exception type counts whatever code it carries.</summary>
        public static CardFailureKind Classify(PCSCException exception) =>
            exception is UserTimeoutException ? CardFailureKind.Timeout : Classify(exception.SCardError);

        public static void Check(SCardError error)
        {
            if (error != SCardError.Success)
            {
                throw CardFailures.ToException(Classify(error));
            }
        }

        public static T Run<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (PCSCException ex)
            {
                throw CardFailures.ToException(Classify(ex), ex);
            }
            catch (DllNotFoundException ex)
            {
                throw new SmartCardServiceUnavailableException(ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new SmartCardServiceUnavailableException(ex);
            }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException || ex.InnerException is EntryPointNotFoundException)
            {
                throw new SmartCardServiceUnavailableException(ex);
            }
        }

        /// <summary>Like Run, for calls on a reader. PCSC throws InvalidOperationException when it is not connected.</summary>
        public static T RunConnected<T>(Func<T> action)
        {
            try
            {
                return Run(action);
            }
            catch (InvalidOperationException ex)
            {
                throw new CardCommunicationException(CardFailureKind.NotConnected, ex);
            }
        }

        public static void RunConnected(Action action)
        {
            RunConnected(() =>
            {
                action();
                return true;
            });
        }
    }
}
