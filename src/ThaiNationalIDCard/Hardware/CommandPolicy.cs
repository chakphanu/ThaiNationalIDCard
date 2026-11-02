using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Card;

namespace ThaiNationalIDCard.Hardware
{
    /// <summary>
    /// Allowlist of what may reach the card. Everything not listed here is refused, and there is no way to add
    /// a raw command. The policy holds no state: <see cref="GuardedCardConnection"/> tracks the selected applet
    /// and the pending response and passes them in, so READ ranges are always those of the selected applet.
    /// </summary>
    internal sealed class CommandPolicy
    {
        private const byte ClaIso = 0x00;
        private const byte ClaProprietary = 0x80;
        private const byte InsSelect = 0xA4;
        private const byte InsGetResponse = 0xC0;
        private const byte InsRead = 0xB0;
        private const byte InsAppletData = 0x00;
        private const int MinimumCommandLength = 4;
        private const int ReadCommandLength = 7;
        private const int ShortCommandLength = 5;
        private const int SelectHeaderLength = 5;

        // The 80 00 00 00 command is only known with this length from public code.
        private const byte AppletDataLe = 0x07;

        // ISO write, life cycle and reference data instructions. They are refused in every class because the
        // allowlist below never needs them. 20, 21, 24 and 2C touch PIN retry counters.
        private static readonly byte[] AlwaysForbiddenInstructions =
        {
            0x04, 0x0E, 0x0F, 0x20, 0x21, 0x24, 0x2C, 0x44, 0xD0, 0xD1, 0xD2, 0xD6, 0xD7, 0xDA, 0xDB, 0xDC, 0xDD,
            0xE0, 0xE2, 0xE4, 0xE6, 0xE8, 0xFE,
        };

        private readonly CardProfile[] _profiles;

        private CommandPolicy(params CardProfile[] profiles)
        {
            _profiles = profiles;
        }

        /// <summary>SELECT of MOI, GET RESPONSE, READ inside the public ranges of the profile.</summary>
        public static CommandPolicy Public { get; } = new CommandPolicy(MoiProfile.Public);

        /// <summary>
        /// Adds SELECT of ADM and NHSO, the private MOI ranges, the NHSO ranges and 80 00 00 00 in ADM.
        /// TODO(step 9): chip data (SELECT without AID, then 80 CA 9F 7F) is left out on purpose. The plan requires
        /// re-checking that SELECT against source first. Until then this policy refuses both commands.
        /// </summary>
        public static CommandPolicy PublicAndPrivate { get; } =
            new CommandPolicy(MoiProfile.PublicAndPrivate, NhsoProfile.Instance, AdmProfile.Instance);

        /// <summary>
        /// Checks one command. <paramref name="selected"/> is the applet the last successful SELECT chose, and
        /// <paramref name="getResponsePending"/> tells whether the last answer offered a GET RESPONSE.
        /// </summary>
        public CommandVerdict Evaluate(byte[] command, CardProfile? selected, bool getResponsePending)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (command.Length < MinimumCommandLength)
            {
                return CommandVerdict.Malformed;
            }

            byte cla = command[0];
            byte ins = command[1];

            if (IsAlwaysForbidden(cla, ins))
            {
                return CommandVerdict.AlwaysForbidden;
            }

            if (cla == ClaIso && ins == InsSelect)
            {
                return EvaluateSelect(command);
            }

            if (cla == ClaIso && ins == InsGetResponse)
            {
                return EvaluateGetResponse(command, getResponsePending);
            }

            if (cla == ClaProprietary && ins == InsRead)
            {
                return EvaluateRead(command, selected);
            }

            if (cla == ClaProprietary && ins == InsAppletData)
            {
                return EvaluateAppletData(command, selected);
            }

            return CommandVerdict.NotAllowlisted;
        }

        /// <summary>The profile a SELECT command would choose, or null when it is not an allowed SELECT.</summary>
        public CardProfile? FindSelectTarget(byte[] command)
        {
            if (command == null || command.Length < SelectHeaderLength || command[0] != ClaIso || command[1] != InsSelect)
            {
                return null;
            }

            return _profiles.FirstOrDefault(p => p.MatchesAid(command, SelectHeaderLength, command[4]));
        }

        private static bool IsAlwaysForbidden(byte cla, byte ins)
        {
            bool pinOrFingerprint =
                (cla == ClaProprietary && (ins == 0x20 || ins == 0xB4)) ||
                (cla == 0xB0 && ins == 0x34);

            return pinOrFingerprint || Array.IndexOf(AlwaysForbiddenInstructions, ins) >= 0;
        }

        private CommandVerdict EvaluateSelect(byte[] command)
        {
            if (command.Length < SelectHeaderLength || command[2] != 0x04 || command[3] != 0x00)
            {
                return CommandVerdict.NotAllowlisted;
            }

            // Exact length: Lc must match the data, with no trailing Le.
            if (command.Length != SelectHeaderLength + command[4])
            {
                return CommandVerdict.Malformed;
            }

            return FindSelectTarget(command) != null ? CommandVerdict.Allowed : CommandVerdict.AppletNotAllowed;
        }

        private static CommandVerdict EvaluateGetResponse(byte[] command, bool getResponsePending)
        {
            if (command.Length != ShortCommandLength || command[2] != 0x00 || command[3] > 0x01)
            {
                return CommandVerdict.NotAllowlisted;
            }

            return getResponsePending ? CommandVerdict.Allowed : CommandVerdict.NoPendingResponse;
        }

        private CommandVerdict EvaluateRead(byte[] command, CardProfile? selected)
        {
            // READ carries its length in the data field: 80 B0 P1 P2 02 00 Le.
            if (command.Length != ReadCommandLength || command[4] != 0x02 || command[5] != 0x00)
            {
                return CommandVerdict.NotAllowlisted;
            }

            if (selected == null)
            {
                return CommandVerdict.NoAppletSelected;
            }

            int offset = (command[2] << 8) | command[3];
            int length = command[6] == 0 ? 256 : command[6];

            return HoldsProfile(selected) && selected.AllowsRead(offset, length)
                ? CommandVerdict.Allowed
                : CommandVerdict.OutsideDeclaredRange;
        }

        private CommandVerdict EvaluateAppletData(byte[] command, CardProfile? selected)
        {
            bool exactShape = command.Length == ShortCommandLength && command[2] == 0x00 && command[3] == 0x00 &&
                command[4] == AppletDataLe;

            if (!exactShape || selected == null || !HoldsProfile(selected) || !selected.AllowsAppletDataCommand)
            {
                return CommandVerdict.NotAllowlisted;
            }

            return CommandVerdict.Allowed;
        }

        private bool HoldsProfile(CardProfile profile) => ((IList<CardProfile>)_profiles).Contains(profile);
    }
}
