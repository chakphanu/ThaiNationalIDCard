using System;
using System.Linq;
using System.Reflection;
using ThaiNationalIDCard.Protocol;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class TraceEntryTests
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [Fact]
        public void Trace_entry_is_a_readonly_struct()
        {
            Type type = typeof(TraceEntry);

            Assert.True(type.IsValueType);
            Assert.Contains(type.GetCustomAttributesData(), a => a.AttributeType.Name == "IsReadOnlyAttribute");
        }

        [Fact]
        public void Trace_entry_has_no_field_able_to_hold_bytes_or_text()
        {
            FieldInfo[] fields = typeof(TraceEntry).GetFields(All).Where(f => !f.IsLiteral).ToArray();

            Assert.NotEmpty(fields);
            foreach (FieldInfo field in fields)
            {
                // Int and enums over int only. This also keeps out byte, char, string, arrays, spans, object and pointers.
                bool isEnum = field.FieldType.IsEnum && Enum.GetUnderlyingType(field.FieldType) == typeof(int);
                Assert.True(isEnum || field.FieldType == typeof(int), field.Name + " is " + field.FieldType);
            }
        }

        [Fact]
        public void Trace_entry_properties_expose_only_the_same_kinds_of_value()
        {
            foreach (PropertyInfo property in typeof(TraceEntry).GetProperties(All))
            {
                Assert.True(property.PropertyType.IsEnum || property.PropertyType == typeof(int), property.Name);
            }
        }

        [Fact]
        public void Test_above_would_catch_a_type_that_holds_bytes()
        {
            // Control: the check is not vacuous. ApduResponse holds bytes, and the same rule rejects it.
            FieldInfo[] fields = typeof(ApduResponse).GetFields(All);

            Assert.Contains(fields, f => f.FieldType == typeof(byte[]));
        }
    }
}
