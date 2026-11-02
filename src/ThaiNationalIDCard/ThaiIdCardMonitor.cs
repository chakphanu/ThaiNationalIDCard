using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Hardware.Pcsc;

namespace ThaiNationalIDCard
{
    /// <summary>A card came or went.</summary>
    public sealed class ThaiIdCardEventArgs : EventArgs
    {
        /// <summary>Creates the arguments, for example for a consumer's mock.</summary>
        /// <param name="readerName">The reader.</param>
        /// <param name="atrHex">The ATR as hex text.</param>
        public ThaiIdCardEventArgs(string readerName, string? atrHex = null)
        {
            ReaderName = readerName;
            AtrHex = atrHex ?? string.Empty;
        }

        /// <summary>The reader, by the full name <see cref="IThaiIdCardReader.ReadAsync(string, ReadOptions, System.Threading.CancellationToken)"/> takes. It can hold a serial.</summary>
        public string ReaderName { get; }

        /// <summary>The ATR as hex text, empty for a removal. It says what the chip announced and nothing about the holder.</summary>
        public string AtrHex { get; }
    }

    /// <summary>Watching one reader failed and has stopped.</summary>
    public sealed class ThaiIdCardMonitorErrorEventArgs : EventArgs
    {
        /// <summary>Creates the arguments, for example for a consumer's mock.</summary>
        /// <param name="readerName">The reader.</param>
        /// <param name="failure">What went wrong.</param>
        public ThaiIdCardMonitorErrorEventArgs(string readerName, CardFailureKind failure)
        {
            ReaderName = readerName;
            Failure = failure;
        }

        /// <summary>The reader no longer watched.</summary>
        public string ReaderName { get; }

        /// <summary>What went wrong.</summary>
        public CardFailureKind Failure { get; }
    }

    /// <summary>
    /// Tells when a card comes or goes, on any number of readers. It only notifies: it never connects to a card and never reads
    /// one. To read, call <see cref="ThaiIdCardReader"/> with <see cref="ThaiIdCardEventArgs.ReaderName"/>.
    /// <para>
    /// Events are raised on a thread pool thread, one at a time and in order across all readers, so a handler that updates a UI
    /// must marshal to the UI thread itself, for example with Control.BeginInvoke or SynchronizationContext.Post. A handler that
    /// takes long holds up the events after it, though never the watching itself. An exception from a handler is dropped, as
    /// this thread has nobody to tell, and the next handler and the next event still run. Catch your own.
    /// </para>
    /// <para>
    /// Each reader has its own PC/SC monitor, since one reader that does not exist would otherwise stop the watch on all of them.
    /// Nothing here was run with a real reader yet.
    /// </para>
    /// </summary>
    public sealed class ThaiIdCardMonitor : IDisposable
    {
        private readonly Func<IReaderMonitor> _monitorFactory;
        private readonly Func<IReadOnlyList<string>> _listReaders;
        private readonly string[]? _configuredReaders;

        // Guards the watches. It is held while starting monitors, and a monitor thread only waits on it after a failure.
        private readonly object _gate = new object();
        private readonly Dictionary<string, Watch> _watches = new Dictionary<string, Watch>(StringComparer.Ordinal);
        private readonly object _deliveryGate = new object();

        private Task _lastDelivery = Task.CompletedTask;
        private int _generation;
        private bool _disposed;

        /// <summary>Creates a monitor that uses PC/SC. Nothing happens until <see cref="Start()"/>.</summary>
        public ThaiIdCardMonitor()
            : this(CreatePcscMonitor, ListPcscReaders)
        {
        }

        /// <summary>Creates a monitor of these readers over PC/SC. Nothing happens until <see cref="Start()"/>.</summary>
        /// <param name="readerNames">Reader names from <see cref="IThaiIdCardReader.GetReaderNames"/>.</param>
        /// <exception cref="ArgumentException">The list is empty or holds an empty name.</exception>
        public ThaiIdCardMonitor(IEnumerable<string> readerNames)
            : this(CreatePcscMonitor, ListPcscReaders, ValidateNames(readerNames))
        {
        }

        internal ThaiIdCardMonitor(Func<IReaderMonitor> monitorFactory, Func<IReadOnlyList<string>> listReaders, string[]? configuredReaders = null)
        {
            _monitorFactory = monitorFactory;
            _listReaders = listReaders;
            _configuredReaders = configuredReaders;
        }

