using System;
using System.Linq;
using ThaiNationalIDCard.Card;

namespace ThaiNationalIDCard.Parsing
{
    /// <summary>The bytes of one card range and how the read of it went.</summary>
    internal readonly struct RawCardField
    {
        public RawCardField(byte[]? data, FieldQuality quality)
        {
            Data = data;
            Quality = quality;
        }

        public byte[]? Data { get; }

        public FieldQuality Quality { get; }
    }

    /// <summary>The public card ranges. A field left out counts as not read.</summary>
    internal sealed class RawCardFields
    {
        public RawCardField CitizenId { get; set; }

        public RawCardField PersonInfo { get; set; }

        public RawCardField Address { get; set; }

        public RawCardField IssueExpire { get; set; }

        public RawCardField Issuer { get; set; }

        public RawCardField Photo { get; set; }
    }

    /// <summary>
    /// Bytes in, model out, with no PC/SC and no I/O. Bytes are always sliced by position before decoding, so a decoder or
    /// a trim cannot move an offset. Malformed data never throws: the field is marked and keeps what the card said, except
    /// a photo that does not walk as a JPEG. Bytes of a failed read are not decoded as text. Layout and label words come from
    /// public code and sources: nothing here was checked on a real card yet.
    /// </summary>
    internal static class CardDataParser
    {
        private const char Separator = '#';
        private const int NameSegments = 4;

        // That 1 is male and 2 is female is an inference from public parsers agreeing, to confirm on a real card.
        private const byte SexCodeMale = (byte)'1';
        private const byte SexCodeFemale = (byte)'2';

        // Label words, matched only at the start of a part, since a label word inside a name is just part of the name.
        // Moo needs its whole label because a part starting with the shorter word can be the name of a village.
        private const string LabelMoo = "\u0E2B\u0E21\u0E39\u0E48\u0E17\u0E35\u0E48";
        private const string LabelTrok = "\u0E15\u0E23\u0E2D\u0E01";
        private const string LabelSoi = "\u0E0B\u0E2D\u0E22";
        private const string LabelRoad = "\u0E16\u0E19\u0E19";
        private const string LabelTambon = "\u0E15\u0E33\u0E1A\u0E25";
        private const string LabelKhwaeng = "\u0E41\u0E02\u0E27\u0E07";
        private const string LabelAmphoe = "\u0E2D\u0E33\u0E40\u0E20\u0E2D";
        private const string LabelKhet = "\u0E40\u0E02\u0E15";
        private const string LabelProvince = "\u0E08\u0E31\u0E07\u0E2B\u0E27\u0E31\u0E14";

        private static readonly string[] AddressLabels =
        {
            LabelMoo, LabelTrok, LabelSoi, LabelRoad, LabelTambon, LabelKhwaeng, LabelAmphoe, LabelKhet, LabelProvince,
        };

        private static readonly char[] Padding = { ' ', '\0' };

        public static ThaiIdCardData Parse(RawCardFields fields, ReadDiagnostics? diagnostics = null)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            byte[] person = Usable(fields.PersonInfo) ? fields.PersonInfo.Data! : Array.Empty<byte>();
            byte[] dates = Usable(fields.IssueExpire) ? fields.IssueExpire.Data! : Array.Empty<byte>();

            (string? citizenId, FieldQuality citizenIdQuality) = ParseText(fields.CitizenId, FieldCheck.CitizenId);
            (string? issuer, FieldQuality issuerQuality) = ParseText(fields.Issuer, FieldCheck.Issuer);
            (ThaiIdCardName thaiName, FieldQuality thaiNameQuality) = ParseName(fields.PersonInfo, Slice(person, 0, FieldValidators.NameSlotLength));
            (ThaiIdCardName englishName, FieldQuality englishNameQuality) =
                ParseName(fields.PersonInfo, Slice(person, FieldValidators.NameSlotLength, FieldValidators.NameSlotLength));
            ThaiCardDate birthDate = ParseDate(Slice(person, FieldValidators.BirthDateOffset, FieldValidators.DateLength));
            (CardSex sex, byte? sexRaw, FieldQuality sexQuality) = ParseSex(fields.PersonInfo, person);
            (ThaiIdCardAddress address, FieldQuality addressQuality) = ParseAddress(fields.Address);
            ThaiCardDate issueDate = ParseDate(Slice(dates, 0, FieldValidators.DateLength));
            ThaiCardDate expiryDate = ParseDate(Slice(dates, FieldValidators.DateLength, FieldValidators.DateLength));
            (byte[]? photo, PhotoFailureReason photoFailure, FieldQuality photoQuality) = ParsePhoto(fields.Photo);

