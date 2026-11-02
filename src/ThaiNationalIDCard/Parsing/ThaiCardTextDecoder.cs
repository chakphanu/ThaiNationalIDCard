using System;
using System.Text;

namespace ThaiNationalIDCard.Parsing
{
    /// <summary>Pure TIS-620 decoding helper. No PC/SC types, no I/O. Callers slice by position before decoding.</summary>
    internal static class ThaiCardTextDecoder
    {
        // Resolves to code page 874, which agrees with TIS-620 on valid Thai text.
        private const string Tis620Name = "TIS-620";
        private static readonly Encoding Tis620;

        static ThaiCardTextDecoder()
        {
            // Runs exactly once per process (type initializer). Needed on netstandard2.0 and harmless on net8.0.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Tis620 = Encoding.GetEncoding(Tis620Name);
        }

        /// <summary>
        /// Decodes TIS-620 bytes directly to a .NET string and trims NUL and space padding at the end. The protocol layer
        /// has stripped the status word by now, so 90 00 at the end of the input is data here.
        /// </summary>
        public static string DecodeTis620(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return Tis620.GetString(data).TrimEnd('\0', ' ');
        }
    }
}
