using System;
using System.Collections.Generic;
using System.Linq;
using PCSC;
using PCSC.Monitoring;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Fake PC/SC types for adapter tests. They do not simulate a card, only return codes and throws.</summary>
    internal sealed class FakeContextFactory : IContextFactory
    {
        public FakeContextFactory(FakePcscContext context)
        {
            Context = context;
        }

        public FakePcscContext Context { get; }

        public Exception? EstablishFailure { get; set; }

        public int EstablishCalls { get; private set; }

        public ISCardContext Establish(SCardScope scope)
        {
            EstablishCalls++;
            if (EstablishFailure != null)
            {
                throw EstablishFailure;
            }

            return Context;
        }
    }

    internal sealed class FakePcscContext : ISCardContext
    {
        public string[] Readers { get; set; } = { "Fake Reader 0" };

        public Exception? GetReadersFailure { get; set; }

        public Func<IntPtr, SCardReaderState[], SCardError>? StatusChange { get; set; }

        public IntPtr LastStatusChangeTimeout { get; private set; }

        public int ReleaseCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public string[] GetReaders()
        {
            if (GetReadersFailure != null)
            {
                throw GetReadersFailure;
            }

            return Readers;
        }

        public SCardError GetStatusChange(IntPtr timeout, SCardReaderState[] readerStates)
        {
            LastStatusChangeTimeout = timeout;
            return StatusChange == null ? SCardError.Success : StatusChange(timeout, readerStates);
        }

        public void Release() => ReleaseCalls++;

        public void Dispose() => DisposeCalls++;

        public IntPtr Handle => throw new NotSupportedException();
        public int MaxAtrSize => throw new NotSupportedException();
        public IntPtr Infinite => throw new NotSupportedException();
        public void Establish(SCardScope scope) => throw new NotSupportedException();
        public SCardError CheckValidity() => throw new NotSupportedException();
        public bool IsValid() => throw new NotSupportedException();
        public string[] GetReaders(string[] groups) => throw new NotSupportedException();
        public string[] GetReaderGroups() => throw new NotSupportedException();
        public ICardReader ConnectReader(string readerName, SCardShareMode mode, SCardProtocol preferredProtocol) => throw new NotSupportedException();
        public ICardHandle Connect(string readerName, SCardShareMode mode, SCardProtocol preferredProtocol) => throw new NotSupportedException();
        public SCardReaderState[] GetReaderStatus(string[] readerNames) => throw new NotSupportedException();
        public SCardReaderState GetReaderStatus(string readerName) => throw new NotSupportedException();
        public SCardError Cancel() => throw new NotSupportedException();
    }

    internal sealed class FakePcscReader : ISCardReader
    {
        public SCardError ConnectResult { get; set; } = SCardError.Success;

        public SCardError StatusResult { get; set; } = SCardError.Success;

        public SCardError TransmitResult { get; set; } = SCardError.Success;

        public SCardError BeginResult { get; set; } = SCardError.Success;

        public SCardError EndResult { get; set; } = SCardError.Success;

        public SCardError DisconnectResult { get; set; } = SCardError.Success;

        public Exception? ConnectThrows { get; set; }

        public Exception? TransmitThrows { get; set; }

        public Exception? BeginThrows { get; set; }

        public Exception? DisconnectThrows { get; set; }

        public SCardProtocol Protocol { get; set; } = SCardProtocol.T1;

        public byte[]? Atr { get; set; } = { 0x3B, 0x79 };

        public byte[] Answer { get; set; } = { 0x90, 0x00 };

        public string? ConnectedReader { get; private set; }

        public SCardShareMode? ShareMode { get; private set; }

        public SCardProtocol? PreferredProtocol { get; private set; }

        public List<byte[]> Sent { get; } = new List<byte[]>();

        public int LastSendLength { get; private set; }

        public int LastReceiveCapacity { get; private set; }

        public List<SCardReaderDisposition> Disconnects { get; } = new List<SCardReaderDisposition>();

        public List<SCardReaderDisposition> Ends { get; } = new List<SCardReaderDisposition>();

        public int BeginCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public bool IsConnected { get; private set; }

        public SCardProtocol ActiveProtocol => Protocol;

        public SCardError Connect(string readerName, SCardShareMode mode, SCardProtocol preferredProtocol)
        {
            ConnectedReader = readerName;
            ShareMode = mode;
            PreferredProtocol = preferredProtocol;
            if (ConnectThrows != null)
            {
                throw ConnectThrows;
            }

            IsConnected = ConnectResult == SCardError.Success;
            return ConnectResult;
        }

        public SCardError Disconnect(SCardReaderDisposition disposition)
        {
            Disconnects.Add(disposition);
            if (DisconnectThrows != null)
            {
                throw DisconnectThrows;
            }

            IsConnected = false;
            return DisconnectResult;
        }

        public SCardError BeginTransaction()
        {
            BeginCalls++;
            if (BeginThrows != null)
            {
                throw BeginThrows;
            }

            return BeginResult;
        }

        public SCardError EndTransaction(SCardReaderDisposition disposition)
        {
            Ends.Add(disposition);
            return EndResult;
        }

        public SCardError Status(out string[] readerName, out SCardState state, out SCardProtocol protocol, out byte[] atr)
        {
            readerName = new[] { ConnectedReader ?? string.Empty };
            state = SCardState.Present;
            protocol = Protocol;
            atr = Atr!;
            return StatusResult;
        }

        // The explicit-length overload is the only Transmit the adapter may use; all others throw below.
        public SCardError Transmit(byte[] pbSendBuffer, int sendBufferLength, byte[] pbRecvBuffer, ref int pcbRecvLength)
        {
            Sent.Add(pbSendBuffer.Take(sendBufferLength).ToArray());
            LastSendLength = sendBufferLength;
            LastReceiveCapacity = pcbRecvLength;
            if (TransmitThrows != null)
            {
                throw TransmitThrows;
            }

            if (TransmitResult != SCardError.Success)
            {
                return TransmitResult;
            }

            Array.Copy(Answer, pbRecvBuffer, Answer.Length);
            pcbRecvLength = Answer.Length;
            return SCardError.Success;
        }

        public void Dispose() => DisposeCalls++;

        public string ReaderName => throw new NotSupportedException();
        public ISCardContext CurrentContext => throw new NotSupportedException();
        public SCardShareMode CurrentShareMode => throw new NotSupportedException();
        public IntPtr CardHandle => throw new NotSupportedException();
        public SCardError Reconnect(SCardShareMode mode, SCardProtocol preferredProtocol, SCardReaderDisposition initialization) => throw new NotSupportedException();
        public SCardError Transmit(IntPtr pioSendPci, byte[] pbSendBuffer, int sendBufferLength, SCardPCI pioRecvPci, byte[] pbRecvBuffer, ref int pcbRecvLength) => throw new NotSupportedException();
        public SCardError Transmit(IntPtr pioSendPci, byte[] pbSendBuffer, SCardPCI pioRecvPci, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError Transmit(IntPtr pioSendPci, byte[] pbSendBuffer, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError Transmit(SCardPCI pioSendPci, byte[] pbSendBuffer, SCardPCI pioRecvPci, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError Transmit(byte[] pbSendBuffer, byte[] pbRecvBuffer, ref int pcbRecvLength) => throw new NotSupportedException();
        public SCardError Transmit(byte[] pbSendBuffer, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError Control(IntPtr dwControlCode, byte[] pbSendBuffer, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError GetAttrib(IntPtr dwAttrId, byte[] pbAttr, out int pcbAttrLen) => throw new NotSupportedException();
        public SCardError GetAttrib(IntPtr dwAttrId, out byte[] pbAttr) => throw new NotSupportedException();
        public SCardError GetAttrib(SCardAttribute attributeId, byte[] pbAttr, out int pcbAttrLen) => throw new NotSupportedException();
        public SCardError GetAttrib(SCardAttribute attributeId, out byte[] pbAttr) => throw new NotSupportedException();
        public SCardError SetAttrib(IntPtr dwAttrId, byte[] pbAttr, int attributeSize) => throw new NotSupportedException();
        public SCardError SetAttrib(IntPtr dwAttrId, byte[] pbAttr) => throw new NotSupportedException();
        public SCardError SetAttrib(SCardAttribute attributeId, byte[] pbAttr, int attributeSize) => throw new NotSupportedException();
        public SCardError SetAttrib(SCardAttribute attributeId, byte[] pbAttr) => throw new NotSupportedException();
    }

    internal sealed class FakePcscMonitor : ISCardMonitor
    {
        public string[]? Started { get; private set; }

        public int CancelCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public Exception? StartThrows { get; set; }

        public event StatusChangeEvent? StatusChanged;

        public event CardInsertedEvent? CardInserted;

        public event CardRemovedEvent? CardRemoved;

        public event CardInitializedEvent? Initialized;

        public event MonitorExceptionEvent? MonitorException;

        public void Start(string readerName) => Start(new[] { readerName });

        public void Start(string[] readerNames)
        {
            if (StartThrows != null)
            {
                throw StartThrows;
            }

            Started = readerNames;
        }

        public void Cancel() => CancelCalls++;

        public void Dispose() => DisposeCalls++;

        public void RaiseInserted(string reader, byte[] atr) => CardInserted?.Invoke(this, new CardStatusEventArgs(reader, SCRState.Present, atr));

        public void RaiseRemoved(string reader) => CardRemoved?.Invoke(this, new CardStatusEventArgs(reader, SCRState.Empty, Array.Empty<byte>()));

        public void RaiseFailure(PCSC.Exceptions.PCSCException ex) => MonitorException?.Invoke(this, ex);

        public int SubscriberCount =>
            (CardInserted?.GetInvocationList().Length ?? 0) + (CardRemoved?.GetInvocationList().Length ?? 0) + (MonitorException?.GetInvocationList().Length ?? 0);

        // Kept so the interface is complete; the adapter does not use them.
        public void TouchUnused()
        {
            StatusChanged?.Invoke(this, null!);
            Initialized?.Invoke(this, null!);
        }

        public IntPtr GetCurrentStateValue(int index) => throw new NotSupportedException();
        public SCRState GetCurrentState(int index) => throw new NotSupportedException();
        public string GetReaderName(int index) => throw new NotSupportedException();
        public string[] ReaderNames => throw new NotSupportedException();
        public bool Monitoring => throw new NotSupportedException();
        public int ReaderCount => throw new NotSupportedException();
    }
}
