using System;
using System.Linq;
using System.Reflection;
using ThaiNationalIDCard.Diagnose;
using Xunit;

namespace ThaiNationalIDCard.Diagnose.Tests
{
    public class ArgumentParserTests
    {
        private static DiagnoseOptions Good(params string[] args)
        {
            DiagnoseOptions? options = DiagnoseOptions.Parse(args, out string? error);
            Assert.Null(error);
            return options!;
        }

        [Fact]
        public void No_arguments_mean_public_mode_text_output_and_any_reader()
        {
            DiagnoseOptions options = Good();

            Assert.Equal(CardReadMode.Public, options.Mode);
            Assert.False(options.Json);
            Assert.Null(options.ReaderName);
            Assert.False(options.ShowHelp);
            Assert.False(options.ShowVersion);
        }

        [Fact]
        public void Mode_takes_public_or_private_with_a_space_or_an_equals_sign()
        {
            Assert.Equal(CardReadMode.Public, Good("--mode", "public").Mode);
            Assert.Equal(CardReadMode.PublicAndPrivate, Good("--mode", "private").Mode);
            Assert.Equal(CardReadMode.PublicAndPrivate, Good("--mode=private").Mode);
        }

        [Fact]
        public void Reader_json_help_and_version_are_understood()
        {
            Assert.Equal("Some Reader 0", Good("--reader", "Some Reader 0").ReaderName);
            Assert.Equal("Some Reader 0", Good("--reader=Some Reader 0").ReaderName);
            Assert.True(Good("--json").Json);
            Assert.True(Good("--help").ShowHelp);
            Assert.True(Good("-h").ShowHelp);
            Assert.True(Good("--version").ShowVersion);
        }

        [Theory]
        [InlineData("--mode")]
        [InlineData("--mode", "all")]
        [InlineData("--mode", "PUBLIC")]
        [InlineData("--mode=")]
        [InlineData("--reader")]
        [InlineData("--reader=")]
        [InlineData("--mode", "public", "--mode", "private")]
        [InlineData("--reader", "a", "--reader", "b")]
        [InlineData("--json", "--json")]
        [InlineData("positional")]
        [InlineData("--json=true")]
        public void Bad_arguments_give_an_error(params string[] args)
        {
            DiagnoseOptions? options = DiagnoseOptions.Parse(args, out string? error);

            Assert.Null(options);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("--raw")]
        [InlineData("--dump")]
        [InlineData("--data")]
        [InlineData("--show-data")]
        [InlineData("--show-card-data")]
        [InlineData("--verbose")]
        [InlineData("--debug")]
        [InlineData("--unsafe")]
        [InlineData("--include-data")]
        [InlineData("--output")]
        public void No_flag_exists_that_could_print_card_data(string flag)
        {
            Assert.Null(DiagnoseOptions.Parse(new[] { flag }, out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void The_options_type_has_only_the_known_settings()
        {
            string[] names = typeof(DiagnoseOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "Json", "Mode", "ReaderName", "ShowHelp", "ShowVersion" }, names);
        }

        [Fact]
        public void An_error_does_not_echo_a_value_that_could_be_a_serial()
        {
            DiagnoseOptions.Parse(new[] { "--mystery=ABC123SERIAL" }, out string? error);
            Assert.DoesNotContain("ABC123SERIAL", error, StringComparison.Ordinal);
            Assert.Contains("--mystery", error, StringComparison.Ordinal);

            DiagnoseOptions.Parse(new[] { "ABC123SERIAL" }, out error);
            Assert.DoesNotContain("ABC123SERIAL", error, StringComparison.Ordinal);
        }
    }
}
