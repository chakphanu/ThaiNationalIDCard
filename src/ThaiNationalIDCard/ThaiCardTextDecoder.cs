using System;
using System.Text;

namespace ThaiNationalIDCard
{
    /// <summary>Pure TIS-620 decoding helper. No PC/SC types, no I/O.</summary>
    internal static class ThaiCardTextDecoder
    {
        // Same encoding name the legacy code used (resolves to the TIS-620 / windows-874 family).
        private const string Tis620Name = "TIS-620";
        private static readonly Encoding Tis620;

        static ThaiCardTextDecoder()
        {
            // Runs exactly once per process (type initializer). Needed on netstandard2.0 and harmless on net8.0.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Tis620 = Encoding.GetEncoding(Tis620Name);
        }

        /// <summary>
        /// Decodes TIS-620 bytes directly to a .NET string and trims NUL/space padding at the end.
        /// When <paramref name="hasStatusWord"/> is true and the data ends with SW 0x90 0x00,
        /// those two bytes are removed before decoding.
        /// </summary>
        public static string DecodeTis620(byte[] data, bool hasStatusWord)
        {
            // ArgumentNullException.ThrowIfNull does not exist on netstandard2.0.
#pragma warning disable CA1510
            if (data == null)
                throw new ArgumentNullException(nameof(data));
#pragma warning restore CA1510

            int length = data.Length;
            if (hasStatusWord
                && length >= 2
                && data[length - 2] == CardConstants.Sw1Normal
                && data[length - 1] == CardConstants.Sw2Normal)
            {
                length -= 2;
            }

            return Tis620.GetString(data, 0, length).TrimEnd('\0', ' ');
        }
    }
}
