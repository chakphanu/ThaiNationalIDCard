using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Parsing;
using ThaiNationalIDCard.Protocol;

namespace ThaiNationalIDCard.Workflow
{
    /// <summary>
    /// The state of one read: the read sequence of the plan, the P2 budget and the retry budget. Both budgets belong to
    /// the read, not to an attempt, so a restart after ResetCard cannot spend them again. An attempt that ends in a
    /// retryable failure drops all its fields: results of two attempts are never mixed.
    /// </summary>
    internal sealed class ReadRun
    {
        private const int NoStatus = TraceEntry.NoValue;

        private readonly GuardedCardConnection _connection;
        private readonly CardReadRequest _request;
        private readonly ReadPolicy _policy;
        private readonly Action<TimeSpan, CancellationToken> _delay;
        private readonly CancellationToken _token;
        private readonly ApduExchange _exchange;
        private readonly CardProfile _moi;
        private readonly List<CardFieldResult> _fields = new List<CardFieldResult>();

        private IReadOnlyList<ReaderDiagnostics> _readers = Array.Empty<ReaderDiagnostics>();
        private string? _readerDisplayName;
        private CardTransportProtocol? _protocol;
        private string _atrHex = string.Empty;
        private IReadOnlyList<byte>? _p2Order;
        private int _p2Index;
        private bool _p2FallbackSpent;
        private bool _connecting;

        public ReadRun(GuardedCardConnection connection, CardReadRequest request, ReadPolicy policy,
            Action<TimeSpan, CancellationToken> delay, CancellationToken token)
        {
            _connection = connection;
            _request = request;
            _policy = policy;
            _delay = delay;
            _token = token;
            _exchange = new ApduExchange(connection);
            _moi = request.Mode == CardReadMode.Public ? MoiProfile.Public : MoiProfile.PublicAndPrivate;
        }

        private byte CurrentP2 => _p2Order![_p2Index];

        private bool GetResponseWasSent => _exchange.Trace.Any(t => t.Kind == TraceCommandKind.GetResponse && t.Verdict == CommandVerdict.Allowed);

