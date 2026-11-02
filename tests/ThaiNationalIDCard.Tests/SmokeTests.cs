using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void LibraryTypeIsAvailable()
        {
            Assert.Equal("ThaiNationalIDCard", typeof(ThaiIdCardReader).Namespace);
        }
    }
}
