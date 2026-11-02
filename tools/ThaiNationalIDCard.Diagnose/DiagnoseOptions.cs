using System;
using System.Linq;

namespace ThaiNationalIDCard.Diagnose
{
    /// <summary>
    /// Command line settings. There is deliberately no option that prints card data, and none should be added: a report
    /// is safe to share because no switch can change what it holds.
    /// </summary>
    internal sealed class DiagnoseOptions
    {
        private const int MaxEchoLength = 40;

        public CardReadMode Mode { get; private set; } = CardReadMode.Public;

        public string? ReaderName { get; private set; }

        public bool Json { get; private set; }

        public bool ShowHelp { get; private set; }

        public bool ShowVersion { get; private set; }

        /// <summary>Returns null and an error text on bad arguments. The text never repeats a value.</summary>
        public static DiagnoseOptions? Parse(string[] args, out string? error)
        {
            var options = new DiagnoseOptions();
            bool modeSeen = false;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                int equals = arg.IndexOf('=', StringComparison.Ordinal);
                string name = equals >= 0 ? arg.Substring(0, equals) : arg;
                string? value = equals >= 0 ? arg.Substring(equals + 1) : null;

                switch (name)
                {
                    case "--mode":
                    case "--reader":
                        if (value == null && i + 1 < args.Length)
                        {
                            value = args[++i];
                        }

                        if (string.IsNullOrEmpty(value))
                        {
                            error = name + " needs a value.";
                            return null;
                        }

                        if (name == "--reader")
                        {
                            if (options.ReaderName != null)
                            {
                                error = "--reader was given twice.";
                                return null;
                            }

                            options.ReaderName = value;
                            break;
                        }

                        if (modeSeen)
                        {
                            error = "--mode was given twice.";
                            return null;
                        }

                        if (value != "public" && value != "private")
                        {
                            error = "--mode takes public or private.";
                            return null;
                        }

                        modeSeen = true;
                        options.Mode = value == "private" ? CardReadMode.PublicAndPrivate : CardReadMode.Public;
                        break;

                    case "--json":
                        if (value != null || options.Json)
                        {
                            error = "--json takes no value and is given once.";
                            return null;
                        }

                        options.Json = true;
                        break;

                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;

                    case "--version":
                        options.ShowVersion = true;
                        break;

                    default:
                        error = name.StartsWith('-') ? "Unknown option " + Echo(name) + "." : "Unexpected argument.";
                        return null;
                }
            }

            return options;
        }

        private static string Echo(string name) =>
            new string(name.Where(c => c > ' ' && c <= '~').Take(MaxEchoLength).ToArray());
    }
}
