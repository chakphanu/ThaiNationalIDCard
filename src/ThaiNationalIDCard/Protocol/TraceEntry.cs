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

        /// <summary>Not a command: a workflow decision, see <see cref="TraceEvent"/>.</summary>
        Event,
    }

    /// <summary>A workflow decision worth seeing next to the commands. Holds no card data.</summary>
    internal enum TraceEvent
    {
        None = 0,
        Connected,
        TransactionOpened,
        TransactionClosed,

        /// <summary>Closing failed. It hides nothing: the read result or the original error still stands.</summary>
        TransactionCloseFailed,

        /// <summary>The attempt ended in a retryable failure. Everything read so far was dropped.</summary>
        Retry,

        /// <summary>The ATR started with 3B 67, so the P2 order was reversed. It says nothing else about the card.</summary>
        AtrP2RuleUsed,
        P2Locked,

        /// <summary>The single P2 fallback of this read was spent. GetResponseP2 holds the new value.</summary>
        P2Fallback,
        ChecksumRetry,

        /// <summary>A CID retry that spent no P2 budget, because no GET RESPONSE was sent so far.</summary>
        NoGetResponseRetry,
        EnteringMoi,
        EnteringNhso,
        EnteringAdm,
        PrivateStageStopped,
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
            int round, int getResponseP2, ExchangeFlags flags, CommandVerdict verdict, CardFailureKind failure,
            TraceEvent traceEvent = TraceEvent.None)
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
            Event = traceEvent;
        }

        /// <summary>An entry for a workflow decision instead of a transmit.</summary>
        public static TraceEntry ForEvent(TraceEvent traceEvent, int getResponseP2 = NoValue, CardFailureKind failure = CardFailureKind.None) =>
            new TraceEntry(TraceCommandKind.Event, NoValue, NoValue, NoValue, NoValue, NoValue, getResponseP2,
                ExchangeFlags.None, CommandVerdict.Allowed, failure, traceEvent);

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

        /// <summary>What the workflow decided, when <see cref="Kind"/> is <see cref="TraceCommandKind.Event"/>.</summary>
        public TraceEvent Event { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} offset={1} requested={2} received={3} sw={4} round={5} p2={6} flags={7} verdict={8} failure={9} event={10}",
            Kind, Offset, RequestedLength, ReceivedLength, StatusWord, Round, GetResponseP2, Flags, Verdict, Failure, Event);
    }
}
