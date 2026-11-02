using System;
using System.Collections.Generic;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// The seam under the command guard: PC/SC in production, the simulator in tests. Only
    /// <see cref="GuardedCardConnection"/> may transmit through it. Implementations report failures with
    /// library exceptions and are not required to be thread safe.
    /// </summary>
    internal interface IRawCardChannel : IDisposable
    {
        /// <summary>Names of the known readers. Empty when there are none.</summary>
        IReadOnlyList<string> ListReaders();

        /// <summary>Current state of every known reader, taken from a status query that does not block.</summary>
        IReadOnlyList<ReaderStatus> GetReaderStatuses();

        /// <summary>Connects in shared mode with T=0 or T=1. A short or empty ATR is not a failure here.</summary>
        CardConnectionInfo Connect(string readerName);

        /// <summary>Disconnects and leaves the card as it is. Does nothing when not connected.</summary>
        void Disconnect();

        void BeginTransaction();

        void EndTransaction();

        /// <summary>
        /// Sends one command and returns exactly the bytes received, including the trailing SW1 and SW2.
        /// The array length is the real response length.
        /// </summary>
        byte[] Transmit(byte[] command);

        /// <summary>Creates a monitor. It has no way to transmit, so it stays outside the guard.</summary>
        IReaderMonitor CreateMonitor();
    }

    internal readonly struct ReaderStatus
    {
        public ReaderStatus(string name, ReaderCardState state)
        {
            Name = name;
            State = state;
        }

        public string Name { get; }

        public ReaderCardState State { get; }
    }

    /// <summary>What a successful connect reports. The ATR is never null.</summary>
    internal sealed class CardConnectionInfo
    {
        public CardConnectionInfo(string readerName, byte[]? atr, CardTransportProtocol protocol)
        {
            ReaderName = readerName;
            Atr = atr ?? Array.Empty<byte>();
            Protocol = protocol;
        }

        public string ReaderName { get; }

        public byte[] Atr { get; }

        public CardTransportProtocol Protocol { get; }
    }
}
