using System;
using System.Threading;
using ThaiNationalIDCard.Hardware;

// Messages are fixed text so they can never hold card data, which is why these do not take the standard
// (string) and (string, Exception) constructors. Consumers can still build them for mocks.
#pragma warning disable CA1032

namespace ThaiNationalIDCard
{
    /// <summary>Base of every exception the library raises for a card or reader problem.</summary>
    public class ThaiIdCardException : Exception
    {
        internal ThaiIdCardException(CardFailureKind failure, string message, Exception? innerException)
            : base(message, innerException)
        {
            Failure = failure;
        }

        /// <summary>Category that callers and the trace use. Holds no card data.</summary>
        public CardFailureKind Failure { get; }

        /// <summary>What the read did up to this failure. The workflow sets it on everything leaving a read.</summary>
        public ReadDiagnostics? Diagnostics { get; internal set; }
    }

    /// <summary>The PC/SC service or its native library is not available.</summary>
    public sealed class SmartCardServiceUnavailableException : ThaiIdCardException
    {
        /// <summary>Creates the exception. Its message is fixed text.</summary>
        /// <param name="innerException">The underlying failure, if any.</param>
        public SmartCardServiceUnavailableException(Exception? innerException = null)
            : base(CardFailureKind.ServiceUnavailable, "The smart card service is not available.", innerException)
        {
        }

        /// <summary>True when the native PC/SC library could not be loaded, as opposed to a service that is down.</summary>
        public bool NativeLibraryMissing
        {
            get
            {
                for (Exception? current = InnerException; current != null; current = current.InnerException)
                {
                    if (current is DllNotFoundException || current is EntryPointNotFoundException)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>No reader exists, or the named reader does not exist.</summary>
    public sealed class ReaderNotFoundException : ThaiIdCardException
    {
        /// <summary>Creates the exception. Its message is fixed text.</summary>
        /// <param name="innerException">The underlying failure, if any.</param>
        public ReaderNotFoundException(Exception? innerException = null)
            : base(CardFailureKind.ReaderNotFound, "No matching smart card reader was found.", innerException)
        {
        }
    }

    /// <summary>The reader exists but holds no usable card.</summary>
    public sealed class CardNotPresentException : ThaiIdCardException
    {
        /// <summary>Creates the exception. Its message is fixed text.</summary>
        /// <param name="innerException">The underlying failure, if any.</param>
        public CardNotPresentException(Exception? innerException = null)
            : base(CardFailureKind.CardNotPresent, "No card is present in the reader.", innerException)
        {
        }
    }

    /// <summary>The card was removed from the reader.</summary>
    public sealed class CardRemovedException : ThaiIdCardException
    {
        /// <summary>Creates the exception. Its message is fixed text.</summary>
        /// <param name="innerException">The underlying failure, if any.</param>
        public CardRemovedException(Exception? innerException = null)
            : base(CardFailureKind.CardRemoved, "The card was removed from the reader.", innerException)
        {
        }
    }

    /// <summary>The reader cannot talk to this card, or the card refused SELECT of the MOI applet.</summary>
    public sealed class UnsupportedCardException : ThaiIdCardException
    {
        /// <summary>
        /// Creates the exception. With a status word it means the card refused selection of the expected applet, and
        /// otherwise that the reader cannot talk to this card. Its message is fixed text.
        /// </summary>
        /// <param name="innerException">The underlying failure, if any.</param>
        /// <param name="statusWord">Status word of the refused SELECT.</param>
        public UnsupportedCardException(Exception? innerException = null, int? statusWord = null)
            : base(
                statusWord == null ? CardFailureKind.UnsupportedCard : CardFailureKind.NoApplet,
                statusWord == null ? "The card is not supported by the reader." : "The card refused selection of the expected applet.",
                innerException)
        {
            StatusWord = statusWord;
        }

        /// <summary>Status word of the refused SELECT, when there was one.</summary>
        public int? StatusWord { get; }
    }

    /// <summary>
    /// The CID gate got no citizen ID with any allowed attempt, so nothing else was read. This is what a card with a
    /// moved layout or an unknown P2 is expected to look like. The attached trace is what the next release needs.
    /// </summary>
    public sealed class UnknownReadBehaviourException : ThaiIdCardException
    {
        /// <summary>Creates the exception. Its message is fixed text.</summary>
        /// <param name="lastStatusWord">The last status word the card gave, if any.</param>
        public UnknownReadBehaviourException(int? lastStatusWord = null)
            : base(CardFailureKind.UnknownReadBehaviour, "The card did not answer the citizen ID read as expected.", null)
        {
            LastStatusWord = lastStatusWord;
        }

        /// <summary>The last status word the card gave, if any.</summary>
        public int? LastStatusWord { get; }
    }

    /// <summary>The caller cancelled the read. It still is an OperationCanceledException, with the trace on it.</summary>
    public sealed class ReadCanceledException : OperationCanceledException
    {
        internal ReadCanceledException(ReadDiagnostics diagnostics, CancellationToken token)
            : base("The read was cancelled.", token)
        {
            Diagnostics = diagnostics;
        }

        /// <summary>What the read did up to the cancellation.</summary>
        public ReadDiagnostics Diagnostics { get; }
    }

    /// <summary>Any other communication failure. <see cref="ThaiIdCardException.Failure"/> tells which.</summary>
    public sealed class CardCommunicationException : ThaiIdCardException
    {
        /// <summary>Creates the exception. Its message is fixed text plus the name of the failure.</summary>
        /// <param name="failure">Which failure this is.</param>
        /// <param name="innerException">The underlying failure, if any.</param>
        /// <param name="statusWord">Status word that caused the failure, when there was one.</param>
        public CardCommunicationException(CardFailureKind failure, Exception? innerException = null, int? statusWord = null)
            : base(failure, "Communication with the card failed: " + failure + ".", innerException)
        {
            StatusWord = statusWord;
        }

        /// <summary>Status word that caused the failure, when there was one.</summary>
        public int? StatusWord { get; internal set; }
    }

    /// <summary>
    /// The guard refused a command. This signals a library or profile bug, not a card problem, so it is
    /// not part of the planned public exception list. It reaches callers as <see cref="ThaiIdCardException"/>.
    /// </summary>
    internal sealed class CommandRejectedException : ThaiIdCardException
    {
        internal CommandRejectedException(CommandVerdict verdict)
            : base(CardFailureKind.None, "The command guard refused a command: " + verdict + ".", null)
        {
            Verdict = verdict;
        }

        /// <summary>Why the guard refused the command.</summary>
        public CommandVerdict Verdict { get; }
    }
}
