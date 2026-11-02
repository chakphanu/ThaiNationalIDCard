using System;
using System.Threading;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard.Workflow
{
    /// <summary>
    /// One read of one card. Every failure leaves with its diagnostics attached, and nothing stays behind in this class.
    /// The channel is used for one read and disposed at its end.
    /// </summary>
    internal sealed class CardReadWorkflow
    {
        private readonly IRawCardChannel _channel;
        private readonly ReadPolicy _policy;
        private readonly Action<TimeSpan, CancellationToken> _delay;

        public CardReadWorkflow(IRawCardChannel channel, ReadPolicy? policy = null, Action<TimeSpan, CancellationToken>? delay = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _policy = policy ?? ReadPolicy.Default;
            _delay = delay ?? WaitOnToken;
        }

        public CardReadResult Read(CardReadRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using var connection = new GuardedCardConnection(_channel, GuardFor(request.Mode));
            var run = new ReadRun(connection, request, _policy, _delay, cancellationToken);

            try
            {
                return run.Execute();
            }
            catch (ThaiIdCardException ex)
            {
                ex.Diagnostics = run.Snapshot(ex.Failure);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new ReadCanceledException(run.Snapshot(CardFailureKind.None), cancellationToken);
            }
        }

        /// <summary>
        /// The field table alone keeps public mode away from private areas today, and the guard must hold even if that
        /// table is wrong. So this choice has its own test: nothing on the wire would show a wider guard.
        /// </summary>
        internal static CommandPolicy GuardFor(CardReadMode mode) =>
            mode == CardReadMode.Public ? CommandPolicy.Public : CommandPolicy.PublicAndPrivate;

        /// <summary>The only wait in a read. <see cref="ReadRun"/> calls it only with the transaction closed.</summary>
        private static void WaitOnToken(TimeSpan delay, CancellationToken token)
        {
            if (token.WaitHandle.WaitOne(delay))
            {
                token.ThrowIfCancellationRequested();
            }
        }
    }
}
