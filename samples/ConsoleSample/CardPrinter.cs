using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ThaiNationalIDCard.ConsoleSample
{
    /// <summary>Prints each field with its quality. A value is what the card gave, and the quality says how far to trust it.</summary>
    public static class CardPrinter
    {
        private const int CitizenIdLength = 13;
        private const int VisibleDigits = 4;
        private const string Missing = "(none)";

        public static void Print(TextWriter output, ThaiIdCardData card, bool showCitizenId)
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(card);

            ThaiIdCardQuality quality = card.Quality;
            string citizenId = showCitizenId ? card.CitizenId ?? Missing : MaskCitizenId(card.CitizenId);

            Line(output, "Citizen ID", citizenId, quality.CitizenId);
            output.WriteLine("  Check digit fits: " + (card.IsValidCitizenId ? "yes" : "no"));
            Line(output, "Thai name", Name(card.ThaiName), quality.ThaiName);
            Line(output, "English name", Name(card.EnglishName), quality.EnglishName);
            Line(output, "Birth date", Date(card.BirthDate), quality.BirthDate);
            Line(output, "Sex", card.Sex.ToString(), quality.Sex);
            Line(output, "Address", card.Address.Full, quality.Address);
            Line(output, "Issue date", Date(card.IssueDate), quality.IssueDate);
            Line(output, "Expiry date", Date(card.ExpiryDate), quality.ExpiryDate);
            Line(output, "Issuer", card.Issuer ?? Missing, quality.Issuer);
            output.WriteLine();
            output.WriteLine("This is what the card returned. It does not prove who the holder is.");
        }

        /// <summary>Keeps the last digits of a full-length ID so a person can tell cards apart. Anything else is masked whole.</summary>
        public static string MaskCitizenId(string? citizenId)
        {
            if (string.IsNullOrEmpty(citizenId))
            {
                return Missing;
            }

            int hidden = citizenId.Length == CitizenIdLength ? citizenId.Length - VisibleDigits : citizenId.Length;
            return new string('*', hidden) + citizenId[hidden..];
        }

        private static void Line(TextWriter output, string label, string value, FieldQuality quality) =>
            output.WriteLine(label + ": " + (value.Length == 0 ? Missing : value) + " [" + quality + "]");

        private static string Name(ThaiIdCardName name) =>
            string.Join(" ", new[] { name.Prefix, name.FirstName, name.MiddleName, name.LastName }.Where(part => part.Length > 0));

        /// <summary>A card can hold 00 for an unknown month or day, and nothing is filled in here for those.</summary>
        private static string Date(ThaiCardDate date)
        {
            if (date.IsLifetime)
            {
                return "no expiry";
            }

            DateTime? whole = date.ToDateTime();
            if (whole != null)
            {
                return whole.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return date.Year == null ? "unknown (card holds: " + date.Raw + ")" : "partly unknown (card holds: " + date.Raw + ")";
        }
    }
}
