using System;
using System.Collections.Generic;
using System.Globalization;

namespace ThaiNationalIDCard
{
    /// <summary>What a read asks for. Public covers what is printed on the card, private adds raw ranges and applets.</summary>
    public enum CardReadMode
    {
        /// <summary>What is printed on the card. The default.</summary>
        Public = 0,

        /// <summary>
        /// Also the private areas, given in <see cref="ThaiIdCardData.Private"/>. The caller is responsible for the holder's
        /// consent and for the law that applies. No mode reads PIN, fingerprint or chip data, or writes to the card.
        /// </summary>
        PublicAndPrivate,
    }

    /// <summary>How far a field can be trusted. Nothing but the state of the read and the validators decide it.</summary>
    public enum FieldQuality
    {
        /// <summary>The field was not asked for, or the read never reached it.</summary>
        NotRead = 0,

        /// <summary>The field was read and has the expected shape. This does not prove the value is genuine.</summary>
        Ok,

        /// <summary>The field was read but something about it is off. The value is still given as it came.</summary>
        Suspect,

        /// <summary>The card gave no usable bytes for this field.</summary>
        Failed,
    }

    /// <summary>What happened while reading a field. Flags, so several can apply.</summary>
    [Flags]
    public enum FieldIssue
    {
        /// <summary>Nothing to report.</summary>
        None = 0,

        /// <summary>All asked bytes arrived and the card offered more, which stays on the card. Not a failure.</summary>
        MoreOffered = 1,

        /// <summary>The card finished with 90 00 and fewer bytes than asked.</summary>
        Truncated = 2,

        /// <summary>A status word gave no usable data. <see cref="FieldDiagnostics.StatusWord"/> holds it.</summary>
        BadStatus = 4,

        /// <summary>The value comes from a read after a checksum mismatch.</summary>
        ChecksumRetried = 8,

        /// <summary>SELECT of the applet holding this field was refused.</summary>
        AppletUnavailable = 16,

        /// <summary>The connection failed before this field was finished. <see cref="FieldDiagnostics.Failure"/> tells how.</summary>
        TransportFailed = 32,
    }

    /// <summary>Which validator checks failed. Names say what shape was wrong and never what the value is.</summary>
    [Flags]
    public enum ValidationFailures
    {
        /// <summary>Every check passed.</summary>
        None = 0,

        /// <summary>The citizen ID is not 13 ASCII digits.</summary>
        NotThirteenDigits = 1,

        /// <summary>The citizen ID has 13 digits and the last one does not match the formula.</summary>
        ChecksumMismatch = 2,

        /// <summary>The Thai name slot does not have the expected separators.</summary>
        ThaiNameShape = 4,

        /// <summary>The English name slot does not have the expected separators.</summary>
        EnglishNameShape = 8,

        /// <summary>The birth date is not 8 digits.</summary>
        BirthDateNotDigits = 16,

        /// <summary>The sex byte is neither of the two known codes.</summary>
        SexCode = 32,

        /// <summary>A text field holds bytes outside TIS-620.</summary>
        TextEncoding = 64,

        /// <summary>The address does not have the expected number of parts.</summary>
        AddressShape = 128,

        /// <summary>The issue date is not 8 digits.</summary>
        IssueDateNotDigits = 256,

        /// <summary>The expiry date is not 8 digits.</summary>
        ExpiryDateNotDigits = 512,

        /// <summary>The photo does not start with a JPEG start marker.</summary>
        JpegStart = 1024,

        /// <summary>Walking the JPEG markers did not reach an end of image.</summary>
        JpegEnd = 2048,

        /// <summary>The ADM applet answer does not have the shape public code suggests. That shape is itself unconfirmed.</summary>
        AppletDataShape = 4096,
    }

    /// <summary>
    /// Every field the library can ask for. Fields whose meaning public sources disagree on are named by position,
    /// so no name here claims more than the evidence does.
    /// </summary>
    public enum CardFieldId
    {
        /// <summary>The citizen ID.</summary>
        CitizenId,

        /// <summary>Thai name, English name, birth date and sex, read as one range.</summary>
        PersonInfo,

        /// <summary>The address.</summary>
        Address,

        /// <summary>Issue date and expiry date, read as one range.</summary>
        IssueExpire,

        /// <summary>The issuer.</summary>
        Issuer,

