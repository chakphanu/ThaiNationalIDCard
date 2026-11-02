using System;
using System.Collections.Generic;
using System.Linq;
using PCSC;
using PCSC.Exceptions;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Scripted in-memory card. Responses are keyed by the command bytes as lowercase hex.
    /// When a command has several scripted responses they are returned in order and the last one repeats.
    /// Every command is recorded so tests can assert counts. Unscripted commands fail loudly.
    /// </summary>
    internal sealed class FakeCardTransport : ICardTransport
    {
        private readonly Dictionary<string, List<Func<byte[]>>> _script = new Dictionary<string, List<Func<byte[]>>>();
        private readonly Dictionary<string, int> _callIndex = new Dictionary<string, int>();

        public byte[] Atr { get; set; } = new byte[] { 0x3B, 0x79, 0x18, 0x00, 0x00, 0xFF };

        /// <summary>Every command passed to Transmit, in order.</summary>
        public List<byte[]> Sent { get; } = new List<byte[]>();

        public List<string> SentHex => Sent.Select(Hex).ToList();

        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public string? ConnectedReaderName { get; private set; }

        /// <summary>When set, Connect throws this error.</summary>
        public SCardError? ConnectError { get; set; }

        public static string Hex(byte[] bytes) =>
            string.Concat(bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Script one or more responses (full bytes including SW1/SW2) for a command given as hex.</summary>
        public FakeCardTransport On(string commandHex, params byte[][] responses)
        {
            Add(commandHex, responses.Select(r => (Func<byte[]>)(() => r)));
            return this;
        }

        /// <summary>Script a command that fails with a transport error (PCSCException).</summary>
        public FakeCardTransport OnError(string commandHex, SCardError error)
        {
            Add(commandHex, new Func<byte[]>[] { () => throw new PCSCException(error, "fake " + error) });
            return this;
        }

        public int CountOf(string commandHex) =>
            SentHex.Count(h => string.Equals(h, commandHex, StringComparison.OrdinalIgnoreCase));

        public void Connect(string readerName)
        {
            ConnectCalls++;
            ConnectedReaderName = readerName;
            if (ConnectError.HasValue)
                throw new PCSCException(ConnectError.Value, "fake connect " + ConnectError.Value);
        }

        public void Disconnect() => DisconnectCalls++;

        public byte[] Transmit(byte[] command)
        {
            Sent.Add((byte[])command.Clone());
            string key = Hex(command);
            if (!_script.TryGetValue(key, out var list))
                throw new InvalidOperationException("FakeCardTransport: unscripted command " + key);

            _callIndex.TryGetValue(key, out int index);
            _callIndex[key] = index + 1;
            return (byte[])list[Math.Min(index, list.Count - 1)]().Clone();
        }

        private void Add(string commandHex, IEnumerable<Func<byte[]>> responses)
        {
            string key = commandHex.Replace(" ", "").ToLowerInvariant();
            if (!_script.TryGetValue(key, out var list))
                _script[key] = list = new List<Func<byte[]>>();
            list.AddRange(responses);
        }
    }
}
