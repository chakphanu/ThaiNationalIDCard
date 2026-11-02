using ThaiNationalIDCard.Parsing;

namespace ThaiNationalIDCard
{
    /// <summary>
    /// Sex as the card codes it. Nothing is inferred from the name prefix. That code 1 means male and code 2 means female is
    /// an inference from public parsers agreeing, and is still to be confirmed on a real card.
    /// </summary>
    public enum CardSex
    {
        /// <summary>The byte was missing or is not a known code. See <see cref="ThaiIdCardData.SexRaw"/>.</summary>
        Unknown = 0,

        /// <summary>The card has code 1.</summary>
        Male,

        /// <summary>The card has code 2.</summary>
        Female,
    }

    /// <summary>Why a photo that was asked for is not given.</summary>
    public enum PhotoFailureReason
    {
        /// <summary>The photo is given, or was not asked for.</summary>
        None = 0,

        /// <summary>The card gave no usable bytes.</summary>
        ReadFailed,

        /// <summary>The bytes do not start with a JPEG start marker.</summary>
        NoStartMarker,

        /// <summary>Walking the JPEG markers did not reach an end of image.</summary>
        NoEndMarker,
    }

    /// <summary>Quality per field of <see cref="ThaiIdCardData"/>. Every field is NotRead unless given.</summary>
    public sealed class ThaiIdCardQuality
    {
        /// <summary>Creates the qualities, for example for a consumer's mock.</summary>
        /// <param name="citizenId">Quality of the citizen ID.</param>
        /// <param name="thaiName">Quality of the Thai name.</param>
        /// <param name="englishName">Quality of the English name.</param>
        /// <param name="birthDate">Quality of the birth date.</param>
        /// <param name="sex">Quality of the sex.</param>
        /// <param name="address">Quality of the address.</param>
        /// <param name="issueDate">Quality of the issue date.</param>
        /// <param name="expiryDate">Quality of the expiry date.</param>
        /// <param name="issuer">Quality of the issuer.</param>
        /// <param name="photo">Quality of the photo.</param>
        public ThaiIdCardQuality(FieldQuality citizenId = FieldQuality.NotRead, FieldQuality thaiName = FieldQuality.NotRead,
            FieldQuality englishName = FieldQuality.NotRead, FieldQuality birthDate = FieldQuality.NotRead, FieldQuality sex = FieldQuality.NotRead,
            FieldQuality address = FieldQuality.NotRead, FieldQuality issueDate = FieldQuality.NotRead, FieldQuality expiryDate = FieldQuality.NotRead,
            FieldQuality issuer = FieldQuality.NotRead, FieldQuality photo = FieldQuality.NotRead)
        {
            CitizenId = citizenId;
            ThaiName = thaiName;
            EnglishName = englishName;
            BirthDate = birthDate;
            Sex = sex;
            Address = address;
            IssueDate = issueDate;
            ExpiryDate = expiryDate;
            Issuer = issuer;
            Photo = photo;
        }

        /// <summary>Quality of the citizen ID.</summary>
        public FieldQuality CitizenId { get; }

        /// <summary>Quality of the Thai name.</summary>
        public FieldQuality ThaiName { get; }

        /// <summary>Quality of the English name.</summary>
        public FieldQuality EnglishName { get; }

        /// <summary>Quality of the birth date.</summary>
        public FieldQuality BirthDate { get; }

        /// <summary>Quality of the sex.</summary>
        public FieldQuality Sex { get; }

        /// <summary>Quality of the address.</summary>
        public FieldQuality Address { get; }

        /// <summary>Quality of the issue date.</summary>
        public FieldQuality IssueDate { get; }

        /// <summary>Quality of the expiry date.</summary>
        public FieldQuality ExpiryDate { get; }

        /// <summary>Quality of the issuer.</summary>
        public FieldQuality Issuer { get; }

        /// <summary>Quality of the photo.</summary>
        public FieldQuality Photo { get; }
    }

    /// <summary>
    /// What one read of a card gave. Immutable, and constructible so consumers can mock a reader. A field that could not be
    /// read is empty and its <see cref="Quality"/> says so; a field that looks wrong keeps its value. Reading a card is not
    /// proof of identity and nothing here says otherwise. ToString never shows card data.
    /// </summary>
    public sealed class ThaiIdCardData
    {
        private readonly byte[]? _photo;

