using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThaiNationalIDCard.Hardware;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// The monitor over fake reader monitors, with events raised by hand. This shows the logic above the seam. It says nothing
    /// about when real PC/SC raises them, and nothing here has run with a real reader.
    /// </summary>
    public class ThaiIdCardMonitorTests
    {
        private const int PatienceMs = 30000;

        private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(PatienceMs);

        private static readonly byte[] Atr = { 0x3B, 0x67, 0x00 };

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public void Readers_given_at_creation_are_what_Start_watches()
        {
            var created = new List<SimulatedReaderMonitor>();
            using var monitor = new ThaiIdCardMonitor(
                () =>
                {
                    var made = new SimulatedReaderMonitor();
                    created.Add(made);
                    return made;
                },
                () => throw new InvalidOperationException("Start must not list readers when they were given."),
                new[] { "Reader A", "Reader B" });

            monitor.Start();
            monitor.Start();

            Assert.Equal(new[] { "Reader A", "Reader B" }, created.SelectMany(m => m.StartedReaders));
        }

        [Fact]
        public void Creating_with_no_reader_names_is_refused_before_any_card_access()
        {
            Assert.Throws<ArgumentException>(() => new ThaiIdCardMonitor(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => new ThaiIdCardMonitor(new[] { " " }));
        }

        private sealed class Rig : IDisposable
        {
            private readonly List<SimulatedReaderMonitor> _created = new List<SimulatedReaderMonitor>();

            public Rig(params string[] readers)
            {
                Readers = readers;
                Monitor = new ThaiIdCardMonitor(Create, () => Readers);
                Monitor.CardInserted += (s, e) => Events.Add("in " + e.ReaderName + " " + e.AtrHex);
                Monitor.CardRemoved += (s, e) => Events.Add("out " + e.ReaderName + " " + e.AtrHex);
                Monitor.MonitorError += (s, e) => Events.Add("error " + e.ReaderName + " " + e.Failure);
            }

            public string[] Readers { get; set; }

            public ThaiIdCardMonitor Monitor { get; }

            public BlockingCollection<string> Events { get; } = new BlockingCollection<string>();

            public Func<int, Exception?> StartFailure { get; set; } = _ => null;

            public SimulatedReaderMonitor[] Created
            {
                get
                {
                    lock (_created)
                    {
                        return _created.ToArray();
                    }
                }
            }

            /// <summary>The newest monitor watching this reader.</summary>
            public SimulatedReaderMonitor For(string reader) => Created.Last(m => m.StartedReaders.Contains(reader));

            public string Next()
            {
                Assert.True(Events.TryTake(out string? item, PatienceMs, Ct), "no event arrived");
                return item!;
            }

            public void Dispose() => Monitor.Dispose();

            private SimulatedReaderMonitor Create()
            {
                var monitor = new SimulatedReaderMonitor();
                lock (_created)
                {
                    monitor.StartThrows = StartFailure(_created.Count);
                    _created.Add(monitor);
                }

                return monitor;
            }
        }

        /// <summary>Runs an action on a thread of its own, as the monitor thread would. An escape fails the test.</summary>
        private static void OnMonitorThread(Action action)
        {
            Exception? escaped = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
#pragma warning disable CA1031 // Reported on the test thread just below.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    escaped = ex;
                }
            });
            thread.Start();
            Assert.True(thread.Join(Patience));
            Assert.Null(escaped);
        }

        // ---- what it reports ----

        [Fact]
        public void Each_reader_reports_under_its_own_name_with_the_atr()
        {
            using var rig = new Rig("Reader A", "Reader B");

            rig.Monitor.Start();
            rig.For("Reader B").RaiseInserted("Reader B", Atr);
            rig.For("Reader A").RaiseInserted("Reader A", new byte[] { 0x3B, 0x79 });
            rig.For("Reader B").RaiseRemoved("Reader B");

            Assert.Equal("in Reader B 3B 67 00", rig.Next());
            Assert.Equal("in Reader A 3B 79", rig.Next());
            Assert.Equal("out Reader B ", rig.Next());
        }

        [Fact]
        public void Every_reader_gets_a_monitor_of_its_own_so_one_failure_cannot_stop_another()
        {
            using var rig = new Rig("Reader A", "Reader B", "Reader C");

            rig.Monitor.Start();

            Assert.Equal(3, rig.Created.Length);
            Assert.All(rig.Created, m => Assert.Single(m.StartedReaders));
            Assert.True(rig.Monitor.IsRunning);
        }

        [Fact]
        public void Start_with_names_watches_only_those_and_counts_each_once()
        {
            using var rig = new Rig("Reader A", "Reader B");

            rig.Monitor.Start(new[] { "Reader B", "Reader B" });

            SimulatedReaderMonitor only = Assert.Single(rig.Created);
            Assert.Equal(new[] { "Reader B" }, only.StartedReaders);
        }

        [Fact]
        public void Start_with_bad_names_is_refused_before_anything_starts()
        {
            using var rig = new Rig("Reader A");

            Assert.Throws<ArgumentNullException>(() => rig.Monitor.Start(null!));
            Assert.Throws<ArgumentException>(() => rig.Monitor.Start(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => rig.Monitor.Start(new[] { "Reader A", " " }));
            Assert.Throws<ArgumentException>(() => rig.Monitor.Start(new[] { "Reader A", null! }));

            Assert.Empty(rig.Created);
            Assert.False(rig.Monitor.IsRunning);
        }

        [Fact]
        public void Start_with_no_reader_says_so()
        {
            using var rig = new Rig();

            Assert.Throws<ReaderNotFoundException>(() => rig.Monitor.Start());

            Assert.False(rig.Monitor.IsRunning);
            Assert.Empty(rig.Created);
        }

        [Fact]
        public void Start_passes_on_a_service_failure_from_listing_the_readers()
        {
            using var monitor = new ThaiIdCardMonitor(() => new SimulatedReaderMonitor(), () => throw new SmartCardServiceUnavailableException());

            Assert.Throws<SmartCardServiceUnavailableException>(() => monitor.Start());
            Assert.False(monitor.IsRunning);
        }

        // ---- never reads ----

        [Fact]
        public void Cycles_of_events_never_touch_the_card()
        {
            var card = new SimulatedCard();
            using var rig = new Rig(card.ReaderName);

            rig.Monitor.Start();
            for (int i = 0; i < 5; i++)
            {
                rig.For(card.ReaderName).RaiseInserted(card.ReaderName, Atr);
                rig.For(card.ReaderName).RaiseRemoved(card.ReaderName);
                rig.Next();
                rig.Next();
            }

            Assert.Equal(0, card.ConnectCalls);
            Assert.Equal(0, card.TransmitCount);
        }

        // Issue #16 reports a card read only the first time. Its cause is not known, so this does not show that it is fixed: it
        // only shows that nothing above the seam stops working after the first cycle.
        [Fact]
        public async Task Removing_and_reinserting_keeps_working_with_a_read_after_every_insert()
        {
            const int Cycles = 25;
            var card = new SimulatedCard();
            var reader = new ThaiIdCardReader(() => card, null, new DelaySpy(card).Wait);
            using var rig = new Rig(card.ReaderName);
            using var reads = new BlockingCollection<Task<ThaiIdCardData>>();
            rig.Monitor.CardInserted += (s, e) => reads.Add(reader.ReadAsync(e.ReaderName, null, Ct), Ct);

            rig.Monitor.Start();
            for (int i = 0; i < Cycles; i++)
            {
                rig.For(card.ReaderName).RaiseInserted(card.ReaderName, Atr);

                Assert.True(reads.TryTake(out Task<ThaiIdCardData>? read, PatienceMs, Ct));
                ThaiIdCardData data = await read!;
                Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
                Assert.Equal("in " + card.ReaderName + " 3B 67 00", rig.Next());

                rig.For(card.ReaderName).RaiseRemoved(card.ReaderName);
                Assert.Equal("out " + card.ReaderName + " ", rig.Next());
                Assert.False(card.IsConnected);
            }

            Assert.Single(rig.Created);
            Assert.Equal(Cycles, card.ConnectCalls);
        }

        // ---- threads ----

        [Fact]
        public void Handlers_run_on_a_background_thread_and_not_on_the_monitor_thread()
        {
            using var rig = new Rig("Reader A");
            using var seen = new ManualResetEventSlim();
            int handlerThread = 0;
            bool background = false;
            rig.Monitor.CardInserted += (s, e) =>
            {
                handlerThread = Environment.CurrentManagedThreadId;
                background = Thread.CurrentThread.IsBackground;
                seen.Set();
            };
            rig.Monitor.Start();

            int monitorThread = 0;
            OnMonitorThread(() =>
            {
                monitorThread = Environment.CurrentManagedThreadId;
                rig.For("Reader A").RaiseInserted("Reader A", Atr);
            });

            Assert.True(seen.Wait(Patience, Ct));
            Assert.NotEqual(monitorThread, handlerThread);
            Assert.True(background);
        }

        [Fact]
        public void A_slow_handler_does_not_hold_up_the_monitor_thread_and_order_is_kept()
        {
            using var rig = new Rig("Reader A");
            using var inside = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            rig.Monitor.CardInserted += (s, e) =>
            {
                inside.Set();
                release.Wait(Patience);
            };
            rig.Monitor.Start();
            SimulatedReaderMonitor monitor = rig.For("Reader A");

            // Both raises return with the first handler still blocked, or this never gets to release it.
            OnMonitorThread(() =>
            {
                monitor.RaiseInserted("Reader A", Atr);
                Assert.True(inside.Wait(Patience));
                monitor.RaiseRemoved("Reader A");
                monitor.RaiseInserted("Reader A", new byte[] { 0x3B });
            });
            release.Set();

            Assert.Equal("in Reader A 3B 67 00", rig.Next());
            Assert.Equal("out Reader A ", rig.Next());
            Assert.Equal("in Reader A 3B", rig.Next());
        }

        [Fact]
        public void A_throwing_handler_costs_neither_the_monitor_thread_nor_later_handlers_or_events()
        {
            using var rig = new Rig("Reader A");
            rig.Monitor.Start();
            rig.Monitor.CardInserted += (s, e) => throw new InvalidOperationException("handler failure");
            rig.Monitor.CardRemoved += (s, e) => throw new InvalidOperationException("handler failure");
            rig.Monitor.MonitorError += (s, e) => throw new InvalidOperationException("handler failure");
            var after = new BlockingCollection<string>();
            rig.Monitor.CardInserted += (s, e) => after.Add("in");
            SimulatedReaderMonitor monitor = rig.For("Reader A");

            for (int i = 0; i < 3; i++)
            {
                OnMonitorThread(() =>
                {
                    monitor.RaiseInserted("Reader A", Atr);
                    monitor.RaiseRemoved("Reader A");
                });

                Assert.True(after.TryTake(out _, PatienceMs, Ct));
                Assert.StartsWith("in ", rig.Next(), StringComparison.Ordinal);
                Assert.StartsWith("out ", rig.Next(), StringComparison.Ordinal);
            }

            Assert.True(rig.Monitor.IsRunning);
            OnMonitorThread(() => monitor.RaiseFailed(CardFailureKind.Communication));
            Assert.Equal("error Reader A Communication", rig.Next());
        }

        [Fact]
        public void Stop_and_dispose_from_inside_a_handler_do_not_hang()
        {
            using var rig = new Rig("Reader A");
            using var done = new ManualResetEventSlim();
            rig.Monitor.CardInserted += (s, e) =>
            {
                rig.Monitor.Stop();
                rig.Monitor.Dispose();
                done.Set();
            };

            rig.Monitor.Start();
            rig.For("Reader A").RaiseInserted("Reader A", Atr);

            Assert.True(done.Wait(Patience, Ct));
            Assert.False(rig.Monitor.IsRunning);
            Assert.Equal(1, rig.Created[0].DisposeCalls);
        }

        // ---- start, stop, dispose ----

        [Fact]
        public void Starting_twice_adds_nothing_and_each_event_arrives_once()
        {
            using var rig = new Rig("Reader A", "Reader B");

            rig.Monitor.Start();
            rig.Monitor.Start();
            rig.Monitor.Start(new[] { "Reader A" });

            Assert.Equal(2, rig.Created.Length);
            rig.For("Reader A").RaiseInserted("Reader A", Atr);
            rig.For("Reader A").RaiseRemoved("Reader A");

            Assert.Equal("in Reader A 3B 67 00", rig.Next());
            Assert.Equal("out Reader A ", rig.Next());
            Assert.Empty(rig.Events);
        }

        [Fact]
        public void A_later_start_adds_a_reader_that_was_not_there_before()
        {
            using var rig = new Rig("Reader A");

            rig.Monitor.Start();
            rig.Readers = new[] { "Reader A", "Reader B" };
            rig.Monitor.Start();

            Assert.Equal(2, rig.Created.Length);
            rig.For("Reader B").RaiseInserted("Reader B", Atr);
            Assert.Equal("in Reader B 3B 67 00", rig.Next());
        }

        [Fact]
        public void Stop_releases_every_monitor_and_is_safe_to_repeat_or_call_first()
        {
            using var rig = new Rig("Reader A", "Reader B");

            rig.Monitor.Stop();
            rig.Monitor.Start();
            rig.Monitor.Stop();
            rig.Monitor.Stop();

            Assert.False(rig.Monitor.IsRunning);
            Assert.All(rig.Created, m =>
            {
                Assert.True(m.Stopped);
                Assert.Equal(1, m.DisposeCalls);
                Assert.Equal(0, m.SubscriberCount);
            });
        }

        [Fact]
        public void Start_after_stop_uses_new_monitors_and_gives_each_event_once()
        {
            using var rig = new Rig("Reader A");

            for (int i = 0; i < 5; i++)
            {
                rig.Monitor.Start();
                rig.For("Reader A").RaiseInserted("Reader A", Atr);
                Assert.Equal("in Reader A 3B 67 00", rig.Next());
                rig.Monitor.Stop();
            }

            Assert.Equal(5, rig.Created.Length);
            Assert.All(rig.Created, m => Assert.Equal(1, m.DisposeCalls));
            Assert.Empty(rig.Events);
        }

        [Fact]
        public void An_event_from_a_stopped_monitor_never_arrives()
        {
            using var rig = new Rig("Reader A");
            rig.Monitor.Start();
            SimulatedReaderMonitor old = rig.For("Reader A");
            EventHandler<ReaderCardEventArgs>? stale = null;
            old.CardInserted += stale = (s, e) => { };

            rig.Monitor.Stop();
            old.RaiseInserted("Reader A", Atr);
            old.RaiseFailed(CardFailureKind.Communication);
            rig.Monitor.Start();
            rig.For("Reader A").RaiseRemoved("Reader A");

            Assert.Equal("out Reader A ", rig.Next());
            old.CardInserted -= stale;
        }

        [Fact]
        public void Queued_events_are_dropped_by_stop_and_do_not_come_back_after_a_new_start()
        {
            using var rig = new Rig("Reader A");
            using var inside = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int calls = 0;
            rig.Monitor.CardInserted += (s, e) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    inside.Set();
                    release.Wait(Patience);
                }
            };
            rig.Monitor.Start();
            SimulatedReaderMonitor first = rig.For("Reader A");

            first.RaiseInserted("Reader A", Atr);
            Assert.True(inside.Wait(Patience, Ct));
            first.RaiseInserted("Reader A", Atr);
            first.RaiseRemoved("Reader A");
            rig.Monitor.Stop();
            rig.Monitor.Start();
            release.Set();
            rig.For("Reader A").RaiseRemoved("Reader A");

            Assert.Equal("in Reader A 3B 67 00", rig.Next());
            Assert.Equal("out Reader A ", rig.Next());
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.Empty(rig.Events);
        }

        [Fact]
        public void Dispose_is_safe_to_repeat_and_start_afterwards_is_refused()
        {
            var rig = new Rig("Reader A");
            rig.Monitor.Start();

            rig.Monitor.Dispose();
            rig.Monitor.Dispose();
            rig.Monitor.Stop();

            Assert.Equal(1, rig.Created[0].DisposeCalls);
            Assert.Equal(0, rig.Created[0].SubscriberCount);
            Assert.False(rig.Monitor.IsRunning);
            Assert.Throws<ObjectDisposedException>(() => rig.Monitor.Start());
            Assert.Throws<ObjectDisposedException>(() => rig.Monitor.Start(new[] { "Reader A" }));
        }

        [Fact]
        public void A_monitor_failing_to_start_leaves_nothing_running_and_a_later_start_works()
        {
            using var rig = new Rig("Reader A", "Reader B");
            rig.StartFailure = index => index == 1 ? new SmartCardServiceUnavailableException() : null;

            Assert.Throws<SmartCardServiceUnavailableException>(() => rig.Monitor.Start());

            Assert.False(rig.Monitor.IsRunning);
            Assert.All(rig.Created, m =>
            {
                Assert.Equal(1, m.DisposeCalls);
                Assert.Equal(0, m.SubscriberCount);
            });

            rig.StartFailure = _ => null;
            rig.Monitor.Start();
            rig.For("Reader B").RaiseInserted("Reader B", Atr);
            Assert.Equal("in Reader B 3B 67 00", rig.Next());
        }

        [Fact]
        public void Racing_start_stop_and_dispose_leave_no_monitor_behind()
        {
            using var rig = new Rig("Reader A", "Reader B");

            Parallel.For(0, 400, new ParallelOptions { CancellationToken = Ct }, i =>
            {
                if (i % 2 == 0)
                {
                    rig.Monitor.Start();
                }
                else
                {
                    rig.Monitor.Stop();
                }
            });
            rig.Monitor.Dispose();

            Assert.False(rig.Monitor.IsRunning);
            Assert.All(rig.Created, m =>
            {
                Assert.Equal(1, m.DisposeCalls);
                Assert.Equal(0, m.SubscriberCount);
            });
            Assert.All(new[] { "Reader A", "Reader B" }, r =>
                Assert.Contains(rig.Created, m => m.StartedReaders.Contains(r)));
        }

        // ---- errors ----

        [Fact]
        public void A_failure_reports_its_reader_and_the_other_reader_keeps_going()
        {
            using var rig = new Rig("Reader A", "Reader B");
            rig.Monitor.Start();
            SimulatedReaderMonitor a = rig.For("Reader A");

            OnMonitorThread(() => a.RaiseFailed(CardFailureKind.ReaderNotFound));

            Assert.Equal("error Reader A ReaderNotFound", rig.Next());
            Assert.True(a.Disposed.Wait(Patience, Ct));
            Assert.Equal(0, a.SubscriberCount);
            Assert.True(rig.Monitor.IsRunning);

            rig.For("Reader B").RaiseInserted("Reader B", Atr);
            Assert.Equal("in Reader B 3B 67 00", rig.Next());
        }

        [Fact]
        public void When_the_last_reader_fails_it_is_no_longer_running()
        {
            using var rig = new Rig("Reader A");
            rig.Monitor.Start();
            rig.For("Reader A").RaiseFailed(CardFailureKind.ServiceUnavailable);

            Assert.Equal("error Reader A ServiceUnavailable", rig.Next());

            Assert.False(rig.Monitor.IsRunning);
        }

        [Fact]
        public void Start_after_a_failure_watches_only_the_failed_reader_again_with_a_new_monitor()
        {
            using var rig = new Rig("Reader A", "Reader B");
            rig.Monitor.Start();
            SimulatedReaderMonitor failed = rig.For("Reader A");
            SimulatedReaderMonitor healthy = rig.For("Reader B");
            failed.RaiseFailed(CardFailureKind.ReaderNotFound);
            rig.Next();

            rig.Monitor.Start();

            Assert.Equal(3, rig.Created.Length);
            Assert.NotSame(failed, rig.For("Reader A"));
            Assert.Same(healthy, rig.For("Reader B"));

            rig.For("Reader A").RaiseInserted("Reader A", Atr);
            Assert.Equal("in Reader A 3B 67 00", rig.Next());
            Assert.Empty(rig.Events);
        }

        [Fact]
        public void A_second_failure_from_the_same_monitor_is_reported_once()
        {
            using var rig = new Rig("Reader A", "Reader B");
            rig.Monitor.Start();
            SimulatedReaderMonitor a = rig.For("Reader A");
            EventHandler<ReaderMonitorFailureEventArgs>? kept = null;
            a.Failed += kept = (s, e) => { };

            a.RaiseFailed(CardFailureKind.ReaderNotFound);
            a.RaiseFailed(CardFailureKind.ReaderNotFound);
            rig.For("Reader B").RaiseRemoved("Reader B");

            Assert.Equal("error Reader A ReaderNotFound", rig.Next());
            Assert.Equal("out Reader B ", rig.Next());
            a.Failed -= kept;
        }

        // ---- public shape ----

        [Fact]
        public void Event_args_can_be_built_by_a_consumer_and_show_no_atr_for_a_removal()
        {
            var inserted = new ThaiIdCardEventArgs("Reader A", "3B 67");
            var removed = new ThaiIdCardEventArgs("Reader A");
            var error = new ThaiIdCardMonitorErrorEventArgs("Reader A", CardFailureKind.ReaderNotFound);

            Assert.Equal("3B 67", inserted.AtrHex);
            Assert.Equal(string.Empty, removed.AtrHex);
            Assert.Equal("Reader A", error.ReaderName);
            Assert.Equal(CardFailureKind.ReaderNotFound, error.Failure);
        }

        [Fact]
        public void The_public_constructors_do_nothing_until_start()
        {
            using var allReaders = new ThaiIdCardMonitor();
            using var givenReaders = new ThaiIdCardMonitor(new[] { "Reader A" });

            Assert.False(allReaders.IsRunning);
            Assert.False(givenReaders.IsRunning);
        }
    }
}
