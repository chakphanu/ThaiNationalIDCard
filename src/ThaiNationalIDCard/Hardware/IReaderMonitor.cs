using System;
using System.Collections.Generic;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// Watches readers for card changes. Events are raised on the monitor thread, so handlers must not
    /// read the card there. Multi-reader behaviour above this seam belongs to step 10.
    /// </summary>
    internal interface IReaderMonitor : IDisposable
    {
        event EventHandler<ReaderCardEventArgs> CardInserted;

        event EventHandler<ReaderCardEventArgs> CardRemoved;

        event EventHandler<ReaderMonitorFailureEventArgs> Failed;

        void Start(IReadOnlyList<string> readerNames);

        void Stop();
    }

    internal sealed class ReaderCardEventArgs : EventArgs
    {
        public ReaderCardEventArgs(string readerName, byte[]? atr)
        {
            ReaderName = readerName;
            Atr = atr ?? Array.Empty<byte>();
        }

        public string ReaderName { get; }

        public byte[] Atr { get; }
    }

    internal sealed class ReaderMonitorFailureEventArgs : EventArgs
    {
        public ReaderMonitorFailureEventArgs(CardFailureKind failure)
        {
            Failure = failure;
        }

        public CardFailureKind Failure { get; }
    }
}
