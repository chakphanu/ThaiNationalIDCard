using System;
using System.Collections.Generic;

namespace ThaiNationalIDCard.Card
{
    /// <summary>
    /// The only place that turns profile data into command bytes, so profiles hold no raw APDU. The guard still
    /// checks every result: nothing here is trusted because of where it came from.
    /// </summary>
    internal static class MoiCardCommands
    {
        private const int MaxReadLength = 256;

        /// <summary>00 A4 04 00 Lc AID, without Le.</summary>
        public static byte[] Select(CardProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            byte[] aid = profile.GetAid();
            var command = new byte[5 + aid.Length];
            command[1] = 0xA4;
            command[2] = 0x04;
            command[4] = (byte)aid.Length;
            Array.Copy(aid, 0, command, 5, aid.Length);
            return command;
        }

        /// <summary>80 B0 P1 P2 02 00 Le. READ carries its length in the data field; Le 00 stands for 256.</summary>
        public static byte[] Read(FieldRange range)
        {
            if (range.Length < 1 || range.Length > MaxReadLength)
            {
                throw new ArgumentOutOfRangeException(nameof(range));
            }

            return new byte[]
            {
                0x80, 0xB0, (byte)(range.Offset >> 8), (byte)(range.Offset & 0xFF), 0x02, 0x00, (byte)(range.Length & 0xFF),
            };
        }

        /// <summary>80 00 00 00 07: the only known form of the applet data command, taken from public code.</summary>
        public static byte[] AppletData() => new byte[] { 0x80, 0x00, 0x00, 0x00, 0x07 };

        /// <summary>The READ ranges of one field: full chunks, then whatever is left.</summary>
        public static IEnumerable<FieldRange> Chunks(FieldRange range)
        {
            for (int done = 0; done < range.Length; done += CardField.MaxChunkLength)
            {
                yield return new FieldRange(range.Offset + done, Math.Min(CardField.MaxChunkLength, range.Length - done));
            }
        }
    }
}
