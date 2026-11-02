using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ThaiNationalIDCard.ConsoleSample;
using Xunit;

namespace ThaiNationalIDCard.Diagnose.Tests
{
    /// <summary>The console sample over a mock reader, with made-up data. Samples get copied, so what they print matters.</summary>
    public class ConsoleSampleTests
    {
        private const string SyntheticId = "1234567890121";
        private const string LeadingDigits = "123456789";

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private sealed class MockReader : IThaiIdCardReader
        {
            private readonly Func<ThaiIdCardData> _read;

            public MockReader(Func<ThaiIdCardData> read)
            {
                _read = read;
            }

            public string? NamedReader { get; private set; }

            public ReadOptions? NamedOptions { get; private set; }

            public IReadOnlyList<string> GetReaderNames() => new[] { "Synthetic Reader" };

            public Task<ThaiIdCardData> ReadAsync(string readerName, ReadOptions? options, CancellationToken cancellationToken)
            {
                NamedReader = readerName;
                NamedOptions = options;
                return Task.FromResult(_read());
            }

            public Task<ThaiIdCardData> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(_read());
        }

        private static ThaiIdCardData Card(string? citizenId = SyntheticId, ThaiIdCardPrivateData? privateData = null) =>
            new ThaiIdCardData(
                citizenId,
                new ThaiIdCardName("Synth", "Prefixless", null, "Sample"),
                new ThaiIdCardName("Mr.", "Synthetic", null, "Sample"),
                new ThaiCardDate("25400100"),
                expiryDate: new ThaiCardDate("99999999"),
                quality: new ThaiIdCardQuality(citizenId: FieldQuality.Ok, birthDate: FieldQuality.Suspect),
                privateData: privateData);

        private static async Task<(int ExitCode, string Output)> Run(MockReader reader, params string[] args)
        {
            using var output = new StringWriter();
            int exitCode = await SampleApp.RunAsync(reader, args, output, Ct);
            return (exitCode, output.ToString());
        }

        [Fact]
        public async Task The_citizen_id_is_masked_by_default()
        {
            (int exitCode, string output) = await Run(new MockReader(() => Card()));

            Assert.Equal(SampleApp.Success, exitCode);
            Assert.DoesNotContain(SyntheticId, output, StringComparison.Ordinal);
            Assert.DoesNotContain(LeadingDigits, output, StringComparison.Ordinal);
            Assert.Contains("*********0121", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_whole_citizen_id_needs_the_option()
        {
            (_, string output) = await Run(new MockReader(() => Card()), "--show-citizen-id");

            Assert.Contains(SyntheticId, output, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null, "(none)")]
        [InlineData("", "(none)")]
        [InlineData("12345", "*****")]
        [InlineData("12345678901234", "**************")]
        public void A_citizen_id_of_another_length_is_masked_whole(string? citizenId, string expected)
        {
            Assert.Equal(expected, CardPrinter.MaskCitizenId(citizenId));
        }

        [Fact]
        public async Task Fields_print_with_their_quality_and_nothing_is_filled_in()
        {
            (_, string output) = await Run(new MockReader(() => Card()));

            Assert.Contains("[Ok]", output, StringComparison.Ordinal);
            Assert.Contains("Birth date: partly unknown (card holds: 25400100) [Suspect]", output, StringComparison.Ordinal);
            Assert.Contains("Expiry date: no expiry", output, StringComparison.Ordinal);
            Assert.Contains("does not prove", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_named_reader_is_read_in_public_mode()
        {
            var reader = new MockReader(() => Card());

            await Run(reader, "--reader", "Synthetic Reader");

            Assert.Equal("Synthetic Reader", reader.NamedReader);
            Assert.Equal(CardReadMode.Public, reader.NamedOptions!.Mode);
        }

        [Fact]
        public async Task Private_data_is_not_printed()
        {
            const string Sentinel = "SENTINELLASER";
            var extra = new ThaiIdCardPrivateData(Sentinel, FieldQuality.Ok);

            (_, string output) = await Run(new MockReader(() => Card(privateData: extra)), "--show-citizen-id");

            Assert.DoesNotContain(Sentinel, output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Bad_arguments_read_nothing()
        {
            var reader = new MockReader(() => throw new InvalidOperationException("Nothing should read."));

            (int exitCode, string output) = await Run(reader, "--mode", "private");

            Assert.Equal(SampleApp.BadArguments, exitCode);
            Assert.Contains("--reader", output, StringComparison.Ordinal);
        }

        public static TheoryData<Exception, int> Failures => new TheoryData<Exception, int>
        {
            { new SmartCardServiceUnavailableException(), SampleApp.ServiceUnavailable },
            { new SmartCardServiceUnavailableException(new DllNotFoundException("synthetic")), SampleApp.ServiceUnavailable },
            { new ReaderNotFoundException(), SampleApp.NoReader },
            { new CardNotPresentException(), SampleApp.NoCard },
            { new CardRemovedException(), SampleApp.CardRemoved },
            { new UnsupportedCardException(), SampleApp.UnsupportedCard },
            { new UnsupportedCardException(statusWord: 0x6A82), SampleApp.UnsupportedCard },
            { new UnknownReadBehaviourException(), SampleApp.UnknownReadBehaviour },
            { new CardCommunicationException(CardFailureKind.Timeout), SampleApp.CommunicationFailed },
            { new OperationCanceledException(), SampleApp.Canceled },
        };

        [Theory]
        [MemberData(nameof(Failures))]
        public async Task Each_exception_type_has_its_own_handling(Exception failure, int expectedExitCode)
        {
            (int exitCode, string output) = await Run(new MockReader(() => throw failure));

            Assert.Equal(expectedExitCode, exitCode);
            Assert.NotEmpty(output);
        }
    }
}
