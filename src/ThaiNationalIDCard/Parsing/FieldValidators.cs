using System;
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
        private const int CitizenIdLength = 13;
        private const int CitizenIdPayloadLength = 12;
        private const int DateLength = 8;
        private const int NameSlotLength = 100;
        private const int BirthDateOffset = 200;
        private const int SexOffset = 208;
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
                default:
                    return ValidationFailures.None;
            }
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

        /// <summary>
        /// A first look only: FF D8 at the start and an FF D9 anywhere after it. Step 7 walks the JPEG markers, and only
        /// then does a failing photo get withheld. Here it is marked and kept like any other field.
        /// </summary>
        private static ValidationFailures CheckJpeg(byte[] data)
        {
            ValidationFailures result = ValidationFailures.None;

            if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
            {
                result |= ValidationFailures.JpegStart;
            }

            for (int i = 2; i + 1 < data.Length; i++)
            {
                if (data[i] == 0xFF && data[i + 1] == 0xD9)
                {
                    return result;
                }
            }

            return result | ValidationFailures.JpegEnd;
        }

        /// <summary>NUL and space padding, ASCII and the assigned Thai range. Anything else suggests a wrong offset.</summary>
        private static bool IsTis620(byte[] data, int offset, int length)
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
