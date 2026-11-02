using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ThaiNationalIDCard.Workflow
{
    /// <summary>What one read asks for.</summary>
    internal sealed class CardReadRequest
    {
        /// <summary>A reader name, or null for the first reader holding a card.</summary>
        public string? ReaderName { get; set; }

        public CardReadMode Mode { get; set; } = CardReadMode.Public;

        public bool IncludePhoto { get; set; } = true;

        /// <summary>
        /// False clears each field buffer as soon as its validators finished and keeps none, as the trace tool needs.
        /// This is best effort: buffers below the workflow and in PC/SC are only released to the garbage collector.
        /// </summary>
        public bool RetainFieldData { get; set; } = true;

        /// <summary>Told bytes read and bytes total after each photo chunk. Runs on the read thread, so it must only queue.</summary>
        public Action<int, int>? PhotoProgress { get; set; }

        /// <summary>
        /// Called once the reader is known and before any connection, and held until the read ended. It may wait, since no
        /// transaction is open yet, and it has to respect the token.
        /// </summary>
        public Func<string, CancellationToken, IDisposable>? AcquireReader { get; set; }
    }

    /// <summary>One field: its outcome and, when the request retains data, its bytes. ToString never shows the bytes.</summary>
    internal sealed class CardFieldResult
    {
        public CardFieldResult(FieldDiagnostics info, byte[]? data)
        {
            Info = info;
            Data = data;
        }

        public FieldDiagnostics Info { get; }

#pragma warning disable CA1819 // The buffer is the result of the read and has a single owner.
        /// <summary>The bytes as the card sent them. Null when not retained or not read.</summary>
        public byte[]? Data { get; }
#pragma warning restore CA1819

        public override string ToString() => Info.ToString();
    }

    /// <summary>The fields of one read in table order, with the diagnostics. ToString never shows card data.</summary>
    internal sealed class CardReadResult
    {
        public CardReadResult(IReadOnlyList<CardFieldResult> fields, ReadDiagnostics diagnostics)
        {
            Fields = fields;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<CardFieldResult> Fields { get; }

        public ReadDiagnostics Diagnostics { get; }

        public CardFieldResult Get(CardFieldId id) => Fields.First(f => f.Info.Id == id);

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "CardReadResult(Fields={0}, {1})", Fields.Count, Diagnostics);
    }
}
