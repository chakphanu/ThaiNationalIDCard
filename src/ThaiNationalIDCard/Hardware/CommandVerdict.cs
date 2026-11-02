namespace ThaiNationalIDCard.Hardware
{
    /// <summary>Result of checking one command against the guard. Holds no command bytes.</summary>
    internal enum CommandVerdict
    {
        Allowed = 0,
        Malformed,

        /// <summary>PIN, fingerprint or write family. No policy can allow these.</summary>
        AlwaysForbidden,
        NotAllowlisted,
        AppletNotAllowed,
        NoAppletSelected,
        OutsideDeclaredRange,
        NoPendingResponse,
    }
}
