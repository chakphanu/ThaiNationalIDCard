using System;
using System.Linq;
using ThaiNationalIDCard.Parsing;
using Xunit;

namespace ThaiNationalIDCard.Tests
{
    public class JpegScannerTests
    {
        private static void AssertFound(byte[] data, int expectedLength)
        {
            JpegScanResult result = JpegScanner.Scan(data);

            Assert.Equal(JpegScanStatus.Found, result.Status);
            Assert.Equal(expectedLength, result.Length);
        }

        [Fact]
        public void Minimal_structure_is_found_through_its_end_marker()
        {
            byte[] jpeg = MiniJpeg.Build(0x11, 0x22, 0x33);

            AssertFound(jpeg, jpeg.Length);
        }

        [Fact]
        public void Padding_after_the_end_marker_is_outside_the_length()
        {
            byte[] jpeg = MiniJpeg.Build(0x11);
            byte[] padded = jpeg.Concat(Enumerable.Repeat((byte)0x20, 140)).ToArray();

            AssertFound(padded, jpeg.Length);
        }

        [Fact]
        public void Bytes_0x20_inside_the_image_are_image_data_right_up_to_the_end_marker()
        {
            byte[] jpeg = MiniJpeg.Build(0x20, 0x20, 0x11, 0x20, 0x20, 0x20);

            AssertFound(jpeg, jpeg.Length);
        }

        [Fact]
        public void Stuffed_ff_00_followed_by_d9_is_not_an_end_marker()
        {
            byte[] jpeg = MiniJpeg.Build(0x11, 0xFF, 0x00, 0xD9, 0x22, 0xFF, 0x00, 0xFF, 0x00, 0x33);

            AssertFound(jpeg, jpeg.Length);
        }

        [Theory]
        [InlineData(0xD0)]
        [InlineData(0xD3)]
        [InlineData(0xD7)]
        public void Restart_markers_stay_inside_the_entropy_data(byte restart)
        {
            byte[] jpeg = MiniJpeg.Build(0x11, 0xFF, restart, 0xD9, 0x22);

            AssertFound(jpeg, jpeg.Length);
        }

        [Fact]
        public void An_end_marker_of_a_thumbnail_inside_an_app_segment_does_not_end_the_image()
        {
            byte[] thumbnail = MiniJpeg.Build(0x44, 0x55);
            byte[] jpeg = MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Segment(0xE1, thumbnail), MiniJpeg.Sos, new byte[] { 0x66 }, MiniJpeg.Eoi);

            AssertFound(jpeg, jpeg.Length);
        }

        [Theory]
        [InlineData(0xFE)] // COM
        [InlineData(0xDB)] // DQT
        [InlineData(0xC4)] // DHT
        [InlineData(0xC0)] // SOF0
        public void End_marker_bytes_inside_a_segment_payload_are_skipped_by_its_length(byte marker)
        {
            byte[] jpeg = MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Segment(marker, 0xFF, 0xD9, 0x00), MiniJpeg.Sos, new byte[] { 0x66 }, MiniJpeg.Eoi);

            AssertFound(jpeg, jpeg.Length);
        }

        [Fact]
        public void The_first_real_end_marker_wins_and_stale_ones_after_it_are_ignored()
        {
            byte[] jpeg = MiniJpeg.Build(0x11);
            byte[] data = MiniJpeg.Join(jpeg, new byte[] { 0x20, 0xFF, 0xD9, 0x20, 0xFF, 0xD9 });

            AssertFound(data, jpeg.Length);
        }

        [Fact]
        public void Fill_bytes_before_a_marker_are_allowed()
        {
            byte[] jpeg = MiniJpeg.Join(MiniJpeg.Soi, new byte[] { 0xFF, 0xFF }, MiniJpeg.Sos, new byte[] { 0x11, 0xFF, 0xFF, 0xFF, 0xD9 });

            AssertFound(jpeg, jpeg.Length);
        }

        [Fact]
        public void Several_scans_with_a_table_between_them_are_walked_to_the_last_end_marker()
        {
            byte[] jpeg = MiniJpeg.Join(
                MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0x11, 0xFF, 0x00 }, MiniJpeg.Segment(0xC4, 0xFF, 0xD9),
                MiniJpeg.Sos, new byte[] { 0x22 }, MiniJpeg.Eoi);

            AssertFound(jpeg, jpeg.Length);
        }

        public static TheoryData<string, byte[]> NoStartCases => new TheoryData<string, byte[]>
        {
            { "empty", Array.Empty<byte>() },
            { "one byte", new byte[] { 0xFF } },
            { "only an end marker", new byte[] { 0xFF, 0xD9 } },
            { "padding before the start", MiniJpeg.Join(new byte[] { 0x20 }, MiniJpeg.Build(0x11)) },
            { "second byte wrong", new byte[] { 0xFF, 0xD9, 0xFF, 0xDA } },
        };

        [Theory]
        [MemberData(nameof(NoStartCases))]
        public void No_start_marker_at_offset_0_is_no_start(string name, byte[] data)
        {
            Assert.False(string.IsNullOrEmpty(name));
            Assert.Equal(JpegScanStatus.NoStart, JpegScanner.Scan(data).Status);
        }

        public static TheoryData<string, byte[]> NoEndCases => new TheoryData<string, byte[]>
        {
            { "start only", MiniJpeg.Soi },
            { "no end marker", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0x11, 0x22 }) },
            { "only stuffed d9", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0xFF, 0x00, 0xD9 }) },
            { "only restart then d9", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0xFF, 0xD0, 0xD9 }) },
            { "ff at the very end", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0x11, 0xFF }) },
            { "end marker before any scan", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Eoi) },
            { "end marker in a segment only", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Segment(0xFE, 0xFF, 0xD9)) },
            { "segment longer than the data", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x7F, 0xFF, 0x00 } },
            { "segment length below 2", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9 } },
            { "length cut off", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 } },
            { "scan header cut off", new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 0x01 } },
            { "data instead of a marker after the start", new byte[] { 0xFF, 0xD8, 0x20, 0x20, 0xFF, 0xD9 } },
            { "second start marker", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Soi, MiniJpeg.Sos, MiniJpeg.Eoi) },
            { "reserved marker inside entropy data", MiniJpeg.Join(MiniJpeg.Soi, MiniJpeg.Sos, new byte[] { 0x11, 0xFF, 0x1E, 0x00, 0x02 }, MiniJpeg.Eoi) },
            { "ff 00 where a marker must be", MiniJpeg.Join(MiniJpeg.Soi, new byte[] { 0xFF, 0x00 }, MiniJpeg.Sos, MiniJpeg.Eoi) },
        };

        [Theory]
        [MemberData(nameof(NoEndCases))]
        public void Anything_that_does_not_walk_to_an_end_marker_is_no_end(string name, byte[] data)
        {
            Assert.False(string.IsNullOrEmpty(name));
            Assert.Equal(JpegScanStatus.NoEnd, JpegScanner.Scan(data).Status);
        }

        [Fact]
        public void Random_bytes_never_throw_and_a_found_length_stays_inside_the_data()
        {
            var random = new Random(20261102);
            for (int i = 0; i < 2000; i++)
            {
                var data = new byte[random.Next(0, 80)];
                random.NextBytes(data);
                if (i % 2 == 0 && data.Length >= 2)
                {
                    data[0] = 0xFF;
                    data[1] = 0xD8;
                }

                JpegScanResult result = JpegScanner.Scan(data);

                Assert.InRange(result.Length, 0, data.Length);
            }
        }
    }
}
