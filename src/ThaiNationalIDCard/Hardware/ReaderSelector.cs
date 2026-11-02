using System;
using System.Collections.Generic;
using System.Linq;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>Picks a reader from a status snapshot. A named reader is never replaced by another one.</summary>
    internal static class ReaderSelector
    {
        public static string Select(IReadOnlyList<ReaderStatus> statuses, string? preferredReader)
        {
            if (statuses.Count == 0)
            {
                throw new ReaderNotFoundException();
            }

            if (string.IsNullOrEmpty(preferredReader))
            {
                foreach (ReaderStatus status in statuses)
                {
                    if (status.State == ReaderCardState.Present)
                    {
                        return status.Name;
                    }
                }

                throw new CardNotPresentException();
            }

            foreach (ReaderStatus status in statuses.Where(s => string.Equals(s.Name, preferredReader, StringComparison.Ordinal)))
            {
                if (status.State == ReaderCardState.Present)
                {
                    return status.Name;
                }

                throw new CardNotPresentException();
            }

            throw new ReaderNotFoundException();
        }
    }
}
