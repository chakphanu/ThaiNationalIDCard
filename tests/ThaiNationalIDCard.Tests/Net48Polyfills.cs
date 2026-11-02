#if NETFRAMEWORK
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>What the tests use from newer frameworks, so that the same tests also run on net48 over the netstandard2.0 asset.</summary>
    internal static class Net48Polyfills
    {
        public static Task CancelAsync(this CancellationTokenSource source)
        {
            source.Cancel();
            return Task.CompletedTask;
        }

        public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken token)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task finished = await Task.WhenAny(task, Task.Delay(timeout, stop.Token));
            if (finished != task)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }

            stop.Cancel();
            await task;
        }

        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken token)
        {
            await ((Task)task).WaitAsync(timeout, token);
            return await task;
        }
    }
}
#endif
