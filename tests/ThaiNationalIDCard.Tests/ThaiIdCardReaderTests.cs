using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Hardware.Pcsc;
using ThaiNationalIDCard.Workflow;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// The public reader over the simulator. All data is made up, and passing here shows the code matches the simulator's
    /// assumptions. It says nothing about how a real card or reader behaves.
    /// </summary>
    public class ThaiIdCardReaderTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private static ThaiIdCardReader Reader(SimulatedCard card) => new ThaiIdCardReader(() => card, null, new DelaySpy(card).Wait);

        private static SimulatedCard Card(string? readerName = null, Action<SimulatedCard>? configure = null)
        {
            var card = new SimulatedCard();
            if (readerName != null)
            {
                card.ReaderName = readerName;
                card.Statuses[0] = new ReaderStatus(readerName, ReaderCardState.Present);
            }

            configure?.Invoke(card);
            return card;
        }

        private static string UniqueName() => "Synthetic Reader " + Guid.NewGuid().ToString("N");

#pragma warning disable xUnit1051 // The helper passes the test token itself.
        private static Task<ThaiIdCardData> ReadNamed(SimulatedCard card, ReadOptions? options = null) =>
            Reader(card).ReadAsync(card.ReaderName, options, Ct);
#pragma warning restore xUnit1051

        // ---- plain reads ----

        [Fact]
        public async Task A_default_read_returns_the_public_fields_without_the_photo()
        {
            SimulatedCard card = Card();

            ThaiIdCardData data = await ReadNamed(card);

            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
            Assert.True(data.IsValidCitizenId);
            Assert.Equal("Mr.", data.EnglishName.Prefix);
            Assert.Equal("Sample", data.EnglishName.LastName);
            Assert.Equal(new DateTime(1997, 1, 31), data.BirthDate.ToDateTime());
            Assert.Equal(CardSex.Male, data.Sex);
            Assert.Equal(new DateTime(2017, 1, 31), data.IssueDate.ToDateTime());
            Assert.Equal(new DateTime(2026, 1, 30), data.ExpiryDate.ToDateTime());
            Assert.Equal(SyntheticImage.IssuerText, data.Issuer);
            Assert.Equal("99/9", data.Address.RawParts[0]);
            Assert.Equal("Province Test", data.Address.RawParts[7]);
            Assert.Null(data.GetPhoto());
            Assert.Equal(FieldQuality.NotRead, data.Quality.Photo);
            Assert.Equal(FieldQuality.Ok, data.Quality.CitizenId);
            Assert.Equal(FieldQuality.Ok, data.Quality.Address);
            Assert.DoesNotContain(card.AnsweredOffsets(), o => o >= SyntheticImage.Photo && o < SyntheticImage.Address);
            Assert.Equal(CardReadMode.Public, data.Diagnostics.Mode);
            Assert.NotEmpty(data.Diagnostics.Trace);
            Assert.False(card.IsConnected);
        }

        [Fact]
        public async Task The_photo_is_read_when_asked_for_and_cut_at_its_end_marker()
        {
            SimulatedCard card = Card();
            byte[] jpeg = MiniJpeg.Build(0x20, 0x20, 0x11);
            Array.Copy(jpeg, 0, card.MoiImage, SyntheticImage.Photo, jpeg.Length);
            Array.Clear(card.MoiImage, SyntheticImage.Photo + jpeg.Length, SyntheticImage.PhotoLength - jpeg.Length);

            ThaiIdCardData data = await ReadNamed(card, new ReadOptions { IncludePhoto = true });

            Assert.Equal(jpeg, data.GetPhoto());
            Assert.Equal(FieldQuality.Ok, data.Quality.Photo);
        }

        [Fact]
        public async Task A_photo_that_does_not_walk_is_not_given_and_the_reason_is_reported()
        {
            SimulatedCard card = Card(configure: c => c.MoiImage[SyntheticImage.Photo] = 0x00);

            ThaiIdCardData data = await ReadNamed(card, new ReadOptions { IncludePhoto = true });

            Assert.Null(data.GetPhoto());
            Assert.Equal(PhotoFailureReason.NoStartMarker, data.PhotoFailure);
            Assert.Equal(FieldQuality.Failed, data.Quality.Photo);
            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
        }

        [Fact]
        public async Task A_failed_field_does_not_cost_the_others()
        {
            SimulatedCard card = Card();
            card.AnswerOverride = (position, command) => ApduText.Hex(command) == "80b000f6020064" ? new byte[] { 0x6A, 0x82 } : null;

            ThaiIdCardData data = await ReadNamed(card);

            Assert.Null(data.Issuer);
            Assert.Equal(FieldQuality.Failed, data.Quality.Issuer);
            Assert.Equal(FieldQuality.Ok, data.Quality.CitizenId);
            Assert.Equal(FieldQuality.Ok, data.Quality.Address);
        }

        // ---- modes ----

        [Fact]
        public async Task Public_mode_reaches_no_private_range_and_no_other_applet()
        {
            SimulatedCard card = Card();
            card.AddApplet(SimulatedCard.NhsoAidHex, new byte[0x200]);
            card.AddApplet(SimulatedCard.AdmAidHex, new byte[0x40]);

            await ReadNamed(card, new ReadOptions { IncludePhoto = true });

            int[] privateOffsets = Enumerable.Range(0, 4)
                .Concat(Enumerable.Range(SyntheticImage.Raw00E2, 20))
                .Concat(Enumerable.Range(SyntheticImage.Raw0177, 4))
                .Concat(Enumerable.Range(SyntheticImage.Raw1619, 14))
                .ToArray();
            Assert.Empty(card.AnsweredOffsets().Intersect(privateOffsets));
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.AdmAidHex));
            Assert.DoesNotContain(card.ReceivedHex, h => h.Contains(SimulatedCard.NhsoAidHex, StringComparison.Ordinal) || h.Contains(SimulatedCard.AdmAidHex, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Private_mode_is_refused_before_anything_touches_the_card()
        {
            int channels = 0;
            SimulatedCard card = Card();
            var reader = new ThaiIdCardReader(() =>
            {
                channels++;
                return card;
            });

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                reader.ReadAsync(card.ReaderName, new ReadOptions { Mode = CardReadMode.PublicAndPrivate }, Ct));

            Assert.Equal(0, channels);
            Assert.Equal(0, card.ConnectCalls);
            Assert.Equal(0, card.TransmitCount);
        }

        [Fact]
        public async Task An_undefined_mode_is_refused()
        {
            SimulatedCard card = Card();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadNamed(card, new ReadOptions { Mode = (CardReadMode)99 }));

            Assert.Equal(0, card.ConnectCalls);
        }

        [Fact]
        public void Read_options_defaults_are_public_and_no_photo()
        {
            var options = new ReadOptions();

            Assert.Equal(CardReadMode.Public, options.Mode);
            Assert.False(options.IncludePhoto);
            Assert.Null(options.PhotoProgress);
        }

        [Fact]
        public async Task Options_changed_after_the_call_do_not_affect_the_read()
        {
            using var firstCommand = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            SimulatedCard card = Card();
            card.AnswerOverride = (position, command) =>
            {
                firstCommand.Set();
                release.Wait(Patience);
                return null;
            };
            var options = new ReadOptions();

            Task<ThaiIdCardData> read = ReadNamed(card, options);
            Assert.True(firstCommand.Wait(Patience, Ct));
            options.IncludePhoto = true;
            options.Mode = CardReadMode.PublicAndPrivate;
            release.Set();

            ThaiIdCardData data = await read;

            Assert.Null(data.GetPhoto());
            Assert.Equal(CardReadMode.Public, data.Diagnostics.Mode);
        }

        // ---- reader names ----

        [Fact]
        public void Get_reader_names_lists_every_reader_and_closes_the_channel()
        {
            SimulatedCard card = Card();
            card.Statuses.Add(new ReaderStatus("Synthetic Reader 1", ReaderCardState.Empty));

            IReadOnlyList<string> names = Reader(card).GetReaderNames();

            Assert.Equal(new[] { "Synthetic Reader 0", "Synthetic Reader 1" }, names);
            Assert.Equal(1, card.DisposeCalls);
        }

        [Fact]
        public void Get_reader_names_is_empty_with_no_readers()
        {
            SimulatedCard card = Card();
            card.Statuses.Clear();

            Assert.Empty(Reader(card).GetReaderNames());
        }

        [Fact]
        public void A_missing_native_library_is_reported_as_the_service_being_unavailable()
        {
            var factory = new FakeContextFactory(new FakePcscContext()) { EstablishFailure = new DllNotFoundException() };
            var reader = new ThaiIdCardReader(() => new PcscRawCardChannel(() => factory, c => new FakePcscReader(), f => throw new InvalidOperationException()));

            var ex = Assert.Throws<SmartCardServiceUnavailableException>(() => reader.GetReaderNames());

            Assert.True(ex.NativeLibraryMissing);
        }

        [Fact]
        public async Task A_named_read_connects_to_that_reader_and_no_other()
        {
            SimulatedCard card = Card();
            card.Statuses.Insert(0, new ReaderStatus("Other Reader", ReaderCardState.Present));

            await Reader(card).ReadAsync("Synthetic Reader 0", null, Ct);

            Assert.Equal(new[] { "Synthetic Reader 0" }, card.ConnectedReaders);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task A_missing_name_is_refused_and_never_falls_back_to_any_reader(string? name)
        {
            SimulatedCard card = Card();

            await Assert.ThrowsAnyAsync<ArgumentException>(() => Reader(card).ReadAsync(name!, null, Ct));

            Assert.Equal(0, card.ConnectCalls);
        }

        [Fact]
        public async Task A_named_reader_with_no_card_is_not_replaced_by_another()
        {
            SimulatedCard card = Card();
            card.Statuses[0] = new ReaderStatus(card.ReaderName, ReaderCardState.Empty);
            card.Statuses.Add(new ReaderStatus("Other Reader", ReaderCardState.Present));

            var ex = await Assert.ThrowsAsync<CardNotPresentException>(() => Reader(card).ReadAsync("Synthetic Reader 0", null, Ct));

            Assert.NotNull(ex.Diagnostics);
            Assert.Empty(card.ConnectedReaders);
        }

        // ---- failures ----

        [Fact]
        public async Task An_unknown_reader_name_fails_with_diagnostics()
        {
            SimulatedCard card = Card();

            var ex = await Assert.ThrowsAsync<ReaderNotFoundException>(() => Reader(card).ReadAsync("No Such Reader", null, Ct));

            Assert.Equal(CardFailureKind.ReaderNotFound, ex.Diagnostics!.Failure);
        }

        [Fact]
        public async Task A_refused_applet_is_an_unsupported_card_with_its_status_word()
        {
            SimulatedCard card = Card(configure: c => c.SelectAnswer = SimSelectAnswer.NotFound6A82);

            var ex = await Assert.ThrowsAsync<UnsupportedCardException>(() => ReadNamed(card));

            Assert.Equal(CardFailureKind.NoApplet, ex.Failure);
            Assert.Equal(0x6A82, ex.StatusWord);
            Assert.False(card.IsConnected);
        }

        [Fact]
        public async Task A_connect_failure_arrives_as_the_library_exception()
        {
            SimulatedCard card = Card(configure: c => c.ConnectFailure = CardFailureKind.CardNotPresent);

            var ex = await Assert.ThrowsAsync<CardNotPresentException>(() => ReadNamed(card));

            Assert.NotNull(ex.Diagnostics);
        }

        [Fact]
        public async Task A_card_removed_during_the_read_fails_and_closes_everything()
        {
            SimulatedCard card = Card();
            card.FailTransmitAt(5, CardFailureKind.CardRemoved);

            var ex = await Assert.ThrowsAsync<CardRemovedException>(() => ReadNamed(card));

            Assert.Equal(CardFailureKind.CardRemoved, ex.Diagnostics!.Failure);
            Assert.False(card.InTransaction);
            Assert.False(card.IsConnected);
        }

        [Fact]
        public async Task A_short_atr_is_not_a_failure()
        {
            // 2.x refused an ATR under 2 bytes. The plan says the ATR only orders P2 and a short one must not fail.
            SimulatedCard card = Card(configure: c => c.Atr = new byte[] { 0x3B });

            ThaiIdCardData data = await ReadNamed(card);

            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
        }

        [Fact]
        public async Task An_atr_of_3b_67_tries_p2_01_first()
        {
            SimulatedCard card = Card(configure: c =>
            {
                c.Atr = new byte[] { 0x3B, 0x67, 0x00, 0x00, 0x00, 0xFF };
                c.GetResponseP2 = 0x01;
            });

            ThaiIdCardData data = await ReadNamed(card);

            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
            Assert.All(Flow.GetResponseP2s(card), p2 => Assert.Equal(0x01, p2));
        }

        // ---- first readable reader ----

        [Fact]
        public async Task Without_a_name_the_first_reader_holding_a_card_is_read()
        {
            SimulatedCard card = Card();
            card.Statuses.Insert(0, new ReaderStatus("Empty Reader", ReaderCardState.Empty));
            card.Statuses.Insert(1, new ReaderStatus("Mute Reader", ReaderCardState.Mute));
            card.Statuses.Add(new ReaderStatus("Later Reader", ReaderCardState.Present));

            await Reader(card).ReadAsync(Ct);

            Assert.Equal(new[] { "Synthetic Reader 0" }, card.ConnectedReaders);
        }

        [Fact]
        public async Task A_reader_whose_card_is_not_readable_is_passed_over()
        {
            var bank = new ReaderBank();
            SimulatedCard other = bank.Add("Reader A", c => c.SelectAnswer = SimSelectAnswer.NotFound6A82);
            SimulatedCard id = bank.Add("Reader B");

            ThaiIdCardData data = await new ThaiIdCardReader(bank.Open).ReadAsync(Ct);

            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
            Assert.Equal(1, other.ConnectCalls);
            Assert.Equal(1, id.ConnectCalls);
        }

        [Fact]
        public async Task When_no_card_is_readable_the_first_failure_is_reported()
        {
            var bank = new ReaderBank();
            bank.Add("Reader A", c => c.SelectAnswer = SimSelectAnswer.NotFound6A82);
            bank.Add("Reader B", c => c.ConnectFailure = CardFailureKind.UnsupportedCard);

            var ex = await Assert.ThrowsAsync<UnsupportedCardException>(() => new ThaiIdCardReader(bank.Open).ReadAsync(Ct));

            Assert.Equal(CardFailureKind.NoApplet, ex.Failure);
            Assert.NotNull(ex.Diagnostics);
        }

        [Theory]
        [InlineData((int)CardFailureKind.SharingViolation, 1)]
        [InlineData((int)CardFailureKind.CardNotPresent, 1)]
        [InlineData((int)CardFailureKind.Timeout, 0)]
        [InlineData((int)CardFailureKind.Communication, 0)]
        public async Task Only_a_failure_of_that_card_moves_on_to_the_next_reader(int failure, int laterConnects)
        {
            var bank = new ReaderBank();
            bank.Add("Reader A", c => c.ConnectFailure = (CardFailureKind)failure);
            SimulatedCard later = bank.Add("Reader B");
            var reader = new ThaiIdCardReader(bank.Open, new ReadPolicy(connectRetryDelays: Array.Empty<TimeSpan>()));

            if (laterConnects == 1)
            {
                Assert.Equal(SimulatedCard.SyntheticCitizenId, (await reader.ReadAsync(Ct)).CitizenId);
            }
            else
            {
                await Assert.ThrowsAsync<CardCommunicationException>(() => reader.ReadAsync(Ct));
            }

            Assert.Equal(laterConnects, later.ConnectCalls);
        }

        [Fact]
        public async Task An_unknown_read_behaviour_moves_on_to_the_next_reader()
        {
            var bank = new ReaderBank();
            bank.Add("Reader A", c => c.AnswerOverride = (position, command) => Flow.IsCidRead(command) ? new byte[] { 0x6F, 0x00 } : null);
            SimulatedCard later = bank.Add("Reader B");

            ThaiIdCardData data = await new ThaiIdCardReader(bank.Open).ReadAsync(Ct);

            Assert.Equal(SimulatedCard.SyntheticCitizenId, data.CitizenId);
            Assert.Equal(1, later.ConnectCalls);
        }

        [Fact]
        public async Task A_card_removed_during_the_read_stops_the_search()
        {
            var bank = new ReaderBank();
            bank.Add("Reader A", c => c.FailTransmitAt(5, CardFailureKind.CardRemoved));
            SimulatedCard later = bank.Add("Reader B");

            await Assert.ThrowsAsync<CardRemovedException>(() => new ThaiIdCardReader(bank.Open).ReadAsync(Ct));

            Assert.Equal(0, later.ConnectCalls);
        }

        [Fact]
        public async Task Without_a_name_and_with_no_card_or_reader_the_failure_has_diagnostics()
        {
            SimulatedCard empty = Card();
            empty.Statuses[0] = new ReaderStatus(empty.ReaderName, ReaderCardState.Empty);
            SimulatedCard none = Card();
            none.Statuses.Clear();

            var noCard = await Assert.ThrowsAsync<CardNotPresentException>(() => Reader(empty).ReadAsync(Ct));
            var noReader = await Assert.ThrowsAsync<ReaderNotFoundException>(() => Reader(none).ReadAsync(Ct));

            Assert.NotNull(noCard.Diagnostics);
            Assert.NotNull(noReader.Diagnostics);
        }

        // ---- threads ----

        [Fact]
        public async Task The_blocking_work_runs_on_the_thread_pool_and_the_call_returns_at_once()
        {
            using var release = new ManualResetEventSlim();
            var onPool = new ConcurrentBag<bool>();
            SimulatedCard card = Card();
            card.AnswerOverride = (position, command) =>
            {
                onPool.Add(Thread.CurrentThread.IsThreadPoolThread);
                release.Wait(Patience);
                return null;
            };

            Task<ThaiIdCardData> read = ReadNamed(card);

            Assert.False(read.IsCompleted);
            release.Set();
            await read;
            Assert.NotEmpty(onPool);
            Assert.All(onPool, Assert.True);
        }

        [Fact]
        public async Task A_token_cancelled_before_the_call_touches_nothing()
        {
            int channels = 0;
            SimulatedCard card = Card();
            var reader = new ThaiIdCardReader(() =>
            {
                channels++;
                return card;
            });
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(card.ReaderName, null, cancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cancelled.Token));

            Assert.Equal(0, channels);
        }

        [Fact]
        public async Task Cancelling_stops_between_photo_chunks_and_closes_the_transaction()
        {
            using var source = new CancellationTokenSource();
            string third = ApduText.Hex(ApduText.Read(SyntheticImage.Photo + (2 * 255), 0xFF));
            string fourth = ApduText.Hex(ApduText.Read(SyntheticImage.Photo + (3 * 255), 0xFF));
            SimulatedCard card = Card();
            card.AnswerOverride = (position, command) =>
            {
                if (ApduText.Hex(command) == third)
                {
                    source.Cancel();
                }

                return null;
            };

            Task<ThaiIdCardData> read = Reader(card).ReadAsync(card.ReaderName, new ReadOptions { IncludePhoto = true }, source.Token);

            var ex = await Assert.ThrowsAsync<ReadCanceledException>(() => read);
            Assert.True(read.IsCanceled);
            Assert.NotEmpty(ex.Diagnostics.Trace);
            Assert.Contains(third, card.ReceivedHex);
            Assert.DoesNotContain(fourth, card.ReceivedHex);
            Assert.False(card.InTransaction);
            Assert.Equal(1, card.EndTransactionCalls);
            Assert.False(card.IsConnected);
        }

        // ---- one read per reader ----

        [Fact]
        public async Task Two_reads_on_one_reader_do_not_overlap_even_from_two_instances()
        {
            string name = UniqueName();
            var tracker = new OverlapTracker();
            ThaiIdCardReader MakeReader() => new ThaiIdCardReader(() => tracker.Wrap(Card(name)));

            ThaiIdCardData[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => MakeReader().ReadAsync(name, null, Ct)));

            Assert.Equal(1, tracker.MostAtOnce);
            Assert.Equal(6, tracker.Connections);
            Assert.All(results, r => Assert.Equal(SimulatedCard.SyntheticCitizenId, r.CitizenId));
        }

        [Fact]
        public async Task A_full_name_and_its_display_form_share_one_lock()
        {
            string serial = "(" + Guid.NewGuid().ToString("N") + ")";
            string full = "Synthetic Model " + serial;
            string shown = ReaderNames.RemoveSerial(full);
            Assert.NotEqual(full, shown);
            var tracker = new OverlapTracker();
            var reader = new ThaiIdCardReader(() => tracker.Wrap(Card(full)));

            await Task.WhenAll(reader.ReadAsync(full, null, Ct), reader.ReadAsync(shown, null, Ct), reader.ReadAsync(full, null, Ct), reader.ReadAsync(shown, null, Ct));

            Assert.Equal(1, tracker.MostAtOnce);
            Assert.Equal(4, tracker.Connections);
        }

        [Fact]
        public async Task Different_readers_do_not_wait_for_each_other()
        {
            using var inRead = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            SimulatedCard slow = Card(UniqueName());
            slow.AnswerOverride = (position, command) =>
            {
                inRead.Set();
                release.Wait(Patience);
                return null;
            };
            SimulatedCard quick = Card(UniqueName());

            Task<ThaiIdCardData> blocked = ReadNamed(slow);
            Assert.True(inRead.Wait(Patience, Ct));

            await ReadNamed(quick).WaitAsync(Patience, Ct);

            Assert.False(blocked.IsCompleted);
            release.Set();
            await blocked;
        }

        [Fact]
        public async Task A_read_waiting_for_its_reader_can_be_cancelled_and_the_card_stays_untouched_by_it()
        {
            string name = UniqueName();
            using var inRead = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            SimulatedCard first = Card(name);
            first.AnswerOverride = (position, command) =>
            {
                inRead.Set();
                release.Wait(Patience);
                return null;
            };
            SimulatedCard second = Card(name);
            using var source = new CancellationTokenSource();

            Task<ThaiIdCardData> running = ReadNamed(first);
            Assert.True(inRead.Wait(Patience, Ct));
            Task<ThaiIdCardData> waiting = Reader(second).ReadAsync(name, null, source.Token);
            await source.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting).WaitAsync(Patience, Ct);

            Assert.Equal(0, second.ConnectCalls);
            release.Set();
            await running;
        }

        [Fact]
        public async Task A_failed_read_lets_the_next_read_of_that_reader_through()
        {
            string name = UniqueName();
            SimulatedCard failing = Card(name, c => c.SelectAnswer = SimSelectAnswer.NotFound6A82);

            await Assert.ThrowsAsync<UnsupportedCardException>(() => ReadNamed(failing));

            await ReadNamed(Card(name)).WaitAsync(Patience, Ct);
        }

        // ---- progress ----

        private sealed class RecordingProgress : IProgress<PhotoReadProgress>
        {
            private readonly ConcurrentQueue<PhotoReadProgress> _seen = new ConcurrentQueue<PhotoReadProgress>();
            private readonly TaskCompletionSource<bool> _whole = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PhotoReadProgress[] Seen => _seen.ToArray();

            public Task Whole => _whole.Task;

            public void Report(PhotoReadProgress value)
            {
                _seen.Enqueue(value);
                if (value.BytesRead == value.TotalBytes)
                {
                    _whole.TrySetResult(true);
                }
            }
        }

        [Fact]
        public async Task Progress_is_reported_per_chunk_in_order_up_to_the_whole_range()
        {
            var progress = new RecordingProgress();
            SimulatedCard card = Card();

            await ReadNamed(card, new ReadOptions { IncludePhoto = true, PhotoProgress = progress });
            await progress.Whole.WaitAsync(Patience, Ct);

            int[] expected = Enumerable.Range(1, 20).Select(i => i * 255).Concat(new[] { SyntheticImage.PhotoLength }).ToArray();
            Assert.Equal(expected, progress.Seen.Select(p => p.BytesRead));
            Assert.All(progress.Seen, p => Assert.Equal(SyntheticImage.PhotoLength, p.TotalBytes));
        }

        [Fact]
        public async Task No_progress_without_a_photo()
        {
            var progress = new RecordingProgress();

            await ReadNamed(Card(), new ReadOptions { IncludePhoto = false, PhotoProgress = progress });

            Assert.Empty(progress.Seen);
        }

        private sealed class BlockingProgress : IProgress<PhotoReadProgress>
        {
            private readonly ManualResetEventSlim _release;

            public BlockingProgress(ManualResetEventSlim release)
            {
                _release = release;
            }

            public int Calls;

            public void Report(PhotoReadProgress value)
            {
                Interlocked.Increment(ref Calls);
                _release.Wait(Patience);
            }
        }

        [Fact]
        public async Task A_handler_that_blocks_cannot_hold_the_transaction_or_the_read()
        {
            using var release = new ManualResetEventSlim();
            var progress = new BlockingProgress(release);
            SimulatedCard card = Card();

            ThaiIdCardData data = await ReadNamed(card, new ReadOptions { IncludePhoto = true, PhotoProgress = progress }).WaitAsync(Patience, Ct);

            Assert.NotNull(data.GetPhoto());
            Assert.False(card.InTransaction);
            Assert.True(Volatile.Read(ref progress.Calls) <= 1);
            release.Set();
        }

        private sealed class ThrowingProgress : IProgress<PhotoReadProgress>
        {
            public void Report(PhotoReadProgress value) => throw new InvalidOperationException("from the handler");
        }

        [Fact]
        public async Task A_handler_that_throws_does_not_cost_the_read()
        {
            ThaiIdCardData data = await ReadNamed(Card(), new ReadOptions { IncludePhoto = true, PhotoProgress = new ThrowingProgress() });

            Assert.NotNull(data.GetPhoto());
        }

        // ---- what consumers can do with the public surface ----

        private sealed class MockReader : IThaiIdCardReader
        {
            public IReadOnlyList<string> GetReaderNames() => new[] { "Mock" };

            public Task<ThaiIdCardData> ReadAsync(string readerName, ReadOptions? options, CancellationToken cancellationToken) =>
                Task.FromResult(new ThaiIdCardData(
                    citizenId: "1234567890121",
                    thaiName: new ThaiIdCardName(firstName: "A"),
                    expiryDate: new ThaiCardDate("99999999"),
                    address: new ThaiIdCardAddress(full: "F", rawParts: new[] { "F" }),
                    quality: new ThaiIdCardQuality(citizenId: FieldQuality.Ok)));

            public Task<ThaiIdCardData> ReadAsync(CancellationToken cancellationToken) => throw new CardNotPresentException();
        }

        [Fact]
        public async Task A_consumer_can_mock_the_reader_with_public_members_only()
        {
            IThaiIdCardReader mock = new MockReader();

            ThaiIdCardData data = await mock.ReadAsync("Mock", new ReadOptions { IncludePhoto = true }, Ct);

            Assert.True(data.IsValidCitizenId);
            Assert.Equal("A", data.ThaiName.FirstName);
            Assert.True(data.ExpiryDate.IsLifetime);
            Assert.Equal(FieldQuality.Ok, data.Quality.CitizenId);
            Assert.Equal(FieldQuality.NotRead, data.Quality.Photo);
            Assert.Empty(data.Diagnostics.Trace);
            await Assert.ThrowsAsync<CardNotPresentException>(() => mock.ReadAsync(Ct));
        }
    }

    /// <summary>Several simulated cards behind one channel, each in its own reader.</summary>
    internal sealed class ReaderBank
    {
        private readonly List<SimulatedCard> _cards = new List<SimulatedCard>();

        public SimulatedCard Add(string reader, Action<SimulatedCard>? configure = null)
        {
            var card = new SimulatedCard { ReaderName = reader };
            configure?.Invoke(card);
            _cards.Add(card);
            return card;
        }

        public IRawCardChannel Open() => new Channel(_cards);

        private sealed class Channel : IRawCardChannel
        {
            private readonly List<SimulatedCard> _cards;
            private SimulatedCard? _active;

            public Channel(List<SimulatedCard> cards)
            {
                _cards = cards;
            }

            public IReadOnlyList<string> ListReaders() => _cards.Select(c => c.ReaderName).ToList();

            public IReadOnlyList<ReaderStatus> GetReaderStatuses() => _cards.Select(c => new ReaderStatus(c.ReaderName, ReaderCardState.Present)).ToList();

            public CardConnectionInfo Connect(string readerName)
            {
                _active = _cards.First(c => c.ReaderName == readerName);
                return _active.Connect(readerName);
            }

            public void Disconnect() => _active?.Disconnect();

            public void BeginTransaction() => _active!.BeginTransaction();

            public void EndTransaction() => _active!.EndTransaction();

            public byte[] Transmit(byte[] command) => _active!.Transmit(command);

            public IReaderMonitor CreateMonitor() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Counts how many connections are open at once over every channel it wraps.</summary>
    internal sealed class OverlapTracker
    {
        private int _open;
        private int _most;
        private int _connections;

        public int MostAtOnce => Volatile.Read(ref _most);

        public int Connections => Volatile.Read(ref _connections);

        public IRawCardChannel Wrap(SimulatedCard card) => new Channel(card, this);

        private void Opened()
        {
            Interlocked.Increment(ref _connections);
            int now = Interlocked.Increment(ref _open);
            int seen;
            while (now > (seen = Volatile.Read(ref _most)))
            {
                Interlocked.CompareExchange(ref _most, now, seen);
            }

            // Gives a wrongly parallel read the time to be seen.
            Thread.Sleep(5);
        }

        private void Closed() => Interlocked.Decrement(ref _open);

        private sealed class Channel : IRawCardChannel
        {
            private readonly SimulatedCard _card;
            private readonly OverlapTracker _tracker;
            private bool _isOpen;

            public Channel(SimulatedCard card, OverlapTracker tracker)
            {
                _card = card;
                _tracker = tracker;
            }

            public IReadOnlyList<string> ListReaders() => _card.ListReaders();

            public IReadOnlyList<ReaderStatus> GetReaderStatuses() => _card.GetReaderStatuses();

            public CardConnectionInfo Connect(string readerName)
            {
                CardConnectionInfo info = _card.Connect(readerName);
                _isOpen = true;
                _tracker.Opened();
                return info;
            }

            public void Disconnect()
            {
                if (_isOpen)
                {
                    _isOpen = false;
                    _tracker.Closed();
                }

                _card.Disconnect();
            }

            public void BeginTransaction() => _card.BeginTransaction();

            public void EndTransaction() => _card.EndTransaction();

            public byte[] Transmit(byte[] command) => _card.Transmit(command);

            public IReaderMonitor CreateMonitor() => _card.CreateMonitor();

            public void Dispose() => Disconnect();
        }
    }
}
