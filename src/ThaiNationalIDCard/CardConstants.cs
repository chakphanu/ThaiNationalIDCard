namespace ThaiNationalIDCard
{
    /// <summary>Which APDU command set a card needs (differs only in GET RESPONSE).</summary>
    internal enum ApduType
    {
        Type01,
        Type02,
    }

    /// <summary>Named constants for the card protocol and data layout.</summary>
    internal static class CardConstants
    {
        // Transport
        /// <summary>Largest short APDU response: 256 data bytes plus SW1/SW2.</summary>
        public const int ReceiveBufferSize = 258;

        /// <summary>Delay before reading the ATR after connecting (legacy value).</summary>
        public const int AtrReadDelayMs = 1500;

        // ATR detection
        public const int AtrMinLength = 2;
        public const byte AtrPrefixByte0 = 0x3B;
        public const byte AtrPrefixByte1 = 0x67;

        // Status words
        public const byte Sw1Normal = 0x90;
        public const byte Sw2Normal = 0x00;

        // Layout of the EF_PERSON_INFO text (209 bytes decoded)
        public const int ThaiNameOffset = 0;
        public const int EnglishNameOffset = 100;
        public const int NameFieldLength = 100;
        public const int BirthYearOffset = 200;
        public const int BirthYearLength = 4;
        public const int BirthMonthOffset = 204;
        public const int BirthDayOffset = 206;
        public const int DatePartLength = 2;
        public const int SexOffset = 208;
        public const int SexLength = 1;
    }
}