            var quality = new ThaiIdCardQuality(
                citizenIdQuality,
                thaiNameQuality,
                englishNameQuality,
                DateQuality(fields.PersonInfo, birthDate, d => d.IsWellFormed),
                sexQuality,
                addressQuality,
                DateQuality(fields.IssueExpire, issueDate, d => d.ToDateTime() != null),
                DateQuality(fields.IssueExpire, expiryDate, d => d.IsLifetime || d.ToDateTime() != null),
                issuerQuality,
                photoQuality);

            return new ThaiIdCardData(citizenId, thaiName, englishName, birthDate, sex, sexRaw, address, issueDate, expiryDate,
                issuer, photo, photoFailure, quality, diagnostics);
        }

        private static bool Usable(RawCardField field) =>
            field.Quality != FieldQuality.NotRead && field.Quality != FieldQuality.Failed && field.Data != null && field.Data.Length > 0;

        /// <summary>What a field with no usable bytes reports.</summary>
        private static FieldQuality WithoutData(RawCardField field) =>
            field.Quality == FieldQuality.NotRead ? FieldQuality.NotRead : FieldQuality.Failed;

        /// <summary>The range as far as the data reaches it. A range past the end is empty.</summary>
        private static byte[] Slice(byte[] data, int offset, int length)
        {
            if (offset >= data.Length)
            {
                return Array.Empty<byte>();
            }

            var slice = new byte[Math.Min(length, data.Length - offset)];
            Array.Copy(data, offset, slice, 0, slice.Length);
            return slice;
        }

        /// <summary>Splits decoded text on the separator. Empty parts stay, so no part moves into the place of another.</summary>
        private static string[] SplitParts(string text) => text.Split(Separator).Select(p => p.Trim(Padding)).ToArray();

        private static string? At(string[] parts, int index) => index < parts.Length ? parts[index] : null;

        private static (string? Text, FieldQuality Quality) ParseText(RawCardField field, FieldCheck check)
        {
            if (!Usable(field))
            {
                return (null, WithoutData(field));
            }

            string text = ThaiCardTextDecoder.DecodeTis620(field.Data!);
            bool good = field.Quality == FieldQuality.Ok && FieldValidators.Check(check, field.Data!) == ValidationFailures.None && text.Length > 0;
            return (text, good ? FieldQuality.Ok : FieldQuality.Suspect);
        }

        private static (ThaiIdCardName Name, FieldQuality Quality) ParseName(RawCardField field, byte[] slot)
        {
            if (!Usable(field))
            {
                return (new ThaiIdCardName(), WithoutData(field));
            }

            if (slot.Length == 0)
            {
                return (new ThaiIdCardName(), FieldQuality.Failed);
            }

            string raw = ThaiCardTextDecoder.DecodeTis620(slot);
            string[] parts = SplitParts(raw);
            bool good = slot.Length == FieldValidators.NameSlotLength && parts.Length == NameSegments &&
                parts.Any(p => p.Length > 0) && FieldValidators.IsTis620(slot, 0, slot.Length);

            return (new ThaiIdCardName(At(parts, 0), At(parts, 1), At(parts, 2), At(parts, 3), raw), good ? FieldQuality.Ok : FieldQuality.Suspect);
        }

        private static ThaiCardDate ParseDate(byte[] slice) => new ThaiCardDate(ThaiCardTextDecoder.DecodeTis620(slice));

