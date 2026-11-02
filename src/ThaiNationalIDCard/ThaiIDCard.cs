/* BSD license
 * Credit:  APDU Command from Mr.Manoi http://hosxp.net/index.php?option=com_smf&topic=22496
 * Require add reference: PresentationCore, System.Xaml, WindowsBase
 * Require add refrernce(Nuget): PCSC ( http://www.nuget.org/packages/PCSC/ ) and PCSC.Iso7816 ( https://www.nuget.org/packages/PCSC.Iso7816/ )
 */

using System;
using System.Linq;
using System.Text;
using System.IO;
using PCSC;
using PCSC.Iso7816;
using System.Diagnostics;
using System.Threading;
using PCSC.Monitoring;
using PCSC.Exceptions;
using PCSC.Utils;

namespace ThaiNationalIDCard
{
    public delegate void handlePhotoProgress(int value, int maximum);
    public delegate void handleCardInserted(Personal personal);
    public delegate void handleCardRemoved();

    public class ThaiIDCard
    {
        const int ECODE_SCardError = 256;
        const int ECODE_UNSUPPORT_CARD = 1;

        private static readonly IContextFactory _contextFactory = ContextFactory.Instance;
        // Only used by GetReaders(); connection handling lives in the transport.
        private ISCardContext _hContext;
        private readonly ICardTransport _transport;
        private readonly int _atrReadDelayMs;

        private IAPDU_THAILAND_IDCARD _apdu;
        private SCardMonitor _monitor;

        private string _error_message;
        private int _error_code;

        public ThaiIDCard()
            : this(new PcscCardTransport(_contextFactory), CardConstants.AtrReadDelayMs)
        {
        }

        /// <summary>Test seam: runs the APDU flow over any transport.</summary>
        internal ThaiIDCard(ICardTransport transport, int atrReadDelayMs)
        {
            _transport = transport;
            _atrReadDelayMs = atrReadDelayMs;
        }

        public event handlePhotoProgress eventPhotoProgress;
        public event handleCardInserted eventCardInserted;
        public event handleCardInserted eventCardInsertedWithPhoto;
        public event handleCardRemoved eventCardRemoved;

        public int ErrorCode()
        {
            return _error_code;
        }

        public string Error()
        {
            return _error_message;
        }

        public string Version()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        // internal (was private) so tests can drive it without an SCardMonitor.
        internal void _Inserted(CardStatusEventArgs args)
        {
            try
            {
                Personal personal;
                // KNOWN BUG (phase 3): (d) when both events are subscribed the card is read twice;
                // readAll can return null (Open failed) and that null is passed to the event handlers;
                // only PCSCException is caught, anything else escapes the monitor thread.
                if (eventCardInserted != null)
                {
                    personal = readAll(false, args.ReaderName);
                    eventCardInserted(personal);
                }
                if (eventCardInsertedWithPhoto != null)
                {
                    personal = readAll(true, args.ReaderName);
                    eventCardInsertedWithPhoto(personal);
                }
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "_Inserted Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
            }
           
        }

        private void _Removed(string eventName, CardStatusEventArgs unknown)
        {
            if (eventCardRemoved != null)
            {
                eventCardRemoved();
            }
        }

        public bool MonitorStart(string readerName)
        {
            try
            {
                _monitor = new SCardMonitor(_contextFactory, SCardScope.System);
                _monitor.CardInserted += (sender, args) => _Inserted(args);
                _monitor.CardRemoved += (sender, args) => _Removed("CardRemoved", args);

                _monitor.Start(readerName);
                return true;
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "MonitorStart Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
                return false;
            }
        }
        public bool MonitorStop(string readerName)
        {
            try
            {
                if (_monitor != null)
                    _monitor.Cancel();
                return true;

            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "MonitorStop Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
                return false;
            }
        }

        private string GetUTF8FromAsciiBytes(byte[] ascii_bytes)
        {
            // SendCommand output is passed as is, so status words are not trimmed here (hasStatusWord: false),
            // same as the legacy code. NUL/space padding at the end is now trimmed by the decoder.
            return ThaiCardTextDecoder.DecodeTis620(ascii_bytes, false);
        }

        private bool SelectApplet()
        {
            byte[] command = _apdu.APDU_SELECT(_apdu.AID_MOI);
            byte[] pbRecvBuffer = _transport.Transmit(command);
            var responseApdu = ParseResponse(pbRecvBuffer);

            if (responseApdu.SW1.Equals((byte)SW1Code.NormalDataResponse) || responseApdu.SW1.Equals((byte)SW1Code.Normal))
            {
                return true;
            }

            return false;
        }

        // The protocol argument of ResponseApdu does not influence SW1/SW2 parsing; it throws
        // InvalidApduException for responses shorter than 2 bytes, exactly as before.
        private static ResponseApdu ParseResponse(byte[] response)
        {
            return new ResponseApdu(response, IsoCase.Case2Short, SCardProtocol.T1);
        }

