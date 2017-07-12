/* BSD license
 * Credit:  APDU Command from Mr.Manoi http://hosxp.net/index.php?option=com_smf&topic=22496
 * Require add reference: PresentationCore, System.Xaml, WindowsBase
 * Require add refrernce(Nuget): PCSC ( http://www.nuget.org/packages/PCSC/ )
 */

using System;
using System.Linq;
using System.Text;
using System.IO;
using PCSC;
using PCSC.Iso7816;
using System.Diagnostics;
using System.Threading;

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
        private ISCardContext _hContext;
        private SCardReader _reader;
        private SCardError _err;
        private IntPtr _pioSendPci;
        
        private IAPDU_THAILAND_IDCARD _apdu;
        private SCardMonitor _monitor;

        private string _error_message;
        private int _error_code;

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

        private void _Inserted(CardStatusEventArgs args)
        {
            try
            {
                Personal personal;
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

        private void CheckErr(SCardError _err)
        {
            if (_err != SCardError.Success)
                throw new PCSCException(_err,
                    SCardHelper.StringifyError(_err));
        }

        private string GetUTF8FromAsciiBytes(byte[] ascii_bytes)
        {
            byte[] utf8;
            utf8 = Encoding.Convert(
                Encoding.GetEncoding("TIS-620"),
                Encoding.UTF8,
                ascii_bytes
                );
            return Encoding.UTF8.GetString(utf8);
        }

        private bool SelectApplet()
        {
            byte[] command = _apdu.APDU_SELECT(_apdu.AID_MOI);
            byte[] pbRecvBuffer;
            pbRecvBuffer = new byte[256];
            _err = _reader.Transmit(_pioSendPci, command, ref pbRecvBuffer);
            CheckErr(_err);
            var responseApdu = new ResponseApdu(pbRecvBuffer, IsoCase.Case2Short, _reader.ActiveProtocol);

            if (responseApdu.SW1.Equals((byte)SW1Code.NormalDataResponse) || responseApdu.SW1.Equals((byte)SW1Code.Normal))
            {
                return true;
            }

            return false;
        }

        private byte[] SendCommand(byte [] command)
        {
            byte[] pbRecvBuffer;
            pbRecvBuffer = new byte[256];
            _err = _reader.Transmit(_pioSendPci, command, ref pbRecvBuffer);
            CheckErr(_err);
            var responseApdu = new ResponseApdu(pbRecvBuffer, IsoCase.Case2Short, _reader.ActiveProtocol);

            if(responseApdu.SW1.Equals((byte)SW1Code.NormalDataResponse))
            {
                command = _apdu.APDU_GET_RESPONSE().Concat(new byte[] { responseApdu.SW2 }).ToArray();
                pbRecvBuffer = new byte[258];
                _err = _reader.Transmit(_pioSendPci, command, ref pbRecvBuffer);
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
                throw ex;
            }
        }


        public Boolean Open(string readerName = null)
        {
            try
            {
                // delay 1.5 second for ATR reading.
                Thread.Sleep(1500);

                _hContext = _contextFactory.Establish(SCardScope.System);
                _reader = new SCardReader(_hContext);

                // Connect to the card
                if (String.IsNullOrEmpty(readerName))
                {
                    // Open first avaliable reader.
                    // Retrieve the list of Smartcard _readers
                    string[] szReaders = _hContext.GetReaders();
                    if (szReaders.Length <= 0)
                        throw new PCSCException(SCardError.NoReadersAvailable,
                            "Could not find any Smartcard _reader.");

                    _err = _reader.Connect(szReaders[0],
                                SCardShareMode.Shared,
                                SCardProtocol.T0 | SCardProtocol.T1);
                    CheckErr(_err);
                }
                else
                {
                    _err = _reader.Connect(readerName,
                                SCardShareMode.Shared,
                                SCardProtocol.T0 | SCardProtocol.T1);
                    CheckErr(_err);
                }


                _pioSendPci = new IntPtr();
                switch (_reader.ActiveProtocol)
                {
                    case SCardProtocol.T0:
                        _pioSendPci = SCardPCI.T0;
                        break;
                    case SCardProtocol.T1:
                        _pioSendPci = SCardPCI.T1;
                        break;
                    default:
                        throw new PCSCException(SCardError.ProtocolMismatch,
                            "Protocol not supported: "
                            + _reader.ActiveProtocol.ToString());
                }

                string[] readerNames;
                SCardProtocol proto;
                SCardState state;
                byte[] atr;

                var sc = _reader.Status(
                    out readerNames,    // contains the reader name(s)
                    out state,          // contains the current state (flags)
                    out proto,          // contains the currently used communication protocol
                    out atr);           // contains the ATR

                if (atr == null || atr.Length < 2)
                {
                    return false;
                }
                
                if (atr[0] == 0x3B && atr[1] == 0x67)
                {
                    /* corruption card */
                    _apdu = new APDU_THAILAND_IDCARD_TYPE_01();
                }
                else
                {
                    _apdu = new APDU_THAILAND_IDCARD_TYPE_02();
                }

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

        public Boolean Close()
        {
            try
            {
                _reader.Disconnect(SCardReaderDisposition.Leave);
                _hContext.Release();
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
