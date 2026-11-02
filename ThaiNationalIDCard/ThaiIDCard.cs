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
        const int ECODE_UNEXPECTED = 512;

        private static readonly IContextFactory _contextFactory = ContextFactory.Instance;
        private ISCardContext _hContext;
        private ISCardReader _reader;
        private SCardError _err;
        private IntPtr _pioSendPci;
        
        private IAPDU_THAILAND_IDCARD _apdu;
        private SCardMonitor _monitor;

        private string _error_message;
        private int _error_code;

        // Providers and delay are replaced only by the internal test constructor below.
        private readonly Func<ISCardContext> _contextProvider;
        private readonly Func<ISCardContext, ISCardReader> _readerProvider;
        private readonly int _atrDelayMilliseconds = 1500;

        public ThaiIDCard()
        {
            _contextProvider = () => _contextFactory.Establish(SCardScope.System);
            _readerProvider = context => new SCardReader(context);
        }

        // Test seam only (see InternalsVisibleTo): drives the card flow with a fake PC/SC context and reader.
        internal ThaiIDCard(ISCardContext context, ISCardReader reader, int atrDelayMilliseconds)
        {
            _contextProvider = () => context;
            _readerProvider = c => reader;
            _atrDelayMilliseconds = atrDelayMilliseconds;
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

        private void _Inserted(CardStatusEventArgs args)
        {
            try
            {
                var inserted = eventCardInserted;
                var insertedWithPhoto = eventCardInsertedWithPhoto;
                if (inserted == null && insertedWithPhoto == null)
                    return;

                // Read the card once and share the result (was: one full read per subscribed event).
                Personal personal = readAll(insertedWithPhoto != null, args.ReaderName);

                // readAll returns null when Open failed (error state already stored). Handlers still receive that
                // null exactly as before, because existing applications check for it to report the failure.
                if (inserted != null)
                    inserted(personal);
                if (insertedWithPhoto != null)
                    insertedWithPhoto(personal);
            }
            catch (PCSCException ex)
            {
                _error_code = ECODE_SCardError;
                _error_message = "_Inserted Err: " + ex.Message + " (" + ex.SCardError.ToString() + ")";
                Debug.Print(_error_message);
            }
            catch (Exception ex)
            {
                // This runs on the PC/SC monitor thread: an escaping exception would crash the process.
                _error_code = ECODE_UNEXPECTED;
                _error_message = "_Inserted Err: " + ex.Message;
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
                // A failed GET RESPONSE used to be ignored (zero-filled buffer returned); now it throws PCSCException.
                CheckErr(_err);
                if(pbRecvBuffer.Length - responseApdu.SW2  == 2)
                {
                    return pbRecvBuffer.Take(pbRecvBuffer.Length - 2).ToArray();
                }
            }

            return pbRecvBuffer;        
        }

        private byte[] SendPhotoCommand()
        {
            byte[][] cmds_photo = _apdu.EF_CARD_PHOTO;
            // Pre-size: each chunk is at most 255 bytes, so the stream never has to grow.
            var s = new MemoryStream(cmds_photo.Length * 256);

            for (int i = 0; i < cmds_photo.Length; i++)
            {
                // Transmit each chunk once and reuse the result (was: SendCommand called twice, 2x APDU traffic).
                byte[] chunk = SendCommand(cmds_photo[i]);
                s.Write(chunk, 0, chunk.Length);
                eventPhotoProgress?.Invoke(i + 1, cmds_photo.Length);
            }

            s.Seek(0, SeekOrigin.Begin);
            return s.ToArray();
        }


        public string[] GetReaders()
        {
            try
            {
                // Local context: do not store a released context in _hContext, and always release it.
                ISCardContext context = _contextProvider();
                string[] szReaders;
                try
                {
                    szReaders = context.GetReaders();
                }
                finally
                {
                    context.Release();
                }

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
                // Rethrow without resetting the stack trace.
                throw;
            }
        }


        // Best-effort release of reader and context after a failed Open/read; never throws, never touches error state.
        private void Abort()
        {
            try
            {
                if (_reader != null && _reader.IsConnected)
                    _reader.Disconnect(SCardReaderDisposition.Leave);
            }
            catch (Exception ex)
            {
                Debug.Print("Abort disconnect Err: " + ex.Message);
            }

            try
            {
                if (_hContext != null)
                    _hContext.Release();
            }
            catch (Exception ex)
            {
                Debug.Print("Abort release Err: " + ex.Message);
            }
        }

        public Boolean Open(string readerName = null)
        {
            bool opened = false;
            try
            {
                // delay 1.5 second for ATR reading.
                Thread.Sleep(_atrDelayMilliseconds);

                _hContext = _contextProvider();
                _reader = _readerProvider(_hContext);

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
                    case SCardProtocol.Raw:
                        _pioSendPci = SCardPCI.Raw;
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
                    opened = true;
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
            finally
            {
                // Every failure path used to leak the connected reader and the context.
                if (!opened)
                    Abort();
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
                try
                {
                    // CID
                    personal.Citizenid = GetUTF8FromAsciiBytes(SendCommand(_apdu.EF_CID));
                }
                catch
                {
                    // Do not leave the card connected when a transmit fails; the exception still propagates.
                    Abort();
                    throw;
                }

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
                try
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
                }
                catch
                {
                    // Do not leave the card connected when a transmit fails; the exception still propagates.
                    Abort();
                    throw;
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
