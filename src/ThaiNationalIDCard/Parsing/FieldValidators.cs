using System;
using System.Linq;
using System.Text;
using ThaiNationalIDCard.Card;

namespace ThaiNationalIDCard.Parsing
{
    /// <summary>
    /// Shape checks on raw bytes, always before any decoding, so a decoder or a trim cannot move an offset. A failure
    /// marks a field and never removes data. The checks come from the signal table in the research report; that a real
    /// card has these shapes is unconfirmed, so the workflow only ever marks with them, and only the CID drives a retry.
    /// A result names the check that failed and never tells anything about the value.
    /// </summary>
    internal static class FieldValidators
    {
        // Layout of the person info range, as public code reads it.
        public const int NameSlotLength = 100;
        public const int BirthDateOffset = 200;
        public const int DateLength = 8;
        public const int SexOffset = 208;

        private const int CitizenIdLength = 13;
        private const int CitizenIdPayloadLength = 12;
        private const int NameSeparators = 3;

        // One real card in public code shows 8 parts. Other sources show other counts, so this only marks.
        private const int AddressSeparators = 7;
        private const byte Separator = (byte)'#';

        public static ValidationFailures Check(FieldCheck check, byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            switch (check)
            {
                case FieldCheck.CitizenId:
                    return CheckCitizenId(data);
                case FieldCheck.PersonInfo:
                    return CheckPersonInfo(data);
                case FieldCheck.Address:
                    return CheckAddress(data);
                case FieldCheck.IssueExpire:
                    return CheckIssueExpire(data);
                case FieldCheck.Issuer:
                    return IsTis620(data, 0, data.Length) ? ValidationFailures.None : ValidationFailures.TextEncoding;
                case FieldCheck.Jpeg:
                    return CheckJpeg(data);
                case FieldCheck.AdmAppletData:
                    return CheckAdmAppletData(data);
                default:
                    return ValidationFailures.None;
            }
        }

        /// <summary>
        /// The text in the value slot of the ADM answer, without its NUL padding. Null unless the answer has the whole assumed
        /// length and the slot holds visible ASCII only: cutting at a guessed place could hand out a wrong value that looks right.
        /// See ASSUMPTION(adm-layout) in <see cref="AdmProfile"/>.
        /// </summary>
        public static string? AdmSlotText(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length != AdmProfile.AppletDataLength)
            {
                return null;
            }

            int end = data.Length;
            while (end > AdmProfile.HeaderLength && data[end - 1] == 0)
            {
                end--;
            }

            for (int i = AdmProfile.HeaderLength; i < end; i++)
            {
                if (data[i] <= (byte)' ' || data[i] > (byte)'~')
                {
                    return null;
                }
            }

            return end == AdmProfile.HeaderLength ? null : Encoding.ASCII.GetString(data, AdmProfile.HeaderLength, end - AdmProfile.HeaderLength);
        }

        /// <summary>True when the range exists and holds ASCII digits only. Thai digits do not count.</summary>
        public static bool IsAllDigits(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset + length > data.Length)
            {
                return false;
            }

