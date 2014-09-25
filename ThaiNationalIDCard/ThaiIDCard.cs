/* BSD license
 * Credit:  APDU Command from Mr.Manoi http://hosxp.net/index.php?option=com_smf&topic=22496
 * Require add reference: PresentationCore, System.Xaml, WindowsBase
 * Require add refrernce(Nuget): PCSC ( http://www.nuget.org/packages/PCSC/ )
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using PCSC;

namespace ThaiNationalIDCard
{
    public delegate void handlePhotoProgress(int value, int maximum);
    public delegate void handleCardInserted(Personal personal);
    public delegate void handleCardRemoved();

    public class ThaiIDCard
    {
        const int ECODE_SCardError = 256;
        const int ECODE_UNSUPPORT_CARD = 1;

        private SCardReader _reader;
        private SCardError _err;
        private IntPtr _pioSendPci;
        private SCardContext _hContext;
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



        private void _StatusChanged(StatusChangeEventArgs args)
        {
            if (args.NewState != SCRState.Present 
                || args.NewState == SCRState.InUse 
                || args.NewState == SCRState.Unpowered
                || args.NewState == SCRState.Mute
                || args.NewState == SCRState.Unavailable)
            {
                return;
            }


            try
            {
                Personal personal;
                if (eventCardInserted != null)
                {
                    personal = readAll();
                    eventCardInserted(personal);
                }
                if (eventCardInsertedWithPhoto != null)
                {
                    personal = readAllPhoto();
                    eventCardInsertedWithPhoto(personal);
                }
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Console.WriteLine(_error_message);
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
                _monitor = new SCardMonitor(new SCardContext(), SCardScope.System);
                _monitor.StatusChanged += (sender, args) => _StatusChanged(args);
                _monitor.CardRemoved += (sender, args) => _Removed("CardRemoved", args);

                _monitor.Start(readerName);
                return true;
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Console.WriteLine(_error_message);
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
                _error_message = "Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Console.WriteLine(_error_message);
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
            string result = System.Text.Encoding.UTF8.GetString(utf8);
            return result.Substring(0, result.Length - 2);
        }

        private byte[] SendCommand(byte[][] commands)
        {
            byte[] pbRecvBuffer;
            pbRecvBuffer = new byte[256];
            foreach (byte[] command in commands)
            {
                pbRecvBuffer = new byte[256];
                _err = _reader.Transmit(_pioSendPci, command, ref pbRecvBuffer);
                CheckErr(_err);
            }
            return pbRecvBuffer;
        }

        private byte[] SendPhotoCommand()
        {
            var s = new System.IO.MemoryStream();
            CMD_PAIR[] cmds_photo = _apdu.GET_CMD_CARD_PHOTO();

            for (int i = 0; i < cmds_photo.Length; i++)
            {
                byte[] recv1 = new byte[256];
                _err = _reader.Transmit(_pioSendPci, cmds_photo[i].CMD1, ref recv1);
                CheckErr(_err);

                if (recv1.Length > 0)
                {
                    byte[] recv2 = new byte[256];
                    _err = _reader.Transmit(_pioSendPci, cmds_photo[i].CMD2, ref recv2);
                    CheckErr(_err);
                    //s.Write(recv2, 0, xwd);
                    s.Write(recv2, 0, recv2.Length - 2);
                }
                if (eventPhotoProgress != null)
                    eventPhotoProgress(i + 1, cmds_photo.Length);
            }

            s.Seek(0, SeekOrigin.Begin);
            return s.ToArray();
        }


        public string[] GetReaders()
        {
            try
            {
                // Establish SCard context
                _hContext = new SCardContext();
                _hContext.Establish(SCardScope.System);
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
                _error_message = "Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Console.WriteLine(_error_message);
                return null;
            }
        }


        public Boolean Open(string readerName = null)
        {
            try
            {
                // Establish SCard context
                _hContext = new SCardContext();
                _hContext.Establish(SCardScope.System);

                // Create a _reader object using the existing context
                _reader = new SCardReader(_hContext);

                // Connect to the card
                if (readerName == null || readerName == String.Empty)
                {
                    // Retrieve the list of Smartcard _readers
                    string[] szReaders = _hContext.GetReaders();
                    if (szReaders.Length <= 0)
                        throw new PCSCException(SCardError.NoReadersAvailable,
                            "Could not find any Smartcard _reader.");

                    _err = _reader.Connect(szReaders[0],
                                SCardShareMode.Exclusive,
                                SCardProtocol.T0 | SCardProtocol.T1);
                    CheckErr(_err);
                }
                else
                {
                    _err = _reader.Connect(readerName,
                                SCardShareMode.Exclusive,
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
                
                if (atr[0] == 0x3B && atr[1] == 0x68)       //Smart card tested with old type (Figure A.)
                {
                    _apdu = new APDU_THAILAND_IDCARD_3B68();
                }
                else if (atr[0] == 0x3B && atr[1] == 0x78)   //Smart card tested with new type (figure B.) 
                {
                    _apdu = new APDU_THAILAND_IDCARD_3B68();
                }
                else if (atr[0] == 0x3B && atr[1] == 0x67)
                {
                    _apdu = new APDU_THAILAND_IDCARD_3B67();
                }
                else
                {
                    _error_code = ECODE_UNSUPPORT_CARD;
                    _error_message = "Card not support";
                    Console.WriteLine(_error_message);
                    return false;
                }


                return true;
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Console.WriteLine(_error_message);
                return false;
            }
        }

        public Boolean Close()
        {
            try
            {
                _hContext.Release();
                return true;
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Console.WriteLine(_error_message);
                return false;
            }
        }

        public Personal readCitizenid()
        {
            Personal personal = new Personal();
            if (Open())
            {
                // Send SELECT/RESET command
                SendCommand(_apdu.CMD_SELECT());

                // CID
                personal.Citizenid = GetUTF8FromAsciiBytes(SendCommand(_apdu.CMD_CID()));

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
                // Send SELECT/RESET command
                SendCommand(_apdu.CMD_SELECT());

                // CID
                personal.Citizenid = GetUTF8FromAsciiBytes(SendCommand(_apdu.CMD_CID()));

                // Fullname Thai + Eng + BirthDate + Sex
                personal.Info = GetUTF8FromAsciiBytes(SendCommand(_apdu.CMD_PERSON_INFO()));

                // Address
                personal.Address = GetUTF8FromAsciiBytes(SendCommand(_apdu.CMD_ADDRESS()));

                // issue/expire
                personal.Issue_Expire = GetUTF8FromAsciiBytes(SendCommand(_apdu.CMD_CARD_ISSUE_EXPIRE()));

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
