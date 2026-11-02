using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using ThaiNationalIDCard.Workflow;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// Invariants over many seeded random scenarios, so a failure can be replayed from its seed. The allowlist below is
    /// written separately from the production policy on purpose.
    /// </summary>
    public class WorkflowPropertyTests
    {
        private const int Runs = 600;

        private static readonly (int Offset, int Length)[] MoiPublic =
        {
            (0x0004, 13), (0x0011, 209), (0x00F6, 100), (0x0167, 16), (0x017B, 5118), (0x1579, 160),
        };

        private static readonly (int Offset, int Length)[] MoiPrivate =
        {
            (0x0000, 4), (0x00E2, 20), (0x0177, 2), (0x1619, 14),
        };

        private static readonly (int Offset, int Length)[] Nhso =
        {
            (0x0004, 0x3C), (0x0040, 0x64), (0x00A4, 0x50), (0x00F4, 0x50), (0x0144, 1), (0x0145, 8), (0x014D, 8), (0x0155, 8), (0x015D, 1),
        };

        private sealed class Scenario
        {
            public int Seed { get; set; }

            public SimulatedCard Card { get; set; } = new SimulatedCard();

            public CardReadMode Mode { get; set; }

            public ReadPolicy Policy { get; set; } = ReadPolicy.Default;
        }

        private static Scenario Build(int seed)
        {
            var random = new Random(seed);
            byte[][] atrs = { new byte[] { 0x3B, 0x67 }, new byte[] { 0x3B, 0x67, 0x00 }, new byte[] { 0x3B, 0x79, 0x18 }, new byte[] { 0x3B }, Array.Empty<byte>() };
            byte[][] wrong = { new byte[] { 0x6A, 0x86 }, new byte[] { 0x6B, 0x00 }, new byte[] { 0x6D, 0x00 }, new byte[] { 0x6F, 0x00 }, new byte[] { 0x67, 0x00 }, Enumerable.Repeat((byte)0x47, 13).Concat(new byte[] { 0x90, 0x00 }).ToArray() };
            SimReaderMode[] modes = { SimReaderMode.GetResponse, SimReaderMode.GetResponse, SimReaderMode.DirectAnswer };
            SimSelectAnswer[] selects = { SimSelectAnswer.Ok9000, SimSelectAnswer.Pending610A, SimSelectAnswer.Ok9000, SimSelectAnswer.NotFound6A82 };
            CardFailureKind[] failures = { CardFailureKind.CardRemoved, CardFailureKind.CardReset, CardFailureKind.CardReset, CardFailureKind.SharingViolation, CardFailureKind.Timeout, CardFailureKind.Communication };
            CardFailureKind[] connectFailures = { CardFailureKind.CardRemoved, CardFailureKind.CardReset, CardFailureKind.SharingViolation };

            SimReaderMode mode = modes[random.Next(modes.Length)];
            int overAnswer = random.Next(4) == 0 ? 2 : 0;
            var card = new SimulatedCard
            {
                Atr = atrs[random.Next(atrs.Length)],
                GetResponseP2 = (byte)random.Next(0, 3),
                WrongP2Response = wrong[random.Next(wrong.Length)],
                WrongP2LosesPending = random.Next(2) == 0,
                Mode = mode,
                SelectAnswer = selects[random.Next(selects.Length)],
                ChainSize = random.Next(3) == 0 ? random.Next(16, 200) : 256,

                // A direct-answer card that volunteers extra bytes sends them whatever Le says. Nothing on this side can
                // stop that, it can only drop them, so that case has its own test and stays out of these invariants.
                OverAnswerBytes = mode == SimReaderMode.DirectAnswer ? 0 : overAnswer,
            };

            if (random.Next(2) == 0)
            {
                card.AddApplet(SimulatedCard.NhsoAidHex, Enumerable.Repeat((byte)0x76, 0x200).ToArray());
            }

            if (random.Next(2) == 0)
            {
                card.AddApplet(SimulatedCard.AdmAidHex, Enumerable.Repeat((byte)0x77, 0x40).ToArray());
            }

            for (int i = random.Next(0, 4); i > 0; i--)
            {
                card.FailTransmitAt(random.Next(1, 90), failures[random.Next(failures.Length)]);
            }

            for (int i = random.Next(0, 3); i > 0; i--)
            {
                card.ConnectFailures.Enqueue(connectFailures[random.Next(connectFailures.Length)]);
            }

            for (int i = random.Next(0, 3); i > 0; i--)
            {
                card.BeginTransactionFailures.Enqueue(connectFailures[random.Next(connectFailures.Length)]);
            }

            if (random.Next(5) == 0)
            {
                card.EndTransactionFailure = CardFailureKind.CardReset;
            }

            return new Scenario
            {
                Seed = seed,
                Card = card,
                Mode = random.Next(2) == 0 ? CardReadMode.Public : CardReadMode.PublicAndPrivate,
                Policy = new ReadPolicy(
                    connectRetryDelays: Flow.ThreeRetries,
                    allowP2Fallback: random.Next(4) != 0,
                    drainSelectResponse: random.Next(4) != 0),
            };
        }

        private static ReadDiagnostics Run(Scenario scenario, DelaySpy spy)
        {
            try
            {
                return Flow.Read(scenario.Card, scenario.Policy, scenario.Mode, spy, retainData: false).Diagnostics;
            }
            catch (ThaiIdCardException ex)
            {
                Assert.NotNull(ex.Diagnostics);
                return ex.Diagnostics!;
            }
        }

        private static bool Inside((int Offset, int Length)[] table, int offset, int length) =>
            table.Any(r => offset >= r.Offset && offset + length <= r.Offset + r.Length);

        [Fact]
        public void Every_command_on_the_wire_is_on_the_separately_written_allowlist()
        {
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);
                Run(scenario, new DelaySpy(scenario.Card));
                string selected = string.Empty;

                foreach (SimExchange exchange in scenario.Card.Log)
                {
                    byte[] c = exchange.Command;
                    string hex = ApduText.Hex(c);
                    if (Flow.IsSelect(c))
                    {
                        selected = hex.Substring(10);
                        bool known = selected == SimulatedCard.MoiAidHex ||
                            (scenario.Mode == CardReadMode.PublicAndPrivate && (selected == SimulatedCard.NhsoAidHex || selected == SimulatedCard.AdmAidHex));
                        Assert.True(known && hex.StartsWith("00a40400", StringComparison.Ordinal), "seed " + seed + ": " + hex);
                    }
                    else if (Flow.IsGetResponse(c))
                    {
                        Assert.True(c.Length == 5 && c[2] == 0x00 && c[3] <= 0x01, "seed " + seed + ": " + hex);
                    }
                    else if (c[0] == 0x80 && c[1] == 0xB0)
                    {
                        int offset = (c[2] << 8) | c[3];
                        int length = c[6] == 0 ? 256 : c[6];
                        bool allowed = selected == SimulatedCard.NhsoAidHex
                            ? Inside(Nhso, offset, length)
                            : Inside(MoiPublic, offset, length) || (scenario.Mode == CardReadMode.PublicAndPrivate && Inside(MoiPrivate, offset, length));
                        Assert.True(allowed && c.Length == 7 && c[4] == 0x02 && c[5] == 0x00, "seed " + seed + ": " + hex);
                    }
                    else
                    {
                        Assert.True(hex == "8000000007" && selected == SimulatedCard.AdmAidHex && scenario.Mode == CardReadMode.PublicAndPrivate, "seed " + seed + ": " + hex);
                    }
                }
            }
        }

        [Fact]
        public void The_guard_never_refuses_what_the_workflow_itself_decides_to_send()
        {
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);
                ReadDiagnostics diagnostics = Run(scenario, new DelaySpy(scenario.Card));

                // P2 02 is what the simulator wants in some seeds, but no refused entry may appear: the workflow only uses 00 and 01.
                Assert.DoesNotContain(diagnostics.Trace, t => t.Verdict != CommandVerdict.Allowed);
            }
        }

        [Fact]
        public void Fallback_is_spent_at_most_once_per_read_across_restarts()
        {
            int restartsSeen = 0;
            int fallbacksSeen = 0;
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);
                ReadDiagnostics diagnostics = Run(scenario, new DelaySpy(scenario.Card));

                int fallbacks = Flow.Events(diagnostics.Trace, TraceEvent.P2Fallback);
                Assert.True(fallbacks <= 1, "seed " + seed);
                Assert.True(Flow.P2Switches(scenario.Card) <= 1, "seed " + seed);
                restartsSeen += Flow.Events(diagnostics.Trace, TraceEvent.Retry) > 0 && fallbacks == 1 ? 1 : 0;
                fallbacksSeen += fallbacks;
            }

            // Control: the scenarios do reach the case under test.
            Assert.True(fallbacksSeen > 20, "fallbacks " + fallbacksSeen);
            Assert.True(restartsSeen > 5, "restarts with fallback " + restartsSeen);
        }

        [Fact]
        public void No_delay_happens_inside_a_transaction()
        {
            int delays = 0;
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);
                var spy = new DelaySpy(scenario.Card);

                Run(scenario, spy);

                Assert.Equal(0, spy.InsideTransaction);
                Assert.True(spy.Delays.Count <= Flow.ThreeRetries.Length, "seed " + seed);
                delays += spy.Delays.Count;
            }

            Assert.True(delays > 50, "delays " + delays);
        }

        [Fact]
        public void Spy_control_it_sees_a_wait_made_inside_a_transaction()
        {
            var card = new SimulatedCard();
            card.Connect(card.ReaderName);
            card.BeginTransaction();
            var spy = new DelaySpy(card);

            spy.Wait(TimeSpan.Zero, TestContext.Current.CancellationToken);

            Assert.Equal(1, spy.InsideTransaction);
        }

        [Fact]
        public void Transaction_is_always_closed_and_the_connection_released()
        {
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);

                Run(scenario, new DelaySpy(scenario.Card));

                SimulatedCard card = scenario.Card;
                Assert.False(card.InTransaction, "seed " + seed);
                Assert.False(card.IsConnected, "seed " + seed);
                Assert.Equal(1, card.DisposeCalls);

                // Every opened transaction has its close, right after it and before the next connect.
                int open = 0;
                foreach (string call in card.CallLog)
                {
                    if (call == "begin")
                    {
                        open++;
                    }
                    else if (call == "transmit")
                    {
                        Assert.True(open == 1, "seed " + seed + ": transmit outside a transaction");
                    }
                    else if (call == "end")
                    {
                        open--;
                    }
                    else if (call == "connect" || call == "delay")
                    {
                        Assert.True(open == 0, "seed " + seed + ": " + call + " inside a transaction");
                    }
                }

                Assert.Equal(0, open);
            }
        }

        [Fact]
        public void Never_read_ranges_stay_on_the_card_in_every_scenario()
        {
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);

                Run(scenario, new DelaySpy(scenario.Card));

                HashSet<int> offsets = scenario.Card.AnsweredOffsets();
                Assert.DoesNotContain(offsets, o => o >= 0x015A && o <= 0x0166);
                Assert.DoesNotContain(offsets, o => o >= 0x1627 && o <= 0x173D);
                if (scenario.Mode == CardReadMode.Public)
                {
                    Assert.DoesNotContain(offsets, o => (o >= 0x0177 && o <= 0x0178) || o < 4 || (o >= 0x00E2 && o <= 0x00F5) || (o >= 0x1619 && o <= 0x1626));
                    Assert.Empty(scenario.Card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
                    Assert.Empty(scenario.Card.AnsweredOffsets(SimulatedCard.AdmAidHex));
                }
            }
        }

        [Fact]
        public void Forbidden_commands_never_reach_the_card_in_any_scenario_or_mode()
        {
            int privateReads = 0;
            for (int seed = 0; seed < Runs; seed++)
            {
                Scenario scenario = Build(seed);

                Run(scenario, new DelaySpy(scenario.Card));

                foreach (SimExchange exchange in scenario.Card.Log)
                {
                    byte[] c = exchange.Command;
                    string hex = ApduText.Hex(c);
                    bool pinOrFingerprint = (c[0] == 0x80 && (c[1] == 0x20 || c[1] == 0xB4)) || (c[0] == 0xB0 && c[1] == 0x34);
                    bool write = c[1] == 0xD0 || c[1] == 0xD2 || c[1] == 0xD6 || c[1] == 0xDA || c[1] == 0xDC || c[1] == 0xE2;
                    bool chipData = (c[0] == 0x80 && c[1] == 0xCA) || (Flow.IsSelect(c) && c.Length <= 5);

                    Assert.False(pinOrFingerprint || write || chipData, "seed " + seed + ": " + hex);
                }

                privateReads += scenario.Mode == CardReadMode.PublicAndPrivate ? 1 : 0;
            }

            // Control: the scenarios do run both modes.
            Assert.True(privateReads > 100 && privateReads < Runs - 100, "private reads " + privateReads);
        }

        [Fact]
        public void Trace_never_exposes_more_than_the_wire_commands_and_their_events()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                Scenario scenario = Build(seed);

                ReadDiagnostics diagnostics = Run(scenario, new DelaySpy(scenario.Card));

                Assert.Equal(scenario.Card.Log.Count, diagnostics.Trace.Count(t => t.Kind != TraceCommandKind.Event));
            }
        }
    }
}
