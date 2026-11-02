using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using PCSC.Exceptions;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Hardware.Pcsc;
using ThaiNationalIDCard.Tests;
using Xunit;
using SCardError = PCSC.SCardError;

namespace ThaiNationalIDCard.Diagnose.Tests
{
    public class DiagnoseAppTests
    {
        private static PcscRawCardChannel Missing(Exception failure) =>
            new PcscRawCardChannel(() => throw failure, context => throw failure, factory => throw failure);

        // ---- report ----

        [Fact]
        public void Text_report_has_versions_os_reader_protocol_atr_trace_p2_and_fields()
        {
            var card = new SimulatedCard();

            ToolRun run = Tool.Run(card);

            Assert.Equal(ExitCodes.Success, run.ExitCode);
            Assert.Contains("tool version: 3.0.0-preview", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("library version: 3.0.0-preview", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("os: ", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("mode: public", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("reader: Synthetic Reader 0", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("protocol: T0", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("atr: 3B 79 18 00 00 FF", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("p2 used: 00", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("kind=Select", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("kind=Read offset=0004 requested=13 received=0 sw=610D", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("kind=GetResponse", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("event=P2Locked", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("CitizenId: pass", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("Photo: pass", run.Stdout, StringComparison.Ordinal);
            Assert.Empty(run.Stderr);
        }

        [Theory]
        [InlineData("--mode", "public")]
        [InlineData("--mode", "private")]
        public void The_tool_never_asks_the_workflow_to_keep_card_bytes(params string[] args)
        {
            DiagnoseOptions options = DiagnoseOptions.Parse(args, out _)!;

            Assert.False(DiagnoseApp.BuildRequest(options).RetainFieldData);
        }

        [Fact]
        public void A_read_built_like_the_tool_does_returns_no_bytes()
        {
            SimulatedCard card = SentinelCard.Build();
            DiagnoseOptions options = DiagnoseOptions.Parse(new[] { "--mode", "private" }, out _)!;

            var result = new ThaiNationalIDCard.Workflow.CardReadWorkflow(card).Read(DiagnoseApp.BuildRequest(options), TestContext.Current.CancellationToken);

            Assert.All(result.Fields, f => Assert.Null(f.Data));
            Assert.NotEmpty(result.Fields);
        }

        [Fact]
        public void Report_says_when_p2_played_no_part()
        {
            ToolRun run = Tool.Run(new SimulatedCard { Mode = SimReaderMode.DirectAnswer });

            Assert.Contains("p2 used: none", run.Stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void A_field_that_is_not_ok_says_fail_with_the_reason()
        {
            var card = new SimulatedCard();
            card.MoiImage[SyntheticImage.PersonInfo + 208] = (byte)'9';

            ToolRun run = Tool.Run(card);

            Assert.Equal(ExitCodes.Success, run.ExitCode);
            Assert.Contains("PersonInfo: fail quality=Suspect", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("checks_failed=SexCode", run.Stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void Reader_serial_is_never_printed()
        {
            const string Named = "Vendor Model (A1B2C3D4E5) 00 00";
            var card = new SimulatedCard { ReaderName = Named };
            card.Statuses[0] = new ReaderStatus(Named, ReaderCardState.Present);

            ToolRun text = Tool.Run(card);
            var again = new SimulatedCard { ReaderName = Named };
            again.Statuses[0] = new ReaderStatus(Named, ReaderCardState.Present);
            ToolRun json = Tool.Run(again, "--json");

            foreach (ToolRun run in new[] { text, json })
            {
                Assert.Contains("Vendor Model 00 00", run.All, StringComparison.Ordinal);
                Assert.DoesNotContain("A1B2C3D4E5", run.All, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Machine_and_user_names_are_not_printed()
        {
            ToolRun run = Tool.Run(new SimulatedCard());

            Assert.DoesNotContain(Environment.MachineName, run.All, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.UserName, run.All, StringComparison.Ordinal);
        }

        [Fact]
        public void All_output_is_ascii()
        {
            var card = SentinelCard.Build();
            card.ReaderName = "Vendor \u0E01 Model 0";
            card.Statuses[0] = new ReaderStatus(card.ReaderName, ReaderCardState.Present);

            ToolRun run = Tool.Run(card, "--mode", "private");

            Assert.All(run.All, c => Assert.True(c <= 0x7E && (c >= 0x20 || c == '\n' || c == '\r')));
        }

        [Fact]
        public void Tool_and_library_report_the_same_version()
        {
            ToolRun run = Tool.Run(new SimulatedCard());
            string[] lines = run.Stdout.Split('\n');
            string tool = lines.First(l => l.StartsWith("tool version: ", StringComparison.Ordinal)).Substring(14).Trim();
            string library = lines.First(l => l.StartsWith("library version: ", StringComparison.Ordinal)).Substring(17).Trim();

            Assert.Equal(tool, library);
        }

        // ---- private mode ----

        [Fact]
        public void Private_mode_reports_every_private_field_by_status_and_length_only()
        {
            SimulatedCard card = SentinelCard.Build();

            ToolRun run = Tool.Run(card, "--mode", "private");

            Assert.Equal(ExitCodes.Success, run.ExitCode);
            foreach (string name in new[] { "MoiRaw0000", "MoiRaw00E2", "MoiRaw0177", "MoiRaw1619", "NhsoRaw0004", "NhsoRaw015D", "AdmAppletData" })
            {
                Assert.Contains(name + ": pass", run.Stdout, StringComparison.Ordinal);
            }

            Assert.Contains("mode: private", run.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(card.ReceivedHex, h => h.StartsWith("80ca", StringComparison.Ordinal));
        }

        [Fact]
        public void Public_mode_does_not_read_private_areas()
        {
            SimulatedCard card = SentinelCard.Build();

            Tool.Run(card);

            Assert.Empty(card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.AdmAidHex));
            Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
        }

        // ---- notice ----

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Notice_comes_before_any_card_access(bool json)
        {
            SimulatedCard card = SentinelCard.Build();
            using var stdout = new WatchedWriter();
            using var stderr = new WatchedWriter();
            int connectsAtNotice = -1;
            WatchedWriter noticeStream = json ? stderr : stdout;
            noticeStream.WriteSeen = () => connectsAtNotice = card.ConnectCalls + card.TransmitCount;

            string[] args = json ? new[] { "--json" } : Array.Empty<string>();
            DiagnoseApp.Run(args, stdout, stderr, () => card, HostPlatform.Linux, TestContext.Current.CancellationToken);

            Assert.Equal(0, connectsAtNotice);
            Assert.Contains("consent", noticeStream.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void Notice_covers_consent_and_why_the_output_is_safe_to_share()
        {
            ToolRun run = Tool.Run(new SimulatedCard());

            Assert.Contains("your own card", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("holder's consent", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("no personal data read from the card", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("Check it before you share it", run.Stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void Private_notice_lists_the_extra_areas_and_the_public_notice_does_not()
        {
            string[] areas = { "0000-0003", "00E2-00F5", "0177-0178", "1619-1626", "NHSO", "ADM" };
            ToolRun publicRun = Tool.Run(new SimulatedCard());
            ToolRun privateRun = Tool.Run(new SimulatedCard(), "--mode", "private");

            foreach (string area in areas)
            {
                Assert.Contains(area, privateRun.Stdout, StringComparison.Ordinal);
                Assert.DoesNotContain(area, publicRun.Stdout, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Notice_claims_no_meaning_for_unconfirmed_areas()
        {
            ToolRun run = Tool.Run(new SimulatedCard(), "--mode", "private");

            string notice = run.Stdout.Substring(0, run.Stdout.IndexOf("tool version", StringComparison.Ordinal));
            Assert.DoesNotContain("religion", notice, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unconfirmed", notice, StringComparison.Ordinal);
        }

        [Fact]
        public void Tool_never_waits_for_input()
        {
            TextReader original = Console.In;
            try
            {
                Console.SetIn(new ThrowingReader());

                Assert.Equal(ExitCodes.Success, Tool.Run(new SimulatedCard(), "--mode", "private").ExitCode);
            }
            finally
            {
                Console.SetIn(original);
            }
        }

        private sealed class ThrowingReader : TextReader
        {
            public override int Read() => throw new InvalidOperationException("The tool must not read input.");

            public override string? ReadLine() => throw new InvalidOperationException("The tool must not read input.");
        }

        // ---- JSON ----

        [Fact]
        public void Json_stdout_is_one_valid_document_and_the_notice_goes_to_stderr()
        {
            ToolRun run = Tool.Run(new SimulatedCard(), "--json");

            using JsonDocument document = JsonDocument.Parse(run.Stdout);
            JsonElement root = document.RootElement;
            Assert.StartsWith("3.0.0-preview", root.GetProperty("tool_version").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("3.0.0-preview", root.GetProperty("library_version").GetString(), StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(root.GetProperty("os").GetString()));
            Assert.Equal("public", root.GetProperty("mode").GetString());
            Assert.Equal("Synthetic Reader 0", root.GetProperty("reader").GetString());
            Assert.Equal("T0", root.GetProperty("protocol").GetString());
            Assert.Equal("3B 79 18 00 00 FF", root.GetProperty("atr").GetString());
            Assert.Equal("00", root.GetProperty("p2_used").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
            Assert.Contains("consent", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("consent", run.Stdout, StringComparison.Ordinal);

            JsonElement read = root.GetProperty("trace").EnumerateArray().First(e => e.GetProperty("kind").GetString() == "Read");
            Assert.Equal("0004", read.GetProperty("offset").GetString());
            Assert.Equal(13, read.GetProperty("requested").GetInt32());
            Assert.Equal(0, read.GetProperty("received").GetInt32());
            Assert.Equal("610D", read.GetProperty("sw").GetString());

            JsonElement cid = root.GetProperty("fields").EnumerateArray().First();
            Assert.Equal("CitizenId", cid.GetProperty("field").GetString());
            Assert.Equal("pass", cid.GetProperty("result").GetString());
            Assert.Equal("Ok", cid.GetProperty("quality").GetString());
            Assert.Equal(13, cid.GetProperty("received").GetInt32());
        }

        [Fact]
        public void Json_failure_has_the_error_and_its_exit_code()
        {
            var card = new SimulatedCard();
            card.Statuses.Clear();

            ToolRun run = Tool.Run(card, "--json");

            using JsonDocument document = JsonDocument.Parse(run.Stdout);
            JsonElement error = document.RootElement.GetProperty("error");
            Assert.Equal("no_reader", error.GetProperty("code").GetString());
            Assert.Equal(ExitCodes.NoReader, error.GetProperty("exit_code").GetInt32());
            Assert.Equal(ExitCodes.NoReader, run.ExitCode);
            Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
        }

        // ---- exit codes ----

        public static IEnumerable<object[]> SimulatedFailures()
        {
            yield return new object[] { "no_reader", ExitCodes.NoReader, (Action<SimulatedCard>)(c => c.Statuses.Clear()) };
            yield return new object[] { "no_card", ExitCodes.NoCard, (Action<SimulatedCard>)(c => c.Statuses[0] = new ReaderStatus(c.ReaderName, ReaderCardState.Empty)) };
            yield return new object[] { "unsupported_card", ExitCodes.UnsupportedCard, (Action<SimulatedCard>)(c => c.SelectAnswer = SimSelectAnswer.NotFound6A82) };
            yield return new object[] { "unsupported_card", ExitCodes.UnsupportedCard, (Action<SimulatedCard>)(c => c.ConnectFailure = CardFailureKind.UnsupportedCard) };
            yield return new object[] { "unknown_read_behaviour", ExitCodes.UnknownReadBehaviour, (Action<SimulatedCard>)(c => c.GetResponseP2 = 0x02) };
            yield return new object[] { "card_removed", ExitCodes.CardRemoved, (Action<SimulatedCard>)(c => c.FailTransmitAt(3, CardFailureKind.CardRemoved)) };
            yield return new object[] { "communication", ExitCodes.Communication, (Action<SimulatedCard>)(c => c.FailTransmitAt(3, CardFailureKind.Timeout)) };
            yield return new object[] { "service_unavailable", ExitCodes.ServiceUnavailable, (Action<SimulatedCard>)(c => c.ConnectFailure = CardFailureKind.ServiceUnavailable) };
        }

        [Theory]
        [MemberData(nameof(SimulatedFailures))]
        public void Each_failure_has_its_own_exit_code_and_message_with_the_trace_still_printed(string code, int exitCode, object setup)
        {
            var card = new SimulatedCard();
            ((Action<SimulatedCard>)setup)(card);

            ToolRun run = Tool.Run(card);

            Assert.Equal(exitCode, run.ExitCode);
            Assert.Contains("error: " + code, run.Stderr, StringComparison.Ordinal);
            Assert.Contains("tool version: ", run.Stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void Exit_codes_for_the_six_required_failures_are_distinct()
        {
            int[] codes =
            {
                ExitCodes.ServiceUnavailable, ExitCodes.NativeLibraryMissing, ExitCodes.NoReader, ExitCodes.NoCard,
                ExitCodes.UnsupportedCard, ExitCodes.UnknownReadBehaviour,
            };

            Assert.Equal(codes.Length, codes.Distinct().Count());
            Assert.DoesNotContain(ExitCodes.Success, codes);
            Assert.DoesNotContain(ExitCodes.Usage, codes);
        }

        [Theory]
        [InlineData(typeof(DllNotFoundException))]
        [InlineData(typeof(EntryPointNotFoundException))]
        public void Missing_native_library_goes_through_the_real_mapping_and_gives_the_linux_hint(Type failure)
        {
            var exception = (Exception)Activator.CreateInstance(failure, "libpcsclite.so.1 SECRETPATH")!;

            ToolRun run = Tool.Run(() => Missing(exception), HostPlatform.Linux);

            Assert.Equal(ExitCodes.NativeLibraryMissing, run.ExitCode);
            Assert.Contains("error: native_library_missing", run.Stderr, StringComparison.Ordinal);
            Assert.Contains("pcscd", run.Stderr, StringComparison.Ordinal);
            Assert.Contains("install", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRETPATH", run.All, StringComparison.Ordinal);
        }

        [Fact]
        public void Type_initialization_failure_around_the_native_library_counts_as_missing()
        {
            ConstructorInfo constructor = typeof(TypeInitializationException).GetConstructors().Single();
            var wrapped = (Exception)constructor.Invoke(new object?[] { "PCSC.Native", new DllNotFoundException("x") });

            ToolRun run = Tool.Run(() => Missing(wrapped), HostPlatform.Linux);

            Assert.Equal(ExitCodes.NativeLibraryMissing, run.ExitCode);
        }

        [Fact]
        public void Windows_gets_no_pcscd_hint()
        {
            ToolRun run = Tool.Run(() => Missing(new DllNotFoundException("x")), HostPlatform.Windows);

            Assert.Equal(ExitCodes.NativeLibraryMissing, run.ExitCode);
            Assert.DoesNotContain("pcscd", run.All, StringComparison.Ordinal);
        }

        [Fact]
        public void Service_down_goes_through_the_real_mapping_and_differs_from_a_missing_library()
        {
            ToolRun linux = Tool.Run(() => Missing(new PCSCException(SCardError.NoService)), HostPlatform.Linux);
            ToolRun windows = Tool.Run(() => Missing(new PCSCException(SCardError.NoService)), HostPlatform.Windows);

            Assert.Equal(ExitCodes.ServiceUnavailable, linux.ExitCode);
            Assert.Contains("error: service_unavailable", linux.Stderr, StringComparison.Ordinal);
            Assert.Contains("systemctl start pcscd", linux.Stderr, StringComparison.Ordinal);
            Assert.Contains("Smart Card service", windows.Stderr, StringComparison.Ordinal);
        }

        [Fact]
        public void Messages_for_the_six_required_failures_are_all_different()
        {
            var messages = new List<string>();
            foreach (object[] item in SimulatedFailures())
            {
                var card = new SimulatedCard();
                ((Action<SimulatedCard>)item[2])(card);
                messages.Add(MessageLine(Tool.Run(card)));
            }

            messages.Add(MessageLine(Tool.Run(() => Missing(new DllNotFoundException("x")), HostPlatform.Linux)));

            // The two unsupported card ways share a code, so one duplicate is expected there at most.
            Assert.True(messages.Distinct().Count() >= messages.Count - 1, string.Join("\n", messages));
        }

        private static string MessageLine(ToolRun run) =>
            run.Stderr.Split('\n').First(l => l.StartsWith("error: ", StringComparison.Ordinal));

        [Fact]
        public void Reader_not_found_lists_what_exists_without_serials()
        {
            const string Named = "Vendor Model (A1B2C3D4E5) 00 00";
            var card = new SimulatedCard { ReaderName = Named };
            card.Statuses[0] = new ReaderStatus(Named, ReaderCardState.Empty);

            ToolRun run = Tool.Run(card, "--reader", "No Such Reader");

            Assert.Equal(ExitCodes.NoReader, run.ExitCode);
            Assert.Contains("Vendor Model 00 00: Empty", run.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("A1B2C3D4E5", run.All, StringComparison.Ordinal);
        }

        [Fact]
        public void Reader_can_be_chosen_by_the_name_the_report_prints()
        {
            const string Named = "Vendor Model (A1B2C3D4E5) 00 00";
            var card = new SimulatedCard { ReaderName = Named };
            card.Statuses[0] = new ReaderStatus(Named, ReaderCardState.Present);

            Assert.Equal(ExitCodes.Success, Tool.Run(card, "--reader", "Vendor Model 00 00").ExitCode);
        }

        [Fact]
        public void Usage_errors_exit_2_with_no_notice_and_no_card_access()
        {
            var card = new SimulatedCard();

            ToolRun run = Tool.Run(card, "--raw");

            Assert.Equal(ExitCodes.Usage, run.ExitCode);
            Assert.Contains("--raw", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("consent", run.All, StringComparison.Ordinal);
            Assert.Equal(0, card.ConnectCalls);
        }

        [Fact]
        public void Help_and_version_touch_no_card()
        {
            var card = new SimulatedCard();

            ToolRun help = Tool.Run(card, "--help");
            ToolRun version = Tool.Run(card, "--version");

            Assert.Equal(ExitCodes.Success, help.ExitCode);
            Assert.Contains("--mode", help.Stdout, StringComparison.Ordinal);
            Assert.Contains("--json", help.Stdout, StringComparison.Ordinal);
            Assert.StartsWith("3.0.0-preview", version.Stdout, StringComparison.Ordinal);
            Assert.Equal(0, card.ConnectCalls);
        }

        [Fact]
        public void Unexpected_exception_prints_only_its_type_and_exits_1()
        {
            ToolRun run = Tool.Run(() => throw new InvalidOperationException(SentinelCard.Text), HostPlatform.Linux);

            Assert.Equal(ExitCodes.Internal, run.ExitCode);
            Assert.Contains("InvalidOperationException", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelCard.Text, run.All, StringComparison.Ordinal);
        }

        [Fact]
        public void Cancelling_exits_130()
        {
            var card = new SimulatedCard();
            using var source = new CancellationTokenSource();
            source.Cancel();
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            int code = DiagnoseApp.Run(Array.Empty<string>(), stdout, stderr, () => card, HostPlatform.Linux, source.Token);

            Assert.Equal(ExitCodes.Canceled, code);
            Assert.Equal(0, card.BeginTransactionCalls - card.EndTransactionCalls);
        }

        [Fact]
        public void Channel_is_disposed_once_a_run_ends()
        {
            var card = new SimulatedCard();

            Tool.Run(card);

            Assert.Equal(1, card.DisposeCalls);
        }
    }
}
