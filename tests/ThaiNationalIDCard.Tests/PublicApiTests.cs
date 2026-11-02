using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PCSC;
using PCSC.Exceptions;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Hardware.Pcsc;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Rules on the public surface, and sentinel checks that card data stays out of every text the library makes.</summary>
    public class PublicApiTests
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly Assembly Library = typeof(ThaiIdCardReader).Assembly;

        private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

        // ---- signatures ----

        private static bool MentionsPcsc(Type? type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.HasElementType)
            {
                return MentionsPcsc(type.GetElementType());
            }

            string? assembly = type.Assembly.GetName().Name;
            return (assembly != null && assembly.StartsWith("PCSC", StringComparison.Ordinal)) ||
                (type.IsGenericType && type.GetGenericArguments().Any(MentionsPcsc));
        }

        private static IEnumerable<string> PublicPcscUses(Type type)
        {
            if (MentionsPcsc(type.BaseType))
            {
                yield return type.Name + " base";
            }

            foreach (Type implemented in type.GetInterfaces().Where(MentionsPcsc))
            {
                yield return type.Name + " implements " + implemented.Name;
            }

            foreach (MemberInfo member in type.GetMembers(Declared))
            {
                bool visible;
                IEnumerable<Type> used;
                switch (member)
                {
                    case FieldInfo field:
                        visible = field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
                        used = new[] { field.FieldType };
                        break;
                    case PropertyInfo property:
                        visible = property.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly);
                        used = new[] { property.PropertyType };
                        break;
                    case EventInfo evt:
                        visible = evt.GetAddMethod(true) is { } add && (add.IsPublic || add.IsFamily || add.IsFamilyOrAssembly);
                        used = new[] { evt.EventHandlerType! };
                        break;
                    case MethodBase method:
                        visible = method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
                        used = method.GetParameters().Select(p => p.ParameterType)
                            .Concat(method is MethodInfo info ? new[] { info.ReturnType } : Array.Empty<Type>());
                        break;
                    case Type nested:
                        visible = nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamORAssem;
                        used = Array.Empty<Type>();
                        break;
                    default:
                        visible = false;
                        used = Array.Empty<Type>();
                        break;
                }

                if (!visible)
                {
                    continue;
                }

                foreach (Type mentioned in used.Where(MentionsPcsc))
                {
                    yield return type.Name + "." + member.Name + " -> " + mentioned.Name;
                }
            }
        }

        [Fact]
        public void No_pcsc_type_in_any_public_signature()
        {
            Type[] exported = Library.GetExportedTypes();

            Assert.NotEmpty(exported);
            Assert.Empty(exported.SelectMany(PublicPcscUses));
        }

        public sealed class PcscLeakProbe
        {
#pragma warning disable CA1051 // The probe needs a visible field.
            public SCardProtocol Field = SCardProtocol.T0;
#pragma warning restore CA1051

            public static SCardError Method(IEnumerable<SCardProtocol> values) => SCardError.Success;
        }

        [Fact]
        public void Scanner_control_it_finds_pcsc_in_a_public_signature()
        {
            string[] found = PublicPcscUses(typeof(PcscLeakProbe)).ToArray();

            Assert.Contains(found, f => f.Contains("Field", StringComparison.Ordinal));
            Assert.Contains(found, f => f.Contains("Method", StringComparison.Ordinal) && f.Contains("SCardError", StringComparison.Ordinal));
            Assert.Contains(found, f => f.Contains("Method", StringComparison.Ordinal) && f.Contains("IEnumerable", StringComparison.Ordinal));
        }

        [Fact]
        public void Every_public_type_is_in_the_root_namespace()
        {
            Assert.Empty(Library.GetExportedTypes().Where(t => t.Namespace != "ThaiNationalIDCard").Select(t => t.FullName));
        }

        [Fact]
        public void The_exceptions_are_the_planned_hierarchy()
        {
            Type[] planned =
            {
                typeof(SmartCardServiceUnavailableException), typeof(ReaderNotFoundException), typeof(CardNotPresentException),
                typeof(CardRemovedException), typeof(UnsupportedCardException), typeof(UnknownReadBehaviourException),
                typeof(CardCommunicationException),
            };
            Type[] found = Library.GetExportedTypes().Where(t => t != typeof(ThaiIdCardException) && typeof(ThaiIdCardException).IsAssignableFrom(t)).ToArray();

            Assert.Equal(planned.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal), found.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.True(typeof(ThaiIdCardException).IsPublic);
            Assert.NotNull(typeof(CardCommunicationException).GetProperty(nameof(CardCommunicationException.StatusWord)));
        }

        // ---- models ----

        [Fact]
        public void No_public_type_is_a_record_with_a_generated_to_string()
        {
            Assert.Empty(Library.GetExportedTypes().Where(t => t.GetMethod("<Clone>$") != null || t.GetMethod("PrintMembers", Declared) != null).Select(t => t.Name));
        }

        [Theory]
        [InlineData(typeof(ThaiIdCardData))]
        [InlineData(typeof(ThaiIdCardName))]
        [InlineData(typeof(ThaiIdCardAddress))]
        [InlineData(typeof(ThaiCardDate))]
        public void Types_holding_card_data_declare_their_own_to_string_and_have_no_public_setter(Type type)
        {
            Assert.Equal(type, type.GetMethod("ToString", Type.EmptyTypes)!.DeclaringType);
            Assert.Empty(type.GetProperties().Where(p => p.GetSetMethod() != null).Select(p => p.Name));
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));
        }

        [Fact]
        public void The_data_type_exposes_no_array_a_caller_could_change()
        {
            Assert.DoesNotContain(typeof(ThaiIdCardData).GetProperties(), p => p.PropertyType.IsArray);
        }

        [Fact]
        public void Address_raw_parts_cannot_be_changed_through_a_cast()
        {
            var address = new ThaiIdCardAddress(rawParts: new[] { "a" });

            Assert.True(!(address.RawParts is string[]) && ((ICollection<string>)address.RawParts).IsReadOnly);
        }

        // ---- sentinels ----

        private const string Marker = "ZQX";
        private const string ThaiMarker = "\u0E0B\u0E04\u0E0B";
        private const string Birth = "25400131";
        private const string IssueExpire = "2560013125690130";

        /// <summary>A card with a marker in every text field and in the photo, and made up digits elsewhere.</summary>
        private static SimulatedCard SentinelCard()
        {
            var card = new SimulatedCard();
            byte[] image = card.MoiImage;
            Put(image, SyntheticImage.PersonInfo, (ThaiMarker + "#" + ThaiMarker + "F#" + ThaiMarker + "M#" + ThaiMarker + "L").PadRight(100) +
                (Marker + "#" + Marker + "F#" + Marker + "M#" + Marker + "L").PadRight(100) + Birth + "1");
            Put(image, SyntheticImage.Address, (Marker + "H#" + Marker + "2#" + Marker + "3#" + Marker + "4#" + Marker + "5#" + Marker + "6#" + Marker + "7#" + ThaiMarker + "8").PadRight(160));
            Put(image, SyntheticImage.IssueExpire, IssueExpire);
            Put(image, SyntheticImage.Issuer, (Marker + "ISSUER").PadRight(100));

            byte[] comment = MiniJpeg.Segment(0xFE, Encoding.ASCII.GetBytes(Marker + "PHOTO"));
            byte[] jpeg = MiniJpeg.Join(MiniJpeg.Soi, comment, MiniJpeg.Sos, Encoding.ASCII.GetBytes(Marker + "ENTROPY"), MiniJpeg.Eoi);
            Array.Clear(image, SyntheticImage.Photo, SyntheticImage.PhotoLength);
            Array.Copy(jpeg, 0, image, SyntheticImage.Photo, jpeg.Length);
            return card;
        }

        private static void Put(byte[] image, int offset, string text)
        {
            byte[] bytes = SyntheticImage.Tis(text);
            Array.Copy(bytes, 0, image, offset, bytes.Length);
        }

        private static string Hex(string text) => string.Concat(SyntheticImage.Tis(text).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

        private static void AssertClean(string text)
        {
            string[] forbidden =
            {
                Marker, ThaiMarker, SimulatedCard.SyntheticCitizenId, Birth, IssueExpire,
                Hex(Marker), Hex(ThaiMarker), Hex(SimulatedCard.SyntheticCitizenId),
                Convert.ToBase64String(SyntheticImage.Tis(Marker + "ENTROPY")),
            };

            foreach (string form in forbidden)
            {
                Assert.DoesNotContain(form, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string Everything(ReadDiagnostics diagnostics) =>
            string.Join("\n", new object[] { diagnostics }
                .Concat(diagnostics.Trace.Cast<object>())
                .Concat(diagnostics.Fields.Cast<object>())
                .Concat(diagnostics.Readers.Select(r => (object)r.DisplayName)))
            + "\n" + diagnostics.AtrHex;

        private static ThaiIdCardReader Reader(SimulatedCard card) => new ThaiIdCardReader(() => card, null, new DelaySpy(card).Wait);

        [Fact]
        public async Task Control_the_sentinels_really_are_in_the_data()
        {
            ThaiIdCardData data = await Reader(SentinelCard()).ReadAsync("Synthetic Reader 0", new ReadOptions { IncludePhoto = true }, Ct);

            Assert.Equal(ThaiMarker, data.ThaiName.Prefix);
            Assert.Equal(Marker + "L", data.EnglishName.LastName);
            Assert.Equal(Marker + "H", data.Address.RawParts[0]);
            Assert.Equal(Marker + "ISSUER", data.Issuer);
            Assert.Equal(Birth, data.BirthDate.Raw);
            Assert.Contains(Marker + "ENTROPY", Encoding.ASCII.GetString(data.GetPhoto()!), StringComparison.Ordinal);
        }

        [Fact]
        public async Task To_string_of_the_result_and_its_parts_holds_no_card_data()
        {
            ThaiIdCardData data = await Reader(SentinelCard()).ReadAsync("Synthetic Reader 0", new ReadOptions { IncludePhoto = true }, Ct);

            object[] parts =
            {
                data, data.ThaiName, data.EnglishName, data.BirthDate, data.Address, data.IssueDate, data.ExpiryDate, data.Quality, data.Diagnostics,
            };

            Assert.All(parts, p => AssertClean(p.ToString()!));
        }

        [Fact]
        public async Task Diagnostics_hold_no_card_data()
        {
            ThaiIdCardData data = await Reader(SentinelCard()).ReadAsync("Synthetic Reader 0", new ReadOptions { IncludePhoto = true }, Ct);

            AssertClean(Everything(data.Diagnostics));
        }

        [Theory]
        [InlineData((int)CardFailureKind.CardRemoved)]
        [InlineData((int)CardFailureKind.Communication)]
        [InlineData((int)CardFailureKind.Timeout)]
        public async Task Exceptions_hold_no_card_data_at_any_position_of_the_read(int failureValue)
        {
            var failure = (CardFailureKind)failureValue;
            for (int position = 1; position <= 40; position += 3)
            {
                SimulatedCard card = SentinelCard();
                card.FailTransmitAt(position, failure);

                var ex = await Assert.ThrowsAnyAsync<ThaiIdCardException>(() => Reader(card).ReadAsync("Synthetic Reader 0", new ReadOptions { IncludePhoto = true }, Ct));

                AssertClean(ex.ToString());
                AssertClean(ex.Message);
                AssertClean(Everything(ex.Diagnostics!));
                Assert.Empty(ex.Data.Keys.Cast<object>());
            }
        }

        [Fact]
        public async Task An_exception_with_an_inner_pcsc_exception_holds_no_card_data()
        {
            for (int position = 1; position <= 40; position += 3)
            {
                int at = position;
                SimulatedCard card = SentinelCard();

                // The real mapping path of the adapter, reached after card data has already passed through.
                card.AnswerOverride = (current, command) => current == at
                    ? PcscErrors.Run<byte[]>(() => throw new PCSCException(SCardError.CommunicationError))
                    : null;

                var ex = await Assert.ThrowsAnyAsync<ThaiIdCardException>(() => Reader(card).ReadAsync("Synthetic Reader 0", new ReadOptions { IncludePhoto = true }, Ct));

                Assert.IsType<PCSCException>(ex.InnerException);
                AssertClean(ex.ToString());
                AssertClean(ex.InnerException!.ToString());
            }
        }

        [Fact]
        public async Task A_cancelled_read_holds_no_card_data()
        {
            using var source = new System.Threading.CancellationTokenSource();
            SimulatedCard card = SentinelCard();
            card.AnswerOverride = (current, command) =>
            {
                if (current == 12)
                {
                    source.Cancel();
                }

                return null;
            };

            var ex = await Assert.ThrowsAsync<ReadCanceledException>(() => Reader(card).ReadAsync("Synthetic Reader 0", new ReadOptions { IncludePhoto = true }, source.Token));

            AssertClean(ex.ToString());
            AssertClean(Everything(ex.Diagnostics));
        }

        [Theory]
        [InlineData(typeof(TraceEntry))]
        [InlineData(typeof(FieldDiagnostics))]
        [InlineData(typeof(PhotoReadProgress))]
        public void Types_that_travel_in_diagnostics_can_hold_no_bytes_or_text(Type type)
        {
            FieldInfo[] fields = type.GetFields(Declared).Where(f => !f.IsLiteral).ToArray();

            Assert.NotEmpty(fields);
            Assert.All(fields, f => Assert.True(f.FieldType == typeof(int) || (f.FieldType.IsEnum && Enum.GetUnderlyingType(f.FieldType) == typeof(int)), f.Name));
        }

        [Fact]
        public void Trace_entry_control_a_field_of_a_text_type_would_be_seen()
        {
            FieldInfo text = typeof(ReaderDiagnostics).GetFields(Declared).Single(f => f.FieldType == typeof(string));

            Assert.False(text.FieldType == typeof(int) || text.FieldType.IsEnum);
        }

        [Fact]
        public void Public_exceptions_can_be_built_by_a_consumer_with_fixed_messages()
        {
            var inner = new InvalidOperationException("inner");
            ThaiIdCardException[] built =
            {
                new SmartCardServiceUnavailableException(inner), new ReaderNotFoundException(inner), new CardNotPresentException(inner),
                new CardRemovedException(inner), new UnsupportedCardException(inner), new UnsupportedCardException(statusWord: 0x6A82),
                new UnknownReadBehaviourException(0x6F00), new CardCommunicationException(CardFailureKind.Timeout, inner, 0x6F00),
            };

            Assert.All(built, e => Assert.False(string.IsNullOrEmpty(e.Message)));
            Assert.Equal(0x6F00, new CardCommunicationException(CardFailureKind.Timeout, inner, 0x6F00).StatusWord);
        }

        [Fact]
        public void Internals_are_not_exposed_by_the_reader_type()
        {
            Assert.Single(typeof(ThaiIdCardReader).GetConstructors());
            Assert.Empty(typeof(ThaiIdCardReader).GetConstructors().Single().GetParameters());
        }

        [Fact]
        public void A_generated_enumerator_or_closure_is_not_public()
        {
            Assert.DoesNotContain(Library.GetExportedTypes(), t => t.GetCustomAttribute<CompilerGeneratedAttribute>() != null);
        }
    }
}
