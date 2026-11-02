using System;
using System.Linq;

namespace ThaiNationalIDCard
{
    /// <summary>
    /// A date as the card holds it: eight digits, Buddhist era year first. Nothing here makes up a date. A part that is
    /// 00 or impossible is null, and <see cref="Raw"/> always keeps what the card said. There is no expiry check on purpose,
    /// because comparing dates alone is not known to be the whole rule. ToString never shows the date.
    /// </summary>
    public sealed class ThaiCardDate
    {
        private const int RawLength = 8;
        private const string LifetimeValue = "99999999";
        private const int MonthsInYear = 12;
        private const int MostDaysInMonth = 31;

        /// <summary>The only place that knows the card counts years in the Buddhist era.</summary>
        private const int BuddhistEraOffset = 543;

        /// <summary>Parses a raw date. This never throws: what does not parse is null and stays in <see cref="Raw"/>.</summary>
        /// <param name="raw">The raw text, normally eight digits.</param>
        public ThaiCardDate(string? raw = null)
        {
            Raw = raw ?? string.Empty;

            if (Raw.Length != RawLength || !Raw.All(c => c >= '0' && c <= '9'))
            {
                return;
            }

            if (Raw == LifetimeValue)
            {
                IsLifetime = true;
                return;
            }

            int buddhistYear = Number(0, 4);
            int month = Number(4, 2);
            int day = Number(6, 2);

            int gregorianYear = buddhistYear - BuddhistEraOffset;
            Year = gregorianYear >= 1 ? gregorianYear : (int?)null;
            Month = month >= 1 && month <= MonthsInYear ? month : (int?)null;
            Day = day >= 1 && day <= MostDaysInMonth ? day : (int?)null;

            // 00 means unknown and nothing more is wrong with it. Any other value that gave null is impossible.
            bool impossibleMonth = month != 0 && Month == null;
            bool impossibleDay = day != 0 && Day == null;
            bool noSuchDay = Year != null && Month != null && Day != null && Day > DateTime.DaysInMonth(Year.Value, Month.Value);
            IsWellFormed = Year != null && !impossibleMonth && !impossibleDay && !noSuchDay;
        }

        /// <summary>The text the card holds, trimmed of padding at the end.</summary>
        public string Raw { get; }

        /// <summary>The Gregorian year. Null when the card has 0000, or when the raw value does not parse.</summary>
        public int? Year { get; }

        /// <summary>The month, 1 to 12. Null when the card has 00, or when the raw value does not parse.</summary>
        public int? Month { get; }

        /// <summary>The day, 1 to 31. Null when the card has 00, or when the raw value does not parse.</summary>
        public int? Day { get; }

        /// <summary>
        /// True for the raw value 99999999, which public sources describe as a card with no expiry. It only means something
        /// on an expiry date. No year, month or day is given then.
        /// </summary>
        public bool IsLifetime { get; }

        /// <summary>
        /// The date to count an age from, with an unknown month or day replaced as Thai Civil Code section 16 says: the first
        /// of the month when only the day is unknown, the first of the year when the month is unknown. It only means something
        /// on a birth date and is not the birth date itself. Null when the year is unknown or a part is impossible.
        /// </summary>
        public DateTime? AgeReferenceDate
        {
            get
            {
                if (!IsWellFormed)
                {
                    return null;
                }

                return Month == null ? new DateTime(Year!.Value, 1, 1) : new DateTime(Year!.Value, Month.Value, Day ?? 1);
            }
        }

        /// <summary>True when the date has the shape of a date: eight digits, a usable year, and no impossible part.</summary>
        internal bool IsWellFormed { get; }

        /// <summary>The date, only when year, month and day are all known and such a day exists.</summary>
        /// <returns>The date with an unspecified kind, or null. Nothing is filled in.</returns>
        public DateTime? ToDateTime() =>
            IsWellFormed && Month != null && Day != null ? new DateTime(Year!.Value, Month.Value, Day.Value) : (DateTime?)null;

#if NET8_0_OR_GREATER
        /// <summary>The date, only when year, month and day are all known and such a day exists.</summary>
        /// <returns>The date, or null. Nothing is filled in.</returns>
        public DateOnly? ToDateOnly() =>
            IsWellFormed && Month != null && Day != null ? new DateOnly(Year!.Value, Month.Value, Day.Value) : (DateOnly?)null;
#endif

        /// <summary>Only called once every character of <see cref="Raw"/> is known to be an ASCII digit.</summary>
        private int Number(int start, int length)
        {
            int value = 0;
            for (int i = start; i < start + length; i++)
            {
                value = (value * 10) + (Raw[i] - '0');
            }

            return value;
        }

        /// <summary>Holds no card data, so logging this cannot leak the date.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(ThaiCardDate) + "(data hidden)";
    }
}
