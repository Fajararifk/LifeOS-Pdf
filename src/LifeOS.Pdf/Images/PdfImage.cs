using System;
using System.IO;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Images
{
    /// <summary>
    /// An image that can be drawn on any number of pages; it is stored in the file once.
    /// v0.1 supports JPEG (embedded as-is with DCTDecode, no re-encoding). PNG arrives in Fase 1.
    /// </summary>
    public sealed class PdfImage
    {
        private readonly byte[] _data;

        private PdfImage(byte[] data, int width, int height, int components, bool adobeInverted)
        {
            _data = data;
            PixelWidth = width;
            PixelHeight = height;
            Components = components;
            AdobeInvertedCmyk = adobeInverted;
        }

        /// <summary>Width in pixels.</summary>
        public int PixelWidth { get; }

        /// <summary>Height in pixels.</summary>
        public int PixelHeight { get; }

        /// <summary>Number of color channels: 1 (gray), 3 (RGB) or 4 (CMYK).</summary>
        public int Components { get; }

        private bool AdobeInvertedCmyk { get; }

        public static PdfImage FromFile(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return FromBytes(File.ReadAllBytes(path));
        }

        public static PdfImage FromStream(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return FromBytes(ms.ToArray());
            }
        }

        public static PdfImage FromBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            {
                throw new NotSupportedException("Only JPEG images are supported in this version. PNG support is planned for Fase 1.");
            }

            var copy = (byte[])data.Clone();
            var info = ParseJpeg(copy);
            return new PdfImage(copy, info.Width, info.Height, info.Components, info.Adobe && info.Components == 4);
        }

        internal PdfStream ToPdfStream()
        {
            var dict = new PdfDictionary()
                .Set("Type", "XObject")
                .Set("Subtype", "Image")
                .Set("Width", PixelWidth)
                .Set("Height", PixelHeight)
                .Set("ColorSpace", Components == 1 ? "DeviceGray" : Components == 3 ? "DeviceRGB" : "DeviceCMYK")
                .Set("BitsPerComponent", 8)
                .Set("Filter", "DCTDecode");

            if (AdobeInvertedCmyk)
            {
                // Photoshop writes CMYK JPEGs inverted; tell the viewer to flip them back.
                dict.Set("Decode", PdfArray.OfNumbers(1, 0, 1, 0, 1, 0, 1, 0));
            }

            return new PdfStream(dict, _data);
        }

        private struct JpegInfo
        {
            public int Width;
            public int Height;
            public int Components;
            public bool Adobe;
        }

        private static JpegInfo ParseJpeg(byte[] d)
        {
            var info = new JpegInfo();
            var found = false;
            var i = 2;
            while (i < d.Length)
            {
                if (d[i] != 0xFF)
                {
                    throw new FormatException("Invalid JPEG: expected a marker at offset " + i + ".");
                }

                // Skip fill bytes.
                while (i < d.Length && d[i] == 0xFF)
                {
                    i++;
                }

                if (i >= d.Length)
                {
                    break;
                }

                var marker = d[i++];
                if (marker == 0xD9 || marker == 0xDA)
                {
                    break; // End of image or start of scan: header is done.
                }

                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    continue; // Markers without a length field.
                }

                if (i + 1 >= d.Length)
                {
                    break;
                }

                var length = (d[i] << 8) | d[i + 1];
                if (length < 2 || i + length > d.Length)
                {
                    throw new FormatException("Invalid JPEG: corrupt segment length.");
                }

                var isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof && length >= 8)
                {
                    var precision = d[i + 2];
                    if (precision != 8)
                    {
                        throw new NotSupportedException("Only 8-bit JPEG images are supported.");
                    }

                    info.Height = (d[i + 3] << 8) | d[i + 4];
                    info.Width = (d[i + 5] << 8) | d[i + 6];
                    info.Components = d[i + 7];
                    found = true;
                }
                else if (marker == 0xEE && length >= 7 &&
                         d[i + 2] == (byte)'A' && d[i + 3] == (byte)'d' && d[i + 4] == (byte)'o' &&
                         d[i + 5] == (byte)'b' && d[i + 6] == (byte)'e')
                {
                    info.Adobe = true;
                }

                i += length;
            }

            if (!found || info.Width <= 0 || info.Height <= 0)
            {
                throw new FormatException("Invalid JPEG: no frame header (SOF) with a valid size was found.");
            }

            if (info.Components != 1 && info.Components != 3 && info.Components != 4)
            {
                throw new NotSupportedException("JPEG with " + info.Components + " color components is not supported.");
            }

            return info;
        }
    }
}
