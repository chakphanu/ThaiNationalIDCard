namespace ThaiNationalIDCard
{
    /// <summary>
    /// Bytes of one card range as the card holds them, and how far they can be trusted. No meaning is attached. A read that
    /// gave no usable bytes has none here, and its <see cref="Quality"/> says so. ToString never shows the bytes.
    /// </summary>
    public sealed class CardRawValue
    {
        private readonly byte[]? _bytes;

        /// <summary>Creates a value, for example for a consumer's mock.</summary>
        /// <param name="bytes">The bytes. The array is copied.</param>
        /// <param name="quality">How the read of them went.</param>
        public CardRawValue(byte[]? bytes = null, FieldQuality quality = FieldQuality.NotRead)
        {
            _bytes = bytes == null ? null : (byte[])bytes.Clone();
            Quality = quality;
        }

        /// <summary>How the read of these bytes went.</summary>
        public FieldQuality Quality { get; }

        /// <summary>The bytes as the card holds them.</summary>
        /// <returns>A new copy of the bytes, or null when there are none.</returns>
        public byte[]? GetBytes() => _bytes == null ? null : (byte[])_bytes.Clone();

        /// <summary>Holds no card data, so logging this cannot leak anything from the card.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(CardRawValue) + "(data hidden)";
    }

    /// <summary>
    /// The nine ranges of the NHSO applet, named by offset inside that applet. The labels in the comments are what public code
    /// calls them. No card has confirmed them, and no source says whether the chip keeps up with the current rights of the
    /// holder. ToString never shows card data.
    /// </summary>
    public sealed class ThaiIdCardNhsoData
    {
        /// <summary>Creates the group, for example for a consumer's mock. Every field left out is NotRead.</summary>
        /// <param name="raw0004">Range 0004, 60 bytes.</param>
        /// <param name="raw0040">Range 0040, 100 bytes.</param>
        /// <param name="raw00A4">Range 00A4, 80 bytes.</param>
        /// <param name="raw00F4">Range 00F4, 80 bytes.</param>
        /// <param name="raw0144">Range 0144, 1 byte.</param>
        /// <param name="raw0145">Range 0145, 8 bytes.</param>
        /// <param name="raw014D">Range 014D, 8 bytes.</param>
        /// <param name="raw0155">Range 0155, 8 bytes.</param>
        /// <param name="raw015D">Range 015D, 1 byte.</param>
        public ThaiIdCardNhsoData(CardRawValue? raw0004 = null, CardRawValue? raw0040 = null, CardRawValue? raw00A4 = null,
            CardRawValue? raw00F4 = null, CardRawValue? raw0144 = null, CardRawValue? raw0145 = null, CardRawValue? raw014D = null,
            CardRawValue? raw0155 = null, CardRawValue? raw015D = null)
        {
            Raw0004 = raw0004 ?? new CardRawValue();
            Raw0040 = raw0040 ?? new CardRawValue();
            Raw00A4 = raw00A4 ?? new CardRawValue();
            Raw00F4 = raw00F4 ?? new CardRawValue();
            Raw0144 = raw0144 ?? new CardRawValue();
            Raw0145 = raw0145 ?? new CardRawValue();
            Raw014D = raw014D ?? new CardRawValue();
            Raw0155 = raw0155 ?? new CardRawValue();
            Raw015D = raw015D ?? new CardRawValue();
        }

        /// <summary>Range 0004, 60 bytes. Public code calls it main insurance.</summary>
        public CardRawValue Raw0004 { get; }

        /// <summary>Range 0040, 100 bytes. Public code calls it sub insurance.</summary>
        public CardRawValue Raw0040 { get; }

        /// <summary>Range 00A4, 80 bytes. Public code calls it main hospital.</summary>
        public CardRawValue Raw00A4 { get; }

        /// <summary>Range 00F4, 80 bytes. Public code calls it sub hospital.</summary>
        public CardRawValue Raw00F4 { get; }

        /// <summary>Range 0144, 1 byte. Public code calls it paid type.</summary>
        public CardRawValue Raw0144 { get; }

        /// <summary>Range 0145, 8 bytes. Public code calls it issue date.</summary>
        public CardRawValue Raw0145 { get; }

        /// <summary>Range 014D, 8 bytes. Public code calls it expiry date.</summary>
        public CardRawValue Raw014D { get; }

        /// <summary>Range 0155, 8 bytes. Public code calls it update date.</summary>
        public CardRawValue Raw0155 { get; }

        /// <summary>Range 015D, 1 byte. Public code calls it change hospital amount.</summary>
        public CardRawValue Raw015D { get; }

        /// <summary>Holds no card data, so logging this cannot leak anything from the card.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(ThaiIdCardNhsoData) + "(data hidden)";
    }

    /// <summary>
    /// What <see cref="CardReadMode.PublicAndPrivate"/> adds to a read. Each field has its own quality: an applet the card does
    /// not have or a bad status shows here and costs nothing else. Values whose meaning public sources disagree on are named by
    /// position, so no name here claims more than the evidence does. The caller is responsible for the holder's consent and for
    /// the law that applies. Nothing here was checked on a real card. ToString never shows card data.
    /// </summary>
    public sealed class ThaiIdCardPrivateData
    {
        /// <summary>Creates the data, for example for a consumer's mock. Every field left out is NotRead.</summary>
        /// <param name="laserId">The value taken as the Laser ID.</param>
        /// <param name="laserIdQuality">Quality of that value.</param>
        /// <param name="admAppletDataRaw">The whole answer it was taken from.</param>
        /// <param name="nhso">The NHSO ranges.</param>
        /// <param name="cardVersionRaw">MOI range 0000-0003.</param>
        /// <param name="moiRaw00E2">MOI range 00E2-00F5.</param>
        /// <param name="moiRaw0177">MOI range 0177-0178.</param>
        /// <param name="moiRaw1619">MOI range 1619-1626.</param>
        /// <param name="chipDataRaw">Chip data. A read never fills this today.</param>
        public ThaiIdCardPrivateData(string? laserId = null, FieldQuality laserIdQuality = FieldQuality.NotRead, CardRawValue? admAppletDataRaw = null,
            ThaiIdCardNhsoData? nhso = null, CardRawValue? cardVersionRaw = null, CardRawValue? moiRaw00E2 = null, CardRawValue? moiRaw0177 = null,
            CardRawValue? moiRaw1619 = null, CardRawValue? chipDataRaw = null)
        {
            LaserId = laserId;
            LaserIdQuality = laserIdQuality;
            AdmAppletDataRaw = admAppletDataRaw ?? new CardRawValue();
            Nhso = nhso ?? new ThaiIdCardNhsoData();
            CardVersionRaw = cardVersionRaw ?? new CardRawValue();
            MoiRaw00E2 = moiRaw00E2 ?? new CardRawValue();
            MoiRaw0177 = moiRaw0177 ?? new CardRawValue();
            MoiRaw1619 = moiRaw1619 ?? new CardRawValue();
            ChipDataRaw = chipDataRaw ?? new CardRawValue();
        }

        /// <summary>
        /// The value taken from the ADM applet answer, with its padding removed. That it is the Laser ID, where it sits and what it
        /// looks like are inferences from public code, so <see cref="LaserIdQuality"/> is Suspect when the answer does not have the
        /// assumed shape, and this is null when it cannot be cut out at all. <see cref="AdmAppletDataRaw"/> always keeps the answer.
        /// Treat this value as sensitive and do not log it.
        /// </summary>
        public string? LaserId { get; }

        /// <summary>Quality of <see cref="LaserId"/>.</summary>
        public FieldQuality LaserIdQuality { get; }

        /// <summary>The whole answer of the ADM applet data command, at most 23 bytes.</summary>
        public CardRawValue AdmAppletDataRaw { get; }

        /// <summary>The ranges of the NHSO applet.</summary>
        public ThaiIdCardNhsoData Nhso { get; }

        /// <summary>MOI range 0000-0003, 4 bytes, which public code reads as a card version. Its format is unconfirmed.</summary>
        public CardRawValue CardVersionRaw { get; }

        /// <summary>MOI range 00E2-00F5, 20 bytes. Public sources disagree on what it holds.</summary>
        public CardRawValue MoiRaw00E2 { get; }

        /// <summary>MOI range 0177-0178, 2 bytes. Public sources disagree on what it holds.</summary>
        public CardRawValue MoiRaw0177 { get; }

        /// <summary>MOI range 1619-1626, 14 bytes. Public sources disagree on what it holds.</summary>
        public CardRawValue MoiRaw1619 { get; }

        /// <summary>
        /// Always NotRead today. The only public source for chip data starts with a SELECT that names no applet, and what that
        /// selects cannot be bounded without a real card, so the command guard refuses it in every mode.
        /// </summary>
        public CardRawValue ChipDataRaw { get; }

        /// <summary>Holds no card data, so logging this cannot leak anything from the card.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(ThaiIdCardPrivateData) + "(data hidden)";
    }
}
