using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class DetectApduTypeTests
    {
        [Theory]
        [InlineData(new byte[] { 0x3B, 0x67 })]
        [InlineData(new byte[] { 0x3B, 0x67, 0x00, 0x00, 0x00, 0xFF })]
        public void Atr3B67IsType01(byte[] atr)
        {
            Assert.Equal(ApduType.Type01, ThaiIDCard.DetectApduType(atr));
        }

        [Theory]
        [InlineData(new byte[] { 0x3B, 0x79, 0x18, 0x00 })]
        [InlineData(new byte[] { 0x3B, 0x68 })]
        [InlineData(new byte[] { 0x3C, 0x67 })]
        [InlineData(new byte[] { 0x67, 0x3B })]
        [InlineData(new byte[] { 0x3B })]
        [InlineData(new byte[0])]
        public void AnyOtherAtrIsType02(byte[] atr)
        {
            Assert.Equal(ApduType.Type02, ThaiIDCard.DetectApduType(atr));
        }

        [Fact]
        public void NullAtrIsType02()
        {
            Assert.Equal(ApduType.Type02, ThaiIDCard.DetectApduType(null!));
        }
    }
}
