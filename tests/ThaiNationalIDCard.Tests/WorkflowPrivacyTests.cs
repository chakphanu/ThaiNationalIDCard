using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Protocol;
using ThaiNationalIDCard.Workflow;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class WorkflowPrivacyTests
    {
        private const string Sentinel = "ZQXSENTINELZQX";

        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static SimulatedCard SentinelCard()
        {
            var card = new SimulatedCard();
            byte[] marker = Encoding.ASCII.GetBytes(Sentinel);
            byte[] image = card.MoiImage;
            for (int i = 0; i < image.Length; i++)
            {
                bool cid = i >= SyntheticImage.CitizenId && i < SyntheticImage.CitizenId + 13;
                if (!cid)
                {
                    image[i] = marker[i % marker.Length];
                }
            }

            card.AddApplet(SimulatedCard.NhsoAidHex, Enumerable.Range(0, 0x200).Select(i => marker[i % marker.Length]).ToArray());
            card.AddApplet(SimulatedCard.AdmAidHex, Enumerable.Range(0, 0x40).Select(i => marker[i % marker.Length]).ToArray());
            card.SelectBlob = marker.Take(10).ToArray();
            return card;
        }

        private static void AssertClean(string text)
        {
            Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
            Assert.DoesNotContain(SimulatedCard.SyntheticCitizenId, text, StringComparison.Ordinal);
            Assert.DoesNotContain("53454E54", text, StringComparison.OrdinalIgnoreCase);
        }

        private static string Everything(ReadDiagnostics diagnostics) =>
            diagnostics + "\n" + string.Join("\n", diagnostics.Trace) + "\n" + string.Join("\n", diagnostics.Fields) + "\n" + diagnostics.AtrHex;

        [Theory]
        [InlineData((int)CardReadMode.Public)]
        [InlineData((int)CardReadMode.PublicAndPrivate)]
        public void Sentinel_is_not_in_result_or_diagnostics_text(int modeValue)
        {
            var mode = (CardReadMode)modeValue;
            SimulatedCard card = SentinelCard();

            CardReadResult result = Flow.Read(card, mode: mode);

            AssertClean(result.ToString());
            AssertClean(string.Join("\n", result.Fields));
            AssertClean(Everything(result.Diagnostics));
        }

        [Fact]
        public void Control_the_sentinel_really_is_in_the_retained_bytes()
        {
            SimulatedCard card = SentinelCard();

            CardReadResult result = Flow.Read(card);

            Assert.Contains("SENTINEL", Encoding.ASCII.GetString(result.Fields.SelectMany(f => f.Data!).ToArray()), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData((int)CardFailureKind.CardRemoved)]
        [InlineData((int)CardFailureKind.Communication)]
        [InlineData((int)CardFailureKind.Timeout)]
        public void Sentinel_is_not_in_any_exception_text(int failureValue)
        {
            var failure = (CardFailureKind)failureValue;
            for (int position = 1; position <= 53; position += 4)
            {
                SimulatedCard card = SentinelCard();
                card.FailTransmitAt(position, failure);

                var ex = Assert.ThrowsAny<ThaiIdCardException>(() => Flow.Read(card));

                AssertClean(ex.ToString());
                AssertClean(Everything(ex.Diagnostics!));
            }
        }

        [Fact]
        public void Sentinel_is_not_in_unknown_behaviour_text()
        {
            SimulatedCard card = SentinelCard();
            card.AnswerOverride = (position, command) =>
                Flow.IsCidRead(command) ? Encoding.ASCII.GetBytes(Sentinel).Take(13).Concat(new byte[] { 0x90, 0x00 }).ToArray() : null;

            var ex = Assert.Throws<UnknownReadBehaviourException>(() => Flow.Read(card));

            AssertClean(ex.ToString());
            AssertClean(Everything(ex.Diagnostics!));
        }

        [Fact]
        public void Trace_entry_stays_numbers_and_enums_with_the_new_event_field()
        {
            foreach (FieldInfo field in typeof(TraceEntry).GetFields(All).Where(f => !f.IsLiteral))
            {
                bool isEnum = field.FieldType.IsEnum && Enum.GetUnderlyingType(field.FieldType) == typeof(int);
                Assert.True(isEnum || field.FieldType == typeof(int), field.Name);
            }
        }

        [Fact]
        public void Field_diagnostics_can_hold_no_bytes_or_text()
        {
            FieldInfo[] fields = typeof(FieldDiagnostics).GetFields(All).Where(f => !f.IsLiteral).ToArray();

            Assert.NotEmpty(fields);
            foreach (FieldInfo field in fields)
            {
                bool isEnum = field.FieldType.IsEnum && Enum.GetUnderlyingType(field.FieldType) == typeof(int);
                Assert.True(isEnum || field.FieldType == typeof(int), field.Name);
            }
        }

        [Fact]
        public void Read_diagnostics_has_no_byte_array_and_its_strings_are_reader_and_atr_only()
        {
            PropertyInfo[] properties = typeof(ReadDiagnostics).GetProperties(All);

            Assert.DoesNotContain(properties, p => p.PropertyType == typeof(byte[]));
            Assert.Equal(new[] { "AtrHex", "ReaderName" },
                properties.Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public void No_static_state_holds_a_read()
        {
            Type[] types = { typeof(CardReadWorkflow), typeof(ReadDiagnostics), typeof(CardReadResult), typeof(CardFieldResult) };
            IEnumerable<FieldInfo> statics = types.SelectMany(t => t.GetFields(All).Where(f => f.IsStatic && !f.IsLiteral));

            Assert.Empty(statics);
        }
    }
}
