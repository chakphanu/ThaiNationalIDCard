using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ThaiNationalIDCard.Windows;
using Xunit;

namespace ThaiNationalIDCard.Windows.Tests
{
    /// <summary>
    /// The helpers on JPEGs made here from blank bitmaps. No UI is shown and no real photo is used. This runs on Windows only,
    /// and says nothing about the JPEGs real cards hold.
    /// </summary>
    public class ThaiIdCardPhotoExtensionsTests
    {
        private const int Width = 3;
        private const int Height = 2;

        private static byte[] Encoded(ImageFormat format)
        {
            using var bitmap = new Bitmap(Width, Height);
            using var stream = new MemoryStream();
            bitmap.Save(stream, format);
            return stream.ToArray();
        }

        private static byte[] Jpeg() => Encoded(ImageFormat.Jpeg);

        private static byte[] Pixels(BitmapSource source)
        {
            int stride = source.PixelWidth * ((source.Format.BitsPerPixel + 7) / 8);
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        public static TheoryData<byte[]> NotJpeg => new TheoryData<byte[]>
        {
            Array.Empty<byte>(),
            new byte[] { 0xFF },
            new byte[] { 0x20, 0x20, 0x20, 0x20 },
            new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 },
            Encoded(ImageFormat.Png),
        };

        [Fact]
        public void ToImage_gives_the_size_of_the_jpeg()
        {
            using Image image = Jpeg().ToImage();

            Assert.Equal(Width, image.Width);
            Assert.Equal(Height, image.Height);
        }

        [Fact]
        public void ToImage_does_not_need_the_bytes_afterwards()
        {
            byte[] jpeg = Jpeg();
            using Image image = jpeg.ToImage();
            Array.Clear(jpeg, 0, jpeg.Length);

            // Saving makes GDI+ use the pixels, which is what fails on an image still tied to a stream that is gone.
            using var saved = new MemoryStream();
            image.Save(saved, ImageFormat.Png);

            Assert.True(saved.Length > 0);
        }

        [Fact]
        public void ToBitmapSource_gives_the_size_of_the_jpeg()
        {
            BitmapSource source = Jpeg().ToBitmapSource();

            Assert.Equal(Width, source.PixelWidth);
            Assert.Equal(Height, source.PixelHeight);
        }

        [Fact]
        public async Task ToBitmapSource_can_be_used_from_another_thread()
        {
            BitmapSource source = Jpeg().ToBitmapSource();

            Assert.True(source.IsFrozen);
            byte[] pixels = await Task.Run(() => Pixels(source), TestContext.Current.CancellationToken);
            Assert.NotEmpty(pixels);
        }

        [Fact]
        public void ToBitmapSource_does_not_need_the_bytes_afterwards()
        {
            byte[] jpeg = Jpeg();
            BitmapSource source = jpeg.ToBitmapSource();
            Array.Clear(jpeg, 0, jpeg.Length);

            Assert.NotEmpty(Pixels(source));
        }

        [Fact]
        public void Null_is_refused_by_name()
        {
            byte[] none = null!;

            Assert.Equal("jpeg", Assert.Throws<ArgumentNullException>(() => none.ToImage()).ParamName);
            Assert.Equal("jpeg", Assert.Throws<ArgumentNullException>(() => none.ToBitmapSource()).ParamName);
        }

        [Theory]
        [MemberData(nameof(NotJpeg))]
        public void ToImage_refuses_what_is_not_a_decodable_jpeg(byte[] bytes)
        {
            Assert.Equal("jpeg", Assert.Throws<ArgumentException>(() => bytes.ToImage()).ParamName);
        }

        [Theory]
        [MemberData(nameof(NotJpeg))]
        public void ToBitmapSource_refuses_what_is_not_a_decodable_jpeg(byte[] bytes)
        {
            Assert.Equal("jpeg", Assert.Throws<ArgumentException>(() => bytes.ToBitmapSource()).ParamName);
        }
    }
}
