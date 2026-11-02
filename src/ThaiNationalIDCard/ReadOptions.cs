using System;
using System.Globalization;

namespace ThaiNationalIDCard
{
    /// <summary>What one read should do. The values are taken when the read starts, so later changes do not affect it.</summary>
    public sealed class ReadOptions
    {
        /// <summary>What to read. <see cref="CardReadMode.Public"/> by default.</summary>
        public CardReadMode Mode { get; set; } = CardReadMode.Public;

        /// <summary>Whether to read the photo as well. False by default, since it is by far the slowest part of a read.</summary>
        public bool IncludePhoto { get; set; }

        /// <summary>
        /// Told how far the photo read is. It is called on a thread pool thread of its own and in order, never on the thread
        /// holding the card transaction, so a slow handler cannot make the card time out. A report can arrive after the read
        /// finished, and a retry after a card reset starts over from the beginning. An exception from it is ignored.
        /// </summary>
        public IProgress<PhotoReadProgress>? PhotoProgress { get; set; }
    }

    /// <summary>How far a photo read is. Counts only, with no card data.</summary>
    public readonly struct PhotoReadProgress
    {
        /// <summary>Creates a progress value.</summary>
        /// <param name="bytesRead">Bytes read so far.</param>
        /// <param name="totalBytes">Bytes the photo range holds.</param>
        public PhotoReadProgress(int bytesRead, int totalBytes)
        {
            BytesRead = bytesRead;
            TotalBytes = totalBytes;
        }

        /// <summary>Bytes read so far.</summary>
        public int BytesRead { get; }

        /// <summary>Bytes the photo range holds. The image inside it is usually shorter.</summary>
        public int TotalBytes { get; }

        /// <summary>Counts only.</summary>
        /// <returns>The counts as text.</returns>
        public override string ToString() => BytesRead.ToString(CultureInfo.InvariantCulture) + "/" + TotalBytes.ToString(CultureInfo.InvariantCulture);
    }
}
