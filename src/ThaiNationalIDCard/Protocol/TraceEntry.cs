using System.Globalization;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard.Protocol
{
    /// <summary>Command family of a trace entry. The command bytes themselves are not recorded.</summary>
    internal enum TraceCommandKind
    {
        Other = 0,
        Select,
        Read,
        GetResponse,
        AppletData,
    }

    /// <summary>
    /// What one transmit attempt did. Fields are enums and int only, so the type cannot hold response bytes or
    /// text; a reflection test keeps it that way. Int is used for status words and offsets instead of byte for
    /// the same reason.
    /// </summary>
    internal readonly struct TraceEntry
    {
        /// <summary>Marks an int field with no value, such as the offset of a command that is not a READ.</summary>
        public const int NoValue = -1;

        public TraceEntry(TraceCommandKind kind, int offset, int requestedLength, int receivedLength, int statusWord,
            int round, int getResponseP2, ExchangeFlags flags, CommandVerdict verdict, CardFailureKind failure)
        {
            Kind = kind;
            Offset = offset;
            RequestedLength = requestedLength;
            ReceivedLength = receivedLength;
            StatusWord = statusWord;
            Round = round;
            GetResponseP2 = getResponseP2;
            Flags = flags;
            Verdict = verdict;
            Failure = failure;
        }

        public TraceCommandKind Kind { get; }

        /// <summary>Card offset of a READ, else <see cref="NoValue"/>.</summary>
        public int Offset { get; }

        /// <summary>Bytes the command asked for, else <see cref="NoValue"/>.</summary>
        public int RequestedLength { get; }

        /// <summary>Data bytes received, without SW1 and SW2. <see cref="NoValue"/> when nothing came back.</summary>
        public int ReceivedLength { get; }

        /// <summary>SW1 and SW2 as one number, else <see cref="NoValue"/>.</summary>
        public int StatusWord { get; }

        /// <summary>0 for the first transmit of an exchange, then one more per follow-up.</summary>
        public int Round { get; }

        /// <summary>P2 of a GET RESPONSE, else <see cref="NoValue"/>.</summary>
        public int GetResponseP2 { get; }

        public ExchangeFlags Flags { get; }

        /// <summary>Guard result. Anything but Allowed means the command never reached the channel.</summary>
        public CommandVerdict Verdict { get; }

        public CardFailureKind Failure { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} offset={1} requested={2} received={3} sw={4} round={5} p2={6} flags={7} verdict={8} failure={9}",
            Kind, Offset, RequestedLength, ReceivedLength, StatusWord, Round, GetResponseP2, Flags, Verdict, Failure);
    }
}
