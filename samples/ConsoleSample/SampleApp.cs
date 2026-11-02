using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ThaiNationalIDCard.ConsoleSample
{
    /// <summary>
    /// Reads one card in Public mode and prints what came back. Use it only on your own card or with the holder's consent. It
    /// keeps nothing: no file, no log and no network. Copy that part too.
    /// </summary>
    public static class SampleApp
    {
        public const int Success = 0;
        public const int BadArguments = 2;
        public const int ServiceUnavailable = 3;
        public const int NoReader = 5;
        public const int NoCard = 6;
        public const int UnsupportedCard = 7;
        public const int UnknownReadBehaviour = 8;
        public const int CardRemoved = 9;
        public const int CommunicationFailed = 10;
        public const int Canceled = 130;

        private const string ShowIdOption = "--show-citizen-id";
        private const string ReaderOption = "--reader";

        private const string Usage = "Options: " + ReaderOption + " NAME   read this reader (default: first reader with a readable card)\n" +
            "         " + ShowIdOption + "   print the whole citizen ID instead of a masked one";

        public static async Task<int> RunAsync(IThaiIdCardReader reader, string[] args, TextWriter output, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);

            bool showCitizenId = args.Contains(ShowIdOption);
            string[] rest = args.Where(a => a != ShowIdOption).ToArray();
            if (!(rest.Length == 0 || (rest.Length == 2 && rest[0] == ReaderOption)))
            {
                output.WriteLine(Usage);
                return BadArguments;
            }

            try
            {
                ThaiIdCardData card = rest.Length == 2
                    ? await reader.ReadAsync(rest[1], new ReadOptions { Mode = CardReadMode.Public }, token)
                    : await reader.ReadAsync(token);

                CardPrinter.Print(output, card, showCitizenId);
                return Success;
            }

            // Each type below is a different thing for the person at the reader to do. Every exception message from the library is
            // fixed text with no card data, so these could log it, but a message written for that person is more use.
            catch (SmartCardServiceUnavailableException ex) when (ex.NativeLibraryMissing)
            {
                return Report(output, ServiceUnavailable, "The PC/SC library is missing. On Linux, install pcscd and libpcsclite.");
            }
            catch (SmartCardServiceUnavailableException)
            {
                return Report(output, ServiceUnavailable, "The smart card service is not running. On Linux, start pcscd.");
            }
            catch (ReaderNotFoundException)
            {
                return Report(output, NoReader, "No matching card reader was found. Connect one, or check the name.");
            }
            catch (CardNotPresentException)
            {
                return Report(output, NoCard, "No card is in the reader. Insert one and run this again.");
            }
            catch (CardRemovedException)
            {
                return Report(output, CardRemoved, "The card was removed during the read. Leave it in until the read ends.");
            }
            catch (UnsupportedCardException)
            {
                return Report(output, UnsupportedCard, "This card did not answer as a Thai ID card, or the reader cannot talk to it.");
            }
            catch (UnknownReadBehaviourException)
            {
                return Report(output, UnknownReadBehaviour, "The card did not answer the citizen ID read as expected. The thaiid-diagnose tool can report why.");
            }
            catch (CardCommunicationException ex)
            {
                // Failure says which kind, for example Timeout or SharingViolation.
                return Report(output, CommunicationFailed, "Communication with the card failed: " + ex.Failure + ".");
            }
            catch (ThaiIdCardException ex)
            {
                // The base class comes last. It catches anything a later version of the library may add.
                return Report(output, CommunicationFailed, "The read failed: " + ex.Failure + ".");
            }
            catch (OperationCanceledException)
            {
                // ReadCanceledException is one of these, and also the plain one for a token cancelled before the read began.
                return Report(output, Canceled, "The read was cancelled.");
            }
        }

        private static int Report(TextWriter output, int exitCode, string message)
        {
            output.WriteLine(message);
            return exitCode;
        }
    }
}
