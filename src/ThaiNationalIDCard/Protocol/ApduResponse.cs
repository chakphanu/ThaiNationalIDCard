using System;
using System.Globalization;

namespace ThaiNationalIDCard.Protocol
{
    /// <summary>Things worth knowing about one exchange. Numbers only, safe to log.</summary>
    [Flags]
    internal enum ExchangeFlags
    {
        None = 0,

        /// <summary>The card offered or sent more than the command asked for; the rest was not kept.</summary>
        ExcessDropped = 1,

        /// <summary>The GET RESPONSE round limit stopped the exchange.</summary>
        RoundLimitReached = 2,

        /// <summary>The guard refused a follow-up (6C resend or GET RESPONSE) that the card asked for.</summary>
        FollowUpRejected = 4,

        /// <summary>The command was sent again once because of 6C xx.</summary>
        ResentAfterWrongLength = 8,

        /// <summary>A second 6C xx came after the single resend.</summary>
        WrongLengthRepeated = 16,

        /// <summary>A READ finished with 90 00 but fewer bytes than it asked for.</summary>
        ShorterThanRequested = 32,
    }

    /// <summary>
    /// Result of one state machine run. ToString never shows <see cref="Data"/>, so logging a response cannot
    /// leak card data. Data is only filled when the data can be used; other status words give empty Data.
    /// </summary>
    internal sealed class ApduResponse
    {
        public ApduResponse(byte[] data, byte sw1, byte sw2, ExchangeFlags flags = ExchangeFlags.None)
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

        public ExchangeFlags Flags { get; }

        public int StatusWord => (Sw1 << 8) | Sw2;

        public bool IsSuccess => StatusWord == 0x9000;

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "ApduResponse(SW={0:X4}, Length={1}, Flags={2})", StatusWord, Data.Length, Flags);
    }
}