        public CardReadResult Execute()
        {
            IReadOnlyList<ReaderStatus> statuses = _connection.GetReaderStatuses();
            _readers = statuses.Select(s => new ReaderDiagnostics(ReaderNames.RemoveSerial(s.Name), s.State)).ToArray();
            string reader = ReaderSelector.Select(statuses, _request.ReaderName);
            _readerDisplayName = ReaderNames.RemoveSerial(reader);

            using IDisposable? hold = _request.AcquireReader?.Invoke(reader, _token);

            int retriesUsed = 0;
            try
            {
                while (true)
                {
                    _token.ThrowIfCancellationRequested();
                    try
                    {
                        return ReadOnce(reader);
                    }
                    catch (ThaiIdCardException ex) when (IsRetryable(ex.Failure) && retriesUsed < _policy.ConnectRetryDelays.Count)
                    {
                        // The filter runs before the finally that closes the transaction, this body after it.
                        DropFields();
                        _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.Retry, failure: ex.Failure));
                        DisconnectQuietly();
                        _delay(_policy.ConnectRetryDelays[retriesUsed], _token);
                        retriesUsed++;
                    }
                }
            }
            finally
            {
                DisconnectQuietly();
            }
        }

        public ReadDiagnostics Snapshot(CardFailureKind failure)
        {
            TraceEntry[] trace = _exchange.Trace.ToArray();
            int? p2Used = null;
            for (int i = trace.Length - 1; i >= 0 && p2Used == null; i--)
            {
                if (trace[i].Kind == TraceCommandKind.GetResponse && trace[i].Verdict == CommandVerdict.Allowed)
                {
                    p2Used = trace[i].GetResponseP2;
                }
            }

            return new ReadDiagnostics(_request.Mode, _readerDisplayName, _readers, _protocol, _atrHex, trace, p2Used,
                _fields.Select(f => f.Info).ToArray(), failure);
        }

        /// <summary>Removal stops a read in progress: the holder took the card, and it may come back as another card.</summary>
        private bool IsRetryable(CardFailureKind failure) =>
            failure == CardFailureKind.CardReset || failure == CardFailureKind.SharingViolation ||
            (_connecting && failure == CardFailureKind.CardRemoved);

        private CardReadResult ReadOnce(string reader)
        {
            _connecting = true;
            CardConnectionInfo info = _connection.Connect(reader);
            _protocol = info.Protocol;
            _atrHex = string.Join(" ", info.Atr.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.Connected));
            DecideP2Order(info.Atr);

            bool opened = false;
            if (_policy.UseTransaction)
            {
                _connection.BeginTransaction();
                opened = true;
                _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.TransactionOpened));
            }

            _connecting = false;
            try
            {
                ReadCard();
            }
            finally
            {
                if (opened)
                {
                    CloseTransaction();
                }
            }

            return new CardReadResult(_fields.ToArray(), Snapshot(CardFailureKind.None));
        }

        /// <summary>Decided once, from the first ATR. A restart keeps the order and the position in it.</summary>
        private void DecideP2Order(byte[] atr)
        {
            if (_p2Order != null)
            {
                return;
            }

            _p2Order = _policy.OrderP2(_moi, atr, out bool ruleUsed);
            if (ruleUsed)
            {
                _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.AtrP2RuleUsed, CurrentP2));
            }
        }

        /// <summary>Nothing raised here may hide the read result or the error already on its way out.</summary>
        private void CloseTransaction()
        {
            try
            {
                _connection.EndTransaction();
                _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.TransactionClosed));
            }
            catch (ThaiIdCardException ex)
            {
                _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.TransactionCloseFailed, failure: ex.Failure));
            }
        }

        private void DisconnectQuietly()
        {
            try
            {
                _connection.Disconnect();
            }
            catch (ThaiIdCardException)
            {
                // Same reason as CloseTransaction, and nothing is left to do with this connection.
            }
        }

        private void DropFields()
        {
            foreach (CardFieldResult field in _fields)
            {
                Clear(field.Data);
            }

            _fields.Clear();
        }

        private static void Clear(byte[]? data)
        {
            if (data != null)
            {
                Array.Clear(data, 0, data.Length);
            }
        }

        private void ReadCard()
        {
            _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.EnteringMoi));
            ApduResponse select = Select(_moi);
            if (!SelectAccepted(select))
            {
                throw new UnsupportedCardException(statusWord: select.StatusWord);
            }

            foreach (CardField field in _moi.Fields.Where(f => !f.IsPrivate))
            {
                _token.ThrowIfCancellationRequested();
                if (field.Check == FieldCheck.CitizenId)
                {
                    _fields.Add(RunCidGate(field));
                }
                else if (field.Id == CardFieldId.Photo && !_request.IncludePhoto)
                {
                    _fields.Add(new CardFieldResult(NotRead(field, FieldIssue.None, CardFailureKind.None), null));
                }
                else
                {
                    _fields.Add(ReadField(field));
                }
            }

            if (_request.Mode == CardReadMode.PublicAndPrivate)
            {
                ReadPrivateStage();
            }
        }

        // ---- SELECT ----

        private ApduResponse Select(CardProfile profile) =>
            _exchange.Exchange(MoiCardCommands.Select(profile), CurrentP2, _policy.DrainSelectResponse);

        private static bool SelectAccepted(ApduResponse select) => select.IsSuccess || select.Sw1 == 0x61;

        private void Reselect(int? lastStatus)
        {
            ApduResponse select = Select(_moi);
            if (!SelectAccepted(select))
            {
                // The applet answered a moment ago, so this is not a card without the applet.
                throw new UnknownReadBehaviourException(lastStatus ?? select.StatusWord);
            }
        }

        // ---- CID gate ----

        /// <summary>
        /// Valid digits lock the P2. Digits with a bad checksum get one re-read with the same P2, then the read goes on
        /// and the field is marked. Anything else gets a re-read: with the other P2 once per read if any GET RESPONSE was
        /// sent, and without touching P2 if none was, because then P2 played no part. No path repeats a GET RESPONSE.
        /// </summary>
        private CardFieldResult RunCidGate(CardField field)
        {
            FieldRange range = field.Range;
            bool checksumRetried = false;
            bool plainRetried = false;
            byte[]? digits = null;
            ExchangeNotes heldFlags = ExchangeNotes.None;
            int heldStatus = NoStatus;

            while (true)
            {
                _token.ThrowIfCancellationRequested();
                ApduResponse response = _exchange.Exchange(MoiCardCommands.Read(range), CurrentP2);
                ReadOutcomeKind outcome = ExchangeOutcome.OfRead(response, range.Length);
                bool complete = outcome == ReadOutcomeKind.Complete || outcome == ReadOutcomeKind.CompleteMoreOffered;
                ValidationFailures validation = complete ? FieldValidators.Check(field.Check, response.Data) : ValidationFailures.NotThirteenDigits;

                if (validation == ValidationFailures.None)
                {
                    LockP2();
                    Clear(digits);
                    FieldIssue issues = (checksumRetried ? FieldIssue.ChecksumRetried : FieldIssue.None) |
                        (outcome == ReadOutcomeKind.CompleteMoreOffered ? FieldIssue.MoreOffered : FieldIssue.None);
                    return Finish(field, response.Data, issues, ValidationFailures.None, response.StatusWord, response.Flags, CardFailureKind.None);
                }

                if (validation == ValidationFailures.ChecksumMismatch)
                {
                    Clear(digits);
                    digits = response.Data;
                    heldFlags = response.Flags;
                    heldStatus = response.StatusWord;
                    if (checksumRetried)
                    {
                        return FinishSuspectCid(field, digits, heldStatus, heldFlags);
                    }

                    checksumRetried = true;
                    _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.ChecksumRetry));
                    Reselect(response.StatusWord);
                    continue;
                }

                // No path below keeps these bytes.
                Clear(response.Data);

                if (digits != null)
                {
                    // The re-read gave no digits at all. What the first read gave is still the best there is.
                    return FinishSuspectCid(field, digits, heldStatus, heldFlags);
                }

                if (!GetResponseWasSent)
                {
                    if (plainRetried)
                    {
                        throw new UnknownReadBehaviourException(response.StatusWord);
                    }

                    plainRetried = true;
                    _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.NoGetResponseRetry));
                    Reselect(response.StatusWord);
                    continue;
                }

                if (!CanFallBack())
                {
                    throw new UnknownReadBehaviourException(LastGetResponseStatus() ?? response.StatusWord);
                }

                _p2FallbackSpent = true;
                _p2Index = (_p2Index + 1) % _p2Order!.Count;
                _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.P2Fallback, CurrentP2));
                Reselect(response.StatusWord);
            }
        }

        private bool CanFallBack() => _policy.AllowP2Fallback && !_p2FallbackSpent && _p2Order!.Count > 1;

        private void LockP2() => _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.P2Locked, CurrentP2));

        private CardFieldResult FinishSuspectCid(CardField field, byte[] digits, int status, ExchangeNotes flags)
        {
            LockP2();
            return Finish(field, digits, FieldIssue.ChecksumRetried, ValidationFailures.ChecksumMismatch, status, flags, CardFailureKind.None);
        }

        /// <summary>The last answer to a GET RESPONSE says more than the 61 xx or 90 00 the exchange stopped on.</summary>
        private int? LastGetResponseStatus()
        {
            for (int i = _exchange.Trace.Count - 1; i >= 0; i--)
            {
                if (_exchange.Trace[i].Kind == TraceCommandKind.GetResponse)
                {
                    return _exchange.Trace[i].StatusWord;
                }
            }

            return null;
        }

        // ---- fields ----

        private CardFieldResult ReadField(CardField field) =>
            field.Access == FieldAccess.AppletData ? ReadAppletData(field) : ReadChunks(field);

        /// <summary>
        /// Reads chunk after chunk and stops at the first one not complete, so nothing after a gap can shift into it.
        /// A status other than 90 00 or complete 61 xx makes the field Failed, with what came before the gap kept for now.
        /// </summary>
        private CardFieldResult ReadChunks(CardField field)
        {
            var buffer = new byte[field.Range.Length];
            int count = 0;
            int status = NoStatus;
            FieldIssue issues = FieldIssue.None;
            ExchangeNotes flags = ExchangeNotes.None;

            foreach (FieldRange chunk in MoiCardCommands.Chunks(field.Range))
            {
                _token.ThrowIfCancellationRequested();
                ApduResponse response = _exchange.Exchange(MoiCardCommands.Read(chunk), CurrentP2);
                ReadOutcomeKind outcome = ExchangeOutcome.OfRead(response, chunk.Length);
                status = response.StatusWord;
                flags |= response.Flags;
                Array.Copy(response.Data, 0, buffer, count, response.Data.Length);
                count += response.Data.Length;
                Clear(response.Data);

                if (field.Id == CardFieldId.Photo && count > 0)
                {
                    _request.PhotoProgress?.Invoke(count, field.Range.Length);
                }

                if (outcome == ReadOutcomeKind.CompleteMoreOffered)
                {
                    issues |= FieldIssue.MoreOffered;
                }
                else if (outcome == ReadOutcomeKind.Truncated)
                {
                    issues |= FieldIssue.Truncated;
                    break;
                }
                else if (outcome == ReadOutcomeKind.Failed)
                {
                    issues |= response.IsSuccess ? FieldIssue.Truncated : FieldIssue.BadStatus;
                    break;
                }
            }

            var data = new byte[count];
            Array.Copy(buffer, data, count);
            Clear(buffer);

            ValidationFailures validation = (issues & FieldIssue.BadStatus) != 0 || count == 0
                ? ValidationFailures.None
                : FieldValidators.Check(field.Check, data);
            return Finish(field, data, issues, validation, status, flags, CardFailureKind.None);
        }

        /// <summary>The answer length is not known, as the 7 + 16 byte reading of this command is unconfirmed.</summary>
        private CardFieldResult ReadAppletData(CardField field)
        {
            _token.ThrowIfCancellationRequested();
            ApduResponse response = _exchange.Exchange(MoiCardCommands.AppletData(), CurrentP2);
            FieldIssue issues = response.IsSuccess && response.Data.Length > 0 ? FieldIssue.None : FieldIssue.BadStatus;
            return Finish(field, response.Data, issues, ValidationFailures.None, response.StatusWord, response.Flags, CardFailureKind.None);
        }

        /// <summary>Validators have run by now, so this is the point where bytes go when the request does not keep them.</summary>
        private CardFieldResult Finish(CardField field, byte[] data, FieldIssue issues, ValidationFailures validation, int status,
            ExchangeNotes flags, CardFailureKind failure)
        {
            int requested = field.Access == FieldAccess.Read ? field.Range.Length : TraceEntry.NoValue;
            FieldQuality quality = QualityOf(data.Length, issues, validation);
            var info = new FieldDiagnostics(field.Id, quality, issues, validation, status, requested, data.Length, flags, failure);

            if (_request.RetainFieldData)
            {
                return new CardFieldResult(info, data);
            }

            Clear(data);
            return new CardFieldResult(info, null);
        }

        private static FieldQuality QualityOf(int length, FieldIssue issues, ValidationFailures validation)
        {
            if ((issues & FieldIssue.BadStatus) != 0 || length == 0)
            {
                return FieldQuality.Failed;
            }

            return (issues & FieldIssue.Truncated) != 0 || validation != ValidationFailures.None ? FieldQuality.Suspect : FieldQuality.Ok;
        }

        private static FieldDiagnostics NotRead(CardField field, FieldIssue issues, CardFailureKind failure) =>
            new FieldDiagnostics(field.Id, FieldQuality.NotRead, issues, ValidationFailures.None, NoStatus,
                field.Access == FieldAccess.Read ? field.Range.Length : TraceEntry.NoValue, 0, ExchangeNotes.None, failure);

        // ---- private stage ----

        /// <summary>
        /// Nothing in here can cost the public fields. A refused applet or a bad status is per field, and a connection
        /// failure stops the stage with the rest marked NotRead, with no restart: the public data is already complete.
        /// A guard refusal still escapes, since that is a library bug and not a card problem.
        /// </summary>
        private void ReadPrivateStage()
        {
            CardField[] moiPrivate = _moi.Fields.Where(f => f.IsPrivate).ToArray();
            try
            {
                foreach (CardField field in moiPrivate)
                {
                    _fields.Add(ReadField(field));
                }

                ReadApplet(NhsoProfile.Instance, TraceEvent.EnteringNhso);
                ReadApplet(AdmProfile.Instance, TraceEvent.EnteringAdm);
            }
            catch (ThaiIdCardException ex) when (!(ex is CommandRejectedException))
            {
                _exchange.AddEvent(TraceEntry.ForEvent(TraceEvent.PrivateStageStopped, failure: ex.Failure));
                IEnumerable<CardField> planned = moiPrivate.Concat(NhsoProfile.Instance.Fields).Concat(AdmProfile.Instance.Fields);
                foreach (CardField field in planned.Where(p => _fields.All(f => f.Info.Id != p.Id)))
                {
                    _fields.Add(new CardFieldResult(NotRead(field, FieldIssue.TransportFailed, ex.Failure), null));
                }
            }
        }

        private void ReadApplet(CardProfile profile, TraceEvent stage)
        {
            _exchange.AddEvent(TraceEntry.ForEvent(stage));
            ApduResponse select = Select(profile);
            if (!SelectAccepted(select))
            {
                foreach (CardField field in profile.Fields)
                {
                    int requested = field.Access == FieldAccess.Read ? field.Range.Length : TraceEntry.NoValue;
                    var info = new FieldDiagnostics(field.Id, FieldQuality.Failed, FieldIssue.AppletUnavailable, ValidationFailures.None,
                        select.StatusWord, requested, 0, select.Flags, CardFailureKind.None);
                    _fields.Add(new CardFieldResult(info, null));
                }

                return;
            }

            foreach (CardField field in profile.Fields)
            {
                _fields.Add(ReadField(field));
            }
        }
    }
}
