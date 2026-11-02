using System;
using System.Runtime.InteropServices;
using System.Threading;
using ThaiNationalIDCard.Hardware.Pcsc;

namespace ThaiNationalIDCard.Diagnose
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                // Let the read end through its finally blocks, so the transaction gets closed.
                e.Cancel = true;
                cancellation.Cancel();
            };

            return DiagnoseApp.Run(args, Console.Out, Console.Error, () => new PcscRawCardChannel(), CurrentPlatform(), cancellation.Token);
        }

        private static HostPlatform CurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return HostPlatform.Linux;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? HostPlatform.Windows : HostPlatform.Other;
        }
    }
}