            for (int i = offset; i < offset + length; i++)
            {
                if (data[i] < (byte)'0' || data[i] > (byte)'9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>(11 - (sum of digit[i] * (13 - i) for i in 0..11) mod 11) mod 10, over the first 12 ASCII digits.</summary>
        public static int CitizenIdCheckDigit(byte[] digits)
        {
            if (digits == null)
            {
                throw new ArgumentNullException(nameof(digits));
            }

            int sum = 0;
            for (int i = 0; i < CitizenIdPayloadLength; i++)
            {
                sum += (digits[i] - (byte)'0') * (13 - i);
            }

            return (11 - (sum % 11)) % 10;
        }

        /// <summary>13 ASCII digits and a matching check digit, nothing more. It says nothing about who holds the number.</summary>
        public static bool IsValidCitizenId(string? text)
        {
            byte[]? digits = ThirteenDigits(text);
            return digits != null && digits[CitizenIdLength - 1] - (byte)'0' == CitizenIdCheckDigit(digits);
        }

        /// <summary>The first digit when the text is 13 ASCII digits, whatever the check digit says.</summary>
        public static string? FirstDigitOfCitizenId(string? text) =>
            ThirteenDigits(text) == null ? null : text!.Substring(0, 1);

        private static byte[]? ThirteenDigits(string? text)
        {
            if (text == null || text.Length != CitizenIdLength)
            {
                return null;
            }

            var digits = new byte[CitizenIdLength];
            for (int i = 0; i < CitizenIdLength; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return null;
                }

                digits[i] = (byte)text[i];
            }

            return digits;
        }

        private static ValidationFailures CheckCitizenId(byte[] data)
        {
            if (data.Length != CitizenIdLength || !IsAllDigits(data, 0, CitizenIdLength))
            {
                return ValidationFailures.NotThirteenDigits;
            }

            return data[CitizenIdLength - 1] - (byte)'0' == CitizenIdCheckDigit(data)
                ? ValidationFailures.None
                : ValidationFailures.ChecksumMismatch;
        }

        private static ValidationFailures CheckPersonInfo(byte[] data)
        {
            ValidationFailures result = ValidationFailures.None;

            if (Count(data, 0, NameSlotLength, Separator) != NameSeparators)
            {
                result |= ValidationFailures.ThaiNameShape;
            }

            if (Count(data, NameSlotLength, NameSlotLength, Separator) != NameSeparators)
            {
                result |= ValidationFailures.EnglishNameShape;
            }

            if (!IsTis620(data, 0, Math.Min(BirthDateOffset, data.Length)))
            {
                result |= ValidationFailures.TextEncoding;
            }

            if (!IsAllDigits(data, BirthDateOffset, DateLength))
            {
                result |= ValidationFailures.BirthDateNotDigits;
            }

            if (data.Length <= SexOffset || (data[SexOffset] != (byte)'1' && data[SexOffset] != (byte)'2'))
            {
                result |= ValidationFailures.SexCode;
            }

            return result;
        }

        /// <summary>See ASSUMPTION(adm-layout) and ASSUMPTION(laser-format) in <see cref="AdmProfile"/>. This only marks.</summary>
        private static ValidationFailures CheckAdmAppletData(byte[] data)
        {
            string? text = AdmSlotText(data);
            bool good = text != null && text.Length == AdmProfile.LaserTextLength &&
                text.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
            return good ? ValidationFailures.None : ValidationFailures.AppletDataShape;
        }

        private static ValidationFailures CheckAddress(byte[] data)
        {
            ValidationFailures result = ValidationFailures.None;

            if (Count(data, 0, data.Length, Separator) != AddressSeparators)
            {
                result |= ValidationFailures.AddressShape;
            }

            if (!IsTis620(data, 0, data.Length))
            {
                result |= ValidationFailures.TextEncoding;
            }

            return result;
        }

        private static ValidationFailures CheckIssueExpire(byte[] data)
        {
            ValidationFailures result = ValidationFailures.None;

            if (!IsAllDigits(data, 0, DateLength))
            {
                result |= ValidationFailures.IssueDateNotDigits;
            }

            if (!IsAllDigits(data, DateLength, DateLength))
            {
                result |= ValidationFailures.ExpiryDateNotDigits;
            }

            return result;
        }

        /// <summary>The same walk the parser uses, so what the trace tool reports is what a caller would get.</summary>
        private static ValidationFailures CheckJpeg(byte[] data)
        {
            switch (JpegScanner.Scan(data).Status)
            {
                case JpegScanStatus.NoStart:
                    return ValidationFailures.JpegStart;
                case JpegScanStatus.NoEnd:
                    return ValidationFailures.JpegEnd;
                default:
                    return ValidationFailures.None;
            }
        }

        /// <summary>NUL and space padding, ASCII and the assigned Thai range. Anything else suggests a wrong offset.</summary>
        public static bool IsTis620(byte[] data, int offset, int length)
        {
            for (int i = offset; i < offset + length; i++)
            {
                byte value = data[i];
                bool valid = value == 0x00 || (value >= 0x20 && value <= 0x7E) || (value >= 0xA1 && value <= 0xDA) || (value >= 0xDF && value <= 0xFB);
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        private static int Count(byte[] data, int offset, int length, byte value)
        {
            int count = 0;
            int end = Math.Min(offset + length, data.Length);
            for (int i = offset; i < end; i++)
            {
                if (data[i] == value)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
