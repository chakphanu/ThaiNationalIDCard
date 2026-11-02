using System;
using System.Collections.Generic;
using System.Linq;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// Picks a reader from a status snapshot. A named reader is never replaced by another one. A name matches exactly
    /// or as <see cref="ReaderNames.RemoveSerial"/> shows it, because the shown form is the only one diagnostics print.
    /// </summary>
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

            IEnumerable<ReaderStatus> named = statuses.Where(s => string.Equals(s.Name, preferredReader, StringComparison.Ordinal))
                .Concat(statuses.Where(s => string.Equals(ReaderNames.RemoveSerial(s.Name), preferredReader, StringComparison.Ordinal)));

            foreach (ReaderStatus status in named)
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
