using System;
using System.Collections.Generic;
using System.Globalization;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;

namespace ThaiNationalIDCard
{
    // TODO(step 8): this file becomes public with the public API.

    /// <summary>What a read asks for. Public covers what is printed on the card, private adds raw ranges and applets.</summary>
    internal enum CardReadMode
    {
        Public = 0,
        PublicAndPrivate,
    }

    /// <summary>How far a field can be trusted. Nothing but the state of the read and the validators decide it.</summary>
    internal enum FieldQuality
    {
        NotRead = 0,
        Ok,
        Suspect,
        Failed,
    }

    /// <summary>What happened while reading a field. Flags, so several can apply.</summary>
    [Flags]
    internal enum FieldIssue
    {
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
    internal enum ValidationFailures
    {
        None = 0,
        NotThirteenDigits = 1,
        ChecksumMismatch = 2,
        ThaiNameShape = 4,
        EnglishNameShape = 8,
        BirthDateNotDigits = 16,
        SexCode = 32,
        TextEncoding = 64,
        AddressShape = 128,
        IssueDateNotDigits = 256,
        ExpiryDateNotDigits = 512,
        JpegStart = 1024,
        JpegEnd = 2048,
    }

    /// <summary>The outcome of one field as numbers and enums, so it cannot hold card data.</summary>
    internal readonly struct FieldDiagnostics
    {
        public FieldDiagnostics(CardFieldId id, FieldQuality quality, FieldIssue issues, ValidationFailures validation,
            int statusWord, int requestedLength, int receivedLength, ExchangeFlags flags, CardFailureKind failure)
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

        public CardFieldId Id { get; }

        public FieldQuality Quality { get; }

        public FieldIssue Issues { get; }

        public ValidationFailures Validation { get; }

        /// <summary>Last status word of the field, else <see cref="TraceEntry.NoValue"/>.</summary>
        public int StatusWord { get; }

        /// <summary>Bytes the table asks for. <see cref="TraceEntry.NoValue"/> for a command without a fixed length.</summary>
        public int RequestedLength { get; }

        public int ReceivedLength { get; }

        public ExchangeFlags Flags { get; }

        public CardFailureKind Failure { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} quality={1} issues={2} validation={3} sw={4} requested={5} received={6} flags={7} failure={8}",
            Id, Quality, Issues, Validation, StatusWord, RequestedLength, ReceivedLength, Flags, Failure);
    }

    /// <summary>A reader as diagnostics show it. The name never holds a serial, see <see cref="ReaderNames"/>.</summary>
    internal readonly struct ReaderDiagnostics
    {
        public ReaderDiagnostics(string displayName, ReaderCardState state)
        {
            DisplayName = displayName;
            State = state;
        }

        public string DisplayName { get; }

        public ReaderCardState State { get; }
    }

    /// <summary>
    /// Everything known about one read, with no card content: reader name without serial, protocol, ATR, trace, P2 and
    /// field outcomes. It travels on results and exceptions and no static state keeps it.
    /// </summary>
    internal sealed class ReadDiagnostics
    {
        public ReadDiagnostics(CardReadMode mode, string? readerName, IReadOnlyList<ReaderDiagnostics> readers,
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

        public CardReadMode Mode { get; }

        /// <summary>The chosen reader without any serial, null before one was chosen.</summary>
        public string? ReaderName { get; }

        public IReadOnlyList<ReaderDiagnostics> Readers { get; }

        public CardTransportProtocol? Protocol { get; }

        public string AtrHex { get; }

        public IReadOnlyList<TraceEntry> Trace { get; }

        /// <summary>P2 of the last GET RESPONSE sent. Null when none was sent, since then P2 played no part.</summary>
        public int? P2Used { get; }

        public IReadOnlyList<FieldDiagnostics> Fields { get; }

        public CardFailureKind Failure { get; }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "ReadDiagnostics(Mode={0}, Trace={1}, Fields={2}, Failure={3})", Mode, Trace.Count, Fields.Count, Failure);
    }
}
