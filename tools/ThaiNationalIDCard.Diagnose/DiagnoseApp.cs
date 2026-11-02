using System;
using System.IO;
using System.Text;
using System.Threading;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Workflow;

namespace ThaiNationalIDCard.Diagnose
{
    /// <summary>
    /// The tool without its process around it, so tests can run it on an injected channel. It reads no input: it has to
    /// work unattended, which is also why the notice asks for nothing.
    /// </summary>
    internal static class DiagnoseApp
    {
        private const string Help =
            "thaiid-diagnose: trace a read of a Thai National ID card. Prints status words, lengths and pass or fail, never card data.\n" +
            "\n" +
            "Usage: thaiid-diagnose [--mode public|private] [--reader NAME] [--json]\n" +
            "\n" +
            "  --mode public   Read what is printed on the card (default).\n" +
            "  --mode private  Also probe private areas for status and length. The tool lists them before it reads.\n" +
            "  --reader NAME   Use this reader, by its name or as this tool prints it. Default: first reader holding a card.\n" +
            "  --json          Report as JSON on stdout. The notice goes to stderr.\n" +
            "  --version       Print the version.\n" +
            "  --help, -h      Print this text.\n" +
            "\n" +
            "Exit codes: 0 read finished (fields may still have failed, see the report), 1 internal, 2 usage,\n" +
            "3 no PC/SC service, 4 native library missing, 5 no reader, 6 no card, 7 unsupported card,\n" +
            "8 unknown read behaviour, 9 card removed, 10 communication failure, 130 cancelled.";

        public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<IRawCardChannel> channelFactory,
            HostPlatform platform, CancellationToken token)
        {
            DiagnoseOptions? options = DiagnoseOptions.Parse(args, out string? error);
            if (options == null)
            {
                stderr.WriteLine("error: usage: " + error);
                stderr.WriteLine("Run thaiid-diagnose --help for the options.");
                return ExitCodes.Usage;
            }

            if (options.ShowHelp)
            {
                stdout.WriteLine(Help);
                return ExitCodes.Success;
            }

            if (options.ShowVersion)
            {
                stdout.WriteLine(ReportWriter.ToolVersion);
                return ExitCodes.Success;
            }

            // In JSON mode stdout holds one document and nothing else.
            TextWriter noticeStream = options.Json ? stderr : stdout;
            foreach (string line in NoticeText.For(options.Mode))
            {
                noticeStream.WriteLine(line);
            }

            noticeStream.Flush();

            (ReadDiagnostics? diagnostics, FailureReport? failure) = Read(options, channelFactory, platform, token);

            if (options.Json)
            {
                WriteJson(stdout, options.Mode, diagnostics, failure);
            }
            else
            {
                ReportWriter.WriteText(stdout, options.Mode, diagnostics);
                if (failure != null)
                {
                    stderr.WriteLine("error: " + failure.Code + ": " + failure.Message);
                    if (failure.Hint != null)
                    {
                        stderr.WriteLine("hint: " + failure.Hint);
                    }
                }
            }

            return failure?.ExitCode ?? ExitCodes.Success;
        }

        /// <summary>The tool never asks to keep card bytes: they are cleared as soon as the validators of a field finished.</summary>
        internal static CardReadRequest BuildRequest(DiagnoseOptions options) =>
            new CardReadRequest { Mode = options.Mode, ReaderName = options.ReaderName, IncludePhoto = true, RetainFieldData = false };

        /// <summary>Card bytes stop here: only diagnostics leave this method.</summary>
        private static (ReadDiagnostics? Diagnostics, FailureReport? Failure) Read(DiagnoseOptions options,
            Func<IRawCardChannel> channelFactory, HostPlatform platform, CancellationToken token)
        {
            try
            {
                CardReadResult result = new CardReadWorkflow(channelFactory()).Read(BuildRequest(options), token);
                return (result.Diagnostics, null);
            }
            catch (ThaiIdCardException ex)
            {
                return (ex.Diagnostics, FailureMessages.For(ex, platform));
            }
            catch (ReadCanceledException ex)
            {
                return (ex.Diagnostics, FailureMessages.Canceled());
            }
#pragma warning disable CA1031 // The last line of defence: an unknown exception must not print its message or a stack.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return (null, FailureMessages.Unexpected(ex));
            }
        }

        private static void WriteJson(TextWriter stdout, CardReadMode mode, ReadDiagnostics? diagnostics, FailureReport? failure)
        {
            using var buffer = new MemoryStream();
            ReportWriter.WriteJson(buffer, mode, diagnostics, failure);
            stdout.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
        }
    }
}
