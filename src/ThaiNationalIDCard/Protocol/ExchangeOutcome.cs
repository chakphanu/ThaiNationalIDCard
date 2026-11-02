using System;

namespace ThaiNationalIDCard.Protocol
{
    /// <summary>What a READ result means for the field it belongs to.</summary>
    internal enum ReadOutcomeKind
    {
        /// <summary>No usable bytes: a bad status word, a stopped exchange, or 90 00 with nothing.</summary>
        Failed = 0,

        /// <summary>90 00 with every requested byte.</summary>
        Complete,

        /// <summary>
        /// Every requested byte arrived with 61 xx: the card holds more, and it stays there because the command did not
        /// ask for it. The status word is not 90 00, yet the field is good.
        /// </summary>
        CompleteMoreOffered,

        /// <summary>90 00 with some of the requested bytes.</summary>
        Truncated,
    }

    /// <summary>The single place that reads the status and data combinations <see cref="ApduExchange"/> returns.</summary>
    internal static class ExchangeOutcome
    {
        public static ReadOutcomeKind OfRead(ApduResponse response, int requestedLength)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            int length = response.Data.Length;

            if (response.IsSuccess)
            {
                if (length == 0)
                {
                    return ReadOutcomeKind.Failed;
                }

                return length < requestedLength ? ReadOutcomeKind.Truncated : ReadOutcomeKind.Complete;
            }

            // A stopped exchange also ends in 61 xx. Its data is empty today, and the flags keep that from mattering.
            bool stopped = (response.Flags & (ExchangeNotes.RoundLimitReached | ExchangeNotes.FollowUpRejected)) != 0;
            return response.Sw1 == 0x61 && !stopped && length == requestedLength
                ? ReadOutcomeKind.CompleteMoreOffered
                : ReadOutcomeKind.Failed;
        }
    }
}
