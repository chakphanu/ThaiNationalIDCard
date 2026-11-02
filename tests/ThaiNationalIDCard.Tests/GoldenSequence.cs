using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Loads the APDU sequences in Golden/ and applies the differences the plan allows. A golden file states what
    /// the release/2.x code at c488da3 sends. It does not state what a card requires.
    /// </summary>
    internal static class GoldenSequence
    {
        public const string P2Zero = "release-2x-c488da3-p2-00.apdu";
        public const string P2One = "release-2x-c488da3-p2-01.apdu";
        public const string Direct = "release-2x-c488da3-direct.apdu";

        public const string OldAddress = "80b01579020064";
        public const string NewAddress = "80b015790200a0";
        public const string LastOldChunk = "80b014680200ff";
        public const string NewLastChunk = "80b01567020012";
        public const string SelectMoi = "00a4040008a000000054480001";

        public static List<string> Load(string name)
        {
            using Stream stream = typeof(GoldenSequence).Assembly.GetManifestResourceStream("Golden." + name)
                ?? throw new InvalidOperationException("Missing golden resource " + name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd()
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line[0] != '#')
                .ToList();
        }

        /// <summary>
        /// The only differences allowed between 2.x and v3, in position:
        /// (1) GET RESPONSE after SELECT when SELECT answers 61 xx,
        /// (2) Le of the address READ and its GET RESPONSE,
        /// (3) READ 15 67 Le 12 and its GET RESPONSE after the last 2.x photo chunk.
        /// Issue/expire Le 10 is not listed: c488da3 already sends it, so the two lines have nothing to differ on.
        /// </summary>
        public static List<string> ExpectedV3(IReadOnlyList<string> golden, int? selectAnnounces = null)
        {
            string prefix = GetResponsePrefix(golden);
            var expected = new List<string>();

            for (int i = 0; i < golden.Count; i++)
            {
                string command = golden[i];
                bool followedByGetResponse = i + 1 < golden.Count && IsGetResponse(golden[i + 1]);

                if (command == SelectMoi)
                {
                    expected.Add(command);
                    if (selectAnnounces.HasValue)
                    {
                        expected.Add(prefix + Hex2(selectAnnounces.Value));
                    }
                }
                else if (command == OldAddress)
                {
                    expected.Add(NewAddress);
                    if (followedByGetResponse)
                    {
                        expected.Add(prefix + "a0");
                        i++;
                    }
                }
                else if (command == LastOldChunk)
                {
                    expected.Add(command);
                    if (followedByGetResponse)
                    {
                        expected.Add(golden[i + 1]);
                        i++;
                    }

                    expected.Add(NewLastChunk);
                    if (followedByGetResponse)
                    {
                        expected.Add(prefix + "12");
                    }
                }
                else
                {
                    expected.Add(command);
                }
            }

            return expected;
        }

        /// <summary>READ offset and Le pairs of a full v3 read in the 2.x order, until step 5 provides the workflow.</summary>
        public static IEnumerable<(int Offset, int Le)> V3Reads()
        {
            yield return (0x0004, 0x0D);
            yield return (0x0011, 0xD1);
            yield return (0x1579, 0xA0);
            yield return (0x0167, 0x10);
            yield return (0x00F6, 0x64);
            for (int chunk = 0; chunk < 20; chunk++)
            {
                yield return (0x017B + chunk * 255, 0xFF);
            }

            yield return (0x1567, 0x12);
        }

        public static bool IsGetResponse(string hex) => hex.StartsWith("00c000", StringComparison.Ordinal);

        private static string GetResponsePrefix(IReadOnlyList<string> golden)
        {
            string? first = golden.FirstOrDefault(IsGetResponse);
            return first == null ? string.Empty : first.Substring(0, 8);
        }

        private static string Hex2(int value) => value.ToString("x2", CultureInfo.InvariantCulture);
    }
}
