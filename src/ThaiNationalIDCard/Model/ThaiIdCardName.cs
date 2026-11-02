namespace ThaiNationalIDCard
{
    /// <summary>
    /// A name as the card holds it. Segments are free text and never checked against a list. A missing segment is an
    /// empty string, so one missing segment cannot shift the next one. ToString never shows the name.
    /// </summary>
    public sealed class ThaiIdCardName
    {
        /// <summary>Creates a name, for example for a consumer's mock.</summary>
        /// <param name="prefix">The prefix, such as a title or a rank.</param>
        /// <param name="firstName">The first name.</param>
        /// <param name="middleName">The middle name.</param>
        /// <param name="lastName">The last name.</param>
        /// <param name="raw">The whole name slot as decoded text.</param>
        public ThaiIdCardName(string? prefix = null, string? firstName = null, string? middleName = null, string? lastName = null, string? raw = null)
        {
            Prefix = prefix ?? string.Empty;
            FirstName = firstName ?? string.Empty;
            MiddleName = middleName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            Raw = raw ?? string.Empty;
        }

        /// <summary>The prefix, such as a title or a rank. Free text, nothing is inferred from it. Empty when missing.</summary>
        public string Prefix { get; }

        /// <summary>The first name. Empty when missing.</summary>
        public string FirstName { get; }

        /// <summary>The middle name. Empty when missing.</summary>
        public string MiddleName { get; }

        /// <summary>The last name. Empty when missing.</summary>
        public string LastName { get; }

        /// <summary>The whole name slot as decoded text with its separators, for when the segments are not enough.</summary>
        public string Raw { get; }

        /// <summary>Holds no card data, so logging this cannot leak the name.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(ThaiIdCardName) + "(data hidden)";
    }
}
