using System;
using System.Globalization;

namespace ThaiNationalIDCard
{
    /// <summary>Command family of a trace entry. The command bytes themselves are not recorded.</summary>
    public enum TraceCommandKind
    {
        /// <summary>A command outside the families below. The guard refuses these.</summary>
        Other = 0,

        /// <summary>SELECT of an applet.</summary>
        Select,

        /// <summary>READ of a byte range.</summary>
        Read,

        /// <summary>GET RESPONSE.</summary>
        GetResponse,

        /// <summary>The data command of an applet.</summary>
        AppletData,

        /// <summary>Not a command: a workflow decision, see <see cref="TraceEvent"/>.</summary>
        Event,
    }

    /// <summary>A workflow decision worth seeing next to the commands. Holds no card data.</summary>
    public enum TraceEvent
    {
        /// <summary>This entry is a command, not a decision.</summary>
        None = 0,

        /// <summary>The connection to the reader was made.</summary>
        Connected,

        /// <summary>The transaction was opened.</summary>
        TransactionOpened,

        /// <summary>The transaction was closed.</summary>
        TransactionClosed,

        /// <summary>Closing failed. It hides nothing: the read result or the original error still stands.</summary>
        TransactionCloseFailed,

        /// <summary>The attempt ended in a retryable failure. Everything read so far was dropped.</summary>
        Retry,

        /// <summary>The ATR started with 3B 67, so the P2 order was reversed. It says nothing else about the card.</summary>
        AtrP2RuleUsed,

        /// <summary>The CID gate fixed the GET RESPONSE P2 for the rest of the read.</summary>
        P2Locked,

        /// <summary>The single P2 fallback of this read was spent. GetResponseP2 holds the new value.</summary>
        P2Fallback,

        /// <summary>The CID had digits with a bad checksum, so it is read once more.</summary>
        ChecksumRetry,

        /// <summary>A CID retry that spent no P2 budget, because no GET RESPONSE was sent so far.</summary>
        NoGetResponseRetry,

        /// <summary>The read entered the MOI applet.</summary>
        EnteringMoi,

        /// <summary>The read entered the NHSO applet.</summary>
        EnteringNhso,

        /// <summary>The read entered the ADM applet.</summary>
        EnteringAdm,

        /// <summary>A connection failure stopped the private stage. The public fields stand.</summary>
        PrivateStageStopped,
    }

    /// <summary>Things worth knowing about one exchange. Numbers only, safe to log.</summary>
    [Flags]
    public enum ExchangeNotes
    {
        /// <summary>Nothing to report.</summary>
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

    /// <summary>Result of checking one command against the guard. Holds no command bytes.</summary>
    public enum CommandVerdict
    {
        /// <summary>The command reached the channel.</summary>
        Allowed = 0,

        /// <summary>The command bytes had no valid form.</summary>
        Malformed,

        /// <summary>PIN, fingerprint or write family. No policy can allow these.</summary>
        AlwaysForbidden,

        /// <summary>The command is not on the allow list of the read mode.</summary>
        NotAllowlisted,

        /// <summary>The applet is not allowed in the read mode.</summary>
        AppletNotAllowed,

        /// <summary>A READ came with no applet selected.</summary>
        NoAppletSelected,

        /// <summary>A READ reached outside the ranges the profile declares.</summary>
        OutsideDeclaredRange,

        /// <summary>A GET RESPONSE came with no response pending.</summary>
        NoPendingResponse,
    }

    /// <summary>
    /// What one transmit attempt did. Fields are enums and int only, so the type cannot hold response bytes or
    /// text; a reflection test keeps it that way. Int is used for status words and offsets instead of byte for
    /// the same reason.
    /// </summary>
    public readonly struct TraceEntry
    {
        /// <summary>Marks an int field with no value, such as the offset of a command that is not a READ.</summary>
        public const int NoValue = -1;

        internal TraceEntry(TraceCommandKind kind, int offset, int requestedLength, int receivedLength, int statusWord,
            int round, int getResponseP2, ExchangeNotes flags, CommandVerdict verdict, CardFailureKind failure,
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
        internal static TraceEntry ForEvent(TraceEvent traceEvent, int getResponseP2 = NoValue, CardFailureKind failure = CardFailureKind.None) =>
            new TraceEntry(TraceCommandKind.Event, NoValue, NoValue, NoValue, NoValue, NoValue, getResponseP2,
                ExchangeNotes.None, CommandVerdict.Allowed, failure, traceEvent);

        /// <summary>Command family, or <see cref="TraceCommandKind.Event"/> for a workflow decision.</summary>
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

        /// <summary>Notes on the exchange this transmit belongs to.</summary>
        public ExchangeNotes Flags { get; }

        /// <summary>Guard result. Anything but Allowed means the command never reached the channel.</summary>
        public CommandVerdict Verdict { get; }

        /// <summary>How the transmit failed, if it did.</summary>
        public CardFailureKind Failure { get; }

        /// <summary>What the workflow decided, when <see cref="Kind"/> is <see cref="TraceCommandKind.Event"/>.</summary>
        public TraceEvent Event { get; }

        /// <summary>Numbers and enum names only.</summary>
        /// <returns>One line describing this entry.</returns>
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} offset={1} requested={2} received={3} sw={4} round={5} p2={6} flags={7} verdict={8} failure={9} event={10}",
            Kind, Offset, RequestedLength, ReceivedLength, StatusWord, Round, GetResponseP2, Flags, Verdict, Failure, Event);
    }
}
