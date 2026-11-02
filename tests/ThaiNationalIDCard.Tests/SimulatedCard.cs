using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>How the simulated reader and card answer a READ.</summary>
    internal enum SimReaderMode
    {
        /// <summary>READ answers 61 xx, data comes with GET RESPONSE.</summary>
        GetResponse,

        /// <summary>READ answers data + 90 00 on the first transmit.</summary>
        DirectAnswer,

        /// <summary>READ answers 6C xx unless Le is what <see cref="SimulatedCard.ExactLength"/> wants.</summary>
        WrongLength,
    }

    /// <summary>What SELECT answers for an applet the card holds.</summary>
    internal enum SimSelectAnswer
    {
        Ok9000,
        Pending610A,
        NotFound6A82,
    }

    /// <summary>One byte range of an applet that left the card.</summary>
    internal readonly struct AnsweredRange
    {
        public AnsweredRange(string appletHex, int offset, int length)
        {
            AppletHex = appletHex;
            Offset = offset;
            Length = length;
        }

        public string AppletHex { get; }

        public int Offset { get; }

        public int Length { get; }
    }

    /// <summary>One call to Transmit: the command and either the response or the injected failure.</summary>
    internal sealed class SimExchange
    {
        public SimExchange(byte[] command, byte[]? response, CardFailureKind failure)
        {
            Command = command;
            Response = response;
            Failure = failure;
        }

        public byte[] Command { get; }

        public byte[]? Response { get; }

        public CardFailureKind Failure { get; }
    }

    /// <summary>
    /// Stateful fake card under the seam, so every test goes through the real guard. It holds a synthetic
    /// 5950 byte memory image and a pending response like T=0 GET RESPONSE.
    /// All data is made up. The status words chosen for wrong use (no pending response, no selected applet,
    /// wrong P2) are test assumptions: no real card was observed, and passing against this class only shows
    /// the logic matches these assumptions.
    /// </summary>
    internal sealed class SimulatedCard : IRawCardChannel
    {
        public const int ImageSize = 5950;

        public const string MoiAidHex = "a000000054480001";
        public const string NhsoAidHex = "a000000054480083";
        public const string AdmAidHex = "a000000084060002";

        /// <summary>Made up: the digits are sequential and the last one satisfies the checksum formula.</summary>
        public const string SyntheticCitizenId = "1234567890121";

        private readonly Dictionary<string, byte[]> _applets = new Dictionary<string, byte[]>();
        private readonly Dictionary<int, CardFailureKind> _transmitFailures = new Dictionary<int, CardFailureKind>();
        private readonly byte[] _selectBlob = Enumerable.Repeat((byte)0x5A, 10).ToArray();

        private byte[]? _pendingSource;
        private string? _pendingApplet;
        private int _pendingOffset;
        private int _pendingRemaining;
        private string? _selected;
        private bool _connected;
        private bool _inTransaction;

        public SimulatedCard()
        {
            _applets[MoiAidHex] = SyntheticImage.Build();
            Statuses = new List<ReaderStatus> { new ReaderStatus(ReaderName, ReaderCardState.Present) };
        }

        public string ReaderName { get; set; } = "Synthetic Reader 0";

        public byte[] Atr { get; set; } = { 0x3B, 0x79, 0x18, 0x00, 0x00, 0xFF };

        public SimReaderMode Mode { get; set; } = SimReaderMode.GetResponse;

        public SimSelectAnswer SelectAnswer { get; set; } = SimSelectAnswer.Ok9000;

        /// <summary>GET RESPONSE P2 the card accepts.</summary>
        public byte GetResponseP2 { get; set; }

        /// <summary>Largest data block per GET RESPONSE. Below 256 this makes chained 61 xx.</summary>
        public int ChainSize { get; set; } = 256;

        /// <summary>The card offers this many bytes more than Le asked for, like an over-long answer.</summary>
        public int OverAnswerBytes { get; set; }

        /// <summary>WrongLength mode: exact length the card wants for (offset, Le).</summary>
        public Func<int, int, int> ExactLength { get; set; } = (offset, le) => le;

        /// <summary>Answer to GET RESPONSE with the wrong P2.</summary>
        public byte[] WrongP2Response { get; set; } = { 0x6A, 0x86 };

        /// <summary>Whether GET RESPONSE with the wrong P2 also loses the pending response.</summary>
        public bool WrongP2LosesPending { get; set; }

        /// <summary>When it returns non null for (1-based transmit position, command), that is the raw answer.</summary>
        public Func<int, byte[], byte[]?>? AnswerOverride { get; set; }

        public List<ReaderStatus> Statuses { get; }

        public CardFailureKind? ConnectFailure { get; set; }

        public CardFailureKind? BeginTransactionFailure { get; set; }

        public List<SimExchange> Log { get; } = new List<SimExchange>();

        public IEnumerable<string> ReceivedHex => Log.Select(e => ApduText.Hex(e.Command));

        /// <summary>Every range that left the card, in order. A READ answered 61 xx records nothing until GET RESPONSE.</summary>
        public List<AnsweredRange> Answered { get; } = new List<AnsweredRange>();

        public int TransmitCount => Log.Count;

        public int BeginTransactionCalls { get; private set; }

        public int EndTransactionCalls { get; private set; }

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public bool IsConnected => _connected;

        public bool InTransaction => _inTransaction;

        public SimulatedReaderMonitor? LastMonitor { get; private set; }

        public byte[] MoiImage => _applets[MoiAidHex];

        public void AddApplet(string aidHex, byte[] image) => _applets[aidHex] = image;

        public void RemoveApplet(string aidHex) => _applets.Remove(aidHex);

        /// <summary>The Nth transmit (1-based) fails instead of reaching the card.</summary>
        public void FailTransmitAt(int position, CardFailureKind failure) => _transmitFailures[position] = failure;

        /// <summary>Every offset of one applet that left the card.</summary>
        public HashSet<int> AnsweredOffsets(string aidHex = MoiAidHex)
        {
            var offsets = new HashSet<int>();
            foreach (AnsweredRange range in Answered.Where(r => r.AppletHex == aidHex))
            {
                offsets.UnionWith(Enumerable.Range(range.Offset, range.Length));
            }

            return offsets;
        }

        public IReadOnlyList<string> ListReaders() => Statuses.Select(s => s.Name).ToList();

        public IReadOnlyList<ReaderStatus> GetReaderStatuses() => Statuses.ToList();

        public CardConnectionInfo Connect(string readerName)
        {
            ConnectCalls++;
            if (ConnectFailure.HasValue)
            {
                throw CardFailures.ToException(ConnectFailure.Value);
            }

            _connected = true;
            ClearCardState();
            return new CardConnectionInfo(readerName, Atr, CardTransportProtocol.T0);
        }

        public void Disconnect()
        {
            DisconnectCalls++;
            _connected = false;
            _inTransaction = false;
            ClearCardState();
        }

        public void BeginTransaction()
        {
            BeginTransactionCalls++;
            if (BeginTransactionFailure.HasValue)
            {
                throw CardFailures.ToException(BeginTransactionFailure.Value);
            }

            if (!_connected)
            {
                throw CardFailures.ToException(CardFailureKind.NotConnected);
            }

            _inTransaction = true;
        }

        public void EndTransaction()
        {
            EndTransactionCalls++;
            _inTransaction = false;
        }

        public byte[] Transmit(byte[] command)
        {
            byte[] copy = (byte[])command.Clone();
            int position = Log.Count + 1;

            if (_transmitFailures.TryGetValue(position, out CardFailureKind failure))
            {
                Log.Add(new SimExchange(copy, null, failure));
                ApplyFailure(failure);
                throw CardFailures.ToException(failure);
            }

            if (!_connected)
            {
                Log.Add(new SimExchange(copy, null, CardFailureKind.NotConnected));
                throw CardFailures.ToException(CardFailureKind.NotConnected);
            }

            byte[] response = AnswerOverride?.Invoke(position, copy) ?? Answer(copy);
            Log.Add(new SimExchange(copy, (byte[])response.Clone(), CardFailureKind.None));
            return response;
        }

        public IReaderMonitor CreateMonitor()
        {
            LastMonitor = new SimulatedReaderMonitor();
            return LastMonitor;
        }

        public void Dispose() => DisposeCalls++;

        private void ApplyFailure(CardFailureKind failure)
        {
            if (failure == CardFailureKind.CardRemoved)
            {
                _connected = false;
                _inTransaction = false;
                ClearCardState();
            }
            else if (failure == CardFailureKind.CardReset)
            {
                _inTransaction = false;
                ClearCardState();
            }
        }

        private void ClearCardState()
        {
            _selected = null;
            ClearPending();
        }

        private void ClearPending()
        {
            _pendingSource = null;
            _pendingRemaining = 0;
        }

        private byte[] Answer(byte[] c)
        {
            bool isGetResponse = c.Length == 5 && c[0] == 0x00 && c[1] == 0xC0;
            if (isGetResponse)
            {
                return AnswerGetResponse(c);
            }

            // Assumption of this simulator: any other command discards what was pending.
            ClearPending();

            if (c.Length >= 5 && c[0] == 0x00 && c[1] == 0xA4)
            {
                return AnswerSelect(c);
            }

            if (c.Length == 7 && c[0] == 0x80 && c[1] == 0xB0)
            {
                return AnswerRead(c);
            }

            return new byte[] { 0x6D, 0x00 };
        }

        private byte[] AnswerSelect(byte[] c)
        {
            string aid = ApduText.Hex(c.Skip(5).ToArray());
            if (!_applets.ContainsKey(aid) || SelectAnswer == SimSelectAnswer.NotFound6A82)
            {
                _selected = null;
                return new byte[] { 0x6A, 0x82 };
            }

            _selected = aid;
            if (SelectAnswer == SimSelectAnswer.Pending610A)
            {
                // The 10 bytes are outside the memory image, so they never appear in Answered.
                _pendingSource = _selectBlob;
                _pendingApplet = null;
                _pendingOffset = 0;
                _pendingRemaining = _selectBlob.Length;
                return new byte[] { 0x61, 0x0A };
            }

            return new byte[] { 0x90, 0x00 };
        }

        private byte[] AnswerRead(byte[] c)
        {
            if (_selected == null)
            {
                return new byte[] { 0x69, 0x85 };
            }

            byte[] image = _applets[_selected];
            int offset = (c[2] << 8) | c[3];
            int le = c[6] == 0 ? 256 : c[6];
            if (offset >= image.Length)
            {
                return new byte[] { 0x6B, 0x00 };
            }

            if (Mode == SimReaderMode.WrongLength)
            {
                int exact = ExactLength(offset, le);
                if (exact != le)
                {
                    return new byte[] { 0x6C, (byte)(exact & 0xFF) };
                }
            }

            int available = Math.Min(le + OverAnswerBytes, image.Length - offset);
            if (Mode == SimReaderMode.GetResponse)
            {
                _pendingSource = image;
                _pendingApplet = _selected;
                _pendingOffset = offset;
                _pendingRemaining = available;
                return new byte[] { 0x61, Announce(available) };
            }

            Answered.Add(new AnsweredRange(_selected, offset, available));
            return image.Skip(offset).Take(available).Concat(new byte[] { 0x90, 0x00 }).ToArray();
        }

        private byte[] AnswerGetResponse(byte[] c)
        {
            if (c[2] != 0x00 || c[3] != GetResponseP2)
            {
                if (WrongP2LosesPending)
                {
                    ClearPending();
                }

                return (byte[])WrongP2Response.Clone();
            }

            if (_pendingSource == null)
            {
                return new byte[] { 0x69, 0x85 };
            }

            int le = c[4] == 0 ? 256 : c[4];
            int give = Math.Min(Math.Min(le, _pendingRemaining), ChainSize);
            byte[] data = _pendingSource.Skip(_pendingOffset).Take(give).ToArray();
            if (_pendingApplet != null)
            {
                Answered.Add(new AnsweredRange(_pendingApplet, _pendingOffset, give));
            }

            _pendingOffset += give;
            _pendingRemaining -= give;

            if (_pendingRemaining > 0)
            {
                return data.Concat(new byte[] { 0x61, Announce(_pendingRemaining) }).ToArray();
            }

            ClearPending();
            return data.Concat(new byte[] { 0x90, 0x00 }).ToArray();
        }

        private byte Announce(int available) => (byte)(Math.Min(available, ChainSize) & 0xFF);
    }

    /// <summary>Monitor for tests: the test raises the events by hand.</summary>
    internal sealed class SimulatedReaderMonitor : IReaderMonitor
    {
        public event EventHandler<ReaderCardEventArgs>? CardInserted;

        public event EventHandler<ReaderCardEventArgs>? CardRemoved;

        public event EventHandler<ReaderMonitorFailureEventArgs>? Failed;

        public IReadOnlyList<string> StartedReaders { get; private set; } = Array.Empty<string>();

        public bool Stopped { get; private set; }

        public void Start(IReadOnlyList<string> readerNames) => StartedReaders = readerNames;

        public void Stop() => Stopped = true;

        public void Dispose()
        {
        }

        public void RaiseInserted(string reader, byte[] atr) => CardInserted?.Invoke(this, new ReaderCardEventArgs(reader, atr));

        public void RaiseRemoved(string reader) => CardRemoved?.Invoke(this, new ReaderCardEventArgs(reader, null));

        public void RaiseFailed(CardFailureKind failure) => Failed?.Invoke(this, new ReaderMonitorFailureEventArgs(failure));
    }

    /// <summary>
    /// Layout and contents of the synthetic image. This table is written separately from MoiProfile on purpose,
    /// so a wrong profile range shows up as a test failure. Bytes are made up, and the markers in ranges that
    /// must not be read make any leak obvious.
    /// </summary>
    internal static class SyntheticImage
    {
        public const int CitizenId = 0x0004;
        public const int PersonInfo = 0x0011;
        public const int Raw00E2 = 0x00E2;
        public const int Issuer = 0x00F6;
        public const int Never015A = 0x015A;
        public const int IssueExpire = 0x0167;
        public const int Raw0177 = 0x0177;
        public const int Gap0179 = 0x0179;
        public const int Photo = 0x017B;
        public const int PhotoLength = 5118;
        public const int Address = 0x1579;
        public const int AddressLength = 160;
        public const int Raw1619 = 0x1619;
        public const int Never1627 = 0x1627;

        /// <summary>Marker of ranges no mode may read. Not valid TIS-620 text and not card-like.</summary>
        public const byte NeverReadMarker = 0xEE;

        public const string PersonInfoText =
            "\u0E19\u0E32\u0E22#\u0E17\u0E14\u0E2A\u0E2D\u0E1A##\u0E2A\u0E31\u0E07\u0E40\u0E04\u0E23\u0E32\u0E30\u0E2B\u0E4C";

        public const string EnglishInfoText = "Mr.#Synthetic##Sample";
        public const string BirthAndSex = "254001311";
        public const string IssueExpireText = "2560013125690130";
        public const string IssuerText = "Synthetic Issuer";
        public const string AddressText = "99/9#Moo 5#Soi Test#Road Test##Tambol Test#Amphur Test#Province Test";

        static SyntheticImage()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static byte[] Tis(string text) => Encoding.GetEncoding("TIS-620").GetBytes(text);

        public static string FromTis(byte[] bytes) => Encoding.GetEncoding("TIS-620").GetString(bytes);

        public static byte[] Build()
        {
            var image = new byte[SimulatedCard.ImageSize];
            Fill(image, 0x0000, 4, 0x71);
            Put(image, CitizenId, SimulatedCard.SyntheticCitizenId);
            Put(image, PersonInfo, PersonInfoText.PadRight(100) + EnglishInfoText.PadRight(100) + BirthAndSex);
            Fill(image, Raw00E2, 20, 0x72);
            Put(image, Issuer, IssuerText.PadRight(100));
            Fill(image, Never015A, 13, NeverReadMarker);
            Put(image, IssueExpire, IssueExpireText);
            Fill(image, Raw0177, 2, 0x73);
            Fill(image, Gap0179, 2, 0x74);
            WritePhoto(image);
            Put(image, Address, AddressText.PadRight(AddressLength));
            Fill(image, Raw1619, 14, 0x75);
            Fill(image, Never1627, SimulatedCard.ImageSize - Never1627, NeverReadMarker);
            return image;
        }

        /// <summary>
        /// JPEG-shaped bytes, not a real picture. 90 00 sits at each 255 byte boundary so that stripping or
        /// keeping status words wrongly changes the result, and 0x20 sits inside the data.
        /// </summary>
        private static void WritePhoto(byte[] image)
        {
            for (int i = 0; i < PhotoLength; i++)
            {
                image[Photo + i] = (byte)((i * 31 + 7) & 0xFF);
            }

            image[Photo] = 0xFF;
            image[Photo + 1] = 0xD8;
            for (int boundary = 255; boundary <= PhotoLength; boundary += 255)
            {
                image[Photo + boundary - 2] = 0x90;
                image[Photo + boundary - 1] = 0x00;
            }

            image[Photo + 300] = 0x20;
            image[Photo + 301] = 0x20;
            image[Photo + PhotoLength - 2] = 0xFF;
            image[Photo + PhotoLength - 1] = 0xD9;
        }

        private static void Put(byte[] image, int offset, string text)
        {
            byte[] bytes = Tis(text);
            Array.Copy(bytes, 0, image, offset, bytes.Length);
        }

        private static void Fill(byte[] image, int offset, int length, byte value)
        {
            for (int i = 0; i < length; i++)
            {
                image[offset + i] = value;
            }
        }
    }

    /// <summary>Hex text and command bytes for tests. Test data only.</summary>
    internal static class ApduText
    {
        public static string Hex(byte[] bytes) =>
            string.Concat(bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        public static byte[] Bytes(string hex)
        {
            string clean = hex.Replace(" ", string.Empty);
            return Enumerable.Range(0, clean.Length / 2)
                .Select(i => Convert.ToByte(clean.Substring(i * 2, 2), 16))
                .ToArray();
        }

        public static byte[] Read(int offset, int le) =>
            new byte[] { 0x80, 0xB0, (byte)(offset >> 8), (byte)offset, 0x02, 0x00, (byte)(le & 0xFF) };

        public static byte[] GetResponse(byte p2, int le) =>
            new byte[] { 0x00, 0xC0, 0x00, p2, (byte)(le & 0xFF) };

        public static byte[] Select(string aidHex)
        {
            byte[] aid = Bytes(aidHex);
            return new byte[] { 0x00, 0xA4, 0x04, 0x00, (byte)aid.Length }.Concat(aid).ToArray();
        }

        public static byte[] Slice(byte[] image, int offset, int length) =>
            image.Skip(offset).Take(length).ToArray();
    }
}
