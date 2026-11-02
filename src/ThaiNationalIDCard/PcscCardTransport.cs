using System;
using PCSC;
using PCSC.Exceptions;
using PCSC.Utils;

namespace ThaiNationalIDCard
{
    /// <summary>PC/SC backed implementation of <see cref="ICardTransport"/>.</summary>
    internal sealed class PcscCardTransport : ICardTransport, IDisposable
    {
        private readonly IContextFactory _contextFactory;
        private ISCardContext _context;
        private SCardReader _reader;

        public PcscCardTransport(IContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public byte[] Atr { get; private set; }

        public void Connect(string readerName)
        {
            _context = _contextFactory.Establish(SCardScope.System);
            _reader = new SCardReader(_context);

            if (string.IsNullOrEmpty(readerName))
            {
                // Open first available reader.
                string[] readers = _context.GetReaders();
                if (readers.Length <= 0)
                    throw new PCSCException(SCardError.NoReadersAvailable,
                        "Could not find any Smartcard _reader.");
                readerName = readers[0];
            }

            CheckErr(_reader.Connect(readerName,
                SCardShareMode.Shared,
                SCardProtocol.T0 | SCardProtocol.T1));

            // Same protocol validation as the legacy Open(). The explicit-length Transmit overload
            // below selects the PCI from ActiveProtocol itself (SCardPCI.GetPci), which matches the
            // T0/T1/Raw mapping the legacy code did by hand.
            switch (_reader.ActiveProtocol)
            {
                case SCardProtocol.T0:
                case SCardProtocol.T1:
                case SCardProtocol.Raw:
                    break;
                default:
                    throw new PCSCException(SCardError.ProtocolMismatch,
                        "Protocol not supported: "
                        + _reader.ActiveProtocol.ToString());
            }

            // The Status() return code was never checked by the legacy code; kept as is.
            _reader.Status(
                out string[] _,
                out SCardState _,
                out SCardProtocol _,
                out byte[] atr);
            Atr = atr;
        }

        public void Disconnect()
        {
            _reader.Disconnect(SCardReaderDisposition.Leave);
            _context.Release();
        }

        public byte[] Transmit(byte[] command)
        {
            // Explicit-length overload: the legacy code relied on the undocumented fact that
            // Transmit(pci, cmd, ref byte[]) shrinks the buffer to the response length.
            // Here the real length is reported through receiveLength and copied exactly.
            var receiveBuffer = new byte[CardConstants.ReceiveBufferSize];
            int receiveLength = receiveBuffer.Length;
            CheckErr(_reader.Transmit(command, command.Length, receiveBuffer, ref receiveLength));

            var response = new byte[receiveLength];
            Array.Copy(receiveBuffer, response, receiveLength);
            return response;
        }

        /// <summary>Releases the reader handle. ThaiIDCard does not call this yet (phase 4: IDisposable).</summary>
        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
        }

        private static void CheckErr(SCardError err)
        {
            if (err != SCardError.Success)
                throw new PCSCException(err,
                    SCardHelper.StringifyError(err));
        }
    }
}
