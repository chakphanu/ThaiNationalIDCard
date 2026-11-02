using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Card;

namespace ThaiNationalIDCard.Workflow
{
    /// <summary>
    /// How a read behaves where only a real card can say what is right. A later real-card finding should change
    /// this class or the profile and nothing else. No default here was checked on a real card.
    /// </summary>
    internal sealed class ReadPolicy
    {
        /// <summary>Defensive values, not card facts. They replace the fixed 1500 ms delay of 2.x.</summary>
        private static readonly TimeSpan[] DefaultRetryDelays =
        {
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900),
        };

        public ReadPolicy(bool useTransaction = true, IEnumerable<TimeSpan>? connectRetryDelays = null, bool allowP2Fallback = true,
            IEnumerable<byte>? p2Order = null, bool drainSelectResponse = true)
        {
            UseTransaction = useTransaction;
            ConnectRetryDelays = (connectRetryDelays ?? DefaultRetryDelays).ToArray();
            AllowP2Fallback = allowP2Fallback;
            P2Order = p2Order?.ToArray();
            DrainSelectResponse = drainSelectResponse;

            if (P2Order != null && (P2Order.Count == 0 || P2Order.Distinct().Count() != P2Order.Count))
            {
                throw new ArgumentException("A fixed P2 order needs distinct values.", nameof(p2Order));
            }
        }

        public static ReadPolicy Default { get; } = new ReadPolicy();

        /// <summary>Hold a transaction during the read. There is never a sleep inside it.</summary>
        public bool UseTransaction { get; }

        /// <summary>
        /// One delay per allowed retry, used outside the transaction. The count is the whole retry budget of a read,
        /// shared by connect, opening the transaction and restarts after ResetCard.
        /// </summary>
        public IReadOnlyList<TimeSpan> ConnectRetryDelays { get; }

        /// <summary>Whether the CID gate may spend its single try of the other P2.</summary>
        public bool AllowP2Fallback { get; }

        /// <summary>Fixed P2 order. Null means the profile candidates, reversed when the ATR starts with 3B 67.</summary>
        public IReadOnlyList<byte>? P2Order { get; }

        /// <summary>Whether a SELECT answering 61 xx gets a GET RESPONSE whose bytes are thrown away unread.</summary>
        public bool DrainSelectResponse { get; }

        /// <summary>The ATR can only change the order of the attempts. A short or empty ATR is the normal order.</summary>
        public IReadOnlyList<byte> OrderP2(CardProfile profile, byte[] atr, out bool atrRuleUsed)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            atrRuleUsed = false;
            if (P2Order != null)
            {
                return P2Order;
            }

            if (atr != null && atr.Length >= 2 && atr[0] == 0x3B && atr[1] == 0x67)
            {
                atrRuleUsed = true;
                return profile.P2Candidates.Reverse().ToArray();
            }

            return profile.P2Candidates;
        }
    }
}
