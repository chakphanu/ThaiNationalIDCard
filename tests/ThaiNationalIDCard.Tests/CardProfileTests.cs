using System;
using System.Linq;
using System.Reflection;
using ThaiNationalIDCard.Card;
using ThaiNationalIDCard.Hardware;
using ThaiNationalIDCard.Workflow;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class CardProfileTests
    {
        private static readonly byte[] SomeAid = { 0xA0, 0x00, 0x00, 0x00, 0x54, 0x48, 0x00, 0x01 };

        [Fact]
        public void Profiles_hold_p2_candidates_00_then_01()
        {
            foreach (CardProfile profile in new[] { MoiProfile.Public, MoiProfile.PublicAndPrivate, NhsoProfile.Instance, AdmProfile.Instance })
            {
                Assert.Equal(new byte[] { 0x00, 0x01 }, profile.P2Candidates);
            }
        }

        [Fact]
        public void Public_field_table_is_the_2x_order_and_matches_the_separate_image_layout()
        {
            Assert.Equal(
                new[]
                {
                    (CardFieldId.CitizenId, SyntheticImage.CitizenId, 13),
                    (CardFieldId.PersonInfo, SyntheticImage.PersonInfo, 209),
                    (CardFieldId.Address, SyntheticImage.Address, SyntheticImage.AddressLength),
                    (CardFieldId.IssueExpire, SyntheticImage.IssueExpire, 16),
                    (CardFieldId.Issuer, SyntheticImage.Issuer, 100),
                    (CardFieldId.Photo, SyntheticImage.Photo, SyntheticImage.PhotoLength),
                },
                MoiProfile.Public.Fields.Select(f => (f.Id, f.Range.Offset, f.Range.Length)));
            Assert.All(MoiProfile.Public.Fields, f => Assert.False(f.IsPrivate));
        }

        [Fact]
        public void Private_table_is_the_public_table_plus_raw_ranges_named_by_position()
        {
            Assert.Equal(MoiProfile.Public.Fields, MoiProfile.PublicAndPrivate.Fields.Take(MoiProfile.Public.Fields.Count));
            Assert.Equal(
                new[] { CardFieldId.MoiRaw0000, CardFieldId.MoiRaw00E2, CardFieldId.MoiRaw0177, CardFieldId.MoiRaw1619 },
                MoiProfile.PublicAndPrivate.Fields.Where(f => f.IsPrivate).Select(f => f.Id));
            Assert.All(Enum.GetNames<CardFieldId>(), name => Assert.DoesNotContain("Religion", name, StringComparison.OrdinalIgnoreCase));
            Assert.All(Enum.GetNames<CardFieldId>(), name => Assert.DoesNotContain("Laser", name, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void No_field_of_any_profile_touches_a_never_read_range()
        {
            foreach (CardField field in MoiProfile.PublicAndPrivate.Fields)
            {
                Assert.DoesNotContain(MoiProfile.PublicAndPrivate.NeverRead, field.Range.Overlaps);
            }
        }

        [Fact]
        public void Nhso_has_nine_private_fields_and_adm_one_data_command()
        {
            Assert.Equal(9, NhsoProfile.Instance.Fields.Count);
            Assert.All(NhsoProfile.Instance.Fields, f => Assert.True(f.IsPrivate));
            CardField adm = Assert.Single(AdmProfile.Instance.Fields);
            Assert.Equal(FieldAccess.AppletData, adm.Access);
            Assert.Empty(AdmProfile.Instance.Readable);
        }

        [Fact]
        public void A_table_line_outside_the_declared_ranges_fails_at_construction()
        {
            var range = new FieldRange(0x10, 4);
            var outside = new CardField(CardFieldId.CitizenId, new FieldRange(0x10, 5));

            Assert.Throws<ArgumentException>(() => new CardProfile(SomeAid, new[] { range }, fields: new[] { outside }));
            Assert.Throws<ArgumentException>(() => new CardProfile(SomeAid, new[] { range }, p2Candidates: Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => new CardProfile(SomeAid, new[] { range },
                fields: new[] { new CardField(CardFieldId.AdmAppletData, default, access: FieldAccess.AppletData) }));
        }

        [Fact]
        public void Profile_exposes_no_command_bytes()
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            Assert.DoesNotContain(typeof(CardField).GetProperties(All), p => p.PropertyType == typeof(byte[]));
            Assert.DoesNotContain(typeof(CardProfile).GetProperties(All), p => p.PropertyType == typeof(byte[]));
            Assert.DoesNotContain(typeof(CardField).GetFields(All), f => f.FieldType == typeof(byte[]));
        }

        // ---- commands ----

        [Fact]
        public void Commands_are_built_from_profile_data()
        {
            Assert.Equal(ApduText.Select(SimulatedCard.MoiAidHex), MoiCardCommands.Select(MoiProfile.Public));
            Assert.Equal(ApduText.Select(SimulatedCard.NhsoAidHex), MoiCardCommands.Select(NhsoProfile.Instance));
            Assert.Equal(ApduText.Select(SimulatedCard.AdmAidHex), MoiCardCommands.Select(AdmProfile.Instance));
            Assert.Equal(ApduText.Read(0x0004, 0x0D), MoiCardCommands.Read(MoiProfile.CitizenId));
            Assert.Equal("8000000007", ApduText.Hex(MoiCardCommands.AppletData()));
        }

        [Fact]
        public void Read_of_256_uses_le_00_and_other_lengths_are_refused()
        {
            Assert.Equal(0x00, MoiCardCommands.Read(new FieldRange(0x0100, 256))[6]);
            Assert.Throws<ArgumentOutOfRangeException>(() => MoiCardCommands.Read(new FieldRange(0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoiCardCommands.Read(new FieldRange(0, 257)));
        }

        [Fact]
        public void Photo_chunks_are_twenty_of_255_then_the_last_18_bytes_at_1567()
        {
            FieldRange[] chunks = MoiCardCommands.Chunks(MoiProfile.Photo).ToArray();

            Assert.Equal(21, chunks.Length);
            Assert.All(chunks.Take(20), c => Assert.Equal(255, c.Length));
            Assert.Equal(0x1567, chunks[20].Offset);
            Assert.Equal(0x12, chunks[20].Length);
            Assert.Equal(MoiProfile.Photo.EndExclusive, chunks[20].EndExclusive);
        }

        [Fact]
        public void Every_command_of_every_table_passes_the_guard_of_its_mode_only()
        {
            foreach (CardField field in MoiProfile.Public.Fields)
            {
                foreach (FieldRange chunk in MoiCardCommands.Chunks(field.Range))
                {
                    Assert.Equal(CommandVerdict.Allowed, CommandPolicy.Public.Evaluate(MoiCardCommands.Read(chunk), MoiProfile.Public, false));
                }
            }

            foreach (CardField field in MoiProfile.PublicAndPrivate.Fields.Where(f => f.IsPrivate))
            {
                byte[] command = MoiCardCommands.Read(field.Range);
                Assert.Equal(CommandVerdict.Allowed, CommandPolicy.PublicAndPrivate.Evaluate(command, MoiProfile.PublicAndPrivate, false));
                Assert.Equal(CommandVerdict.OutsideDeclaredRange, CommandPolicy.Public.Evaluate(command, MoiProfile.Public, false));
            }

            foreach (CardField field in NhsoProfile.Instance.Fields)
            {
                Assert.Equal(CommandVerdict.Allowed, CommandPolicy.PublicAndPrivate.Evaluate(MoiCardCommands.Read(field.Range), NhsoProfile.Instance, false));
            }

            Assert.Equal(CommandVerdict.Allowed, CommandPolicy.PublicAndPrivate.Evaluate(MoiCardCommands.AppletData(), AdmProfile.Instance, false));
        }

        // ---- policy ----

        [Fact]
        public void Default_policy_uses_a_transaction_fallback_and_drain_with_short_delays()
        {
            ReadPolicy policy = ReadPolicy.Default;

            Assert.True(policy.UseTransaction);
            Assert.True(policy.AllowP2Fallback);
            Assert.True(policy.DrainSelectResponse);
            Assert.Null(policy.P2Order);
            Assert.NotEmpty(policy.ConnectRetryDelays);
            Assert.All(policy.ConnectRetryDelays, d => Assert.True(d < TimeSpan.FromMilliseconds(1500)));
        }

        [Theory]
        [InlineData("3b67", true, 0x01, 0x00)]
        [InlineData("3b6700000000", true, 0x01, 0x00)]
        [InlineData("3b79180000", false, 0x00, 0x01)]
        [InlineData("3b", false, 0x00, 0x01)]
        [InlineData("", false, 0x00, 0x01)]
        [InlineData("67", false, 0x00, 0x01)]
        [InlineData("00673b", false, 0x00, 0x01)]
        [InlineData("3b68", false, 0x00, 0x01)]
        [InlineData("3c67", false, 0x00, 0x01)]
        [InlineData("673b", false, 0x00, 0x01)]
        public void Atr_only_orders_p2_and_a_short_atr_is_the_normal_order(string atrHex, bool used, byte first, byte second)
        {
            Assert.Equal(new[] { first, second }, ReadPolicy.Default.OrderP2(MoiProfile.Public, ApduText.Bytes(atrHex), out bool ruleUsed));
            Assert.Equal(used, ruleUsed);
        }

        [Fact]
        public void A_null_atr_is_the_normal_order()
        {
            Assert.Equal(new byte[] { 0x00, 0x01 }, ReadPolicy.Default.OrderP2(MoiProfile.Public, null!, out bool ruleUsed));
            Assert.False(ruleUsed);
        }

        [Fact]
        public void A_fixed_p2_order_replaces_the_atr_rule()
        {
            var policy = new ReadPolicy(p2Order: new byte[] { 0x01 });

            Assert.Equal(new byte[] { 0x01 }, policy.OrderP2(MoiProfile.Public, ApduText.Bytes("3b79"), out bool used));
            Assert.False(used);
            Assert.Equal(new byte[] { 0x01 }, policy.OrderP2(MoiProfile.Public, ApduText.Bytes("3b67"), out used));
            Assert.False(used);
            Assert.Throws<ArgumentException>(() => new ReadPolicy(p2Order: Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => new ReadPolicy(p2Order: new byte[] { 0x00, 0x00 }));
        }
    }
}
