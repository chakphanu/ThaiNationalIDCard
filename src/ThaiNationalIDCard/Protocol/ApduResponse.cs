using System;
using System.Globalization;

namespace ThaiNationalIDCard.Protocol
{
    /// <summary>
    /// Result of one state machine run. ToString never shows <see cref="Data"/>, so logging a response cannot
    /// leak card data. Data is only filled when the data can be used; other status words give empty Data.
    /// </summary>
    internal sealed class ApduResponse
    {
        public ApduResponse(byte[] data, byte sw1, byte sw2, ExchangeNotes flags = ExchangeNotes.None)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Sw1 = sw1;
            Sw2 = sw2;
            Flags = flags;
        }

#pragma warning disable CA1819 // The buffer is the result of the exchange and has a single owner.
        public byte[] Data { get; }
#pragma warning restore CA1819

        public byte Sw1 { get; }

        public byte Sw2 { get; }

        public ExchangeNotes Flags { get; }

        public int StatusWord => (Sw1 << 8) | Sw2;

        public bool IsSuccess => StatusWord == 0x9000;

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "ApduResponse(SW={0:X4}, Length={1}, Flags={2})", StatusWord, Data.Length, Flags);
    }
}
