using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Workflow;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// The golden files state what the old code sends, not what a card requires. These tests pin the v3 wire
    /// sequence against them, with only the differences the plan lists. The driver is the real workflow.
    /// </summary>
    public class GoldenSequenceTests
    {
        private static List<string> Drive(SimulatedCard card)
        {
            CardReadResult result = new CardReadWorkflow(card).Read(new CardReadRequest(), TestContext.Current.CancellationToken);

            Assert.All(result.Fields, f => Assert.Equal(FieldQuality.Ok, f.Info.Quality));
            return card.ReceivedHex.ToList();
        }

        [Fact]
        public void Golden_file_has_select_plus_25_commands_with_their_get_response()
        {
            List<string> golden = GoldenSequence.Load(GoldenSequence.P2Zero);

            Assert.Equal(1 + 25 * 2, golden.Count);
            Assert.Equal(GoldenSequence.SelectMoi, golden[0]);
            Assert.Equal(new[] { "80b0000402000d", "80b000110200d1", GoldenSequence.OldAddress, "80b00167020010", "80b000f6020064" },
                golden.Where(c => !GoldenSequence.IsGetResponse(c)).Skip(1).Take(5));
        }

        [Fact]
        public void C488da3_already_sends_issue_expire_le_10_so_it_is_no_difference()
        {
            foreach (string name in new[] { GoldenSequence.P2Zero, GoldenSequence.P2One, GoldenSequence.Direct })
            {
                List<string> golden = GoldenSequence.Load(name);
                Assert.Contains("80b00167020010", golden);
                Assert.DoesNotContain("80b00167020012", golden);
                Assert.Contains("80b00167020010", GoldenSequence.ExpectedV3(golden));
            }
        }

        [Fact]
        public void Golden_files_differ_only_in_get_response()
        {
            List<string> zero = GoldenSequence.Load(GoldenSequence.P2Zero);
            List<string> one = GoldenSequence.Load(GoldenSequence.P2One);
            List<string> direct = GoldenSequence.Load(GoldenSequence.Direct);

            Assert.Equal(zero.Select(c => GoldenSequence.IsGetResponse(c) ? string.Concat("00c00001", c.AsSpan(8)) : c), one);
            Assert.Equal(zero.Where(c => !GoldenSequence.IsGetResponse(c)), direct);
        }

        [Fact]
        public void Golden_sequence_replays_on_the_simulator_and_stops_before_0177()
        {
            var card = new SimulatedCard();
            card.Connect(card.ReaderName);

            foreach (string command in GoldenSequence.Load(GoldenSequence.P2Zero))
            {
                byte[] response = card.Transmit(ApduText.Bytes(command));
                byte sw1 = response[response.Length - 2];
                Assert.True(sw1 == 0x90 || sw1 == 0x61, command);
            }

            Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
        }

        [Fact]
        public void Allowed_differences_are_exactly_the_listed_commands()
        {
            List<string> golden = GoldenSequence.Load(GoldenSequence.P2Zero);
            List<string> expected = GoldenSequence.ExpectedV3(golden, selectAnnounces: 0x0A);

            Assert.Equal(
                new[] { "00c0000064", GoldenSequence.OldAddress }.OrderBy(s => s, StringComparer.Ordinal),
                MultisetMinus(golden, expected).OrderBy(s => s, StringComparer.Ordinal));
            Assert.Equal(
                new[] { "00c000000a", "00c00000a0", "00c0000012", GoldenSequence.NewAddress, GoldenSequence.NewLastChunk }
                    .OrderBy(s => s, StringComparer.Ordinal),
                MultisetMinus(expected, golden).OrderBy(s => s, StringComparer.Ordinal));
        }

        [Fact]
        public void Workflow_with_select_9000_matches_golden_plus_allowed_differences()
        {
            List<string> expected = GoldenSequence.ExpectedV3(GoldenSequence.Load(GoldenSequence.P2Zero));

            Assert.Equal(expected, Drive(new SimulatedCard()));
        }

        [Fact]
        public void Workflow_with_select_610a_adds_only_the_get_response_after_select()
        {
            List<string> expected = GoldenSequence.ExpectedV3(GoldenSequence.Load(GoldenSequence.P2Zero), selectAnnounces: 0x0A);

            Assert.Equal(expected, Drive(new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A }));
        }

        [Fact]
        public void Workflow_with_a_3b67_atr_matches_the_p2_one_golden()
        {
            List<string> expected = GoldenSequence.ExpectedV3(GoldenSequence.Load(GoldenSequence.P2One));

            Assert.Equal(expected, Drive(new SimulatedCard { GetResponseP2 = 0x01, Atr = new byte[] { 0x3B, 0x67, 0x00, 0x00 } }));
        }

        [Fact]
        public void Workflow_on_a_direct_answer_reader_matches_the_direct_golden()
        {
            List<string> expected = GoldenSequence.ExpectedV3(GoldenSequence.Load(GoldenSequence.Direct));

            Assert.Equal(expected, Drive(new SimulatedCard { Mode = SimReaderMode.DirectAnswer }));
        }

        [Theory]
        [InlineData("80b00167020010", "80b00167020012")] // issue/expire back to Le 12
        [InlineData("80b000f6020064", "80b000f60200a0")] // Le change on the wrong field
        [InlineData("00c0000010", "00c0000012")]
        [InlineData("80b0017b0200ff", "80b0017b0200fe")] // photo chunk Le
        public void Any_other_changed_command_fails_the_comparison(string from, string to)
        {
            List<string> expected = GoldenSequence.ExpectedV3(GoldenSequence.Load(GoldenSequence.P2Zero));
            List<string> mutated = expected.Select(c => c == from ? to : c).ToList();

            Assert.NotEqual(expected, mutated);
        }

        [Fact]
        public void Issuer_get_response_is_not_covered_by_the_address_difference()
        {
            List<string> golden = GoldenSequence.Load(GoldenSequence.P2Zero);
            List<string> expected = GoldenSequence.ExpectedV3(golden);

            int issuer = expected.IndexOf("80b000f6020064");
            Assert.Equal("00c0000064", expected[issuer + 1]);
        }

        [Fact]
        public void Reordered_doubled_or_missing_commands_fail_the_comparison()
        {
            List<string> expected = GoldenSequence.ExpectedV3(GoldenSequence.Load(GoldenSequence.P2Zero));

            var swapped = new List<string>(expected);
            (swapped[1], swapped[3]) = (swapped[3], swapped[1]);
            Assert.NotEqual(expected, swapped);

            var doubled = new List<string>(expected);
            doubled.InsertRange(13, expected.Skip(11).Take(2));
            Assert.NotEqual(expected, doubled);

            Assert.NotEqual(expected, expected.Where(c => c != GoldenSequence.NewLastChunk).ToList());
        }

        private static List<string> MultisetMinus(IEnumerable<string> left, IEnumerable<string> right)
        {
            var result = new List<string>(left);
            foreach (string item in right)
            {
                result.Remove(item);
            }

            return result;
        }
    }
}
