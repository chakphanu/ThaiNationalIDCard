using System;
using System.Collections.Generic;
using ThaiNationalIDCard.Card;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// The only way to send a command. Every test and production path goes through this class, over PC/SC or the
    /// simulator alike. It tracks which applet is selected and whether a GET RESPONSE is due, because READ ranges
    /// belong to one applet: 015D is an NHSO field but inside an off limits range of MOI.
    /// </summary>
    internal sealed class GuardedCardConnection : IDisposable
    {
        private const byte Sw1Success = 0x90;
        private const byte Sw2Success = 0x00;
        private const byte Sw1ResponsePending = 0x61;
        private const byte Sw1WrongLength = 0x6C;

        private readonly IRawCardChannel _channel;
        private readonly CommandPolicy _policy;

        private CardProfile? _selected;
        private bool _getResponsePending;
        private bool _disposed;

        public GuardedCardConnection(IRawCardChannel channel, CommandPolicy policy)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public IReadOnlyList<string> ListReaders()
        {
            ThrowIfDisposed();
            return _channel.ListReaders();
        }

        /// <summary>Current state of every known reader.</summary>
        public IReadOnlyList<ReaderStatus> GetReaderStatuses()
        {
            ThrowIfDisposed();
            return _channel.GetReaderStatuses();
        }

        /// <summary>Chooses a reader from a status snapshot. See <see cref="ReaderSelector"/> for the rules.</summary>
        public string SelectReader(string? preferredReader)
        {
            ThrowIfDisposed();
            return ReaderSelector.Select(_channel.GetReaderStatuses(), preferredReader);
        }

        public CardConnectionInfo Connect(string readerName)
        {
            ThrowIfDisposed();
            ForgetCardState();
            return _channel.Connect(readerName);
        }

        public void Disconnect()
        {
            ThrowIfDisposed();
            ForgetCardState();
            _channel.Disconnect();
        }

        /// <summary>Begins a transaction. A shared connection may have changed applet before it, so SELECT comes next.</summary>
        public void BeginTransaction()
        {
            ThrowIfDisposed();
            ForgetCardState();
            _channel.BeginTransaction();
        }

        /// <summary>Ends a transaction. Another application may select right after it.</summary>
        public void EndTransaction()
        {
            ThrowIfDisposed();
            ForgetCardState();
            _channel.EndTransaction();
        }

        /// <summary>
        /// Checks a command and sends it. Throws <see cref="CommandRejectedException"/> without touching the
        /// channel when the policy refuses it. Returns the raw response bytes.
        /// </summary>
        public byte[] Transmit(byte[] command)
        {
            ThrowIfDisposed();

            CommandVerdict verdict = _policy.Evaluate(command, _selected, _getResponsePending);
            if (verdict != CommandVerdict.Allowed)
            {
                throw new CommandRejectedException(verdict);
            }

            byte[] response;
            try
            {
                response = _channel.Transmit(command);
            }
            catch (ThaiIdCardException)
            {
                // A reset or a removed card loses the selection, and after any failure it cannot be assumed.
                ForgetCardState();
                throw;
            }

            Remember(command, response);
            return response;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ForgetCardState();
            _channel.Dispose();
        }

        private void Remember(byte[] command, byte[] response)
        {
            if (response.Length < 2)
            {
                ForgetCardState();
                return;
            }

            byte sw1 = response[response.Length - 2];
            byte sw2 = response[response.Length - 1];
            CardProfile? selectTarget = _policy.FindSelectTarget(command);

            if (selectTarget != null)
            {
                // A failed SELECT forgets on purpose: nothing below assumes the previous applet is still selected.
                _selected = (sw1 == Sw1Success && sw2 == Sw2Success) || sw1 == Sw1ResponsePending ? selectTarget : null;
            }

            bool isGetResponse = command[0] == 0x00 && command[1] == 0xC0;
            _getResponsePending = sw1 == Sw1ResponsePending || (isGetResponse && sw1 == Sw1WrongLength);
        }

        private void ForgetCardState()
        {
            _selected = null;
            _getResponsePending = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GuardedCardConnection));
            }
        }
    }
}
