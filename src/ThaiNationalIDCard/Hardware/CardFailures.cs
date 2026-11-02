using System;

namespace ThaiNationalIDCard.Hardware
{
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
