using System;
using System.IO;
using LifeOS.Pdf.Images.Png;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Images
{
    /// <summary>
    /// An image that can be drawn on any number of pages; it is stored in the file once.
    /// Supports JPEG (embedded as-is, no re-encoding) and PNG (including transparency).
    /// The format is detected from the file content, not the extension.
    /// </summary>
    public sealed class PdfImage
    {
        private readonly byte[]? _jpeg;
        private readonly PngImage? _png;
        private readonly bool _adobeInvertedCmyk;

        private PdfImage(byte[] jpeg, JpegInfo info)
        {
            _jpeg = jpeg;
            Format = PdfImageFormat.Jpeg;
            PixelWidth = info.Width;
            PixelHeight = info.Height;
            Components = info.Components;
            DpiX = info.DpiX;
            DpiY = info.DpiY;
            _adobeInvertedCmyk = info.Adobe && info.Components == 4;
        }

        private PdfImage(PngImage png)
        {
            _png = png;
            Format = PdfImageFormat.Png;
            PixelWidth = png.Width;
            PixelHeight = png.Height;
            Components = png.Components;
            DpiX = png.DpiX;
            DpiY = png.DpiY;
        }

        public PdfImageFormat Format { get; }

        /// <summary>Width in pixels.</summary>
        public int PixelWidth { get; }

        /// <summary>Height in pixels.</summary>
        public int PixelHeight { get; }

        /// <summary>Color channels: 1 (gray), 3 (RGB, also for palette PNGs) or 4 (CMYK JPEG).</summary>
        public int Components { get; }

        /// <summary>True when the image has transparent pixels (written as a soft mask).</summary>
        public bool HasAlpha => _png != null && _png.HasAlpha;

        /// <summary>Horizontal resolution stored in the file (JPEG JFIF / PNG pHYs), or null.</summary>
        public double? DpiX { get; }

        /// <summary>Vertical resolution stored in the file, or null.</summary>
        public double? DpiY { get; }

        public static PdfImage FromFile(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return FromBytesNoCopy(File.ReadAllBytes(path));
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
                return FromBytesNoCopy(ms.ToArray());
            }
        }

        public static PdfImage FromBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return FromBytesNoCopy((byte[])data.Clone());
        }

        internal PdfStream ToPdfStream(PdfReference? softMask)
        {
            if (_png != null)
            {
                return _png.CreateImageStream(softMask);
            }

            var dict = new PdfDictionary()
                .Set("Type", "XObject")
                .Set("Subtype", "Image")
                .Set("Width", PixelWidth)
                .Set("Height", PixelHeight)
                .Set("ColorSpace", Components == 1 ? "DeviceGray" : Components == 3 ? "DeviceRGB" : "DeviceCMYK")
                .Set("BitsPerComponent", 8)
                .Set("Filter", "DCTDecode");

            if (_adobeInvertedCmyk)
            {
                // Photoshop writes CMYK JPEGs inverted; tell the viewer to flip them back.
                dict.Set("Decode", PdfArray.OfNumbers(1, 0, 1, 0, 1, 0, 1, 0));
            }

            return new PdfStream(dict, _jpeg!);
        }

        internal PdfStream? ToSoftMaskStream() => _png?.CreateSoftMaskStream();

        private static PdfImage FromBytesNoCopy(byte[] data)
        {
            if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            {
                return new PdfImage(data, JpegInfo.Parse(data));
            }

            if (PngImage.IsPng(data))
            {
                return new PdfImage(PngImage.Decode(data));
            }

            throw new NotSupportedException("Unsupported image format. Supported formats are JPEG and PNG.");
        }

        private sealed class JpegInfo
        {
            public int Width;
            public int Height;
            public int Components;
            public bool Adobe;
            public double? DpiX;
            public double? DpiY;

            public static JpegInfo Parse(byte[] d)
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
                        if (d[i + 2] != 8)
                        {
                            throw new NotSupportedException("Only 8-bit JPEG images are supported.");
                        }

                        info.Height = (d[i + 3] << 8) | d[i + 4];
                        info.Width = (d[i + 5] << 8) | d[i + 6];
                        info.Components = d[i + 7];
                        found = true;
                    }
                    else if (marker == 0xE0 && length >= 16 && Matches(d, i + 2, "JFIF\0"))
                    {
                        // JFIF density: units 1 = dots per inch, 2 = dots per cm.
                        var units = d[i + 9];
                        var x = (d[i + 10] << 8) | d[i + 11];
                        var y = (d[i + 12] << 8) | d[i + 13];
                        if ((units == 1 || units == 2) && x > 0 && y > 0)
                        {
                            var factor = units == 2 ? 2.54 : 1.0;
                            info.DpiX = Math.Round(x * factor, 2);
                            info.DpiY = Math.Round(y * factor, 2);
                        }
                    }
                    else if (marker == 0xEE && length >= 7 && Matches(d, i + 2, "Adobe"))
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

            private static bool Matches(byte[] d, int offset, string ascii)
            {
                if (offset + ascii.Length > d.Length)
                {
                    return false;
                }

                for (var k = 0; k < ascii.Length; k++)
                {
                    if (d[offset + k] != ascii[k])
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
