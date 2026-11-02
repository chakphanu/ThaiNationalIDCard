using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ThaiNationalIDCard.Hardware;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Layer rules, checked on the built assembly including IL of method bodies.</summary>
    public class ArchitectureTests
    {
        private const string Root = "ThaiNationalIDCard";

        private static IEnumerable<Type> TypesIn(string ns) =>
            typeof(IRawCardChannel).Assembly.GetTypes().Where(t => t.Namespace == ns);

        private static bool IsPcsc(Type type)
        {
            string? assembly = type.Assembly.GetName().Name;
            return assembly != null && assembly.StartsWith("PCSC", StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ThaiNationalIDCard.Protocol")]
        [InlineData("ThaiNationalIDCard.Card")]
        [InlineData("ThaiNationalIDCard.Hardware")]
        [InlineData("ThaiNationalIDCard.Workflow")]
        [InlineData("ThaiNationalIDCard.Parsing")]
        [InlineData("ThaiNationalIDCard")]
        public void Namespace_does_not_reference_pcsc_types(string ns)
        {
            List<Type> types = TypesIn(ns).ToList();

            Assert.NotEmpty(types);
            Assert.Empty(types.SelectMany(t => ReferencedTypes(t).Where(IsPcsc).Select(r => t.Name + " -> " + r.FullName)));
        }

        [Theory]
        [InlineData("ThaiIDCard")]
        [InlineData("Personal")]
        [InlineData("APDU_THAILAND_IDCARD")]
        [InlineData("APDU_THAILAND_IDCARD_TYPE_01")]
        [InlineData("APDU_THAILAND_IDCARD_TYPE_02")]
        [InlineData("IAPDU_THAILAND_IDCARD")]
        [InlineData("CardConstants")]
        [InlineData("ICardTransport")]
        [InlineData("PcscCardTransport")]
        public void The_legacy_types_are_gone(string name)
        {
            Assert.Null(typeof(IRawCardChannel).Assembly.GetType(Root + "." + name));
        }

        [Fact]
        public void The_library_has_no_way_to_log()
        {
            // No console, debug or trace output means nothing a read holds can end up in a log by accident.
            Type[] logging = { typeof(Console), typeof(System.Diagnostics.Debug), typeof(System.Diagnostics.Trace), typeof(System.Diagnostics.TraceSource) };
            List<string> found = typeof(IRawCardChannel).Assembly.GetTypes()
                .SelectMany(t => ReferencedTypes(t).Where(logging.Contains).Select(r => t.Name + " -> " + r.Name))
                .ToList();

            Assert.Empty(found);
        }

        [Fact]
        public void Card_does_not_depend_on_the_layers_below_it()
        {
            Type[] referenced = TypesIn("ThaiNationalIDCard.Card").SelectMany(ReferencedTypes).ToArray();

            Assert.DoesNotContain(referenced, t => t.Namespace != null && t.Namespace.StartsWith(Root + ".Hardware", StringComparison.Ordinal));
            Assert.DoesNotContain(referenced, t => t.Namespace == Root + ".Protocol");
        }

        [Fact]
        public void Parsing_does_not_depend_on_hardware_protocol_or_workflow()
        {
            Type[] referenced = TypesIn("ThaiNationalIDCard.Parsing").SelectMany(ReferencedTypes).ToArray();

            Assert.Contains(referenced, t => t.Namespace == Root + ".Card");
            Assert.DoesNotContain(referenced, t => t.Namespace != null && t.Namespace.StartsWith(Root + ".Hardware", StringComparison.Ordinal));
            Assert.DoesNotContain(referenced, t => t.Namespace == Root + ".Protocol" || t.Namespace == Root + ".Workflow");
        }

        [Fact]
        public void Workflow_has_no_way_to_wait_except_the_injected_delay()
        {
            // Thread.Sleep or Task.Delay here could end up inside the transaction, where Windows resets an idle card.
            Type[] waiting = { typeof(System.Threading.Thread), typeof(System.Threading.Tasks.Task), typeof(System.Threading.Timer), typeof(System.Threading.SpinWait) };
            List<string> found = TypesIn("ThaiNationalIDCard.Workflow")
                .SelectMany(t => ReferencedTypes(t).Where(waiting.Contains).Select(r => t.Name + " -> " + r.Name))
                .ToList();

            Assert.Empty(found);
        }

        [Fact]
        public void The_monitor_has_no_way_to_read_a_card()
        {
            Type[] reading = { typeof(Hardware.GuardedCardConnection), typeof(Workflow.CardReadWorkflow), typeof(ThaiIdCardReader), typeof(Protocol.ApduExchange) };
            List<Type> referenced = typeof(ThaiIdCardMonitor).Assembly.GetTypes()
                .Where(t => t == typeof(ThaiIdCardMonitor) || (t.FullName ?? string.Empty).StartsWith(typeof(ThaiIdCardMonitor).FullName + "+", StringComparison.Ordinal))
                .SelectMany(ReferencedTypes)
                .ToList();

            Assert.Contains(typeof(Hardware.IReaderMonitor), referenced);
            Assert.DoesNotContain(referenced, reading.Contains);
        }

        [Fact]
        public void Scanner_control_it_finds_pcsc_in_the_adapter()
        {
            // Without this the tests above could pass because the scanner finds nothing.
            Assert.Contains(TypesIn("ThaiNationalIDCard.Hardware.Pcsc").SelectMany(ReferencedTypes), IsPcsc);
        }

        [Fact]
        public void Scanner_control_it_finds_a_reference_that_exists_only_in_a_method_body()
        {
            Assert.Contains(ReferencedTypes(typeof(ScannerProbe)), t => t == typeof(PCSC.SCardProtocol));
        }

        private static class ScannerProbe
        {
            // No signature or field mentions PCSC; only the body does.
            public static string Probe() => new PCSC.Exceptions.PCSCException(PCSC.SCardError.Success).Message + typeof(PCSC.SCardProtocol).Name;
        }

        private static HashSet<Type> ReferencedTypes(Type type)
        {
            var found = new HashSet<Type>();
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly;

            Add(found, type.BaseType);
            foreach (Type iface in type.GetInterfaces())
            {
                Add(found, iface);
            }

            foreach (FieldInfo field in type.GetFields(All))
            {
                Add(found, field.FieldType);
            }

            foreach (PropertyInfo property in type.GetProperties(All))
            {
                Add(found, property.PropertyType);
            }

            foreach (EventInfo evt in type.GetEvents(All))
            {
                Add(found, evt.EventHandlerType);
            }

            IEnumerable<MethodBase> methods = type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All));
            foreach (MethodBase method in methods)
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Add(found, parameter.ParameterType);
                }

                if (method is MethodInfo info)
                {
                    Add(found, info.ReturnType);
                }

                MethodBody? body = method.GetMethodBody();
                if (body != null)
                {
                    foreach (LocalVariableInfo local in body.LocalVariables)
                    {
                        Add(found, local.LocalType);
                    }

                    foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
                    {
                        if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
                        {
                            Add(found, clause.CatchType);
                        }
                    }

                    ScanIl(method, body, found);
                }
            }

            foreach (Type nested in type.GetNestedTypes(All))
            {
                found.UnionWith(ReferencedTypes(nested));
            }

            return found;
        }

        private static void ScanIl(MethodBase method, MethodBody body, HashSet<Type> found)
        {
            byte[]? il = body.GetILAsByteArray();
            if (il == null)
            {
                return;
            }

            Type[]? typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            Module module = method.Module;

            for (int i = 0; i < il.Length;)
            {
                OpCode op;
                if (il[i] == 0xFE)
                {
                    op = TwoByteOpCodes[il[i + 1]];
                    i += 2;
                }
                else
                {
                    op = OneByteOpCodes[il[i]];
                    i += 1;
                }

                switch (op.OperandType)
                {
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        MemberInfo? member = module.ResolveMember(BitConverter.ToInt32(il, i), typeArguments, methodArguments);
                        AddMember(found, member);
                        i += 4;
                        break;
                    case OperandType.InlineSwitch:
                        i += 4 + (4 * BitConverter.ToInt32(il, i));
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    default:
                        i += 4;
                        break;
                }
            }
        }

        private static void AddMember(HashSet<Type> found, MemberInfo? member)
        {
            switch (member)
            {
                case Type type:
                    Add(found, type);
                    break;
                case MethodBase method:
                    Add(found, method.DeclaringType);
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Add(found, parameter.ParameterType);
                    }

                    if (method is MethodInfo info)
                    {
                        Add(found, info.ReturnType);
                    }

                    break;
                case FieldInfo field:
                    Add(found, field.DeclaringType);
                    Add(found, field.FieldType);
                    break;
            }
        }

        private static void Add(HashSet<Type> found, Type? type)
        {
            if (type == null)
            {
                return;
            }

            if (type.HasElementType)
            {
                Add(found, type.GetElementType());
                return;
            }

            if (!found.Add(type))
            {
                return;
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    Add(found, argument);
                }
            }
        }

        private static readonly OpCode[] OneByteOpCodes = BuildOpCodes(single: true);
        private static readonly OpCode[] TwoByteOpCodes = BuildOpCodes(single: false);

        private static OpCode[] BuildOpCodes(bool single)
        {
            var table = new OpCode[256];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (OpCode)field.GetValue(null)!;
                if (op.Size == 1 && single)
                {
                    table[op.Value & 0xFF] = op;
                }
                else if (op.Size == 2 && !single)
                {
                    table[op.Value & 0xFF] = op;
                }
            }

            return table;
        }
    }
}
