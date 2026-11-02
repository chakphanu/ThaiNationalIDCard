using System;
using System.Collections.Generic;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// Watches readers for card changes. Events are raised on the monitor thread, so handlers must not read the card there or
    /// wait for anything. <see cref="ThaiIdCardMonitor"/> is the only subscriber and only queues. Dispose must not block on
    /// the monitor thread, because <see cref="ThaiIdCardMonitor.Stop"/> can run inside a handler. A monitor is used once: a new
    /// watch gets a new one, since PCSC keeps reporting Monitoring until Cancel even after its thread ended on an error.
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