        /// <summary>The photo.</summary>
        Photo,

        /// <summary>MOI applet, offset 0000. Meaning unconfirmed.</summary>
        MoiRaw0000,

        /// <summary>MOI applet, offset 00E2. Meaning unconfirmed.</summary>
        MoiRaw00E2,

        /// <summary>MOI applet, offset 0177. Meaning unconfirmed.</summary>
        MoiRaw0177,

        /// <summary>MOI applet, offset 1619. Meaning unconfirmed.</summary>
        MoiRaw1619,

        /// <summary>NHSO applet, offset 0004. Meaning unconfirmed.</summary>
        NhsoRaw0004,

        /// <summary>NHSO applet, offset 0040. Meaning unconfirmed.</summary>
        NhsoRaw0040,

        /// <summary>NHSO applet, offset 00A4. Meaning unconfirmed.</summary>
        NhsoRaw00A4,

        /// <summary>NHSO applet, offset 00F4. Meaning unconfirmed.</summary>
        NhsoRaw00F4,

        /// <summary>NHSO applet, offset 0144. Meaning unconfirmed.</summary>
        NhsoRaw0144,

        /// <summary>NHSO applet, offset 0145. Meaning unconfirmed.</summary>
        NhsoRaw0145,

        /// <summary>NHSO applet, offset 014D. Meaning unconfirmed.</summary>
        NhsoRaw014D,

        /// <summary>NHSO applet, offset 0155. Meaning unconfirmed.</summary>
        NhsoRaw0155,

        /// <summary>NHSO applet, offset 015D. Meaning unconfirmed.</summary>
        NhsoRaw015D,

        /// <summary>The answer of the ADM applet data command. Meaning unconfirmed.</summary>
        AdmAppletData,
    }

    /// <summary>Failure categories. The library reports failures only through these.</summary>
    public enum CardFailureKind
    {
        /// <summary>No failure.</summary>
        None = 0,

        /// <summary>The PC/SC service or its native library is not available.</summary>
        ServiceUnavailable,

        /// <summary>No matching reader exists.</summary>
        ReaderNotFound,

        /// <summary>The reader holds no usable card.</summary>
        CardNotPresent,

        /// <summary>The card was removed.</summary>
        CardRemoved,

        /// <summary>The card was reset, for example by another application.</summary>
        CardReset,

        /// <summary>Another application holds the card.</summary>
        SharingViolation,

        /// <summary>The operation timed out.</summary>
        Timeout,

        /// <summary>The connection was gone when a command was sent.</summary>
        NotConnected,

        /// <summary>The reader cannot talk to this card.</summary>
        UnsupportedCard,

        /// <summary>The answer was too short to hold a status word.</summary>
        MalformedResponse,

        /// <summary>Any other communication failure.</summary>
        Communication,

        /// <summary>SELECT of the MOI applet was refused.</summary>
        NoApplet,

        /// <summary>The CID gate did not get a citizen ID with any allowed attempt.</summary>
        UnknownReadBehaviour,
    }

    /// <summary>Card state of one reader.</summary>
    public enum ReaderCardState
    {
        /// <summary>The state could not be told.</summary>
        Unknown = 0,

        /// <summary>No card in the reader.</summary>
        Empty,

        /// <summary>A card is in the reader.</summary>
        Present,

        /// <summary>A card is in the reader but did not answer the reset.</summary>
        Mute,

        /// <summary>The reader cannot be used right now.</summary>
        Unavailable,
    }

    /// <summary>Transport protocol of a connection.</summary>
    public enum CardTransportProtocol
    {
        /// <summary>T=0.</summary>
        T0,

        /// <summary>T=1.</summary>
        T1,

        /// <summary>Raw.</summary>
        Raw,
    }

