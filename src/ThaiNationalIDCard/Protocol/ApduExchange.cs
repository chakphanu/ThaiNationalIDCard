using System;
using System.Collections.Generic;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard.Protocol
{
    /// <summary>
    /// The SW1 state machine: 90 00 finishes, 61 xx sends GET RESPONSE (00 means 256) up to a round limit, 6C xx
    /// resends once with the corrected length, anything else returns the status with empty data.
    /// One instance serves one read: <see cref="Trace"/> grows with every transmit attempt and survives an
    /// exception, so the caller attaches it to its result or error. No other state stays behind.
    /// </summary>
    internal sealed class ApduExchange
    {
        /// <summary>Stops a card that keeps announcing data. The value is a defensive choice, not a card fact.</summary>
        public const int MaxGetResponseRounds = 32;

        private const int ShortResponseLimit = 256;
        private const int ReadLeIndex = 6;
        private const int ReadCommandLength = 7;
        private const int ShortLeIndex = 4;
        private const int ShortCommandLength = 5;
        private const byte Sw1Success = 0x90;
        private const byte Sw1ResponsePending = 0x61;
        private const byte Sw1WrongLength = 0x6C;

        private readonly GuardedCardConnection _connection;
        private readonly List<TraceEntry> _trace = new List<TraceEntry>();

        public ApduExchange(GuardedCardConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>One entry per transmit attempt, including those the guard refused.</summary>
        public IReadOnlyList<TraceEntry> Trace => _trace;

        /// <summary>
        /// Runs one command to its end. For a READ, Data never holds more than the command asked for: GET RESPONSE
        /// asks for no more than what is still needed, and anything more that arrives is dropped and flagged.
        /// For SELECT, Data is always empty and the status is the one of SELECT itself, because the bytes after
        /// 61 xx are drained and discarded unread and the drain result decides nothing.
        /// Throws <see cref="CommandRejectedException"/> when the guard refuses the command itself. A refused
        /// follow-up comes back as a status with <see cref="ExchangeFlags.FollowUpRejected"/>, because the card
        /// asked for it, not the caller. With <paramref name="drainSelectResponse"/> false, a SELECT answering
        /// 61 xx stops there and sends no GET RESPONSE.
        /// </summary>
        public ApduResponse Exchange(byte[] command, byte getResponseP2, bool drainSelectResponse = true)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            TraceCommandKind kind = Classify(command);
            bool discard = kind == TraceCommandKind.Select;
            int capacity = discard ? 0 : RequestedLength(command, kind);
            var data = new byte[capacity];
            int count = 0;

            byte[] current = command;
            int round = 0;
            int getResponseRounds = 0;
            bool resent = false;
            byte firstSw1 = 0;
            byte firstSw2 = 0;
            byte lastSw1 = 0;
            byte lastSw2 = 0;
            ExchangeFlags flags = ExchangeFlags.None;

            while (true)
            {
                byte[] response;
                try
                {
                    response = _connection.Transmit(current);
                }
                catch (CommandRejectedException ex)
                {
                    AddTrace(current, round, getResponseP2, null, TraceEntry.NoValue, ExchangeFlags.None, ex.Verdict, CardFailureKind.None);
                    if (ReferenceEquals(current, command))
                    {
                        throw;
                    }

                    flags |= ExchangeFlags.FollowUpRejected;
                    return discard
                        ? Finish(Array.Empty<byte>(), firstSw1, firstSw2, flags)
                        : Finish(Array.Empty<byte>(), lastSw1, lastSw2, flags);
                }
                catch (ThaiIdCardException ex)
                {
                    AddTrace(current, round, getResponseP2, null, TraceEntry.NoValue, ExchangeFlags.None, CommandVerdict.Allowed, ex.Failure);
                    throw;
                }

                if (response.Length < 2)
                {
                    AddTrace(current, round, getResponseP2, null, TraceEntry.NoValue, ExchangeFlags.None, CommandVerdict.Allowed, CardFailureKind.MalformedResponse);
                    throw new CardCommunicationException(CardFailureKind.MalformedResponse);
                }

                int bodyLength = response.Length - 2;
                lastSw1 = response[bodyLength];
                lastSw2 = response[bodyLength + 1];
                if (round == 0)
                {
                    firstSw1 = lastSw1;
                    firstSw2 = lastSw2;
                }

                bool success = lastSw1 == Sw1Success && lastSw2 == 0x00;
                ExchangeFlags stepFlags = ExchangeFlags.None;

                if ((success || lastSw1 == Sw1ResponsePending) && !discard)
                {
                    int take = Math.Min(bodyLength, capacity - count);
                    Array.Copy(response, 0, data, count, take);
                    count += take;
                    if (take < bodyLength)
                    {
                        stepFlags |= ExchangeFlags.ExcessDropped;
                    }
                }

                if (discard && !drainSelectResponse && lastSw1 == Sw1ResponsePending)
                {
                    AddTrace(current, round, getResponseP2, bodyLength, (lastSw1 << 8) | lastSw2, stepFlags, CommandVerdict.Allowed, CardFailureKind.None);
                    return Finish(Array.Empty<byte>(), firstSw1, firstSw2, flags);
                }

                if (lastSw1 == Sw1ResponsePending)
                {
                    int available = lastSw2 == 0 ? ShortResponseLimit : lastSw2;
                    int wanted = discard ? ShortResponseLimit : capacity - count;

                    if (wanted == 0)
                    {
                        // All asked bytes are here and the card offers more. It stays on the card.
                        stepFlags |= ExchangeFlags.ExcessDropped;
                        flags |= stepFlags;
                        AddTrace(current, round, getResponseP2, bodyLength, (lastSw1 << 8) | lastSw2, stepFlags, CommandVerdict.Allowed, CardFailureKind.None);
                        return Finish(Slice(data, count), lastSw1, lastSw2, flags);
                    }

                    if (getResponseRounds >= MaxGetResponseRounds)
                    {
                        stepFlags |= ExchangeFlags.RoundLimitReached;
                        flags |= stepFlags;
                        AddTrace(current, round, getResponseP2, bodyLength, (lastSw1 << 8) | lastSw2, stepFlags, CommandVerdict.Allowed, CardFailureKind.None);
                        return discard
                            ? Finish(Array.Empty<byte>(), firstSw1, firstSw2, flags)
                            : Finish(Array.Empty<byte>(), lastSw1, lastSw2, flags);
                    }

                    if (available > wanted)
                    {
                        stepFlags |= ExchangeFlags.ExcessDropped;
                    }

                    flags |= stepFlags;
                    AddTrace(current, round, getResponseP2, bodyLength, (lastSw1 << 8) | lastSw2, stepFlags, CommandVerdict.Allowed, CardFailureKind.None);
                    current = BuildGetResponse(getResponseP2, Math.Min(available, wanted));
                    getResponseRounds++;
                    round++;
                    continue;
                }

                if (lastSw1 == Sw1WrongLength)
                {
                    byte[]? corrected = resent ? null : WithCorrectedLength(current, CorrectedLength(current, lastSw2, discard, capacity - count));
                    if (corrected != null)
                    {
                        resent = true;
                        stepFlags |= ExchangeFlags.ResentAfterWrongLength;
                        flags |= stepFlags;
                        AddTrace(current, round, getResponseP2, bodyLength, (lastSw1 << 8) | lastSw2, stepFlags, CommandVerdict.Allowed, CardFailureKind.None);
                        current = corrected;
                        round++;
                        continue;
                    }

                    if (resent)
                    {
                        stepFlags |= ExchangeFlags.WrongLengthRepeated;
                    }
                }

                if (success && kind == TraceCommandKind.Read && count < capacity)
                {
                    stepFlags |= ExchangeFlags.ShorterThanRequested;
                }

                flags |= stepFlags;
                AddTrace(current, round, getResponseP2, bodyLength, (lastSw1 << 8) | lastSw2, stepFlags, CommandVerdict.Allowed, CardFailureKind.None);

                if (discard)
                {
                    return Finish(Array.Empty<byte>(), firstSw1, firstSw2, flags);
                }

                // A status other than 90 00 gives no data, not even what a chain delivered before it.
                return Finish(success ? Slice(data, count) : Array.Empty<byte>(), lastSw1, lastSw2, flags);
            }
        }

        /// <summary>Adds a workflow decision to the trace, in order with the commands.</summary>
        public void AddEvent(TraceEntry entry) => _trace.Add(entry);

        private static ApduResponse Finish(byte[] data, byte sw1, byte sw2, ExchangeFlags flags) =>
            new ApduResponse(data, sw1, sw2, flags);

        private static byte[] Slice(byte[] data, int count)
        {
            if (count == data.Length)
            {
                return data;
            }

            var result = new byte[count];
            Array.Copy(data, result, count);
            return result;
        }

        private static TraceCommandKind Classify(byte[] command)
        {
            if (command.Length < 2)
            {
                return TraceCommandKind.Other;
            }

            byte cla = command[0];
            byte ins = command[1];
            if (cla == 0x00 && ins == 0xA4)
            {
                return TraceCommandKind.Select;
            }

            if (cla == 0x00 && ins == 0xC0)
            {
                return TraceCommandKind.GetResponse;
            }

            if (cla == 0x80 && ins == 0xB0)
            {
                return TraceCommandKind.Read;
            }

            return cla == 0x80 && ins == 0x00 ? TraceCommandKind.AppletData : TraceCommandKind.Other;
        }

        private static int LengthOf(byte le) => le == 0 ? ShortResponseLimit : le;

        private static int RequestedLength(byte[] command, TraceCommandKind kind)
        {
            if (kind == TraceCommandKind.Read && command.Length == ReadCommandLength)
            {
                return LengthOf(command[ReadLeIndex]);
            }

            return ShortResponseLimit;
        }

        /// <summary>A GET RESPONSE stays limited to what is still needed, also when 6C xx offers more.</summary>
        private static byte CorrectedLength(byte[] command, byte sw2, bool discard, int wanted)
        {
            bool limited = !discard && wanted > 0 && Classify(command) == TraceCommandKind.GetResponse;
            return limited ? (byte)(Math.Min(LengthOf(sw2), wanted) & 0xFF) : sw2;
        }

        private static byte[] BuildGetResponse(byte p2, int length) =>
            new byte[] { 0x00, 0xC0, 0x00, p2, (byte)(length & 0xFF) };

        /// <summary>
        /// The same command with the length 6C xx asked for, or null when it has no length to fix. READ keeps its
        /// length in the last byte of 02 00 Le, others with Le in P3. SELECT has none.
        /// </summary>
        private static byte[]? WithCorrectedLength(byte[] command, byte length)
        {
            TraceCommandKind kind = Classify(command);
            int index;
            if (kind == TraceCommandKind.Read && command.Length == ReadCommandLength)
            {
                index = ReadLeIndex;
            }
            else if (kind != TraceCommandKind.Select && command.Length == ShortCommandLength)
            {
                index = ShortLeIndex;
            }
            else
            {
                return null;
            }

            var corrected = (byte[])command.Clone();
            corrected[index] = length;
            return corrected;
        }

        private void AddTrace(byte[] command, int round, byte getResponseP2, int? receivedLength, int statusWord, ExchangeFlags flags,
            CommandVerdict verdict, CardFailureKind failure)
        {
            TraceCommandKind kind = Classify(command);
            int offset = TraceEntry.NoValue;
            int requested = TraceEntry.NoValue;
            int p2 = TraceEntry.NoValue;

            if (kind == TraceCommandKind.Read && command.Length == ReadCommandLength)
            {
                offset = (command[2] << 8) | command[3];
                requested = LengthOf(command[ReadLeIndex]);
            }
            else if (kind == TraceCommandKind.GetResponse && command.Length == ShortCommandLength)
            {
                requested = LengthOf(command[ShortLeIndex]);
                p2 = getResponseP2;
            }

            _trace.Add(new TraceEntry(
                kind,
                offset,
                requested,
                receivedLength ?? TraceEntry.NoValue,
                statusWord,
                round,
                p2,
                flags,
                verdict,
                failure));
        }
    }
}
