using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Hardware.Pcsc;
using ThaiNationalIDCard.Parsing;
using ThaiNationalIDCard.Workflow;

namespace ThaiNationalIDCard
{
    /// <summary>
    /// Reads Thai national ID cards through PC/SC, with one read per call and nothing kept between calls. PC/SC itself is
    /// blocking and offers no asynchronous calls, so the Async methods here are not asynchronous I/O: each one holds a
    /// thread pool thread for the whole read, which is seconds with a photo. They do return to the caller at once.
    /// Reads of one reader run one after the other within this process, even from different instances. Other processes are
    /// kept out by the card transaction. Instances hold no state and can be shared. Reading a card shows what the chip holds,
    /// and is not proof of identity. No code here has been run against a real card yet.
    /// </summary>
    public sealed class ThaiIdCardReader : IThaiIdCardReader
    {
        private readonly Func<IRawCardChannel> _channelFactory;
        private readonly ReadPolicy? _policy;
        private readonly Action<TimeSpan, CancellationToken>? _delay;

        /// <summary>Creates a reader that uses PC/SC. It holds no connection and nothing happens until a call.</summary>
        public ThaiIdCardReader()
            : this(() => new PcscRawCardChannel())
        {
        }

        internal ThaiIdCardReader(Func<IRawCardChannel> channelFactory, ReadPolicy? policy = null, Action<TimeSpan, CancellationToken>? delay = null)
        {
            _channelFactory = channelFactory;
            _policy = policy;
            _delay = delay;
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetReaderNames()
        {
            using IRawCardChannel channel = _channelFactory();
            return channel.ListReaders().ToArray();
        }

        /// <inheritdoc/>
        public Task<ThaiIdCardData> ReadAsync(string readerName, ReadOptions? options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(readerName))
            {
                // Null must not mean "any reader" here: that could read the card of someone else in another reader.
                throw new ArgumentException("A reader name is required.", nameof(readerName));
            }

            ReadOptions snapshot = Snapshot(options);
            return Task.Run(() => ReadOne(readerName, snapshot, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public Task<ThaiIdCardData> ReadAsync(CancellationToken cancellationToken)
        {
            ReadOptions snapshot = Snapshot(null);
            return Task.Run(() => ReadFirstReadable(snapshot, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Private mode is refused here, before anything touches the card. The workflow can read those areas, but no public
        /// model exists yet to return them in, and reading sensitive data just to drop it would be worse than saying no.
        /// </summary>
        private static ReadOptions Snapshot(ReadOptions? options)
        {
            ReadOptions source = options ?? new ReadOptions();
            switch (source.Mode)
            {
                case CardReadMode.Public:
                    return new ReadOptions { Mode = source.Mode, IncludePhoto = source.IncludePhoto, PhotoProgress = source.PhotoProgress };
                case CardReadMode.PublicAndPrivate:
                    throw new NotSupportedException("CardReadMode.PublicAndPrivate is not available yet in this preview. Nothing was read.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(options), "The read mode is not a defined value.");
            }
        }

        /// <summary>A failure of the card in this reader, as opposed to one that would stop any further read too.</summary>
        private static bool MeansNotReadable(CardFailureKind failure) =>
            failure == CardFailureKind.NoApplet || failure == CardFailureKind.UnsupportedCard || failure == CardFailureKind.UnknownReadBehaviour ||
            failure == CardFailureKind.SharingViolation || failure == CardFailureKind.CardNotPresent;

        private static RawCardField Raw(CardReadResult result, CardFieldId id)
        {
            CardFieldResult field = result.Get(id);
            return new RawCardField(field.Data, field.Info.Quality);
        }

        private ThaiIdCardData ReadFirstReadable(ReadOptions options, CancellationToken token)
        {
            string[] holdingACard = ReadersHoldingACard();
            if (holdingACard.Length == 0)
            {
                // The workflow finds out again and raises the right failure with its diagnostics.
                return ReadOne(null, options, token);
            }

            ExceptionDispatchInfo? first = null;
            foreach (string reader in holdingACard)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return ReadOne(reader, options, token);
                }
                catch (ThaiIdCardException ex) when (MeansNotReadable(ex.Failure))
                {
                    first ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            first!.Throw();
            throw new InvalidOperationException();
        }

        private string[] ReadersHoldingACard()
        {
            try
            {
                using IRawCardChannel channel = _channelFactory();
                return channel.GetReaderStatuses().Where(s => s.State == ReaderCardState.Present).Select(s => s.Name).ToArray();
            }
            catch (ThaiIdCardException)
            {
                // Same as no reader: the read that follows meets the same failure and reports it with diagnostics.
                return Array.Empty<string>();
            }
        }

        private ThaiIdCardData ReadOne(string? readerName, ReadOptions options, CancellationToken token)
        {
            ProgressRelay? relay = options.PhotoProgress == null ? null : new ProgressRelay(options.PhotoProgress);
            var request = new CardReadRequest
            {
                ReaderName = readerName,
                Mode = options.Mode,
                IncludePhoto = options.IncludePhoto,
                PhotoProgress = relay == null ? (Action<int, int>?)null : relay.Post,
                AcquireReader = ReaderGates.Acquire,
            };

            CardReadResult result = new CardReadWorkflow(_channelFactory(), _policy, _delay).Read(request, token);
            try
            {
                var fields = new RawCardFields
                {
                    CitizenId = Raw(result, CardFieldId.CitizenId),
                    PersonInfo = Raw(result, CardFieldId.PersonInfo),
                    Address = Raw(result, CardFieldId.Address),
                    IssueExpire = Raw(result, CardFieldId.IssueExpire),
                    Issuer = Raw(result, CardFieldId.Issuer),
                    Photo = Raw(result, CardFieldId.Photo),
                };

                return CardDataParser.Parse(fields, result.Diagnostics);
            }
            finally
            {
                // Best effort, as buffers below this point were only released to the garbage collector.
                foreach (CardFieldResult field in result.Fields.Where(f => f.Data != null))
                {
                    Array.Clear(field.Data!, 0, field.Data!.Length);
                }
            }
        }

        /// <summary>
        /// One gate per reader for this whole process. The key is the name the workflow resolved, so a full name and its
        /// display form share a gate. A read waits here before it connects, so never inside a transaction, and only names are
        /// kept here.
        /// </summary>
        private static class ReaderGates
        {
            private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

            public static IDisposable Acquire(string reader, CancellationToken token)
            {
                SemaphoreSlim gate = Gates.GetOrAdd(reader, _ => new SemaphoreSlim(1, 1));
                gate.Wait(token);
                return new Release(gate);
            }

            private sealed class Release : IDisposable
            {
                private SemaphoreSlim? _gate;

                public Release(SemaphoreSlim gate)
                {
                    _gate = gate;
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref _gate, null)?.Release();
                }
            }
        }

        /// <summary>
        /// Hands progress to the caller's handler on the thread pool, one report after the other. The read thread only queues
        /// here, so caller code never runs on the thread holding the card transaction and cannot delay it.
        /// </summary>
        private sealed class ProgressRelay
        {
            private readonly IProgress<PhotoReadProgress> _handler;
            private readonly object _gate = new object();
            private Task _last = Task.CompletedTask;

            public ProgressRelay(IProgress<PhotoReadProgress> handler)
            {
                _handler = handler;
            }

            public void Post(int bytesRead, int totalBytes)
            {
                var progress = new PhotoReadProgress(bytesRead, totalBytes);
                lock (_gate)
                {
                    _last = _last.ContinueWith(_ => Deliver(progress), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                }
            }

            private void Deliver(PhotoReadProgress progress)
            {
                try
                {
                    _handler.Report(progress);
                }
#pragma warning disable CA1031 // A bad handler must not cost the read, and this thread has nobody to tell.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        }
    }
}
