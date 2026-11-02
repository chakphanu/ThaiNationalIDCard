using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Tests;
using Xunit;

namespace ThaiNationalIDCard.Diagnose.Tests
{
    /// <summary>The privacy promise of the tool: no sentinel byte of any field, in any form, in any output.</summary>
    public class SentinelOutputTests
    {
        private static byte[] Direct(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        private static readonly byte[] Ok = { 0x90, 0x00 };

        private static IEnumerable<(string Name, Action<SimulatedCard> Setup)> Scenarios()
        {
            byte[] marker = Encoding.ASCII.GetBytes(SentinelCard.Text);

            yield return ("plain", c => { });
            yield return ("select 61 0A", c => c.SelectAnswer = SimSelectAnswer.Pending610A);
            yield return ("select 6A 82", c => c.SelectAnswer = SimSelectAnswer.NotFound6A82);
            yield return ("direct", c => c.Mode = SimReaderMode.DirectAnswer);
            yield return ("direct extra", c => { c.Mode = SimReaderMode.DirectAnswer; c.OverAnswerBytes = 2; });
            yield return ("chained", c => c.ChainSize = 0x40);
            yield return ("61 more offered", c => c.OverAnswerBytes = 2);
            yield return ("6C", c => { c.Mode = SimReaderMode.WrongLength; c.ExactLength = (o, l) => l > 16 ? l - 1 : l; });
            yield return ("6C outside", c => { c.Mode = SimReaderMode.WrongLength; c.ExactLength = (o, l) => o == SyntheticImage.IssueExpire ? 0x12 : l; });
            yield return ("p2 01 3B67", c => { c.GetResponseP2 = 1; c.Atr = new byte[] { 0x3B, 0x67 }; });
            foreach (byte[] wrong in new[] { new byte[] { 0x6A, 0x86 }, new byte[] { 0x6B, 0x00 }, new byte[] { 0x6D, 0x00 }, new byte[] { 0x6F, 0x00 }, new byte[] { 0x67, 0x00 } })
            {
                yield return ("wrong p2 " + wrong[0], c => { c.GetResponseP2 = 1; c.WrongP2Response = wrong; });
            }

            yield return ("wrong p2 garbage", c => { c.GetResponseP2 = 1; c.WrongP2Response = Direct(SentinelCard.Fill(13, marker), Ok); });
            yield return ("pending lost", c => { c.GetResponseP2 = 1; c.WrongP2LosesPending = true; });
            yield return ("no p2 works", c => c.GetResponseP2 = 2);
            yield return ("cid garbage direct", c =>
            {
                c.Mode = SimReaderMode.DirectAnswer;
                c.AnswerOverride = (p, command) => command[3] == 0x04 && command[0] == 0x80 ? Direct(SentinelCard.Fill(13, marker), Ok) : null;
            }
            );
            yield return ("cid bad checksum", c =>
            {
                c.Mode = SimReaderMode.DirectAnswer;
                c.AnswerOverride = (p, command) => command[3] == 0x04 && command[0] == 0x80 ? Direct(Encoding.ASCII.GetBytes("1234567890123"), Ok) : null;
            }
            );
            yield return ("truncated", c =>
            {
                c.Mode = SimReaderMode.DirectAnswer;
                c.AnswerOverride = (p, command) => command[0] == 0x80 && command[2] == 0x15 && command[3] == 0x79 ? Direct(SentinelCard.Fill(9, marker), Ok) : null;
            }
            );
            yield return ("bad status with bytes", c =>
            {
                c.AnswerOverride = (p, command) => command[0] == 0x80 && command[3] == 0x11 ? Direct(SentinelCard.Fill(9, marker), new byte[] { 0x6F, 0x00 }) : null;
            }
            );
            yield return ("malformed", c => c.AnswerOverride = (p, command) => p == 3 ? new byte[] { 0x53 } : null);
            foreach (int position in new[] { 1, 2, 3, 4, 10, 30, 53, 60 })
            {
                yield return ("removed at " + position, c => c.FailTransmitAt(position, CardFailureKind.CardRemoved));
                yield return ("reset at " + position, c => c.FailTransmitAt(position, CardFailureKind.CardReset));
            }

            yield return ("sharing on connect", c => c.ConnectFailure = CardFailureKind.SharingViolation);
            yield return ("sharing on transaction", c => c.BeginTransactionFailure = CardFailureKind.SharingViolation);
            yield return ("timeout", c => c.FailTransmitAt(5, CardFailureKind.Timeout));
            yield return ("close fails", c => c.EndTransactionFailure = CardFailureKind.Communication);
            yield return ("absent nhso", c => c.RemoveApplet(SimulatedCard.NhsoAidHex));
            yield return ("absent adm", c => c.RemoveApplet(SimulatedCard.AdmAidHex));
            yield return ("absent both", c => { c.RemoveApplet(SimulatedCard.NhsoAidHex); c.RemoveApplet(SimulatedCard.AdmAidHex); });
            yield return ("no reader", c => c.Statuses.Clear());
            yield return ("no card", c => c.Statuses[0] = new ReaderStatus(c.ReaderName, ReaderCardState.Empty));
        }

        public static IEnumerable<object[]> Matrix()
        {
            string[][] argumentSets =
            {
                Array.Empty<string>(),
                new[] { "--json" },
                new[] { "--mode", "private" },
                new[] { "--mode", "private", "--json" },
            };

            foreach ((string name, _) in Scenarios())
            {
                foreach (string[] args in argumentSets)
                {
                    yield return new object[] { name, string.Join(" ", args) };
                }
            }
        }

        private static List<string> ForbiddenForms()
        {
            var forms = new List<string> { "SENTINEL", SimulatedCard.SyntheticCitizenId, "1234567890123", SentinelCard.ThaiText };
            byte[] marker = Encoding.ASCII.GetBytes(SentinelCard.Text);
            string hex = Convert.ToHexString(marker);

            forms.Add(hex);
            forms.Add(string.Join(" ", marker.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))));
            forms.Add(string.Join("-", marker.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))));
            forms.Add(string.Join(",", marker.Select(b => b.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            forms.Add(Convert.ToHexString(Encoding.ASCII.GetBytes(SimulatedCard.SyntheticCitizenId)));
            forms.Add(Convert.ToHexString(SyntheticImage.Tis(SentinelCard.ThaiText)));

            // Every byte alignment of the repeated pattern, since a field can start anywhere in it.
            byte[] repeated = SentinelCard.Fill(marker.Length * 3, marker);
            for (int start = 0; start < 3; start++)
            {
                forms.Add(Convert.ToBase64String(repeated, start, marker.Length * 2).TrimEnd('='));
            }

            return forms;
        }

        private static void AssertClean(ToolRun run, string scenario)
        {
            foreach (string form in ForbiddenForms())
            {
                Assert.False(run.Stdout.Contains(form, StringComparison.OrdinalIgnoreCase), scenario + ": stdout holds " + form);
                Assert.False(run.Stderr.Contains(form, StringComparison.OrdinalIgnoreCase), scenario + ": stderr holds " + form);
            }
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public void Sentinel_is_in_no_output_in_any_scenario_mode_or_format(string scenario, string arguments)
        {
            SimulatedCard card = SentinelCard.Build();
            Scenarios().First(s => s.Name == scenario).Setup(card);
            string[] args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            ToolRun run = Tool.Run(card, args);

            AssertClean(run, scenario + " [" + arguments + "]");

            // Ranges no mode may read stayed on the card, whatever happened. The exception is a card volunteering bytes
            // past what was asked: nothing on this side can stop that, and the output check above still has to hold.
            if (scenario != "direct extra")
            {
                HashSet<int> answered = card.AnsweredOffsets();
                Assert.DoesNotContain(answered, o => o >= 0x015A && o <= 0x0166);
                Assert.DoesNotContain(answered, o => o >= 0x1627 && o <= 0x173D);
            }
        }

        [Fact]
        public void Control_the_card_really_holds_the_sentinel_and_the_tool_really_read_it()
        {
            SimulatedCard card = SentinelCard.Build();

            ToolRun run = Tool.Run(card, "--mode", "private");

            Assert.Equal(ExitCodes.Success, run.ExitCode);
            Assert.Contains(card.Log, e => e.Response != null && Encoding.ASCII.GetString(e.Response).Contains("SENTINEL", StringComparison.Ordinal));
            Assert.NotEmpty(card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
            Assert.NotEmpty(card.AnsweredOffsets(SimulatedCard.AdmAidHex));
        }

        [Fact]
        public void Control_the_check_would_catch_a_leak()
        {
            var leaked = new ToolRun(0, "value " + Convert.ToHexString(Encoding.ASCII.GetBytes(SentinelCard.Text)), string.Empty);

            Assert.ThrowsAny<Exception>(() => AssertClean(leaked, "control"));
        }

        [Fact]
        public void The_scenario_names_are_unique_so_the_matrix_covers_each()
        {
            string[] names = Scenarios().Select(s => s.Name).ToArray();

            Assert.Equal(names.Length, names.Distinct().Count());
            Assert.True(names.Length >= 40);
        }
    }
}