        private byte[] SendCommand(byte [] command)
        {
            byte[] pbRecvBuffer = _transport.Transmit(command);
            var responseApdu = ParseResponse(pbRecvBuffer);

            if(responseApdu.SW1.Equals((byte)SW1Code.NormalDataResponse))
            {
                command = _apdu.APDU_GET_RESPONSE().Concat(new byte[] { responseApdu.SW2 }).ToArray();
                // KNOWN BUG (phase 3): (b) the legacy code ignored the SCardError of this second Transmit.
                // The transport now throws PCSCException on any non-success error, so on this error path
                // the failure is surfaced instead of silently returning a zero-filled buffer. Phase 3 should
                // pin this with a test.
                pbRecvBuffer = _transport.Transmit(command);
                // Legacy: relied on the buffer being shrunk to the real length, so "Length - SW2 == 2"
                // meant "response is SW2 data bytes + SW1/SW2". With exact-length responses the check is
                // unchanged. Note it is also false when SW2 == 0 (256 data bytes), as before.
                if(pbRecvBuffer.Length - responseApdu.SW2  == 2)
                {
                    return pbRecvBuffer.Take(pbRecvBuffer.Length - 2).ToArray();
                }
            }

            return pbRecvBuffer;        
        }

        private byte[] SendPhotoCommand()
        {
            var s = new MemoryStream();
            byte[][] cmds_photo = _apdu.EF_CARD_PHOTO;

            for (int i = 0; i < cmds_photo.Length; i++)
            {
                // KNOWN BUG (phase 3): (a) SendCommand is called twice per chunk (40 transmits instead of 20).
                s.Write(SendCommand(cmds_photo[i]), 0, SendCommand(cmds_photo[i]).Length);
                eventPhotoProgress?.Invoke(i + 1, cmds_photo.Length);
            }

            s.Seek(0, SeekOrigin.Begin);
            return s.ToArray();
        }


        public string[] GetReaders()
        {
            try
            {
                _hContext = _contextFactory.Establish(SCardScope.System);
                string[] szReaders = _hContext.GetReaders();
                _hContext.Release();

                if (szReaders.Length <= 0)
                    throw new PCSCException(SCardError.NoReadersAvailable,
                        "Could not find any Smartcard reader.");
                return szReaders;
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "GetReaders Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
                // KNOWN BUG (phase 3): (c) "throw ex;" resets the stack trace, should be "throw;".
                throw ex;
            }
        }


        public Boolean Open(string readerName = null)
        {
            try
            {
                // delay 1.5 second for ATR reading.
                Thread.Sleep(_atrReadDelayMs);

                _transport.Connect(readerName);

                byte[] atr = _transport.Atr;

                if (atr == null || atr.Length < CardConstants.AtrMinLength)
                {
                    return false;
                }

                _apdu = CreateApdu(DetectApduType(atr));

                // select MOI Applet
                if (SelectApplet())
                {
                    return true;
                }
                else
                {
                    _error_code = ECODE_UNSUPPORT_CARD;
                    _error_message = "SmartCard not support(Cannot select Ministry of Interior Applet.)";
                    return false;
                }

                
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "Open Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
                return false;
            }
        }

        /// <summary>
        /// Legacy rule: ATR starting with 3B 67 is the "corruption card" (Type01), everything else is Type02.
        /// Open() rejects a null or shorter than 2 bytes ATR before calling this; here such input is Type02.
        /// </summary>
        internal static ApduType DetectApduType(byte[] atr)
        {
            if (atr != null
                && atr.Length >= CardConstants.AtrMinLength
                && atr[0] == CardConstants.AtrPrefixByte0
                && atr[1] == CardConstants.AtrPrefixByte1)
            {
                return ApduType.Type01;
            }

            return ApduType.Type02;
        }

        private static IAPDU_THAILAND_IDCARD CreateApdu(ApduType type)
        {
            return type == ApduType.Type01
                ? (IAPDU_THAILAND_IDCARD)new APDU_THAILAND_IDCARD_TYPE_01()
                : new APDU_THAILAND_IDCARD_TYPE_02();
        }

        public Boolean Close()
        {
            try
            {
                _transport.Disconnect();
                return true;
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "Close Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
                return false;
            }
        }

        public Personal readCitizenid(string readerName = null)
        {
            Personal personal = new Personal();
            if (Open(readerName))
            {
                // CID
                personal.Citizenid = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_CID));

                Close();
                return personal;
            }

            return null;
        }

        public Personal readAll(bool with_photo = false, string readerName = null)
        {
            Personal personal = new Personal();
            if (Open(readerName))
            {

                // CID
                personal.Citizenid = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_CID));

                // Fullname Thai + Eng + BirthDate + Sex
                personal.Info = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_PERSON_INFO));

                // Address
                personal.Address = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_ADDRESS));

                // issue/expire
                personal.Issue_Expire = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_CARD_ISSUE_EXPIRE));

                // issuer
                personal.Issuer = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_CARD_ISSUER));

                if (with_photo)
                {
                    // Photo
                    personal.PhotoRaw = SendPhotoCommand();
                }


                Close();
                return personal;
            }
            return null;
        }

        public Personal readAllPhoto(string readerName = null)
        {
            return readAll(true, readerName);
        }



    } // End class


}
