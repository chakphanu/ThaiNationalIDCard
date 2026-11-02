using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// Turns a reader name into the form diagnostics may show. pcsc-lite puts the USB serial in parentheses, and other
    /// shapes are caught by what a serial tends to look like. This errs toward removing too much: a lost model suffix
    /// costs little and a kept serial identifies a device. Real reader names have not been seen here yet.
    /// </summary>
    internal static class ReaderNames
    {
        private const string Unnamed = "(unnamed reader)";
        private const int SerialDigitRun = 5;
        private const int SerialHexLength = 8;
        private const int SerialMixedLength = 10;
        private const int SerialMixedDigits = 3;

        private static readonly char[] Space = { ' ' };

        private static readonly Regex Parenthesized = new Regex(@"\([^)]*(\)|$)", RegexOptions.CultureInvariant);

        private static readonly Regex Labelled = new Regex(
            @"\b(?:S/?N|SERIAL(?:\s*(?:NO|NUM(?:BER)?))?)\s*[:#=.]?\s*[^\s\]]*\d[^\s\]]*",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string RemoveSerial(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Unnamed;
            }

            string text = Parenthesized.Replace(ToPrintableAscii(name!), " ");
            text = Labelled.Replace(text, " ");

            string[] kept = text.Split(Space, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !LooksLikeSerial(token))
                .ToArray();

            return kept.Length == 0 ? Unnamed : string.Join(" ", kept);
        }

        private static string ToPrintableAscii(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(c >= ' ' && c <= '~' ? c : ' ');
            }

            return builder.ToString();
        }

        private static bool LooksLikeSerial(string token)
        {
            string core = token.Trim('[', ']', ',', ';', ':');
            int digits = core.Count(c => c >= '0' && c <= '9');
            if (digits == 0)
            {
                return false;
            }

            bool alphanumeric = core.All(IsAsciiLetterOrDigit);
            bool hex = core.All(IsHex);

            return LongestDigitRun(core) >= SerialDigitRun ||
                (hex && core.Length >= SerialHexLength) ||
                (alphanumeric && core.Length >= SerialMixedLength && digits >= SerialMixedDigits);
        }

        private static bool IsAsciiLetterOrDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        private static int LongestDigitRun(string text)
        {
            int longest = 0;
            int current = 0;
            foreach (char c in text)
            {
                current = c >= '0' && c <= '9' ? current + 1 : 0;
                longest = Math.Max(longest, current);
            }

            return longest;
        }
    }
}
