using System;
using System.Collections.Generic;
using System.Linq;
using PCSC;
using PCSC.Exceptions;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Fake PC/SC context. Only the members used by ThaiIDCard are implemented.</summary>
    internal sealed class FakeContext : ISCardContext
    {
        public string[] Readers { get; set; } = new[] { "Fake Reader 0" };
        public Exception GetReadersFailure { get; set; }
        public int ReleaseCalls { get; private set; }

        public string[] GetReaders()
        {
            if (GetReadersFailure != null)
                throw GetReadersFailure;
            return Readers;
        }

        public void Release() => ReleaseCalls++;

        // Everything below is unused by ThaiIDCard.
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
        public SCardError GetStatusChange(IntPtr timeout, SCardReaderState[] readerStates) => throw new NotSupportedException();
        public SCardError Cancel() => throw new NotSupportedException();
        public void Dispose() { }
    }

    /// <summary>
    /// Scripted in-memory card behind ISCardReader. Responses are keyed by the command bytes as lowercase hex.
    /// Several responses for one command are returned in order, the last one repeats.
    /// Every transmitted command is recorded so tests can count them. Unscripted commands fail loudly.
    /// Transmit returns exact-length response buffers (the same shape PCSC's Transmit(ref) produces).
    /// </summary>
    internal sealed class FakeReader : ISCardReader
    {
        private readonly Dictionary<string, List<byte[]>> _script = new Dictionary<string, List<byte[]>>();
        private readonly Dictionary<string, SCardError> _errors = new Dictionary<string, SCardError>();
        private readonly Dictionary<string, int> _callIndex = new Dictionary<string, int>();

        public byte[] Atr { get; set; } = new byte[] { 0x3B, 0x79, 0x18, 0x00, 0x00, 0xFF };
        public SCardError ConnectError { get; set; } = SCardError.Success;
        public SCardProtocol Protocol { get; set; } = SCardProtocol.T1;

        public List<byte[]> Sent { get; } = new List<byte[]>();
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public string ConnectedReaderName { get; private set; }

        public static string Hex(byte[] bytes) =>
            string.Concat(bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        public IEnumerable<string> SentHex => Sent.Select(Hex);

        public int CountOf(string commandHex) =>
            SentHex.Count(h => string.Equals(h, commandHex, StringComparison.OrdinalIgnoreCase));

        public FakeReader On(string commandHex, params byte[][] responses)
        {
            string key = Normalize(commandHex);
            if (!_script.TryGetValue(key, out var list))
                _script[key] = list = new List<byte[]>();
            list.AddRange(responses);
            return this;
        }

        /// <summary>Transmit of this command returns the given SCardError (like a card/reader failure).</summary>
        public FakeReader OnError(string commandHex, SCardError error)
        {
            _errors[Normalize(commandHex)] = error;
            return this;
        }

        private static string Normalize(string hex) => hex.Replace(" ", "").ToLowerInvariant();

        public bool IsConnected { get; private set; }
        public SCardProtocol ActiveProtocol => Protocol;

        public SCardError Connect(string readerName, SCardShareMode mode, SCardProtocol preferredProtocol)
        {
            ConnectCalls++;
            ConnectedReaderName = readerName;
            if (ConnectError != SCardError.Success)
                return ConnectError;
            IsConnected = true;
            return SCardError.Success;
        }

        public SCardError Disconnect(SCardReaderDisposition disposition)
        {
            DisconnectCalls++;
            IsConnected = false;
            return SCardError.Success;
        }

        public SCardError Status(out string[] readerName, out SCardState state, out SCardProtocol protocol, out byte[] atr)
        {
            readerName = new[] { ConnectedReaderName };
            state = SCardState.Present;
            protocol = Protocol;
            atr = Atr;
            return SCardError.Success;
        }

        public SCardError Transmit(IntPtr pioSendPci, byte[] pbSendBuffer, ref byte[] pbRecvBuffer)
        {
            Sent.Add((byte[])pbSendBuffer.Clone());
            string key = Hex(pbSendBuffer);

            if (_errors.TryGetValue(key, out var error))
                return error;

            if (!_script.TryGetValue(key, out var list))
                throw new InvalidOperationException("FakeReader: unscripted command " + key);

            _callIndex.TryGetValue(key, out int index);
            _callIndex[key] = index + 1;
            pbRecvBuffer = (byte[])list[Math.Min(index, list.Count - 1)].Clone();
            return SCardError.Success;
        }

        // Everything below is unused by ThaiIDCard.
        public string ReaderName => throw new NotSupportedException();
        public ISCardContext CurrentContext => throw new NotSupportedException();
        public SCardShareMode CurrentShareMode => throw new NotSupportedException();
        public IntPtr CardHandle => throw new NotSupportedException();
        public SCardError Reconnect(SCardShareMode mode, SCardProtocol preferredProtocol, SCardReaderDisposition initialization) => throw new NotSupportedException();
        public SCardError BeginTransaction() => throw new NotSupportedException();
        public SCardError EndTransaction(SCardReaderDisposition disposition) => throw new NotSupportedException();
        public SCardError Transmit(IntPtr pioSendPci, byte[] pbSendBuffer, int sendBufferLength, SCardPCI pioRecvPci, byte[] pbRecvBuffer, ref int pcbRecvLength) => throw new NotSupportedException();
        public SCardError Transmit(IntPtr pioSendPci, byte[] pbSendBuffer, SCardPCI pioRecvPci, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError Transmit(SCardPCI pioSendPci, byte[] pbSendBuffer, SCardPCI pioRecvPci, ref byte[] pbRecvBuffer) => throw new NotSupportedException();
        public SCardError Transmit(byte[] pbSendBuffer, int sendBufferLength, byte[] pbRecvBuffer, ref int pcbRecvLength) => throw new NotSupportedException();
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
        public void Dispose() { }
    }
}
