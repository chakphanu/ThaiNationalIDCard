using System;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>Transport level failure categories. The seam reports failures only through these.</summary>
    internal enum CardFailureKind
    {
        None = 0,
        ServiceUnavailable,
        ReaderNotFound,
        CardNotPresent,
        CardRemoved,
        CardReset,
        SharingViolation,
        Timeout,
        NotConnected,
        UnsupportedCard,
        MalformedResponse,
        Communication,

        /// <summary>SELECT of the MOI applet was refused.</summary>
        NoApplet,

        /// <summary>The CID gate did not get a citizen ID with any allowed attempt.</summary>
        UnknownReadBehaviour,
    }

    /// <summary>Single place that turns a failure category into the library exception for it.</summary>
    internal static class CardFailures
    {
        public static ThaiIdCardException ToException(CardFailureKind failure, Exception? innerException = null)
        {
            switch (failure)
            {
                case CardFailureKind.ServiceUnavailable:
                    return new SmartCardServiceUnavailableException(innerException);
                case CardFailureKind.ReaderNotFound:
                    return new ReaderNotFoundException(innerException);
                case CardFailureKind.CardNotPresent:
                    return new CardNotPresentException(innerException);
                case CardFailureKind.CardRemoved:
                    return new CardRemovedException(innerException);
                case CardFailureKind.UnsupportedCard:
                    return new UnsupportedCardException(innerException);
                default:
                    return new CardCommunicationException(failure, innerException);
            }
        }
    }
}
