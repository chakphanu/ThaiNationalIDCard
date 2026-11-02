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
    }

    /// <summary>The PC/SC service or its native library is not available.</summary>
    internal sealed class SmartCardServiceUnavailableException : ThaiIdCardException
    {
        internal SmartCardServiceUnavailableException(Exception? innerException = null)
            : base(CardFailureKind.ServiceUnavailable, "The smart card service is not available.", innerException)
        {
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

    /// <summary>The reader cannot talk to this card at the transport level.</summary>
    internal sealed class UnsupportedCardException : ThaiIdCardException
    {
        internal UnsupportedCardException(Exception? innerException = null)
            : base(CardFailureKind.UnsupportedCard, "The card is not supported by the reader.", innerException)
        {
        }
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
