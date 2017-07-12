
namespace ThaiNationalIDCard
{
    class APDU_THAILAND_IDCARD_TYPE_01 : APDU_THAILAND_IDCARD
    {
        public override byte[] APDU_GET_RESPONSE()
        {
            return new byte[] { 0x00, 0xc0, 0x00, 0x01 };
        }
    }
}
