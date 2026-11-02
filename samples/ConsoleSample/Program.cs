using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ThaiNationalIDCard.ConsoleSample
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Thai text needs this on a Windows console.
            Console.OutputEncoding = Encoding.UTF8;

            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancel.Cancel();
            };

            return await SampleApp.RunAsync(new ThaiIdCardReader(), args, Console.Out, cancel.Token);
        }
    }
}
