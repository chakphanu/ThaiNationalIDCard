using System;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard
{
    // TODO(step 8): these become public with the public API. They are internal until then because the
    // public surface is designed in step 8. Messages are fixed text: they never hold card data.

    /// <summary>Base of every exception the library raises for a card or reader problem.</summary>
    internal class ThaiIdCardException : Exception
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
    internal sealed class SmartCardServiceUnavailableException : ThaiIdCardException
    {
        internal SmartCardServiceUnavailableException(Exception? innerException = null)
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
    internal sealed class ReaderNotFoundException : ThaiIdCardException
    {
        internal ReaderNotFoundException(Exception? innerException = null)
            : base(CardFailureKind.ReaderNotFound, "No matching smart card reader was found.", innerException)
        {
        }
    }

    /// <summary>The reader exists but holds no usable card.</summary>
    internal sealed class CardNotPresentException : ThaiIdCardException
    {
        internal CardNotPresentException(Exception? innerException = null)
            : base(CardFailureKind.CardNotPresent, "No card is present in the reader.", innerException)
        {
        }
    }

    /// <summary>The card was removed from the reader.</summary>
    internal sealed class CardRemovedException : ThaiIdCardException
    {
        internal CardRemovedException(Exception? innerException = null)
            : base(CardFailureKind.CardRemoved, "The card was removed from the reader.", innerException)
        {
        }
    }

    /// <summary>The reader cannot talk to this card, or the card refused SELECT of the MOI applet.</summary>
    internal sealed class UnsupportedCardException : ThaiIdCardException
    {
        internal UnsupportedCardException(Exception? innerException = null)
            : base(CardFailureKind.UnsupportedCard, "The card is not supported by the reader.", innerException)
        {
        }

        internal UnsupportedCardException(int statusWord)
            : base(CardFailureKind.NoApplet, "The card refused selection of the expected applet.", null)
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
    internal sealed class UnknownReadBehaviourException : ThaiIdCardException
    {
        internal UnknownReadBehaviourException(int? lastStatusWord)
            : base(CardFailureKind.UnknownReadBehaviour, "The card did not answer the citizen ID read as expected.", null)
        {
            LastStatusWord = lastStatusWord;
        }

        public int? LastStatusWord { get; }
    }

    /// <summary>The caller cancelled the read. It still is an OperationCanceledException, with the trace on it.</summary>
    internal sealed class ReadCanceledException : OperationCanceledException
    {
        internal ReadCanceledException(ReadDiagnostics diagnostics, System.Threading.CancellationToken token)
            : base("The read was cancelled.", token)
        {
            Diagnostics = diagnostics;
        }

        public ReadDiagnostics Diagnostics { get; }
    }

    /// <summary>Any other communication failure. <see cref="ThaiIdCardException.Failure"/> tells which.</summary>
    internal sealed class CardCommunicationException : ThaiIdCardException
    {
        internal CardCommunicationException(CardFailureKind failure, Exception? innerException = null)
            : base(failure, "Communication with the card failed: " + failure + ".", innerException)
        {
        }

        /// <summary>Status word that caused the failure, when there was one.</summary>
        public int? StatusWord { get; internal set; }
    }

    /// <summary>
    /// The guard refused a command. This signals a library or profile bug, not a card problem, so it is
    /// not part of the planned public exception list.
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
