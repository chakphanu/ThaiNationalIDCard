using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ThaiNationalIDCard.Protocol;
using ThaiNationalIDCard.Workflow;

namespace ThaiNationalIDCard.Tests
{
    /// <summary>Records every wait and whether the card was inside a transaction at that moment.</summary>
    internal sealed class DelaySpy
    {
        private readonly SimulatedCard _card;

        public DelaySpy(SimulatedCard card)
        {
            _card = card;
        }

        public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

        public int InsideTransaction { get; private set; }

        public void Wait(TimeSpan delay, CancellationToken token)
        {
            Delays.Add(delay);
            _card.CallLog.Add("delay");
            if (_card.InTransaction)
            {
                InsideTransaction++;
            }
        }
    }

    internal static class Flow
    {
        public static readonly TimeSpan[] ThreeRetries =
        {
            TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(3),
        };

        public static CardReadResult Read(SimulatedCard card, ReadPolicy? policy = null, CardReadMode mode = CardReadMode.Public,
            DelaySpy? delay = null, string? reader = null, bool includePhoto = true, bool retainData = true)
        {
            DelaySpy spy = delay ?? new DelaySpy(card);
            return new CardReadWorkflow(card, policy, spy.Wait).Read(
                new CardReadRequest { Mode = mode, ReaderName = reader, IncludePhoto = includePhoto, RetainFieldData = retainData },
                Xunit.TestContext.Current.CancellationToken);
        }

        public static CardReadResult ReadWithToken(SimulatedCard card, CancellationToken token) =>
            new CardReadWorkflow(card, null, new DelaySpy(card).Wait).Read(new CardReadRequest(), token);

        /// <summary>A READ of the CID: 80 B0 00 04 02 00 0D.</summary>
        public static bool IsCidRead(byte[] command) => ApduText.Hex(command) == "80b0000402000d";

        public static bool IsSelect(byte[] command) => command[0] == 0x00 && command[1] == 0xA4;

        public static bool IsGetResponse(byte[] command) => command[0] == 0x00 && command[1] == 0xC0;

        /// <summary>P2 of every GET RESPONSE that reached the card, in order.</summary>
        public static List<byte> GetResponseP2s(SimulatedCard card) =>
            card.Log.Where(e => IsGetResponse(e.Command)).Select(e => e.Command[3]).ToList();

        public static int P2Switches(SimulatedCard card)
        {
            List<byte> p2s = GetResponseP2s(card);
            return Enumerable.Range(1, Math.Max(0, p2s.Count - 1)).Count(i => p2s[i] != p2s[i - 1]);
        }

        public static int Events(IEnumerable<TraceEntry> trace, TraceEvent which) => trace.Count(t => t.Event == which);

        /// <summary>A CID answer for the given text, direct with 90 00.</summary>
        public static byte[] DirectText(string text) =>
            System.Text.Encoding.ASCII.GetBytes(text).Concat(new byte[] { 0x90, 0x00 }).ToArray();
    }
}