        /// <summary>Creates the data, for example for a consumer's mock. Every parameter is optional.</summary>
        /// <param name="citizenId">The citizen ID as the card holds it.</param>
        /// <param name="thaiName">The Thai name.</param>
        /// <param name="englishName">The English name.</param>
        /// <param name="birthDate">The birth date.</param>
        /// <param name="sex">The sex.</param>
        /// <param name="sexRaw">The sex byte as the card holds it.</param>
        /// <param name="address">The address.</param>
        /// <param name="issueDate">The issue date.</param>
        /// <param name="expiryDate">The expiry date.</param>
        /// <param name="issuer">The issuer.</param>
        /// <param name="photo">The JPEG bytes. The array is copied.</param>
        /// <param name="photoFailure">Why a photo that was asked for is missing.</param>
        /// <param name="quality">Quality per field.</param>
        /// <param name="diagnostics">Diagnostics of the read.</param>
        /// <param name="privateData">What private mode added. Null for a public read.</param>
        public ThaiIdCardData(string? citizenId = null, ThaiIdCardName? thaiName = null, ThaiIdCardName? englishName = null,
            ThaiCardDate? birthDate = null, CardSex sex = CardSex.Unknown, byte? sexRaw = null, ThaiIdCardAddress? address = null,
            ThaiCardDate? issueDate = null, ThaiCardDate? expiryDate = null, string? issuer = null, byte[]? photo = null,
            PhotoFailureReason photoFailure = PhotoFailureReason.None, ThaiIdCardQuality? quality = null, ReadDiagnostics? diagnostics = null,
            ThaiIdCardPrivateData? privateData = null)
        {
            CitizenId = citizenId;
            ThaiName = thaiName ?? new ThaiIdCardName();
            EnglishName = englishName ?? new ThaiIdCardName();
            BirthDate = birthDate ?? new ThaiCardDate();
            Sex = sex;
            SexRaw = sexRaw;
            Address = address ?? new ThaiIdCardAddress();
            IssueDate = issueDate ?? new ThaiCardDate();
            ExpiryDate = expiryDate ?? new ThaiCardDate();
            Issuer = issuer;
            _photo = photo == null ? null : (byte[])photo.Clone();
            PhotoFailure = photoFailure;
            Quality = quality ?? new ThaiIdCardQuality();
            Diagnostics = diagnostics ?? ReadDiagnostics.Empty();
            Private = privateData;
        }

        /// <summary>The citizen ID as the card holds it, even when malformed. Null when not read.</summary>
        public string? CitizenId { get; }

        /// <summary>
        /// True when <see cref="CitizenId"/> has 13 ASCII digits and a matching check digit. This checks the form only: it
        /// does not show that the number exists, that it belongs to the holder, or that the card is genuine.
        /// </summary>
        public bool IsValidCitizenId => FieldValidators.IsValidCitizenId(CitizenId);

        /// <summary>
        /// The raw first digit of a 13 digit <see cref="CitizenId"/>, else null. No meaning is attached on purpose: it does
        /// not tell nationality, age or current address.
        /// </summary>
        public string? PersonCategoryCode => FieldValidators.FirstDigitOfCitizenId(CitizenId);

        /// <summary>The Thai name.</summary>
        public ThaiIdCardName ThaiName { get; }

        /// <summary>The English name.</summary>
        public ThaiIdCardName EnglishName { get; }

        /// <summary>The birth date.</summary>
        public ThaiCardDate BirthDate { get; }

        /// <summary>The sex. See <see cref="SexRaw"/> for what the card holds.</summary>
        public CardSex Sex { get; }

        /// <summary>The sex byte as the card holds it, null when it was not read.</summary>
        public byte? SexRaw { get; }

        /// <summary>The address.</summary>
        public ThaiIdCardAddress Address { get; }

        /// <summary>The issue date.</summary>
        public ThaiCardDate IssueDate { get; }

        /// <summary>The expiry date.</summary>
        public ThaiCardDate ExpiryDate { get; }

        /// <summary>The issuer, null when not read.</summary>
        public string? Issuer { get; }

        /// <summary>Why a photo that was asked for is not given. <see cref="PhotoFailureReason.None"/> otherwise.</summary>
        public PhotoFailureReason PhotoFailure { get; }

        /// <summary>Quality per field.</summary>
        public ThaiIdCardQuality Quality { get; }

        /// <summary>Diagnostics of the read, which hold no card data. Empty for data built by hand.</summary>
        public ReadDiagnostics Diagnostics { get; }

        /// <summary>What <see cref="CardReadMode.PublicAndPrivate"/> added. Null in <see cref="CardReadMode.Public"/>.</summary>
        public ThaiIdCardPrivateData? Private { get; }

        /// <summary>The photo as JPEG bytes, from the start marker through the end marker.</summary>
        /// <returns>A new copy of the bytes, or null when there is no photo.</returns>
        public byte[]? GetPhoto() => _photo == null ? null : (byte[])_photo.Clone();

        /// <summary>Holds no card data, so logging this cannot leak anything from the card.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(ThaiIdCardData) + "(data hidden)";
    }
}