        private static FieldQuality DateQuality(RawCardField field, ThaiCardDate date, Func<ThaiCardDate, bool> isGood)
        {
            if (!Usable(field))
            {
                return WithoutData(field);
            }

            if (date.Raw.Length == 0)
            {
                return FieldQuality.Failed;
            }

            return isGood(date) ? FieldQuality.Ok : FieldQuality.Suspect;
        }

        /// <summary>Only the byte decides: anything else is Unknown, and the name prefix plays no part.</summary>
        private static (CardSex Sex, byte? Raw, FieldQuality Quality) ParseSex(RawCardField field, byte[] person)
        {
            if (!Usable(field))
            {
                return (CardSex.Unknown, null, WithoutData(field));
            }

            if (person.Length <= FieldValidators.SexOffset)
            {
                return (CardSex.Unknown, null, FieldQuality.Failed);
            }

            byte raw = person[FieldValidators.SexOffset];
            switch (raw)
            {
                case SexCodeMale:
                    return (CardSex.Male, raw, FieldQuality.Ok);
                case SexCodeFemale:
                    return (CardSex.Female, raw, FieldQuality.Ok);
                default:
                    return (CardSex.Unknown, raw, FieldQuality.Suspect);
            }
        }

        private static (ThaiIdCardAddress Address, FieldQuality Quality) ParseAddress(RawCardField field)
        {
            if (!Usable(field))
            {
                return (new ThaiIdCardAddress(), WithoutData(field));
            }

            byte[] data = field.Data!;
            string[] parts = SplitParts(ThaiCardTextDecoder.DecodeTis620(data));
            var values = new string?[AddressLabels.Length];
            bool ambiguous = false;

            for (int i = 0; i < AddressLabels.Length; i++)
            {
                string label = AddressLabels[i];
                string[] labelled = parts.Where(p => p.StartsWith(label, StringComparison.Ordinal)).ToArray();

                // Two parts with one label cannot both be right, and picking one would be a guess.
                ambiguous |= labelled.Length > 1;
                values[i] = labelled.Length == 1 ? labelled[0].Substring(label.Length).Trim(Padding) : null;
            }

            bool good = field.Quality == FieldQuality.Ok && !ambiguous && data.Length == MoiProfile.Address.Length &&
                FieldValidators.Check(FieldCheck.Address, data) == ValidationFailures.None;
            string full = string.Join(" ", parts.Where(p => p.Length > 0));

            var address = new ThaiIdCardAddress(full, parts, values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]);
            return (address, good ? FieldQuality.Ok : FieldQuality.Suspect);
        }

        /// <summary>
        /// A photo that does not walk is not given. One that does is given even when the read stopped later, since every byte
        /// up to its end marker arrived in order, and the quality says the read was not clean.
        /// </summary>
        private static (byte[]? Photo, PhotoFailureReason Failure, FieldQuality Quality) ParsePhoto(RawCardField field)
        {
            if (field.Quality == FieldQuality.NotRead)
            {
                return (null, PhotoFailureReason.None, FieldQuality.NotRead);
            }

            byte[] data = field.Data ?? Array.Empty<byte>();
            if (data.Length == 0)
            {
                return (null, PhotoFailureReason.ReadFailed, FieldQuality.Failed);
            }

            JpegScanResult scan = JpegScanner.Scan(data);
            if (scan.Status == JpegScanStatus.Found)
            {
                return (Slice(data, 0, scan.Length), PhotoFailureReason.None, field.Quality == FieldQuality.Ok ? FieldQuality.Ok : FieldQuality.Suspect);
            }

            if (field.Quality == FieldQuality.Failed)
            {
                return (null, PhotoFailureReason.ReadFailed, FieldQuality.Failed);
            }

            return (null, scan.Status == JpegScanStatus.NoStart ? PhotoFailureReason.NoStartMarker : PhotoFailureReason.NoEndMarker, FieldQuality.Failed);
        }
    }
}
