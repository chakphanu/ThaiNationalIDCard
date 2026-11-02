using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class CommandPolicyTests
    {
        private static readonly CommandPolicy Pub = CommandPolicy.Public;
        private static readonly CommandPolicy Priv = CommandPolicy.PublicAndPrivate;

        private const string BioAid = "a000000084060000";
        private const string OldMoiAid = "a000000054480011";

        private static CommandVerdict Read(CommandPolicy policy, CardProfile? selected, int offset, int le) =>
            policy.Evaluate(ApduText.Read(offset, le), selected, false);

        // ---- SELECT ----

        [Fact]
        public void Public_selects_only_the_moi_applet()
        {
            Assert.Equal(CommandVerdict.Allowed, Pub.Evaluate(ApduText.Select(SimulatedCard.MoiAidHex), null, false));
            foreach (string aid in new[] { SimulatedCard.NhsoAidHex, SimulatedCard.AdmAidHex, BioAid, OldMoiAid })
            {
                Assert.Equal(CommandVerdict.AppletNotAllowed, Pub.Evaluate(ApduText.Select(aid), null, false));
            }
        }

        [Fact]
        public void Private_adds_adm_and_nhso_but_never_the_bio_applet()
        {
            foreach (string aid in new[] { SimulatedCard.MoiAidHex, SimulatedCard.NhsoAidHex, SimulatedCard.AdmAidHex })
            {
                Assert.Equal(CommandVerdict.Allowed, Priv.Evaluate(ApduText.Select(aid), null, false));
            }

            Assert.Equal(CommandVerdict.AppletNotAllowed, Priv.Evaluate(ApduText.Select(BioAid), null, false));
            Assert.Equal(CommandVerdict.AppletNotAllowed, Priv.Evaluate(ApduText.Select(OldMoiAid), null, false));
        }

        [Fact]
        public void Chip_data_sequence_stays_rejected_until_step_9_rechecks_its_select()
        {
            // TODO(step 9): the plan requires re-checking the chip data SELECT against source before adding it.
            byte[] selectWithoutAid = { 0x00, 0xA4, 0x04, 0x00 };
            byte[] selectEmptyAid = { 0x00, 0xA4, 0x04, 0x00, 0x00 };
            byte[] getData = { 0x80, 0xCA, 0x9F, 0x7F, 0x2D };

            Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(selectWithoutAid, null, false));
            Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(selectEmptyAid, null, false));
            Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(getData, MoiProfile.PublicAndPrivate, false));
        }

        [Theory]
        [InlineData("00a4040008a00000005448000100")] // trailing Le
        [InlineData("00a4040009a000000054480001")]   // Lc longer than the data
        [InlineData("00a4040007a000000054480001")]   // Lc shorter than the data
        [InlineData("00a4040108a000000054480001")]   // other P2
        [InlineData("00a4000008a000000054480001")]   // other P1
        [InlineData("80a4040008a000000054480001")]   // other CLA
        public void Select_in_any_other_shape_is_rejected(string hex)
        {
            Assert.NotEqual(CommandVerdict.Allowed, Pub.Evaluate(ApduText.Bytes(hex), null, false));
        }

        // ---- GET RESPONSE ----

        [Theory]
        [InlineData("00c0000000", true)]
        [InlineData("00c00000ff", true)]
        [InlineData("00c0000110", true)]
        [InlineData("00c0000210", false)]
        [InlineData("00c0010010", false)]
        [InlineData("80c0000010", false)]
        [InlineData("00c00000", false)]
        [InlineData("00c000001000", false)]
        public void Get_response_shape(string hex, bool allowed)
        {
            CommandVerdict verdict = Pub.Evaluate(ApduText.Bytes(hex), MoiProfile.Public, true);

            Assert.Equal(allowed, verdict == CommandVerdict.Allowed);
        }

        [Fact]
        public void Get_response_without_a_pending_response_is_rejected()
        {
            Assert.Equal(CommandVerdict.NoPendingResponse, Pub.Evaluate(ApduText.GetResponse(0, 13), MoiProfile.Public, false));
        }

        // ---- commands that are always forbidden ----

        public static IEnumerable<object[]> ForbiddenCommands() => new[]
        {
            "802001002000000000", // PIN verify
            "80b400000000",       // PIN path in ADM
            "b03400760000",       // fingerprint
            "00200000",           // ISO VERIFY
            "00d6000004aabbccdd", // UPDATE BINARY
            "00d0000004aabbccdd", // WRITE BINARY
            "00dc000004aabbccdd", // UPDATE RECORD
            "00d2000004aabbccdd", // WRITE RECORD
            "00e2000004aabbccdd", // APPEND RECORD
            "00da000004aabbccdd", // PUT DATA
            "00e0000004aabbccdd", // CREATE FILE
            "00e4000000",         // DELETE FILE
            "000e000000",         // ERASE BINARY
            "80d6000004aabbccdd", // write family in class 80
        }.Select(hex => new object[] { hex });

        [Theory]
        [MemberData(nameof(ForbiddenCommands))]
        public void Forbidden_family_is_rejected_by_every_policy_in_every_state(string hex)
        {
            byte[] command = ApduText.Bytes(hex);
            foreach (CommandPolicy policy in new[] { Pub, Priv })
            {
                foreach (CardProfile? selected in new[] { null, MoiProfile.PublicAndPrivate, AdmProfile.Instance, NhsoProfile.Instance })
                {
                    foreach (bool pending in new[] { false, true })
                    {
                        Assert.Equal(CommandVerdict.AlwaysForbidden, policy.Evaluate(command, selected, pending));
                    }
                }
            }
        }

        [Fact]
        public void Every_class_and_instruction_outside_the_allowlist_is_rejected()
        {
            byte[][] shapes = { new byte[] { 0, 0, 0, 0, 7 }, new byte[] { 0, 0, 0, 0, 2, 0, 13 } };

            for (int cla = 0; cla <= 0xFF; cla++)
            {
                for (int ins = 0; ins <= 0xFF; ins++)
                {
                    if ((cla, ins) is (0x00, 0xC0) or (0x80, 0xB0) or (0x80, 0x00))
                    {
                        continue;
                    }

                    foreach (byte[] shape in shapes)
                    {
                        var command = (byte[])shape.Clone();
                        command[0] = (byte)cla;
                        command[1] = (byte)ins;
                        Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(command, MoiProfile.PublicAndPrivate, true));
                        Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(command, AdmProfile.Instance, true));
                    }
                }
            }
        }

        // ---- READ shape and ranges ----

        [Theory]
        [InlineData("80b0000402010d")]   // second length byte is not 00
        [InlineData("80b0000403000d")]   // P3 is not 02
        [InlineData("80b0000402000d00")] // trailing byte
        [InlineData("80b00004020d")]     // too short
        public void Read_in_any_other_shape_is_rejected(string hex)
        {
            Assert.NotEqual(CommandVerdict.Allowed, Pub.Evaluate(ApduText.Bytes(hex), MoiProfile.Public, false));
        }

        [Fact]
        public void Read_without_a_selected_applet_is_rejected()
        {
            Assert.Equal(CommandVerdict.NoAppletSelected, Read(Pub, null, SyntheticImage.CitizenId, 13));
        }

        [Theory]
        [InlineData(0x0004, 0x0D)]
        [InlineData(0x0011, 0xD1)]
        [InlineData(0x00F6, 0x64)]
        [InlineData(0x0167, 0x10)]
        [InlineData(0x1579, 0xA0)]
        [InlineData(0x1567, 0x12)]
        [InlineData(0x017B, 0xFF)]
        public void Public_allows_each_field_up_to_its_last_byte_and_not_one_more(int offset, int le)
        {
            Assert.Equal(CommandVerdict.Allowed, Read(Pub, MoiProfile.Public, offset, le));

            // 15 67 is the last chunk inside the photo range, so only its upper end is an edge.
            if (offset != 0x1567)
            {
                Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Pub, MoiProfile.Public, offset - 1, le));
            }

            // The photo range goes on after a full FF chunk, so FF has no upper edge here.
            if (le < 0xFF)
            {
                Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Pub, MoiProfile.Public, offset, le + 1));
            }
        }

        [Fact]
        public void Last_photo_chunk_cannot_run_into_the_address()
        {
            Assert.Equal(CommandVerdict.Allowed, Read(Pub, MoiProfile.Public, 0x1567, 0x12));
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Pub, MoiProfile.Public, 0x1567, 0x13));
        }

        [Fact]
        public void A_read_across_two_neighbouring_declared_ranges_is_rejected()
        {
            // CID and person info touch. Containment is per range, so neighbours cannot add up.
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Pub, MoiProfile.Public, 0x0004, 0x0E));
            // In private mode 0167-0176 and 0177-0178 touch: the 18 byte 2.x read still fails.
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Priv, MoiProfile.PublicAndPrivate, 0x0167, 0x12));
        }

        [Fact]
        public void Le_00_means_256_bytes()
        {
            Assert.Equal(CommandVerdict.Allowed, Read(Pub, MoiProfile.Public, SyntheticImage.Photo, 0));
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Pub, MoiProfile.Public, SyntheticImage.CitizenId, 0));
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Pub, MoiProfile.Public, 0x1500, 0)); // 256 from 1500 ends past photo
        }

        [Fact]
        public void Exhaustive_every_offset_and_le_is_allowed_only_inside_one_public_range()
        {
            // Table written independently of MoiProfile.
            (int Start, int End)[] table =
            {
                (0x0004, 0x0010), (0x0011, 0x00E1), (0x00F6, 0x0159), (0x0167, 0x0176), (0x017B, 0x1578), (0x1579, 0x1618),
            };

            for (int offset = 0; offset < SimulatedCard.ImageSize + 8; offset++)
            {
                for (int le = 0; le <= 0xFF; le++)
                {
                    int length = le == 0 ? 256 : le;
                    bool inTable = table.Any(r => offset >= r.Start && offset + length - 1 <= r.End);
                    Assert.Equal(inTable, Read(Pub, MoiProfile.Public, offset, le) == CommandVerdict.Allowed);
                }
            }
        }

        [Fact]
        public void Exhaustive_no_mode_reaches_the_off_limits_ranges_of_the_moi_applet()
        {
            (int Start, int End)[] offLimits = { (0x015A, 0x0166), (0x1627, 0x173D) };

            foreach ((CommandPolicy policy, CardProfile profile) in new[] { (Pub, MoiProfile.Public), (Priv, MoiProfile.PublicAndPrivate) })
            {
                for (int offset = 0; offset < SimulatedCard.ImageSize + 8; offset++)
                {
                    for (int le = 0; le <= 0xFF; le++)
                    {
                        int length = le == 0 ? 256 : le;
                        bool touches = offLimits.Any(r => offset <= r.End && offset + length - 1 >= r.Start);
                        if (touches)
                        {
                            Assert.NotEqual(CommandVerdict.Allowed, Read(policy, profile, offset, le));
                        }
                    }
                }
            }
        }

        [Fact]
        public void Public_never_reaches_0177_or_any_private_range_or_the_gap_before_the_photo()
        {
            (int Start, int End)[] privateRanges =
            {
                (0x0000, 0x0003), (0x00E2, 0x00F5), (0x0177, 0x0178), (0x0179, 0x017A), (0x1619, 0x1626),
            };

            for (int offset = 0; offset < SimulatedCard.ImageSize; offset++)
            {
                for (int le = 0; le <= 0xFF; le++)
                {
                    int length = le == 0 ? 256 : le;
                    if (privateRanges.Any(r => offset <= r.End && offset + length - 1 >= r.Start))
                    {
                        Assert.NotEqual(CommandVerdict.Allowed, Read(Pub, MoiProfile.Public, offset, le));
                    }
                }
            }
        }

        [Theory]
        [InlineData(0x0000, 4)]
        [InlineData(0x00E2, 20)]
        [InlineData(0x0177, 2)]
        [InlineData(0x1619, 14)]
        public void Private_adds_the_raw_ranges(int offset, int le)
        {
            Assert.Equal(CommandVerdict.Allowed, Read(Priv, MoiProfile.PublicAndPrivate, offset, le));
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Priv, MoiProfile.PublicAndPrivate, offset, le + 1));
        }

        // ---- ranges belong to the selected applet ----

        [Fact]
        public void Nhso_offsets_do_not_open_the_same_numbers_in_the_moi_applet()
        {
            // 015D is inside the NHSO table, but in the MOI applet it is inside the off limits 015A-0166.
            Assert.Equal(CommandVerdict.Allowed, Read(Priv, NhsoProfile.Instance, 0x015D, 0x01));
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Priv, MoiProfile.PublicAndPrivate, 0x015D, 0x01));
            Assert.Equal(CommandVerdict.Allowed, Read(Priv, NhsoProfile.Instance, 0x0004, 0x3C));
            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Priv, MoiProfile.PublicAndPrivate, 0x0004, 0x3C));
        }

        [Fact]
        public void Adm_has_no_read_range_and_its_data_command_needs_adm_selected()
        {
            byte[] admCommand = { 0x80, 0x00, 0x00, 0x00, 0x07 };

            Assert.Equal(CommandVerdict.OutsideDeclaredRange, Read(Priv, AdmProfile.Instance, 0x0004, 13));
            Assert.Equal(CommandVerdict.Allowed, Priv.Evaluate(admCommand, AdmProfile.Instance, false));
            Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(admCommand, MoiProfile.PublicAndPrivate, false));
            Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(admCommand, null, false));
            Assert.NotEqual(CommandVerdict.Allowed, Priv.Evaluate(new byte[] { 0x80, 0x00, 0x00, 0x00, 0x08 }, AdmProfile.Instance, false));
            Assert.NotEqual(CommandVerdict.Allowed, Pub.Evaluate(admCommand, AdmProfile.Instance, false));
        }

        [Fact]
        public void Public_policy_ignores_a_selected_profile_it_does_not_hold()
        {
            Assert.NotEqual(CommandVerdict.Allowed, Read(Pub, NhsoProfile.Instance, 0x0004, 0x3C));
            Assert.NotEqual(CommandVerdict.Allowed, Read(Pub, MoiProfile.PublicAndPrivate, 0x0177, 2));
        }

        [Fact]
        public void A_profile_that_overlaps_a_never_read_range_cannot_be_built()
        {
            Assert.Throws<ArgumentException>(() => new CardProfile(
                new byte[] { 1 }, new[] { new FieldRange(0x0150, 0x20) }, new[] { MoiProfile.Never015A }));
        }

        [Fact]
        public void Too_short_commands_are_malformed()
        {
            Assert.Equal(CommandVerdict.Malformed, Pub.Evaluate(new byte[] { 0x80, 0xB0, 0x00 }, MoiProfile.Public, false));
            Assert.Equal(CommandVerdict.Malformed, Pub.Evaluate(Array.Empty<byte>(), MoiProfile.Public, false));
        }
    }
}
