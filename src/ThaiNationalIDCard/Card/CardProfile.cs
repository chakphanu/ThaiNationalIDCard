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

    /// <summary>Which applet a profile describes. Used for trace stages, it says nothing about card contents.</summary>
    internal enum AppletKind
    {
        Moi,
        Nhso,
        Adm,
    }

    /// <summary>Which validator a field gets. None means status and length only.</summary>
    internal enum FieldCheck
    {
        None,
        CitizenId,
        PersonInfo,
        Address,
        IssueExpire,
        Issuer,
        Jpeg,
        AdmAppletData,
    }

    internal enum FieldAccess
    {
        /// <summary>80 B0 over the field range, in chunks.</summary>
        Read,

        /// <summary>The 80 00 00 00 data command of the applet.</summary>
        AppletData,
    }

    /// <summary>One line of the field table: data only, commands come from <see cref="MoiCardCommands"/>.</summary>
    internal sealed class CardField
    {
        /// <summary>Largest READ the 2.x sequence uses. Le 00 (256) is not used by any field.</summary>
        public const int MaxChunkLength = 255;

        public CardField(CardFieldId id, FieldRange range, FieldCheck check = FieldCheck.None, bool isPrivate = false,
            FieldAccess access = FieldAccess.Read)
        {
            Id = id;
            Range = range;
            Check = check;
            IsPrivate = isPrivate;
            Access = access;
        }

        public CardFieldId Id { get; }

        public FieldRange Range { get; }

        public FieldCheck Check { get; }

        /// <summary>Read only in the private mode.</summary>
        public bool IsPrivate { get; }

        public FieldAccess Access { get; }
    }

    /// <summary>
    /// Typed data about one applet, with no raw APDU: AID, GET RESPONSE P2 candidates, readable ranges and the field
    /// table. The table order is the read order.
    /// </summary>
    internal sealed class CardProfile
    {
        private static readonly byte[] DefaultP2Candidates = { 0x00, 0x01 };

        private readonly byte[] _aid;

        public CardProfile(byte[] aid, IEnumerable<FieldRange> readable, IEnumerable<FieldRange>? neverRead = null,
            bool allowsAppletDataCommand = false, AppletKind applet = AppletKind.Moi, IEnumerable<CardField>? fields = null,
            IEnumerable<byte>? p2Candidates = null)
        {
            _aid = (byte[])aid.Clone();
            Readable = readable.ToArray();
            NeverRead = (neverRead ?? Enumerable.Empty<FieldRange>()).ToArray();
            AllowsAppletDataCommand = allowsAppletDataCommand;
            Applet = applet;
            Fields = (fields ?? Enumerable.Empty<CardField>()).ToArray();
            P2Candidates = (p2Candidates ?? DefaultP2Candidates).ToArray();

            // Profile data cannot widen what is off limits: a bad table fails here, before any card access.
            foreach (FieldRange range in Readable)
            {
                if (NeverRead.Any(range.Overlaps))
                {
                    throw new ArgumentException("A readable range overlaps a never-read range.", nameof(readable));
                }
            }

            // A table line the guard would refuse is a bad table, so it fails here and not on a card.
            foreach (CardField field in Fields)
            {
                bool declared = field.Access == FieldAccess.AppletData
                    ? AllowsAppletDataCommand
                    : Readable.Any(r => r.Offset == field.Range.Offset && r.Length == field.Range.Length);
                if (!declared)
                {
                    throw new ArgumentException("A field is outside what the profile declares.", nameof(fields));
                }
            }

            if (P2Candidates.Count == 0)
            {
                throw new ArgumentException("A profile needs at least one P2 candidate.", nameof(p2Candidates));
            }
        }

        public IReadOnlyList<FieldRange> Readable { get; }

        public IReadOnlyList<FieldRange> NeverRead { get; }

        /// <summary>Whether the applet accepts the 80 00 00 00 data command (ADM only).</summary>
        public bool AllowsAppletDataCommand { get; }

        public AppletKind Applet { get; }

        /// <summary>Fields in read order.</summary>
        public IReadOnlyList<CardField> Fields { get; }

        /// <summary>GET RESPONSE P2 values worth trying, in the normal order. The ATR hint can only reorder them.</summary>
        public IReadOnlyList<byte> P2Candidates { get; }

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

        // Same order as the 2.x sequence, which the golden test pins.
        private static readonly CardField[] PublicFields =
        {
            new CardField(CardFieldId.CitizenId, CitizenId, FieldCheck.CitizenId),
            new CardField(CardFieldId.PersonInfo, PersonInfo, FieldCheck.PersonInfo),
            new CardField(CardFieldId.Address, Address, FieldCheck.Address),
            new CardField(CardFieldId.IssueExpire, IssueExpire, FieldCheck.IssueExpire),
            new CardField(CardFieldId.Issuer, Issuer, FieldCheck.Issuer),
            new CardField(CardFieldId.Photo, Photo, FieldCheck.Jpeg),
        };

        private static readonly CardField[] PrivateFields =
        {
            new CardField(CardFieldId.MoiRaw0000, Raw0000, isPrivate: true),
            new CardField(CardFieldId.MoiRaw00E2, Raw00E2, isPrivate: true),
            new CardField(CardFieldId.MoiRaw0177, Raw0177, isPrivate: true),
            new CardField(CardFieldId.MoiRaw1619, Raw1619, isPrivate: true),
        };

        public static readonly CardProfile Public = new CardProfile(Aid, PublicRanges, NeverRead, fields: PublicFields);

        public static readonly CardProfile PublicAndPrivate = new CardProfile(
            Aid,
            PublicRanges.Concat(new[] { Raw0000, Raw00E2, Raw0177, Raw1619 }),
            NeverRead,
            fields: PublicFields.Concat(PrivateFields));
    }

    /// <summary>
    /// NHSO applet, private mode only. Ranges come from public project code and were not checked on a card.
    /// Offsets here are inside this applet, so they say nothing about the same numbers in the MOI applet.
    /// </summary>
    internal static class NhsoProfile
    {
        private static readonly byte[] Aid = { 0xA0, 0x00, 0x00, 0x00, 0x54, 0x48, 0x00, 0x83 };

        private static readonly CardField[] Fields =
        {
            new CardField(CardFieldId.NhsoRaw0004, new FieldRange(0x0004, 0x3C), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw0040, new FieldRange(0x0040, 0x64), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw00A4, new FieldRange(0x00A4, 0x50), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw00F4, new FieldRange(0x00F4, 0x50), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw0144, new FieldRange(0x0144, 0x01), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw0145, new FieldRange(0x0145, 0x08), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw014D, new FieldRange(0x014D, 0x08), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw0155, new FieldRange(0x0155, 0x08), isPrivate: true),
            new CardField(CardFieldId.NhsoRaw015D, new FieldRange(0x015D, 0x01), isPrivate: true),
        };

        public static readonly CardProfile Instance = new CardProfile(
            Aid, Fields.Select(f => f.Range), applet: AppletKind.Nhso, fields: Fields);
    }

    /// <summary>
    /// ADM applet, private mode only. It has no READ range, only the 80 00 00 00 data command. Public code suggests
    /// the answer holds the Laser ID; that is unconfirmed, so the field is named after the command.
    /// </summary>
    internal static class AdmProfile
    {
        // ASSUMPTION(adm-layout): the answer is a 7 byte header followed by a 16 byte slot, 23 bytes in all. This is an inference:
        // go-thai and libthai-idcard send 80 00 00 00 07 and then GET RESPONSE 10, and Advanced-Logic expects 0x17 bytes with the
        // value at bytes 7 to 22. 7 + 16 = 0x17 is the one reading that fits them all, and nobody has published a trace of it.
        public const int HeaderLength = 7;
        public const int SlotLength = 16;
        public const int AppletDataLength = HeaderLength + SlotLength;

        // ASSUMPTION(laser-format): the slot holds the Laser ID as 12 ASCII letters or digits, then 4 NUL bytes. This comes from
        // GoSCApi, which describes a buffer of 12 characters and 4 NUL. No specification says so, and no card here showed it.
        public const int LaserTextLength = 12;

        private static readonly byte[] Aid = { 0xA0, 0x00, 0x00, 0x00, 0x84, 0x06, 0x00, 0x02 };

        public static readonly CardProfile Instance = new CardProfile(
            Aid,
            Enumerable.Empty<FieldRange>(),
            allowsAppletDataCommand: true,
            applet: AppletKind.Adm,
            fields: new[] { new CardField(CardFieldId.AdmAppletData, default, FieldCheck.AdmAppletData, isPrivate: true, access: FieldAccess.AppletData) });
    }
}
