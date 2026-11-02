using System;
using System.Collections.Generic;
using System.Linq;

namespace ThaiNationalIDCard.Card
{
    /// <summary>A byte range inside an applet: first offset and length.</summary>
    internal readonly struct FieldRange
    {
        public FieldRange(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }

        public int EndExclusive => Offset + Length;

        public bool Contains(int offset, int length) =>
            offset >= Offset && length > 0 && offset + length <= EndExclusive;

        public bool Overlaps(FieldRange other) =>
            Offset < other.EndExclusive && other.Offset < EndExclusive;
    }

    /// <summary>
    /// Typed data about one applet, with no raw APDU. Only what the guard needs is here so far: step 5 adds
    /// P2 candidates and the field table, step 9 the private fields.
    /// </summary>
    internal sealed class CardProfile
    {
        private readonly byte[] _aid;

        public CardProfile(byte[] aid, IEnumerable<FieldRange> readable, IEnumerable<FieldRange>? neverRead = null,
            bool allowsAppletDataCommand = false)
        {
            _aid = (byte[])aid.Clone();
            Readable = readable.ToArray();
            NeverRead = (neverRead ?? Enumerable.Empty<FieldRange>()).ToArray();
            AllowsAppletDataCommand = allowsAppletDataCommand;

            // Profile data cannot widen what is off limits: a bad table fails here, before any card access.
            foreach (FieldRange range in Readable)
            {
                if (NeverRead.Any(range.Overlaps))
                {
                    throw new ArgumentException("A readable range overlaps a never-read range.", nameof(readable));
                }
            }
        }

        public IReadOnlyList<FieldRange> Readable { get; }

        public IReadOnlyList<FieldRange> NeverRead { get; }

        /// <summary>Whether the applet accepts the 80 00 00 00 data command (ADM only).</summary>
        public bool AllowsAppletDataCommand { get; }

        public byte[] GetAid() => (byte[])_aid.Clone();

        /// <summary>True when one single declared range holds the whole read. Neighbouring ranges do not add up.</summary>
        public bool AllowsRead(int offset, int length) => Readable.Any(r => r.Contains(offset, length));

        public bool MatchesAid(byte[] command, int aidStart, int aidLength)
        {
            if (aidLength != _aid.Length || aidStart + aidLength > command.Length)
            {
                return false;
            }

            for (int i = 0; i < aidLength; i++)
            {
                if (command[aidStart + i] != _aid[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// MOI applet layout. Offsets come from public projects and from the 2.x commands, none of it from a
    /// government specification or from a test on a real card in this repository. Ranges with conflicting
    /// labels in public sources are named by position, not by meaning.
    /// </summary>
    internal static class MoiProfile
    {
        public static readonly FieldRange CitizenId = new FieldRange(0x0004, 13);
        public static readonly FieldRange PersonInfo = new FieldRange(0x0011, 209);
        public static readonly FieldRange Issuer = new FieldRange(0x00F6, 100);
        public static readonly FieldRange IssueExpire = new FieldRange(0x0167, 16);
        public static readonly FieldRange Photo = new FieldRange(0x017B, 5118);
        public static readonly FieldRange Address = new FieldRange(0x1579, 160);

        // Private mode only. Meaning unknown: public sources disagree, so nothing here is named by meaning.
        public static readonly FieldRange Raw0000 = new FieldRange(0x0000, 4);
        public static readonly FieldRange Raw00E2 = new FieldRange(0x00E2, 20);
        public static readonly FieldRange Raw0177 = new FieldRange(0x0177, 2);
        public static readonly FieldRange Raw1619 = new FieldRange(0x1619, 14);

        // Off limits in every mode. Public sources disagree on what these hold.
        public static readonly FieldRange Never015A = new FieldRange(0x015A, 13);
        public static readonly FieldRange Never1627 = new FieldRange(0x1627, 279);

        private static readonly byte[] Aid = { 0xA0, 0x00, 0x00, 0x00, 0x54, 0x48, 0x00, 0x01 };

        private static readonly FieldRange[] NeverRead = { Never015A, Never1627 };

        private static readonly FieldRange[] PublicRanges = { CitizenId, PersonInfo, Issuer, IssueExpire, Photo, Address };

        public static readonly CardProfile Public = new CardProfile(Aid, PublicRanges, NeverRead);

        public static readonly CardProfile PublicAndPrivate = new CardProfile(
            Aid,
            PublicRanges.Concat(new[] { Raw0000, Raw00E2, Raw0177, Raw1619 }),
            NeverRead);
    }

    /// <summary>
    /// NHSO applet, private mode only. Ranges come from public project code and were not checked on a card.
    /// Offsets here are inside this applet, so they say nothing about the same numbers in the MOI applet.
    /// </summary>
    internal static class NhsoProfile
    {
        private static readonly byte[] Aid = { 0xA0, 0x00, 0x00, 0x00, 0x54, 0x48, 0x00, 0x83 };

        public static readonly CardProfile Instance = new CardProfile(Aid, new[]
        {
            new FieldRange(0x0004, 0x3C),
            new FieldRange(0x0040, 0x64),
            new FieldRange(0x00A4, 0x50),
            new FieldRange(0x00F4, 0x50),
            new FieldRange(0x0144, 0x01),
            new FieldRange(0x0145, 0x08),
            new FieldRange(0x014D, 0x08),
            new FieldRange(0x0155, 0x08),
            new FieldRange(0x015D, 0x01),
        });
    }

    /// <summary>ADM applet, private mode only. It has no READ range, only the 80 00 00 00 data command.</summary>
    internal static class AdmProfile
    {
        private static readonly byte[] Aid = { 0xA0, 0x00, 0x00, 0x00, 0x84, 0x06, 0x00, 0x02 };

        public static readonly CardProfile Instance = new CardProfile(
            Aid, Enumerable.Empty<FieldRange>(), allowsAppletDataCommand: true);
    }
}
