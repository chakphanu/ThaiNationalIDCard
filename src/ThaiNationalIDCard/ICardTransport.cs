using System;

namespace ThaiNationalIDCard
{
    /// <summary>
    /// Minimal abstraction over a smart card connection. It exists so the APDU flow in
    /// <see cref="ThaiIDCard"/> can run against a scripted fake without PC/SC hardware.
    /// Implementations are not required to be thread safe.
    /// </summary>
    internal interface ICardTransport
    {
        /// <summary>
        /// ATR of the connected card. Only valid after <see cref="Connect"/> succeeded.
        /// May be null or empty when the reader reports no ATR.
        /// </summary>
        byte[] Atr { get; }

        /// <summary>
        /// Opens a connection to the card. A null or empty <paramref name="readerName"/> means
        /// "use the first available reader". Throws <see cref="PCSC.Exceptions.PCSCException"/> on failure.
        /// </summary>
        void Connect(string readerName);

        /// <summary>Closes the connection and releases transport resources.</summary>
        void Disconnect();

        /// <summary>
        /// Sends one command APDU and returns EXACTLY the bytes received from the card,
        /// including the trailing SW1/SW2. The returned array length is the real response length.
        /// Throws <see cref="PCSC.Exceptions.PCSCException"/> on a non-success transport error.
        /// </summary>
        byte[] Transmit(byte[] command);
    }
}
