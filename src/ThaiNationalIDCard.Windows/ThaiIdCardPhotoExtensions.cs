using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ThaiNationalIDCard.Windows
{
    /// <summary>
    /// Turns the JPEG bytes from ThaiIdCardData.GetPhoto() into something a Windows UI can show. GetPhoto() returns null when
    /// there is no photo, so call these as <c>data.GetPhoto()?.ToImage()</c>. Neither method keeps the bytes or a stream open:
    /// the result stands on its own. The photo is personal data, so nothing here writes it anywhere. No photo from a real card
    /// has been through this code yet.
    /// </summary>
    public static class ThaiIdCardPhotoExtensions
    {
        private const byte JpegMarkerPrefix = 0xFF;
        private const byte JpegStartOfImage = 0xD8;

        /// <summary>Decodes the JPEG for Windows Forms or System.Drawing.</summary>
        /// <param name="jpeg">The JPEG bytes.</param>
        /// <returns>A new image, which the caller owns and has to dispose.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="jpeg"/> is null.</exception>
        /// <exception cref="ArgumentException">The bytes are not a JPEG that can be decoded.</exception>
        public static Image ToImage(this byte[] jpeg)
        {
            RequireJpegStart(jpeg);
            try
            {
                using var stream = new MemoryStream(jpeg, writable: false);
                using Image decoded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

                // An image from a stream needs that stream for as long as it lives, and this copy is what lets the stream go.
                return new Bitmap(decoded);
            }
            catch (Exception ex) when (IsDecodeFailure(ex))
            {
                throw NotDecodable(nameof(jpeg), ex);
            }
        }

        /// <summary>Decodes the JPEG for WPF.</summary>
        /// <param name="jpeg">The JPEG bytes.</param>
        /// <returns>A frozen image, so any thread can use it, including one other than the thread that called this.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="jpeg"/> is null.</exception>
        /// <exception cref="ArgumentException">The bytes are not a JPEG that can be decoded.</exception>
        public static BitmapSource ToBitmapSource(this byte[] jpeg)
        {
            RequireJpegStart(jpeg);
            try
            {
                using var stream = new MemoryStream(jpeg, writable: false);
                var image = new BitmapImage();
                image.BeginInit();

                // OnLoad decodes everything before EndInit returns, which is what lets the stream go.
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (IsDecodeFailure(ex))
            {
                throw NotDecodable(nameof(jpeg), ex);
            }
        }

        /// <summary>The decoders disagree on how to report bad data: GDI+ even says it is out of memory.</summary>
        private static bool IsDecodeFailure(Exception ex) =>
            ex is ArgumentException || ex is NotSupportedException || ex is IOException || ex is ExternalException ||
            ex is InvalidOperationException || ex is OutOfMemoryException || ex is OverflowException;

        private static ArgumentException NotDecodable(string parameter, Exception inner) =>
            new ArgumentException("The bytes could not be decoded as a JPEG.", parameter, inner);

        /// <summary>The decoders also take other formats, and these methods promise a JPEG.</summary>
        private static void RequireJpegStart(byte[] jpeg)
        {
            if (jpeg == null)
            {
                throw new ArgumentNullException(nameof(jpeg));
            }

            if (jpeg.Length < 2 || jpeg[0] != JpegMarkerPrefix || jpeg[1] != JpegStartOfImage)
            {
                throw new ArgumentException("The bytes do not start with a JPEG start marker.", nameof(jpeg));
            }
        }
    }
}
