using System.Runtime.CompilerServices;
using System.Text;

namespace ThaiNationalIDCard.Tests
{
    internal static class TestEnvironment
    {
        [ModuleInitializer]
        internal static void Init()
        {
            // The library calls Encoding.GetEncoding("TIS-620"); modern .NET needs the code pages provider.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
