using System.Linq;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Handmade JPEG byte structures for the scanner. They have valid markers and no real image: no decoder could show a
    /// picture from these, and no photo of anyone was involved.
    /// </summary>
    internal static class MiniJpeg
    {
        public static readonly byte[] Soi = { 0xFF, 0xD8 };
        public static readonly byte[] Eoi = { 0xFF, 0xD9 };

        /// <summary>SOS with a 6 byte header. Entropy coded data follows it.</summary>
        public static readonly byte[] Sos = { 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 };

        public static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        /// <summary>A marker with its big endian length, which counts itself.</summary>
        public static byte[] Segment(byte marker, params byte[] payload)
        {
            int length = payload.Length + 2;
            return new byte[] { 0xFF, marker, (byte)(length >> 8), (byte)length }.Concat(payload).ToArray();
        }

        public static byte[] Build(params byte[] entropy) => Join(Soi, Sos, entropy, Eoi);
    }
}
