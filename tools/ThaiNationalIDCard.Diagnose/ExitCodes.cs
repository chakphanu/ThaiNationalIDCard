namespace ThaiNationalIDCard.Diagnose
{
    /// <summary>
    /// Process exit codes. Success means the read ran to its end: individual fields can still have failed, and the
    /// report says which, because a failing field on a real card is exactly what this tool exists to show.
    /// </summary>
    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int Internal = 1;
        public const int Usage = 2;
        public const int ServiceUnavailable = 3;
        public const int NativeLibraryMissing = 4;
        public const int NoReader = 5;
        public const int NoCard = 6;
        public const int UnsupportedCard = 7;
        public const int UnknownReadBehaviour = 8;
        public const int CardRemoved = 9;
        public const int Communication = 10;
        public const int Canceled = 130;
    }
}
