using System;
using System.Collections.Generic;
using System.Linq;

namespace ThaiNationalIDCard
{
    /// <summary>
    /// An address as the card holds it. A labelled property is set only when one part starts with that Thai label, and
    /// never from the position of a part: public sources disagree on the positions and no card with every part filled
    /// was found to settle it. <see cref="RawParts"/> keeps every part, so an unlabelled part such as a house number or a
    /// province with no label word is still there. ToString never shows the address.
    /// </summary>
    public sealed class ThaiIdCardAddress
    {
        /// <summary>Creates an address, for example for a consumer's mock.</summary>
        /// <param name="full">The whole address as one line.</param>
        /// <param name="rawParts">Every part in card order.</param>
        /// <param name="moo">The value after the moo label.</param>
        /// <param name="trok">The value after the trok label.</param>
        /// <param name="soi">The value after the soi label.</param>
        /// <param name="road">The value after the road label.</param>
        /// <param name="tambon">The value after the tambon label.</param>
        /// <param name="khwaeng">The value after the Bangkok khwaeng label.</param>
        /// <param name="amphoe">The value after the amphoe label.</param>
        /// <param name="khet">The value after the Bangkok khet label.</param>
        /// <param name="province">The value after the province label.</param>
        public ThaiIdCardAddress(string? full = null, IEnumerable<string>? rawParts = null, string? moo = null, string? trok = null,
            string? soi = null, string? road = null, string? tambon = null, string? khwaeng = null, string? amphoe = null,
            string? khet = null, string? province = null)
        {
            Full = full ?? string.Empty;
            RawParts = Array.AsReadOnly((rawParts ?? Enumerable.Empty<string>()).ToArray());
            Moo = moo;
            Trok = trok;
            Soi = soi;
            Road = road;
            Tambon = tambon;
            Khwaeng = khwaeng;
            Amphoe = amphoe;
            Khet = khet;
            Province = province;
        }

        /// <summary>The non-empty parts in card order on one line.</summary>
        public string Full { get; }

        /// <summary>Every part between separators in card order, trimmed of padding and with empty parts kept.</summary>
        public IReadOnlyList<string> RawParts { get; }

        /// <summary>The value after the moo label, null when no part has it.</summary>
        public string? Moo { get; }

        /// <summary>The value after the trok label, null when no part has it.</summary>
        public string? Trok { get; }

        /// <summary>The value after the soi label, null when no part has it.</summary>
        public string? Soi { get; }

        /// <summary>The value after the road label, null when no part has it.</summary>
        public string? Road { get; }

        /// <summary>The value after the tambon label, null when no part has it.</summary>
        public string? Tambon { get; }

        /// <summary>The value after the Bangkok khwaeng label, null when no part has it.</summary>
        public string? Khwaeng { get; }

        /// <summary>The value after the amphoe label, null when no part has it.</summary>
        public string? Amphoe { get; }

        /// <summary>The value after the Bangkok khet label, null when no part has it.</summary>
        public string? Khet { get; }

        /// <summary>The value after the province label, null when no part has it.</summary>
        public string? Province { get; }

        /// <summary>Holds no card data, so logging this cannot leak the address.</summary>
        /// <returns>A fixed text.</returns>
        public override string ToString() => nameof(ThaiIdCardAddress) + "(data hidden)";
    }
}
