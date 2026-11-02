using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using ThaiNationalIDCard.Workflow;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>
    /// The read sequence against the simulator. Wrong-P2 answers and retry behaviour are assumptions about cards, so
    /// passing shows the logic follows the plan, not that a real card behaves this way.
    /// </summary>
    public class CardReadWorkflowTests
    {
        private const string BadChecksumCid = "1234567890123";
        private const string SelectMoi = GoldenSequence.SelectMoi;
        private const string CidRead = "80b0000402000d";

        private static ReadPolicy Retries(bool allowP2Fallback = true) =>
            new ReadPolicy(connectRetryDelays: Flow.ThreeRetries, allowP2Fallback: allowP2Fallback);

        // ---- sequence ----

        [Fact]
        public void Read_connects_opens_reads_closes_then_disconnects_with_nothing_outside_the_transaction()
        {
            var card = new SimulatedCard();

            Flow.Read(card);

            List<string> calls = card.CallLog.Where(c => c != "transmit").ToList();
            Assert.Equal(new[] { "connect", "begin", "end", "disconnect" }, calls);
            int begin = card.CallLog.IndexOf("begin");
            int end = card.CallLog.IndexOf("end");
            Assert.All(card.CallLog.Select((c, i) => (c, i)).Where(x => x.c == "transmit"), x => Assert.InRange(x.i, begin + 1, end - 1));
            Assert.False(card.IsConnected);
            Assert.False(card.InTransaction);
            Assert.Equal(1, card.DisposeCalls);
        }

        [Fact]
        public void Without_a_transaction_policy_none_is_opened()
        {
            var card = new SimulatedCard();

            CardReadResult result = Flow.Read(card, new ReadPolicy(useTransaction: false));

            Assert.Equal(0, card.BeginTransactionCalls);
            Assert.Equal(0, card.EndTransactionCalls);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        [Fact]
        public void Good_read_gives_every_public_field_with_its_exact_bytes()
        {
            var card = new SimulatedCard();

            CardReadResult result = Flow.Read(card);

            Assert.Equal(MoiProfile.Public.Fields.Select(f => f.Id), result.Fields.Select(f => f.Info.Id));
            Assert.All(result.Fields, f => Assert.Equal(FieldQuality.Ok, f.Info.Quality));
            foreach (CardField field in MoiProfile.Public.Fields)
            {
                Assert.Equal(ApduText.Slice(card.MoiImage, field.Range.Offset, field.Range.Length), result.Get(field.Id).Data);
            }
        }

        [Fact]
        public void Select_refused_is_unsupported_card_and_nothing_else_is_sent()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.NotFound6A82 };

            var ex = Assert.Throws<UnsupportedCardException>(() => Flow.Read(card));

            Assert.Equal(1, card.TransmitCount);
            Assert.Equal(CardFailureKind.NoApplet, ex.Failure);
            Assert.Equal(0x6A82, ex.StatusWord);
            Assert.Equal(CardFailureKind.NoApplet, ex.Diagnostics!.Failure);
            Assert.Equal(1, card.EndTransactionCalls);
        }

        [Fact]
        public void Select_610a_is_drained_with_the_first_p2_and_the_bytes_appear_nowhere()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };

            CardReadResult result = Flow.Read(card);

            Assert.Equal(new[] { SelectMoi, "00c000000a", CidRead }, card.ReceivedHex.Take(3));
            Assert.All(result.Fields, f => Assert.False(f.Data != null && f.Data.Length == 10));
        }

        [Fact]
        public void Drain_off_skips_the_get_response_after_select()
        {
            var card = new SimulatedCard { SelectAnswer = SimSelectAnswer.Pending610A };

            Flow.Read(card, new ReadPolicy(drainSelectResponse: false));

            Assert.Equal(new[] { SelectMoi, CidRead }, card.ReceivedHex.Take(2));
        }

        [Fact]
        public void Include_photo_false_sends_no_photo_read_and_reports_not_read()
        {
            var card = new SimulatedCard();

            CardReadResult result = Flow.Read(card, includePhoto: false);

            Assert.Equal(FieldQuality.NotRead, result.Get(CardFieldId.Photo).Info.Quality);
            Assert.Null(result.Get(CardFieldId.Photo).Data);
            Assert.DoesNotContain(card.AnsweredOffsets(), o => o >= SyntheticImage.Photo && o < SyntheticImage.Address);
        }

        // ---- reader selection ----

        [Fact]
        public void First_reader_holding_a_card_is_chosen_and_a_named_reader_is_not_replaced()
        {
            var card = new SimulatedCard();
            card.Statuses.Clear();
            card.Statuses.Add(new ReaderStatus("Empty Reader 0", ReaderCardState.Empty));
            card.Statuses.Add(new ReaderStatus("Full Reader 1", ReaderCardState.Present));

            Assert.Equal("Full Reader 1", Flow.Read(card).Diagnostics.ReaderName);

            var ex = Assert.Throws<CardNotPresentException>(() => Flow.Read(card, reader: "Empty Reader 0"));
            Assert.Equal(2, ex.Diagnostics!.Readers.Count);
        }

        [Fact]
        public void No_reader_and_no_card_fail_with_diagnostics_and_no_connect()
        {
            var none = new SimulatedCard();
            none.Statuses.Clear();
            var empty = new SimulatedCard();
            empty.Statuses[0] = new ReaderStatus(empty.ReaderName, ReaderCardState.Empty);

            Assert.NotNull(Assert.Throws<ReaderNotFoundException>(() => Flow.Read(none)).Diagnostics);
            Assert.NotNull(Assert.Throws<CardNotPresentException>(() => Flow.Read(empty)).Diagnostics);
            Assert.Equal(0, none.ConnectCalls);
            Assert.Equal(0, empty.ConnectCalls);
        }

        // ---- ATR ----

        [Fact]
        public void Atr_3b67_reverses_the_order_and_is_logged_in_the_trace()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01, Atr = new byte[] { 0x3B, 0x67 } };

            CardReadResult result = Flow.Read(card);

            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.AtrP2RuleUsed));
            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.All(Flow.GetResponseP2s(card), p2 => Assert.Equal(0x01, p2));
            Assert.Equal(0x01, result.Diagnostics.P2Used);
        }

        [Theory]
        [InlineData("")]
        [InlineData("3b")]
        [InlineData("3b79180000")]
        public void Other_or_short_atr_never_fails_and_logs_no_rule(string atrHex)
        {
            var card = new SimulatedCard { Atr = ApduText.Bytes(atrHex) };

            CardReadResult result = Flow.Read(card);

            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.AtrP2RuleUsed));
            Assert.Equal(0x00, result.Diagnostics.P2Used);
        }

        [Fact]
        public void Atr_is_kept_as_hex_in_the_diagnostics()
        {
            CardReadResult result = Flow.Read(new SimulatedCard { Atr = new byte[] { 0x3B, 0x79, 0x18 } });

            Assert.Equal("3B 79 18", result.Diagnostics.AtrHex);
        }

        // ---- CID gate: valid ----

        [Fact]
        public void Valid_cid_locks_the_p2_with_no_extra_commands()
        {
            var card = new SimulatedCard();

            CardReadResult result = Flow.Read(card);

            Assert.Equal(1, card.ReceivedHex.Count(h => h == CidRead));
            Assert.Equal(1, card.ReceivedHex.Count(h => h == SelectMoi));
            TraceEntry locked = Assert.Single(result.Diagnostics.Trace, t => t.Event == TraceEvent.P2Locked);
            Assert.Equal(0x00, locked.GetResponseP2);
        }

        // ---- CID gate: digits with a bad checksum ----

        [Fact]
        public void Bad_checksum_twice_reselects_rereads_with_the_same_p2_then_goes_on_and_marks_it()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            card.AnswerOverride = (position, command) => Flow.IsCidRead(command) ? Flow.DirectText(BadChecksumCid) : null;

            CardReadResult result = Flow.Read(card);

            Assert.Equal(new[] { SelectMoi, CidRead, SelectMoi, CidRead }, card.ReceivedHex.Take(4));
            CardFieldResult cid = result.Get(CardFieldId.CitizenId);
            Assert.Equal(FieldQuality.Suspect, cid.Info.Quality);
            Assert.True(cid.Info.Validation.HasFlag(ValidationFailures.ChecksumMismatch));
            Assert.Equal(BadChecksumCid, System.Text.Encoding.ASCII.GetString(cid.Data!));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.Address).Info.Quality);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.ChecksumRetry));
            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
        }

        [Fact]
        public void Bad_checksum_on_get_response_cards_keeps_the_p2_and_spends_no_budget()
        {
            var card = new SimulatedCard();
            int gets = 0;
            card.AnswerOverride = (position, command) =>
            {
                bool cidGet = command[0] == 0x00 && command[1] == 0xC0 && command[4] == 0x0D;
                return cidGet && ++gets > 0 ? Flow.DirectText(BadChecksumCid) : null;
            };

            CardReadResult result = Flow.Read(card);

            Assert.Equal(2, card.ReceivedHex.Count(h => h == CidRead));
            Assert.Equal(0, Flow.P2Switches(card));
            Assert.Equal(FieldQuality.Suspect, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        [Fact]
        public void Bad_checksum_then_a_good_read_is_ok_and_remembers_the_retry()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            int reads = 0;
            card.AnswerOverride = (position, command) => Flow.IsCidRead(command) && ++reads == 1 ? Flow.DirectText(BadChecksumCid) : null;

            CardFieldResult cid = Flow.Read(card).Get(CardFieldId.CitizenId);

            Assert.Equal(FieldQuality.Ok, cid.Info.Quality);
            Assert.True(cid.Info.Issues.HasFlag(FieldIssue.ChecksumRetried));
            Assert.Equal(SimulatedCard.SyntheticCitizenId, System.Text.Encoding.ASCII.GetString(cid.Data!));
        }

        [Fact]
        public void Bad_checksum_then_garbage_keeps_the_digits_it_had_and_marks_them()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            int reads = 0;
            card.AnswerOverride = (position, command) =>
            {
                if (!Flow.IsCidRead(command))
                {
                    return null;
                }

                return ++reads == 1 ? Flow.DirectText(BadChecksumCid) : new byte[] { 0x6F, 0x00 };
            };

            CardFieldResult cid = Flow.Read(card).Get(CardFieldId.CitizenId);

            Assert.Equal(FieldQuality.Suspect, cid.Info.Quality);
            Assert.Equal(BadChecksumCid, System.Text.Encoding.ASCII.GetString(cid.Data!));
            Assert.Equal(2, reads);
        }

        [Fact]
        public void Select_failing_on_the_checksum_retry_is_unknown_behaviour()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            int selects = 0;
            card.AnswerOverride = (position, command) =>
            {
                if (Flow.IsSelect(command) && ++selects == 2)
                {
                    return new byte[] { 0x6A, 0x82 };
                }

                return Flow.IsCidRead(command) ? Flow.DirectText(BadChecksumCid) : null;
            };

            Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card));
            Assert.Equal(2, selects);
        }

        // ---- CID gate: anything else, with GET RESPONSE ----

        [Fact]
        public void Wrong_p2_reselects_sends_a_new_read_and_never_repeats_get_response()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };

            CardReadResult result = Flow.Read(card);

            Assert.Equal(new[] { SelectMoi, CidRead, "00c000000d", SelectMoi, CidRead, "00c000010d" }, card.ReceivedHex.Take(6));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Equal(0x01, result.Diagnostics.P2Used);
            Assert.All(Flow.GetResponseP2s(card).Skip(1), p2 => Assert.Equal(0x01, p2));
        }

        [Fact]
        public void Neither_p2_working_is_unknown_behaviour_after_exactly_one_fallback()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x02 };

            var ex = Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card));

            Assert.Equal(new byte[] { 0x00, 0x01 }, Flow.GetResponseP2s(card));
            Assert.Equal(2, card.ReceivedHex.Count(h => h == CidRead));
            Assert.Equal(0x6A86, ex.LastStatusWord);
            Assert.Equal(CardFailureKind.UnknownReadBehaviour, ex.Failure);
            Assert.Equal(1, Flow.Events(ex.Diagnostics!.Trace, TraceEvent.P2Fallback));
            Assert.Equal(1, card.EndTransactionCalls);
        }

        [Fact]
        public void Fallback_off_stops_at_the_first_p2()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };

            Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card, new ReadPolicy(allowP2Fallback: false)));

            Assert.Equal(new[] { SelectMoi, CidRead, "00c000000d" }, card.ReceivedHex);
        }

        [Fact]
        public void A_single_fixed_p2_has_nothing_to_fall_back_to()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x00 };

            Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card, new ReadPolicy(p2Order: new byte[] { 0x01 })));

            Assert.Equal(new byte[] { 0x01 }, Flow.GetResponseP2s(card));
        }

        [Fact]
        public void Fallback_giving_digits_with_a_bad_checksum_goes_the_checksum_way_and_is_marked()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            card.AnswerOverride = (position, command) =>
                Flow.IsGetResponse(command) && command[3] == 0x01 && command[4] == 0x0D ? Flow.DirectText(BadChecksumCid) : null;

            CardFieldResult cid = Flow.Read(card).Get(CardFieldId.CitizenId);

            Assert.Equal(FieldQuality.Suspect, cid.Info.Quality);
            Assert.Equal(3, card.ReceivedHex.Count(h => h == CidRead));
            Assert.Equal(1, Flow.P2Switches(card));
        }

        [Fact]
        public void Select_failing_on_the_fallback_is_unknown_behaviour()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            int selects = 0;
            card.AnswerOverride = (position, command) =>
                Flow.IsSelect(command) && ++selects == 2 ? new byte[] { 0x6A, 0x82 } : null;

            Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card));
        }

        // ---- CID gate: no GET RESPONSE was ever sent ----

        [Fact]
        public void Direct_garbage_retries_once_without_touching_p2_or_its_budget()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            int reads = 0;
            card.AnswerOverride = (position, command) =>
                Flow.IsCidRead(command) && ++reads == 1 ? new byte[] { 0x6F, 0x00 } : null;

            CardReadResult result = Flow.Read(card);

            Assert.Equal(new[] { SelectMoi, CidRead, SelectMoi, CidRead }, card.ReceivedHex.Take(4));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.NoGetResponseRetry));
            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Null(result.Diagnostics.P2Used);
        }

        [Fact]
        public void Direct_retry_does_not_depend_on_the_fallback_switch()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            int reads = 0;
            card.AnswerOverride = (position, command) =>
                Flow.IsCidRead(command) && ++reads == 1 ? Flow.DirectText("not digits at") : null;

            CardReadResult result = Flow.Read(card, new ReadPolicy(allowP2Fallback: false));

            Assert.Equal(2, reads);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        [Fact]
        public void Direct_garbage_twice_is_unknown_behaviour_after_two_reads_only()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            card.AnswerOverride = (position, command) => Flow.IsCidRead(command) ? new byte[] { 0x6F, 0x00 } : null;

            var ex = Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card));

            Assert.Equal(new[] { SelectMoi, CidRead, SelectMoi, CidRead }, card.ReceivedHex);
            Assert.Equal(0x6F00, ex.LastStatusWord);
            Assert.Null(ex.Diagnostics!.P2Used);
        }

        [Fact]
        public void A_drain_counts_as_a_get_response_so_the_retry_then_is_the_p2_fallback()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer, SelectAnswer = SimSelectAnswer.Pending610A };
            int reads = 0;
            card.AnswerOverride = (position, command) =>
                Flow.IsCidRead(command) && ++reads == 1 ? new byte[] { 0x6F, 0x00 } : null;
            card.GetResponseP2 = 0x00;

            CardReadResult result = Flow.Read(card);

            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.NoGetResponseRetry));
        }

        [Fact]
        public void Direct_first_answer_then_61_xx_on_the_retry_can_still_spend_the_p2_fallback()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            int reads = 0;
            card.AnswerOverride = (position, command) =>
                Flow.IsCidRead(command) && ++reads == 1 ? new byte[] { 0x6F, 0x00 } : null;

            CardReadResult result = Flow.Read(card);

            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.NoGetResponseRetry));
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        // ---- wrong P2 matrix ----

        public static IEnumerable<object[]> WrongP2Matrix()
        {
            string[] atrs = { "3b67", "3b6700ff", "3b79180000", "3b", string.Empty };
            string[] answers = { "6a86", "6b00", "6d00", "6f00", "6700", "garbage9000", "lost6a86" };
            foreach (string atr in atrs)
            {
                foreach (byte required in new byte[] { 0x00, 0x01 })
                {
                    foreach (string answer in answers)
                    {
                        foreach (SimSelectAnswer select in new[] { SimSelectAnswer.Ok9000, SimSelectAnswer.Pending610A })
                        {
                            yield return new object[] { atr, required, answer, (int)select };
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(WrongP2Matrix))]
        public void Every_atr_and_p2_and_wrong_answer_reads_the_card_with_at_most_one_fallback(string atrHex, byte required, string answer, int selectValue)
        {
            var select = (SimSelectAnswer)selectValue;
            byte[] garbage = Enumerable.Repeat((byte)0x47, 13).Concat(new byte[] { 0x90, 0x00 }).ToArray();
            var card = new SimulatedCard
            {
                Atr = ApduText.Bytes(atrHex),
                GetResponseP2 = required,
                SelectAnswer = select,
                WrongP2Response = answer == "garbage9000" ? garbage : ApduText.Bytes(answer.Replace("lost", string.Empty, StringComparison.Ordinal)),
                WrongP2LosesPending = answer.StartsWith("lost", StringComparison.Ordinal),
            };
            bool rule = atrHex.StartsWith("3b67", StringComparison.Ordinal);
            byte first = rule ? (byte)0x01 : (byte)0x00;

            CardReadResult result = Flow.Read(card);

            Assert.All(result.Fields, f => Assert.Equal(FieldQuality.Ok, f.Info.Quality));
            Assert.Equal(first, Flow.GetResponseP2s(card)[0]);
            Assert.Equal(first == required ? 0 : 1, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Equal(rule ? 1 : 0, Flow.Events(result.Diagnostics.Trace, TraceEvent.AtrP2RuleUsed));
            Assert.Equal(required, result.Diagnostics.P2Used);
            Assert.True(Flow.P2Switches(card) <= 1);
        }

        // ---- fields ----

        [Fact]
        public void All_bytes_with_61_xx_on_every_field_is_a_good_read_flagged_more_offered()
        {
            var card = new SimulatedCard { OverAnswerBytes = 2 };

            CardReadResult result = Flow.Read(card);

            Assert.All(result.Fields, f =>
            {
                Assert.Equal(FieldQuality.Ok, f.Info.Quality);
                Assert.True(f.Info.Issues.HasFlag(FieldIssue.MoreOffered), f.Info.Id.ToString());
            });
            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
            Assert.Equal(ApduText.Slice(card.MoiImage, SyntheticImage.IssueExpire, 16), result.Get(CardFieldId.IssueExpire).Data);
        }

        [Fact]
        public void Bad_status_on_one_field_fails_only_that_field_and_gives_no_data()
        {
            var card = new SimulatedCard();
            card.AnswerOverride = (position, command) => ApduText.Hex(command) == "80b015790200a0" ? new byte[] { 0x6A, 0x82 } : null;

            CardReadResult result = Flow.Read(card);

            CardFieldResult address = result.Get(CardFieldId.Address);
            Assert.Equal(FieldQuality.Failed, address.Info.Quality);
            Assert.True(address.Info.Issues.HasFlag(FieldIssue.BadStatus));
            Assert.Equal(0x6A82, address.Info.StatusWord);
            Assert.Empty(address.Data ?? Array.Empty<byte>());
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.IssueExpire).Info.Quality);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.Photo).Info.Quality);
        }

        [Fact]
        public void Truncated_field_keeps_its_bytes_and_is_suspect()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            card.AnswerOverride = (position, command) =>
                ApduText.Hex(command) == "80b000f6020064" ? Flow.DirectText("Short") : null;

            CardFieldResult issuer = Flow.Read(card).Get(CardFieldId.Issuer);

            Assert.Equal(FieldQuality.Suspect, issuer.Info.Quality);
            Assert.True(issuer.Info.Issues.HasFlag(FieldIssue.Truncated));
            Assert.Equal(5, issuer.Data!.Length);
            Assert.Equal(5, issuer.Info.ReceivedLength);
            Assert.Equal(100, issuer.Info.RequestedLength);
        }

        [Fact]
        public void Failed_validator_marks_the_field_and_never_discards()
        {
            var card = new SimulatedCard();
            card.MoiImage[SyntheticImage.PersonInfo + 208] = (byte)'9';

            CardFieldResult person = Flow.Read(card).Get(CardFieldId.PersonInfo);

            Assert.Equal(FieldQuality.Suspect, person.Info.Quality);
            Assert.True(person.Info.Validation.HasFlag(ValidationFailures.SexCode));
            Assert.Equal(209, person.Data!.Length);
        }

        [Fact]
        public void Photo_without_jpeg_markers_is_marked_here_and_the_parser_withholds_it()
        {
            var card = new SimulatedCard();
            card.MoiImage[SyntheticImage.Photo] = 0x00;

            CardFieldResult photo = Flow.Read(card).Get(CardFieldId.Photo);

            Assert.Equal(FieldQuality.Suspect, photo.Info.Quality);
            Assert.True(photo.Info.Validation.HasFlag(ValidationFailures.JpegStart));
            Assert.Equal(SyntheticImage.PhotoLength, photo.Data!.Length);
        }

        [Fact]
        public void Failed_photo_chunk_stops_the_field_so_later_chunks_cannot_shift_into_the_gap()
        {
            var card = new SimulatedCard();
            string third = ApduText.Hex(ApduText.Read(SyntheticImage.Photo + 2 * 255, 0xFF));
            card.AnswerOverride = (position, command) => ApduText.Hex(command) == third ? new byte[] { 0x6F, 0x00 } : null;

            CardFieldResult photo = Flow.Read(card).Get(CardFieldId.Photo);

            Assert.Equal(FieldQuality.Failed, photo.Info.Quality);
            Assert.Equal(0x6F00, photo.Info.StatusWord);
            Assert.DoesNotContain(ApduText.Hex(ApduText.Read(SyntheticImage.Photo + 3 * 255, 0xFF)), card.ReceivedHex);
            Assert.Equal(ApduText.Slice(card.MoiImage, SyntheticImage.Photo, 2 * 255), photo.Data);
        }

        [Fact]
        public void Short_photo_chunk_stops_the_field_and_is_suspect()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            string first = ApduText.Hex(ApduText.Read(SyntheticImage.Photo, 0xFF));
            card.AnswerOverride = (position, command) => ApduText.Hex(command) == first ? Flow.DirectText("abc") : null;

            CardFieldResult photo = Flow.Read(card).Get(CardFieldId.Photo);

            Assert.Equal(FieldQuality.Suspect, photo.Info.Quality);
            Assert.True(photo.Info.Issues.HasFlag(FieldIssue.Truncated));
            Assert.Equal(3, photo.Data!.Length);
            Assert.DoesNotContain(ApduText.Hex(ApduText.Read(SyntheticImage.Photo + 255, 0xFF)), card.ReceivedHex);
        }

        [Fact]
        public void Requested_6c_past_the_range_fails_the_field_and_0177_stays_on_the_card()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.WrongLength, ExactLength = (offset, le) => offset == SyntheticImage.IssueExpire ? 0x12 : le };

            CardReadResult result = Flow.Read(card);

            Assert.Equal(FieldQuality.Failed, result.Get(CardFieldId.IssueExpire).Info.Quality);
            Assert.Equal(0x6C12, result.Get(CardFieldId.IssueExpire).Info.StatusWord);
            Assert.DoesNotContain(SyntheticImage.Raw0177, card.AnsweredOffsets());
            Assert.Contains(result.Diagnostics.Trace, t => t.Verdict == CommandVerdict.OutsideDeclaredRange);
        }

        [Fact]
        public void Malformed_response_fails_the_read_with_its_trace()
        {
            var card = new SimulatedCard { AnswerOverride = (position, command) => position == 3 ? new byte[] { 0x90 } : null };

            var ex = Assert.Throws<CardCommunicationException>(() => Flow.Read(card));

            Assert.Equal(CardFailureKind.MalformedResponse, ex.Failure);
            Assert.Equal(CardFailureKind.MalformedResponse, ex.Diagnostics!.Failure);
            Assert.Contains(ex.Diagnostics.Trace, t => t.Failure == CardFailureKind.MalformedResponse);
            Assert.Equal(1, card.EndTransactionCalls);
        }

        // ---- connect retry ----

        [Theory]
        [InlineData((int)CardFailureKind.CardRemoved)]
        [InlineData((int)CardFailureKind.CardReset)]
        [InlineData((int)CardFailureKind.SharingViolation)]
        public void Retryable_connect_failure_waits_outside_any_transaction_and_succeeds(int failureValue)
        {
            var failure = (CardFailureKind)failureValue;
            var card = new SimulatedCard();
            card.ConnectFailures.Enqueue(failure);
            card.ConnectFailures.Enqueue(failure);
            var spy = new DelaySpy(card);

            CardReadResult result = Flow.Read(card, Retries(), delay: spy);

            Assert.Equal(3, card.ConnectCalls);
            Assert.Equal(Flow.ThreeRetries.Take(2), spy.Delays);
            Assert.Equal(0, spy.InsideTransaction);
            Assert.Equal(2, Flow.Events(result.Diagnostics.Trace, TraceEvent.Retry));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        [Fact]
        public void Sharing_violation_that_never_ends_uses_the_whole_budget_then_fails_with_diagnostics()
        {
            var card = new SimulatedCard { ConnectFailure = CardFailureKind.SharingViolation };
            var spy = new DelaySpy(card);

            var ex = Assert.Throws<CardCommunicationException>(() => Flow.Read(card, Retries(), delay: spy));

            Assert.Equal(4, card.ConnectCalls);
            Assert.Equal(3, spy.Delays.Count);
            Assert.Equal(CardFailureKind.SharingViolation, ex.Failure);
            Assert.Equal(3, Flow.Events(ex.Diagnostics!.Trace, TraceEvent.Retry));
            Assert.Equal(0, card.BeginTransactionCalls);
        }

        [Theory]
        [InlineData((int)CardFailureKind.Timeout)]
        [InlineData((int)CardFailureKind.Communication)]
        [InlineData((int)CardFailureKind.UnsupportedCard)]
        [InlineData((int)CardFailureKind.CardNotPresent)]
        public void Other_connect_failures_are_not_retried(int failureValue)
        {
            var failure = (CardFailureKind)failureValue;
            var card = new SimulatedCard { ConnectFailure = failure };
            var spy = new DelaySpy(card);

            var ex = Assert.ThrowsAny<ThaiIdCardException>(() => Flow.Read(card, Retries(), delay: spy));

            Assert.Equal(failure, ex.Failure);
            Assert.Equal(1, card.ConnectCalls);
            Assert.Empty(spy.Delays);
        }

        [Fact]
        public void Sharing_violation_on_the_transaction_retries_from_connect_and_closes_nothing_it_did_not_open()
        {
            var card = new SimulatedCard();
            card.BeginTransactionFailures.Enqueue(CardFailureKind.SharingViolation);
            var spy = new DelaySpy(card);

            CardReadResult result = Flow.Read(card, Retries(), delay: spy);

            Assert.Equal(2, card.ConnectCalls);
            Assert.Equal(2, card.BeginTransactionCalls);
            Assert.Equal(1, card.EndTransactionCalls);
            Assert.Equal(0, spy.InsideTransaction);
            Assert.Equal(new[] { "connect", "begin-failed", "disconnect", "delay", "connect", "begin" }, card.CallLog.Take(6));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        [Fact]
        public void Sharing_violation_on_the_transaction_that_never_ends_fails_without_closing()
        {
            var card = new SimulatedCard { BeginTransactionFailure = CardFailureKind.SharingViolation };

            var ex = Assert.Throws<CardCommunicationException>(() => Flow.Read(card, Retries()));

            Assert.Equal(CardFailureKind.SharingViolation, ex.Failure);
            Assert.Equal(4, card.BeginTransactionCalls);
            Assert.Equal(0, card.EndTransactionCalls);
            Assert.False(card.IsConnected);
        }

        // ---- reset and removal in the middle of a read ----

        public static IEnumerable<object[]> AllPublicPositions() =>
            Enumerable.Range(1, 1 + (26 * 2)).Select(position => new object[] { position });

        [Fact]
        public void Position_list_covers_the_whole_public_read()
        {
            var card = new SimulatedCard();
            Flow.Read(card);

            Assert.Equal(AllPublicPositions().Count(), card.TransmitCount);
        }

        [Theory]
        [MemberData(nameof(AllPublicPositions))]
        public void Removed_card_at_any_position_stops_the_read_with_a_closed_transaction_and_a_trace(int position)
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(position, CardFailureKind.CardRemoved);
            var spy = new DelaySpy(card);

            var ex = Assert.Throws<CardRemovedException>(() => Flow.Read(card, Retries(), delay: spy));

            Assert.Equal(position, card.TransmitCount);
            Assert.Equal(1, card.ConnectCalls);
            Assert.Empty(spy.Delays);
            Assert.Equal(1, card.BeginTransactionCalls);
            Assert.Equal(1, card.EndTransactionCalls);
            Assert.False(card.IsConnected);
            Assert.Equal(CardFailureKind.CardRemoved, ex.Diagnostics!.Failure);
            Assert.Contains(ex.Diagnostics.Trace, t => t.Failure == CardFailureKind.CardRemoved);
        }

        [Theory]
        [MemberData(nameof(AllPublicPositions))]
        public void Reset_at_any_position_restarts_outside_the_transaction_and_gives_the_exact_bytes(int position)
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(position, CardFailureKind.CardReset);
            var spy = new DelaySpy(card);

            CardReadResult result = Flow.Read(card, Retries(), delay: spy);

            Assert.Equal(2, card.ConnectCalls);
            Assert.Equal(2, card.EndTransactionCalls);
            Assert.Single(spy.Delays);
            Assert.Equal(0, spy.InsideTransaction);
            Assert.All(result.Fields, f => Assert.Equal(FieldQuality.Ok, f.Info.Quality));
            foreach (CardField field in MoiProfile.Public.Fields)
            {
                Assert.Equal(ApduText.Slice(card.MoiImage, field.Range.Offset, field.Range.Length), result.Get(field.Id).Data);
            }
        }

        [Fact]
        public void Restart_starts_again_from_select_and_keeps_the_trace_of_the_first_attempt()
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(5, CardFailureKind.CardReset);

            CardReadResult result = Flow.Read(card, Retries());

            Assert.Equal(SelectMoi, ApduText.Hex(card.Log[5].Command));
            Assert.Equal(CidRead, ApduText.Hex(card.Log[6].Command));
            Assert.Contains(result.Diagnostics.Trace, t => t.Failure == CardFailureKind.CardReset);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.Retry));
            Assert.Equal(6, result.Fields.Count);
        }

        [Fact]
        public void Fallback_budget_survives_a_restart_so_a_spent_fallback_is_not_spent_again()
        {
            // Wrong first P2 spends the fallback, then a reset hits. After the restart the card stops answering any
            // GET RESPONSE, and the read must give up instead of trying P2 00 again.
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            card.FailTransmitAt(8, CardFailureKind.CardReset);
            card.AnswerOverride = (position, command) =>
                position > 8 && Flow.IsGetResponse(command) ? new byte[] { 0x6A, 0x86 } : null;

            var ex = Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card, Retries()));

            Assert.Equal(1, Flow.Events(ex.Diagnostics!.Trace, TraceEvent.P2Fallback));
            Assert.Equal(1, Flow.P2Switches(card));
            Assert.Equal(0x01, Flow.GetResponseP2s(card).Last());
        }

        [Fact]
        public void Fallback_not_spent_before_a_restart_is_still_available_after_it()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            card.FailTransmitAt(2, CardFailureKind.CardReset);

            CardReadResult result = Flow.Read(card, Retries());

            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
        }

        [Fact]
        public void Locked_p2_is_used_again_after_a_restart()
        {
            var card = new SimulatedCard { GetResponseP2 = 0x01 };
            card.FailTransmitAt(20, CardFailureKind.CardReset);

            CardReadResult result = Flow.Read(card, Retries());

            Assert.Equal(1, Flow.P2Switches(card));
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Equal(0x01, result.Diagnostics.P2Used);
        }

        [Fact]
        public void Resets_without_end_share_one_retry_budget_and_then_fail()
        {
            var card = new SimulatedCard();
            for (int position = 1; position <= 200; position += 2)
            {
                card.FailTransmitAt(position, CardFailureKind.CardReset);
            }

            var spy = new DelaySpy(card);

            var ex = Assert.Throws<CardCommunicationException>(() => Flow.Read(card, Retries(), delay: spy));

            Assert.Equal(CardFailureKind.CardReset, ex.Failure);
            Assert.Equal(3, spy.Delays.Count);
            Assert.Equal(4, card.ConnectCalls);
            Assert.Equal(card.BeginTransactionCalls, card.EndTransactionCalls);
        }

        [Fact]
        public void Connect_retries_and_restarts_share_the_budget()
        {
            var card = new SimulatedCard();
            card.ConnectFailures.Enqueue(CardFailureKind.SharingViolation);
            card.ConnectFailures.Enqueue(CardFailureKind.SharingViolation);
            card.FailTransmitAt(1, CardFailureKind.CardReset);
            card.FailTransmitAt(2, CardFailureKind.CardReset);

            Assert.Throws<CardCommunicationException>(() => Flow.Read(card, Retries()));

            Assert.Equal(4, card.ConnectCalls);
        }

        [Fact]
        public void Mid_read_sharing_violation_restarts_like_a_reset()
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(4, CardFailureKind.SharingViolation);
            var spy = new DelaySpy(card);

            CardReadResult result = Flow.Read(card, Retries(), delay: spy);

            Assert.Equal(2, card.ConnectCalls);
            Assert.Equal(0, spy.InsideTransaction);
            Assert.All(result.Fields, f => Assert.Equal(FieldQuality.Ok, f.Info.Quality));
        }

        [Fact]
        public void Mid_read_timeout_is_not_retried()
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(3, CardFailureKind.Timeout);
            var spy = new DelaySpy(card);

            var ex = Assert.Throws<CardCommunicationException>(() => Flow.Read(card, Retries(), delay: spy));

            Assert.Equal(CardFailureKind.Timeout, ex.Failure);
            Assert.Empty(spy.Delays);
            Assert.Equal(1, card.ConnectCalls);
        }

        // ---- closing the transaction ----

        [Fact]
        public void Failing_to_close_after_a_good_read_keeps_the_result_and_says_so()
        {
            var card = new SimulatedCard { EndTransactionFailure = CardFailureKind.CardReset };

            CardReadResult result = Flow.Read(card, Retries());

            Assert.Equal(1, card.ConnectCalls);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.TransactionCloseFailed));
        }

        [Fact]
        public void Failing_to_close_never_hides_the_original_error()
        {
            var card = new SimulatedCard { EndTransactionFailure = CardFailureKind.Communication };
            card.FailTransmitAt(4, CardFailureKind.CardRemoved);

            Assert.Throws<CardRemovedException>(() => Flow.Read(card, Retries()));
        }

        // ---- cancellation ----

        [Fact]
        public void Cancelling_closes_the_transaction_and_the_exception_has_the_trace()
        {
            var card = new SimulatedCard();
            using var source = new CancellationTokenSource();
            card.AnswerOverride = (position, command) =>
            {
                if (position == 4)
                {
                    source.Cancel();
                }

                return null;
            };

            var ex = Assert.Throws<ReadCanceledException>(() => Flow.ReadWithToken(card, source.Token));

            Assert.IsAssignableFrom<OperationCanceledException>(ex);
            Assert.NotEmpty(ex.Diagnostics.Trace);
            Assert.Equal(card.BeginTransactionCalls, card.EndTransactionCalls);
            Assert.False(card.IsConnected);
            Assert.True(card.TransmitCount < 10);
        }

        // ---- data retention ----

        [Fact]
        public void Without_retention_quality_is_still_judged_and_no_field_keeps_bytes()
        {
            var card = new SimulatedCard();
            card.MoiImage[SyntheticImage.PersonInfo + 208] = (byte)'9';

            CardReadResult result = Flow.Read(card, retainData: false);

            Assert.All(result.Fields, f => Assert.Null(f.Data));
            Assert.Equal(FieldQuality.Suspect, result.Get(CardFieldId.PersonInfo).Info.Quality);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.CitizenId).Info.Quality);
            Assert.Equal(209, result.Get(CardFieldId.PersonInfo).Info.ReceivedLength);
        }

        [Fact]
        public void Without_retention_a_suspect_cid_keeps_no_bytes_either()
        {
            var card = new SimulatedCard { Mode = SimReaderMode.DirectAnswer };
            card.AnswerOverride = (position, command) => Flow.IsCidRead(command) ? Flow.DirectText(BadChecksumCid) : null;

            CardFieldResult cid = Flow.Read(card, retainData: false).Get(CardFieldId.CitizenId);

            Assert.Equal(FieldQuality.Suspect, cid.Info.Quality);
            Assert.Null(cid.Data);
        }

        // ---- private mode ----

        private static SimulatedCard WithPrivateApplets()
        {
            var card = new SimulatedCard();
            card.AddApplet(SimulatedCard.NhsoAidHex, Enumerable.Repeat((byte)0x76, 0x200).ToArray());
            card.AddApplet(SimulatedCard.AdmAidHex, Enumerable.Repeat((byte)0x77, 0x40).ToArray());
            return card;
        }

        [Fact]
        public void Each_mode_runs_under_the_guard_of_that_mode()
        {
            Assert.Same(CommandPolicy.Public, CardReadWorkflow.GuardFor(CardReadMode.Public));
            Assert.Same(CommandPolicy.PublicAndPrivate, CardReadWorkflow.GuardFor(CardReadMode.PublicAndPrivate));
        }

        [Fact]
        public void Public_mode_never_touches_private_ranges_or_applets()
        {
            SimulatedCard card = WithPrivateApplets();

            Flow.Read(card);

            HashSet<int> offsets = card.AnsweredOffsets();
            foreach (FieldRange range in new[] { MoiProfile.Raw0000, MoiProfile.Raw00E2, MoiProfile.Raw0177, MoiProfile.Raw1619 })
            {
                Assert.DoesNotContain(offsets, o => o >= range.Offset && o < range.EndExclusive);
            }

            Assert.Empty(card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.AdmAidHex));
            Assert.DoesNotContain(ApduText.Hex(ApduText.Select(SimulatedCard.NhsoAidHex)), card.ReceivedHex);
            Assert.DoesNotContain(ApduText.Hex(ApduText.Select(SimulatedCard.AdmAidHex)), card.ReceivedHex);
        }

        [Theory]
        [InlineData((int)CardReadMode.Public)]
        [InlineData((int)CardReadMode.PublicAndPrivate)]
        public void No_mode_touches_the_never_read_ranges_even_with_a_card_offering_more(int modeValue)
        {
            var mode = (CardReadMode)modeValue;
            SimulatedCard card = WithPrivateApplets();
            card.OverAnswerBytes = 2;

            Flow.Read(card, mode: mode);

            HashSet<int> offsets = card.AnsweredOffsets();
            Assert.DoesNotContain(offsets, o => o >= MoiProfile.Never015A.Offset && o < MoiProfile.Never015A.EndExclusive);
            Assert.DoesNotContain(offsets, o => o >= MoiProfile.Never1627.Offset && o < MoiProfile.Never1627.EndExclusive);
        }

        [Fact]
        public void Private_mode_reads_public_then_raw_then_nhso_then_adm()
        {
            SimulatedCard card = WithPrivateApplets();

            CardReadResult result = Flow.Read(card, mode: CardReadMode.PublicAndPrivate);

            Assert.Equal(
                MoiProfile.PublicAndPrivate.Fields.Concat(NhsoProfile.Instance.Fields).Concat(AdmProfile.Instance.Fields).Select(f => f.Id),
                result.Fields.Select(f => f.Info.Id));
            Assert.All(result.Fields, f => Assert.Equal(FieldQuality.Ok, f.Info.Quality));
            Assert.Equal(new byte[] { 0x73, 0x73 }, result.Get(CardFieldId.MoiRaw0177).Data);
            Assert.Equal(16, result.Get(CardFieldId.AdmAppletData).Data!.Length);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.EnteringNhso));
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.EnteringAdm));
            Assert.Contains("8000000007", card.ReceivedHex);
        }

        [Fact]
        public void Private_applet_commands_use_the_locked_p2()
        {
            SimulatedCard card = WithPrivateApplets();
            card.GetResponseP2 = 0x01;

            Flow.Read(card, mode: CardReadMode.PublicAndPrivate);

            Assert.Equal(1, Flow.P2Switches(card));
            Assert.Equal("00c0000110", card.ReceivedHex.Last());
        }

        [Fact]
        public void Chip_data_commands_are_never_sent_in_step_5()
        {
            SimulatedCard card = WithPrivateApplets();

            Flow.Read(card, mode: CardReadMode.PublicAndPrivate);

            Assert.DoesNotContain(card.ReceivedHex, h => h.StartsWith("80ca", StringComparison.Ordinal));
        }

        [Fact]
        public void Absent_private_applets_fail_their_fields_only()
        {
            var card = new SimulatedCard();

            CardReadResult result = Flow.Read(card, mode: CardReadMode.PublicAndPrivate);

            Assert.All(MoiProfile.PublicAndPrivate.Fields, f => Assert.Equal(FieldQuality.Ok, result.Get(f.Id).Info.Quality));
            foreach (CardField field in NhsoProfile.Instance.Fields.Concat(AdmProfile.Instance.Fields))
            {
                FieldDiagnostics info = result.Get(field.Id).Info;
                Assert.Equal(FieldQuality.Failed, info.Quality);
                Assert.True(info.Issues.HasFlag(FieldIssue.AppletUnavailable));
                Assert.Equal(0x6A82, info.StatusWord);
            }

            Assert.Equal(0, Flow.Events(result.Diagnostics.Trace, TraceEvent.P2Fallback));
            Assert.Empty(card.AnsweredOffsets(SimulatedCard.NhsoAidHex));
        }

        [Fact]
        public void One_absent_private_applet_does_not_stop_the_other()
        {
            var card = new SimulatedCard();
            card.AddApplet(SimulatedCard.AdmAidHex, Enumerable.Repeat((byte)0x77, 0x40).ToArray());

            CardReadResult result = Flow.Read(card, mode: CardReadMode.PublicAndPrivate);

            Assert.Equal(FieldQuality.Failed, result.Get(CardFieldId.NhsoRaw0004).Info.Quality);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.AdmAppletData).Info.Quality);
        }

        [Fact]
        public void Bad_status_on_a_private_range_fails_that_field_only()
        {
            SimulatedCard card = WithPrivateApplets();
            string raw = ApduText.Hex(MoiCardCommands.Read(MoiProfile.Raw0177));
            card.AnswerOverride = (position, command) => ApduText.Hex(command) == raw ? new byte[] { 0x6A, 0x82 } : null;

            CardReadResult result = Flow.Read(card, mode: CardReadMode.PublicAndPrivate);

            Assert.Equal(FieldQuality.Failed, result.Get(CardFieldId.MoiRaw0177).Info.Quality);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.MoiRaw1619).Info.Quality);
            Assert.Equal(FieldQuality.Ok, result.Get(CardFieldId.Photo).Info.Quality);
        }

        [Theory]
        [InlineData((int)CardFailureKind.CardRemoved)]
        [InlineData((int)CardFailureKind.CardReset)]
        [InlineData((int)CardFailureKind.Communication)]
        public void Transport_failure_in_the_private_stage_keeps_every_public_field(int failureValue)
        {
            var failure = (CardFailureKind)failureValue;
            SimulatedCard card = WithPrivateApplets();
            int publicCommands = 1 + (26 * 2);
            card.FailTransmitAt(publicCommands + 3, failure);
            var spy = new DelaySpy(card);

            CardReadResult result = Flow.Read(card, Retries(), CardReadMode.PublicAndPrivate, spy);

            Assert.All(MoiProfile.Public.Fields, f => Assert.Equal(FieldQuality.Ok, result.Get(f.Id).Info.Quality));
            Assert.Equal(1, card.ConnectCalls);
            Assert.Empty(spy.Delays);
            Assert.Equal(1, card.EndTransactionCalls);

            FieldDiagnostics stopped = result.Get(CardFieldId.AdmAppletData).Info;
            Assert.Equal(FieldQuality.NotRead, stopped.Quality);
            Assert.True(stopped.Issues.HasFlag(FieldIssue.TransportFailed));
            Assert.Equal(failure, stopped.Failure);
            Assert.Equal(1, Flow.Events(result.Diagnostics.Trace, TraceEvent.PrivateStageStopped));
            Assert.Equal(CardFailureKind.None, result.Diagnostics.Failure);
        }

        // ---- diagnostics ----

        [Fact]
        public void Result_diagnostics_hold_reader_protocol_atr_trace_p2_and_fields()
        {
            var card = new SimulatedCard();

            ReadDiagnostics diagnostics = Flow.Read(card).Diagnostics;

            Assert.Equal(CardReadMode.Public, diagnostics.Mode);
            Assert.Equal(card.ReaderName, diagnostics.ReaderName);
            Assert.Equal(CardTransportProtocol.T0, diagnostics.Protocol);
            Assert.Equal("3B 79 18 00 00 FF", diagnostics.AtrHex);
            Assert.Equal(0x00, diagnostics.P2Used);
            Assert.Equal(6, diagnostics.Fields.Count);
            Assert.Equal(CardFailureKind.None, diagnostics.Failure);
            Assert.Equal(card.TransmitCount, diagnostics.Trace.Count(t => t.Kind != TraceCommandKind.Event));
            Assert.Equal(1, Flow.Events(diagnostics.Trace, TraceEvent.TransactionOpened));
            Assert.Equal(1, Flow.Events(diagnostics.Trace, TraceEvent.TransactionClosed));
        }

        [Fact]
        public void Exception_diagnostics_hold_the_fields_finished_before_the_failure()
        {
            var card = new SimulatedCard();
            card.FailTransmitAt(6, CardFailureKind.CardRemoved);

            var ex = Assert.Throws<CardRemovedException>(() => Flow.Read(card));

            Assert.Equal(new[] { CardFieldId.CitizenId, CardFieldId.PersonInfo }, ex.Diagnostics!.Fields.Select(f => f.Id));
        }

        [Fact]
        public void Reader_serial_never_reaches_the_diagnostics()
        {
            const string Named = "Vendor Model (A1B2C3D4E5) 00 00";
            var card = new SimulatedCard { ReaderName = Named };
            card.Statuses[0] = new ReaderStatus(Named, ReaderCardState.Present);

            ReadDiagnostics diagnostics = Flow.Read(card).Diagnostics;

            Assert.Equal("Vendor Model 00 00", diagnostics.ReaderName);
            Assert.Equal("Vendor Model 00 00", Assert.Single(diagnostics.Readers).DisplayName);
        }
    }
}
