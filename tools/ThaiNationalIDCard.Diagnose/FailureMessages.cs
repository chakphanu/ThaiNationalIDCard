using System;
using System.Globalization;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard.Diagnose
{
    internal enum HostPlatform
    {
        Other = 0,
        Linux,
        Windows,
    }

    /// <summary>What a failed run reports. Fixed text and numbers only.</summary>
    internal sealed class FailureReport
    {
        public FailureReport(string code, int exitCode, string message, string? hint = null)
        {
            Code = code;
            ExitCode = exitCode;
            Message = message;
            Hint = hint;
        }

        public string Code { get; }

        public int ExitCode { get; }

        public string Message { get; }

        public string? Hint { get; }
    }

    /// <summary>
    /// Maps the exception types of the library to a report. Exception messages are never printed: the library promises
    /// its own text holds no card data, but this tool must not depend on that for an exception it does not know.
    /// </summary>
    internal static class FailureMessages
    {
        public static FailureReport For(ThaiIdCardException exception, HostPlatform platform)
        {
            switch (exception)
            {
                case SmartCardServiceUnavailableException service:
                    return service.NativeLibraryMissing ? NativeLibraryMissing(platform) : ServiceUnavailable(platform);
                case ReaderNotFoundException _:
                    return new FailureReport("no_reader", ExitCodes.NoReader, "No matching smart card reader was found.",
                        "Connect a reader. With --reader, use a name exactly as listed under readers in this report.");
                case CardNotPresentException _:
                    return new FailureReport("no_card", ExitCodes.NoCard, "No card is in the reader.",
                        "Insert the card and run the tool again.");
                case UnsupportedCardException unsupported:
                    return Unsupported(unsupported);
                case UnknownReadBehaviourException unknown:
                    return new FailureReport("unknown_read_behaviour", ExitCodes.UnknownReadBehaviour,
                        "The card did not answer the citizen ID read as expected" + Status(unknown.LastStatusWord) + ".",
                        "Please send this report. It holds no personal data read from the card and it is what the next release needs.");
                case CardRemovedException _:
                    return new FailureReport("card_removed", ExitCodes.CardRemoved, "The card was removed during the read.",
                        "Leave the card in the reader until the tool ends.");
                case CommandRejectedException rejected:
                    return new FailureReport("guard_refused", ExitCodes.Internal,
                        "The command guard refused a command: " + rejected.Verdict + ". This is a library bug, not a card problem.");
                default:
                    return new FailureReport("communication", ExitCodes.Communication,
                        "Communication with the card failed: " + exception.Failure + ".");
            }
        }

        public static FailureReport Canceled() =>
            new FailureReport("canceled", ExitCodes.Canceled, "The read was cancelled.");

        public static FailureReport Unexpected(Exception exception) =>
            new FailureReport("internal", ExitCodes.Internal, "Unexpected failure of type " + exception.GetType().Name + ".");

        private static FailureReport ServiceUnavailable(HostPlatform platform)
        {
            string? hint = null;
            if (platform == HostPlatform.Linux)
            {
                hint = "Start the service: sudo systemctl start pcscd";
            }
            else if (platform == HostPlatform.Windows)
            {
                hint = "Start the Smart Card service: net start SCardSvr";
            }

            return new FailureReport("service_unavailable", ExitCodes.ServiceUnavailable, "The PC/SC smart card service is not available.", hint);
        }

        private static FailureReport NativeLibraryMissing(HostPlatform platform)
        {
            string? hint = null;
            if (platform == HostPlatform.Linux)
            {
                hint = "Install the PC/SC service and library: sudo apt install pcscd libpcsclite1 (Debian, Ubuntu) or sudo dnf install pcsc-lite (Fedora).";
            }
            else if (platform == HostPlatform.Windows)
            {
                hint = "winscard.dll is part of Windows, so check the Windows installation.";
            }

            return new FailureReport("native_library_missing", ExitCodes.NativeLibraryMissing, "The native PC/SC library could not be loaded.", hint);
        }

        private static FailureReport Unsupported(UnsupportedCardException exception) =>
            exception.Failure == CardFailureKind.NoApplet
                ? new FailureReport("unsupported_card", ExitCodes.UnsupportedCard,
                    "The card refused selection of the expected applet" + Status(exception.StatusWord) + ", so it may not be a Thai ID card.")
                : new FailureReport("unsupported_card", ExitCodes.UnsupportedCard, "The reader cannot talk to this card.");

        private static string Status(int? statusWord) =>
            statusWord.HasValue && statusWord.Value >= 0
                ? " (status " + statusWord.Value.ToString("X4", CultureInfo.InvariantCulture) + ")"
                : string.Empty;
    }
}
