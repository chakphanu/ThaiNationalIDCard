using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ThaiNationalIDCard.Card;

namespace ThaiNationalIDCard.Diagnose
{
    /// <summary>The notice printed before any card access. The private areas come from the same tables the read uses.</summary>
    internal static class NoticeText
    {
        public static IEnumerable<string> For(CardReadMode mode)
        {
            yield return "NOTICE: use this tool only on your own card or with the holder's consent.";
            yield return "The output holds no personal data read from the card: it has status words, lengths, pass or fail, reader model, ATR and OS. Check it before you share it.";

            if (mode == CardReadMode.PublicAndPrivate)
            {
                yield return "Private mode also reads these areas, whose meaning is unconfirmed:";
                yield return "  MOI applet offsets " + Ranges(MoiProfile.PublicAndPrivate.Fields.Where(f => f.IsPrivate));
                yield return "  NHSO applet offsets " + Ranges(NhsoProfile.Instance.Fields);
                yield return "  ADM applet data command";
                yield return "Their bytes are checked for status and length only, then dropped. Chip data is not read.";
            }
        }

        private static string Ranges(IEnumerable<CardField> fields) =>
            string.Join(", ", fields.Select(f => string.Format(
                CultureInfo.InvariantCulture, "{0:X4}-{1:X4}", f.Range.Offset, f.Range.EndExclusive - 1)));
    }
}