        /// <summary>
        /// A card came into a watched reader. This includes a card already there when the watch of its reader started, so a
        /// caller needs no separate check for that. See the class remarks about the thread.
        /// </summary>
        public event EventHandler<ThaiIdCardEventArgs>? CardInserted;

        /// <summary>A card left a watched reader. See the class remarks about the thread.</summary>
        public event EventHandler<ThaiIdCardEventArgs>? CardRemoved;

        /// <summary>
        /// Watching one reader failed and has ended, for example because the reader does not exist or was unplugged. The other
        /// readers go on. <see cref="Start()"/> tries the failed reader again. See the class remarks about the thread.
        /// </summary>
        public event EventHandler<ThaiIdCardMonitorErrorEventArgs>? MonitorError;

        /// <summary>True while at least one reader is watched. A reader whose watch failed does not count.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _watches.Count > 0;
                }
            }
        }

        /// <summary>
        /// Watches the readers given when this monitor was created, or else every reader known right now. Calling it again adds
        /// readers that are new or whose watch failed, and leaves the others alone, so no event doubles. A reader plugged in later
        /// is not noticed until then.
        /// </summary>
        /// <exception cref="ReaderNotFoundException">There is no reader.</exception>
        /// <exception cref="ThaiIdCardException">PC/SC failed. No reader of this call is left watched.</exception>
        /// <exception cref="ObjectDisposedException">The monitor was disposed.</exception>
        public void Start()
        {
            ThrowIfDisposed();
            IReadOnlyList<string> names = _configuredReaders ?? _listReaders();
            if (names.Count == 0)
            {
                throw new ReaderNotFoundException();
            }

            AddWatches(names);
        }

        /// <summary>Watches these readers as well as any already watched. See <see cref="Start()"/>.</summary>
        /// <param name="readerNames">Reader names from <see cref="IThaiIdCardReader.GetReaderNames"/>.</param>
        /// <exception cref="ArgumentException">The list is empty or holds an empty name.</exception>
        /// <exception cref="ThaiIdCardException">PC/SC failed. No reader of this call is left watched.</exception>
        /// <exception cref="ObjectDisposedException">The monitor was disposed.</exception>
        public void Start(IEnumerable<string> readerNames) => AddWatches(ValidateNames(readerNames));

        /// <summary>
        /// Ends every watch and drops events not delivered yet. It can be called from a handler, and it does not wait for one: a
        /// handler already running finishes on its own thread, and none starts after this returns, apart from one that was just
        /// about to. <see cref="Start()"/> may follow.
        /// </summary>
        public void Stop()
        {
            Watch[] stopping;
            lock (_gate)
            {
                stopping = DetachAll();
            }

            foreach (Watch watch in stopping)
            {
                watch.Close();
            }
        }

        /// <summary>Stops, for good. Safe to repeat, and from a handler too. See <see cref="Stop"/>.</summary>
        public void Dispose()
        {
            Watch[] stopping;
            lock (_gate)
            {
                _disposed = true;
                stopping = DetachAll();
            }

            foreach (Watch watch in stopping)
            {
                watch.Close();
            }
        }

        private static string[] ValidateNames(IEnumerable<string> readerNames)
        {
            if (readerNames == null)
            {
                throw new ArgumentNullException(nameof(readerNames));
            }

            string[] names = readerNames.ToArray();
            if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("At least one reader name is needed and none may be empty.", nameof(readerNames));
            }

            return names;
        }

        private static IReaderMonitor CreatePcscMonitor()
        {
            using var channel = new PcscRawCardChannel();
            return channel.CreateMonitor();
        }

        private static IReadOnlyList<string> ListPcscReaders()
        {
            using var channel = new PcscRawCardChannel();
            return channel.ListReaders();
        }

        private static string ToHex(byte[] atr) => string.Join(" ", atr.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        /// <summary>One handler at a time, so that a handler throwing costs neither the ones after it nor the delivery chain.</summary>
        private void Raise<T>(EventHandler<T>? handlers, T args)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<T> handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
            {
                try
                {
                    handler(this, args);
                }
#pragma warning disable CA1031 // A bad handler must not cost the others, and this thread has nobody to tell.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        }

        private void AddWatches(IReadOnlyList<string> names)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                var added = new List<Watch>();
                try
                {
                    foreach (string name in names.Distinct(StringComparer.Ordinal).Where(n => !_watches.ContainsKey(n)))
                    {
                        var watch = new Watch(this, name, _monitorFactory(), _generation);
                        added.Add(watch);
                        _watches.Add(name, watch);
                        watch.Open();
                    }
                }
                catch
                {
                    foreach (Watch watch in added)
                    {
                        _watches.Remove(watch.Name);
                        watch.Active = false;
                        watch.Close();
                    }

                    throw;
                }
            }
        }

        private Watch[] DetachAll()
        {
            _generation++;
            Watch[] all = _watches.Values.ToArray();
            _watches.Clear();
            foreach (Watch watch in all)
            {
                watch.Active = false;
            }

            return all;
        }

        /// <summary>
        /// Queues a delivery and returns at once, and this is the only thing a monitor thread does here, so no caller code ever
        /// runs on a monitor thread. An exception there would end that thread and, as nothing above it catches, the process.
        /// A delivery of an earlier generation is dropped, because a stop came after it.
        /// </summary>
        private void Post(int generation, Action deliver)
        {
            lock (_deliveryGate)
            {
                _lastDelivery = _lastDelivery.ContinueWith(
                    _ =>
                    {
                        if (generation == Volatile.Read(ref _generation))
                        {
                            deliver();
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        private void OnFailed(Watch watch, CardFailureKind failure)
        {
            lock (_gate)
            {
                if (!watch.Active)
                {
                    return;
                }

                watch.Active = false;
                _watches.Remove(watch.Name);
            }

            var args = new ThaiIdCardMonitorErrorEventArgs(watch.Name, failure);
            Post(watch.Generation, () => Raise(MonitorError, args));

            // This runs on the monitor thread that is failing, which is no place to dispose its own monitor.
            Task.Run(watch.Close);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ThaiIdCardMonitor));
            }
        }

        /// <summary>The watch of one reader. Its monitor is never reused: a new watch gets a new one.</summary>
        private sealed class Watch
        {
            private readonly ThaiIdCardMonitor _owner;
            private readonly IReaderMonitor _monitor;
            private bool _active = true;
            private int _closed;

            public Watch(ThaiIdCardMonitor owner, string name, IReaderMonitor monitor, int generation)
            {
                _owner = owner;
                Name = name;
                _monitor = monitor;
                Generation = generation;
            }

            public string Name { get; }

            public int Generation { get; }

            /// <summary>False once stopped or failed, so anything a monitor still raises is ignored.</summary>
            public bool Active
            {
                get => Volatile.Read(ref _active);
                set => Volatile.Write(ref _active, value);
            }

            public void Open()
            {
                _monitor.CardInserted += OnInserted;
                _monitor.CardRemoved += OnRemoved;
                _monitor.Failed += OnFailed;
                _monitor.Start(new[] { Name });
            }

            /// <summary>Does not wait for the monitor thread, so a handler can get here. Nothing may escape from here.</summary>
            public void Close()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0)
                {
                    return;
                }

                _monitor.CardInserted -= OnInserted;
                _monitor.CardRemoved -= OnRemoved;
                _monitor.Failed -= OnFailed;

                try
                {
                    _monitor.Stop();
                }
                catch (ThaiIdCardException)
                {
                    // Dispose below still has to run, and nobody here could act on this.
                }

                _monitor.Dispose();
            }

            private void OnInserted(object? sender, ReaderCardEventArgs e)
            {
                if (Active)
                {
                    var args = new ThaiIdCardEventArgs(Name, ToHex(e.Atr));
                    _owner.Post(Generation, () => _owner.Raise(_owner.CardInserted, args));
                }
            }

            private void OnRemoved(object? sender, ReaderCardEventArgs e)
            {
                if (Active)
                {
                    var args = new ThaiIdCardEventArgs(Name);
                    _owner.Post(Generation, () => _owner.Raise(_owner.CardRemoved, args));
                }
            }

            private void OnFailed(object? sender, ReaderMonitorFailureEventArgs e) => _owner.OnFailed(this, e.Failure);
        }
    }
}
