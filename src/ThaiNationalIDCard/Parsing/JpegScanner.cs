using System;

namespace ThaiNationalIDCard.Parsing
{
    /// <summary>What walking the markers of a JPEG found.</summary>
    internal enum JpegScanStatus
    {
        Found,

        /// <summary>The bytes do not start with FF D8. Nothing searches further in, so bytes from a wrong offset are not taken for a photo.</summary>
        NoStart,

        /// <summary>The walk did not reach an end of image after a scan.</summary>
        NoEnd,
    }

    internal readonly struct JpegScanResult
    {
        public JpegScanResult(JpegScanStatus status, int length)
        {
            Status = status;
            Length = length;
        }

        public JpegScanStatus Status { get; }

        /// <summary>Bytes from the start marker through the end marker, 0 when not found.</summary>
        public int Length { get; }
    }

    /// <summary>
    /// Finds where a JPEG ends by walking its structure: segments are skipped by their length, so FF D9 in a thumbnail or a
    /// comment does not count, and inside entropy coded data FF 00 and the restart markers are data. Nothing is trimmed by
    /// value, because 0x20 and 0x00 can be image data. Bytes after the end marker, padding or not, are simply not part of
    /// the image. This checks structure only and does not decode a picture.
    /// </summary>
    internal static class JpegScanner
    {
        private const byte MarkerPrefix = 0xFF;
        private const byte StartOfImage = 0xD8;
        private const byte EndOfImage = 0xD9;
        private const byte StartOfScan = 0xDA;
        private const byte Stuffed = 0x00;
        private const byte Temporary = 0x01;
        private const byte FirstRestart = 0xD0;
        private const byte LastRestart = 0xD7;

        // 02-BF are reserved, so one of these means the bytes are not a JPEG, or not one that was read whole.
        private const byte FirstReserved = 0x02;
        private const byte LastReserved = 0xBF;

        private const int HeaderLength = 2;
        private const int NotFound = -1;

        public static JpegScanResult Scan(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length < HeaderLength || data[0] != MarkerPrefix || data[1] != StartOfImage)
            {
                return new JpegScanResult(JpegScanStatus.NoStart, 0);
            }

            int position = HeaderLength;
            bool scanSeen = false;

            while (position < data.Length && data[position] == MarkerPrefix)
            {
                position = SkipFill(data, position);
                if (position >= data.Length)
                {
                    break;
                }

                byte marker = data[position];
                position++;

                if (marker == EndOfImage)
                {
                    return scanSeen ? new JpegScanResult(JpegScanStatus.Found, position) : new JpegScanResult(JpegScanStatus.NoEnd, 0);
                }

                if (marker == Temporary)
                {
                    continue;
                }

                if (!HasSegment(marker))
                {
                    break;
                }

                position = SkipSegment(data, position);
                if (position == NotFound)
                {
                    break;
                }

                if (marker == StartOfScan)
                {
                    scanSeen = true;
                    position = SkipEntropyData(data, position);
                    if (position == NotFound)
                    {
                        break;
                    }
                }
            }

            return new JpegScanResult(JpegScanStatus.NoEnd, 0);
        }

        /// <summary>Any marker may follow any number of FF fill bytes.</summary>
        private static int SkipFill(byte[] data, int position)
        {
            while (position < data.Length && data[position] == MarkerPrefix)
            {
                position++;
            }

            return position;
        }

        private static bool HasSegment(byte marker) =>
            marker != Stuffed && marker != StartOfImage && !IsRestart(marker) && !(marker >= FirstReserved && marker <= LastReserved);

        private static bool IsRestart(byte value) => value >= FirstRestart && value <= LastRestart;

        /// <summary>The big endian length counts itself, so 2 is the smallest possible value.</summary>
        private static int SkipSegment(byte[] data, int position)
        {
            if (position + HeaderLength > data.Length)
            {
                return NotFound;
            }

            int length = (data[position] << 8) | data[position + 1];
            if (length < HeaderLength || position + length > data.Length)
            {
                return NotFound;
            }

            return position + length;
        }

        /// <summary>Returns the position of the FF of the next real marker, or NotFound when the data ends first.</summary>
        private static int SkipEntropyData(byte[] data, int position)
        {
            while (position < data.Length)
            {
                if (data[position] != MarkerPrefix)
                {
                    position++;
                    continue;
                }

                int next = SkipFill(data, position);
                if (next >= data.Length)
                {
                    return NotFound;
                }

                if (data[next] != Stuffed && !IsRestart(data[next]))
                {
                    return position;
                }

                position = next + 1;
            }

            return NotFound;
        }
    }
}