    /// <summary>The outcome of one field as numbers and enums, so it cannot hold card data.</summary>
    public readonly struct FieldDiagnostics
    {
        internal FieldDiagnostics(CardFieldId id, FieldQuality quality, FieldIssue issues, ValidationFailures validation,
            int statusWord, int requestedLength, int receivedLength, ExchangeNotes flags, CardFailureKind failure)
        {
            Id = id;
            Quality = quality;
            Issues = issues;
            Validation = validation;
            StatusWord = statusWord;
            RequestedLength = requestedLength;
            ReceivedLength = receivedLength;
            Flags = flags;
            Failure = failure;
        }

        /// <summary>Which field this is.</summary>
        public CardFieldId Id { get; }

        /// <summary>Quality of the read of this range, before it is split into model fields.</summary>
        public FieldQuality Quality { get; }

        /// <summary>What happened while reading.</summary>
        public FieldIssue Issues { get; }

        /// <summary>Which checks failed.</summary>
        public ValidationFailures Validation { get; }

        /// <summary>Last status word of the field, else <see cref="TraceEntry.NoValue"/>.</summary>
        public int StatusWord { get; }

        /// <summary>Bytes the table asks for. <see cref="TraceEntry.NoValue"/> for a command without a fixed length.</summary>
        public int RequestedLength { get; }

        /// <summary>Bytes received.</summary>
        public int ReceivedLength { get; }

        /// <summary>Notes on the exchanges of this field.</summary>
        public ExchangeNotes Flags { get; }

        /// <summary>The connection failure that stopped this field, if any.</summary>
        public CardFailureKind Failure { get; }

        /// <summary>Numbers and enum names only.</summary>
        /// <returns>One line describing this field.</returns>
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} quality={1} issues={2} validation={3} sw={4} requested={5} received={6} flags={7} failure={8}",
            Id, Quality, Issues, Validation, StatusWord, RequestedLength, ReceivedLength, Flags, Failure);
    }

    /// <summary>A reader as diagnostics show it. The display name never holds a serial.</summary>
    public readonly struct ReaderDiagnostics
    {
        internal ReaderDiagnostics(string displayName, ReaderCardState state)
        {
            DisplayName = displayName;
            State = state;
        }

        /// <summary>The reader name with anything that looks like a serial removed.</summary>
        public string DisplayName { get; }

        /// <summary>Card state when the read started.</summary>
        public ReaderCardState State { get; }
    }

    /// <summary>
    /// Everything known about one read, with no card content: reader name without serial, protocol, ATR, trace, P2 and
    /// field outcomes. It travels on results and exceptions and no static state keeps it.
    /// </summary>
    public sealed class ReadDiagnostics
    {
        internal ReadDiagnostics(CardReadMode mode, string? readerName, IReadOnlyList<ReaderDiagnostics> readers,
            CardTransportProtocol? protocol, string atrHex, IReadOnlyList<TraceEntry> trace, int? p2Used,
            IReadOnlyList<FieldDiagnostics> fields, CardFailureKind failure)
        {
            Mode = mode;
            ReaderName = readerName;
            Readers = readers;
            Protocol = protocol;
            AtrHex = atrHex;
            Trace = trace;
            P2Used = p2Used;
            Fields = fields;
            Failure = failure;
        }

        /// <summary>The mode of the read.</summary>
        public CardReadMode Mode { get; }

        /// <summary>The chosen reader without any serial, null before one was chosen.</summary>
        public string? ReaderName { get; }

        /// <summary>Every reader known when the read started.</summary>
        public IReadOnlyList<ReaderDiagnostics> Readers { get; }

        /// <summary>The protocol of the connection, null before one was made.</summary>
        public CardTransportProtocol? Protocol { get; }

        /// <summary>The ATR as hex text, empty before a connection was made.</summary>
        public string AtrHex { get; }

        /// <summary>Every command and workflow decision, in order.</summary>
        public IReadOnlyList<TraceEntry> Trace { get; }

        /// <summary>P2 of the last GET RESPONSE sent. Null when none was sent, since then P2 played no part.</summary>
        public int? P2Used { get; }

        /// <summary>Outcome per card range.</summary>
        public IReadOnlyList<FieldDiagnostics> Fields { get; }

        /// <summary>How the read failed, or None.</summary>
        public CardFailureKind Failure { get; }

        /// <summary>Counts and enum names only.</summary>
        /// <returns>One line describing this read.</returns>
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "ReadDiagnostics(Mode={0}, Trace={1}, Fields={2}, Failure={3})", Mode, Trace.Count, Fields.Count, Failure);

        /// <summary>For data no read produced, such as a consumer's mock.</summary>
        internal static ReadDiagnostics Empty() => new ReadDiagnostics(CardReadMode.Public, null, Array.Empty<ReaderDiagnostics>(), null,
            string.Empty, Array.Empty<TraceEntry>(), null, Array.Empty<FieldDiagnostics>(), CardFailureKind.None);
    }
}
