using System;
using System.IO;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Images.Png
{
    /// <summary>
    /// PNG decoder (W3C PNG Specification, 3rd edition) producing PDF image data.
    ///
    /// Opaque, non-interlaced images take the fast path: the zlib data from IDAT is
    /// embedded unchanged and the viewer undoes the PNG row filters itself
    /// (FlateDecode with /Predictor 15, ISO 32000-1 §7.4.4.4). Everything else
    /// (alpha, tRNS, interlace) is decoded and re-compressed, with alpha written as a
    /// soft mask (§11.6.5.3). Every size is validated before allocating.
    /// </summary>
    internal sealed class PngImage
    {
        public const int MaxDimension = 30000;
        public const long MaxPixels = 200_000_000;

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private PdfDictionary _imageDict = new PdfDictionary();
        private byte[] _imageData = Array.Empty<byte>();
        private byte[]? _alphaData;

        private PngImage()
        {
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public double? DpiX { get; private set; }

        public double? DpiY { get; private set; }

        /// <summary>Color channels in the PDF image (1 gray, 3 RGB). Palettes count as 3.</summary>
        public int Components { get; private set; }

        public bool HasAlpha => _alphaData != null;

        /// <summary>Raw decoded samples, kept only when decoding was forced (tests).</summary>
        internal byte[]? DecodedColor { get; private set; }

        internal byte[]? DecodedAlpha { get; private set; }

        public static bool IsPng(byte[] data)
        {
            if (data.Length < Signature.Length)
            {
                return false;
            }

            for (var i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <param name="data">PNG file bytes.</param>
        /// <param name="forceDecode">Always decode pixels (tests use this to compare every image pixel by pixel).</param>
        public static PngImage Decode(byte[] data, bool forceDecode = false)
        {
            var png = new PngImage { _forceDecode = forceDecode };
            png.Parse(data);
            return png;
        }

        private bool _forceDecode;

        /// <summary>True when the IDAT data was embedded unchanged (predictor fast path).</summary>
        public bool UsedFastPath { get; private set; }

        public PdfStream CreateImageStream(PdfReference? softMask)
        {
            var dict = new PdfDictionary()
                .Set("Type", "XObject")
                .Set("Subtype", "Image")
                .Set("Width", Width)
                .Set("Height", Height);
            foreach (var key in new[] { "ColorSpace", "BitsPerComponent", "Filter", "DecodeParms" })
            {
                var value = _imageDict.Get(key);
                if (value != null)
                {
                    dict.Set(key, value);
                }
            }

            if (softMask != null)
            {
                dict.Set("SMask", softMask);
            }

            return new PdfStream(dict, _imageData);
        }

        public PdfStream? CreateSoftMaskStream()
        {
            if (_alphaData == null)
            {
                return null;
            }

            var dict = new PdfDictionary()
                .Set("Type", "XObject")
                .Set("Subtype", "Image")
                .Set("Width", Width)
                .Set("Height", Height)
                .Set("ColorSpace", "DeviceGray")
                .Set("BitsPerComponent", 8)
                .Set("Filter", "FlateDecode");
            return new PdfStream(dict, _alphaData);
        }

        private void Parse(byte[] d)
        {
            if (!IsPng(d))
            {
                throw new FormatException("Invalid PNG: wrong signature.");
            }

            int bitDepth = 0, colorType = -1, interlace = 0;
            byte[]? palette = null;
            byte[]? trns = null;
            var idat = new MemoryStream();
            var pos = 8;
            var sawHeader = false;
            var sawEnd = false;

            while (pos + 12 <= d.Length && !sawEnd)
            {
                var length = ReadU32(d, pos);
                if (length > int.MaxValue || pos + 12L + length > d.Length)
                {
                    throw new FormatException("Invalid PNG: chunk at offset " + pos + " is truncated.");
                }

                var type = new string(new[] { (char)d[pos + 4], (char)d[pos + 5], (char)d[pos + 6], (char)d[pos + 7] });
                var dataStart = pos + 8;
                var len = (int)length;
                var storedCrc = ReadU32(d, dataStart + len);
                if (Crc32.Compute(d, pos + 4, len + 4) != storedCrc)
                {
                    throw new FormatException("Invalid PNG: CRC mismatch in chunk '" + type + "'. The file is corrupt.");
                }

                if (!sawHeader && type != "IHDR")
                {
                    throw new FormatException("Invalid PNG: IHDR must be the first chunk.");
                }

                switch (type)
                {
                    case "IHDR":
                        if (len != 13)
                        {
                            throw new FormatException("Invalid PNG: IHDR has the wrong length.");
                        }

                        var w = ReadU32(d, dataStart);
                        var h = ReadU32(d, dataStart + 4);
                        bitDepth = d[dataStart + 8];
                        colorType = d[dataStart + 9];
                        interlace = d[dataStart + 12];
                        if (d[dataStart + 10] != 0 || d[dataStart + 11] != 0 || interlace > 1)
                        {
                            throw new FormatException("Invalid PNG: unknown compression, filter or interlace method.");
                        }

                        if (w == 0 || h == 0 || w > MaxDimension || h > MaxDimension || (long)w * h > MaxPixels)
                        {
                            throw new NotSupportedException("PNG size " + w + "×" + h + " is outside the supported range (max " + MaxDimension + " px per side, " + MaxPixels + " pixels).");
                        }

                        Width = (int)w;
                        Height = (int)h;
                        ValidateDepth(colorType, bitDepth);
                        sawHeader = true;
                        break;

                    case "PLTE":
                        if (len == 0 || len % 3 != 0 || len > 768)
                        {
                            throw new FormatException("Invalid PNG: bad palette length.");
                        }

                        palette = Slice(d, dataStart, len);
                        break;

                    case "tRNS":
                        trns = Slice(d, dataStart, len);
                        break;

                    case "pHYs":
                        if (len == 9 && d[dataStart + 8] == 1)
                        {
                            // Unit 1 = pixels per metre.
                            DpiX = Math.Round(ReadU32(d, dataStart) * 0.0254, 2);
                            DpiY = Math.Round(ReadU32(d, dataStart + 4) * 0.0254, 2);
                        }

                        break;

                    case "IDAT":
                        idat.Write(d, dataStart, len);
                        break;

                    case "IEND":
                        sawEnd = true;
                        break;

                    default:
                        // Uppercase first letter = critical chunk we must understand (spec §5.4).
                        if ((type[0] & 0x20) == 0)
                        {
                            throw new NotSupportedException("PNG uses an unsupported critical chunk '" + type + "'.");
                        }

                        break; // ancillary chunks (gAMA, iCCP, tEXt, …) are ignored
                }

                pos = dataStart + len + 4;
            }

            if (!sawHeader)
            {
                throw new FormatException("Invalid PNG: no IHDR chunk.");
            }

            if (idat.Length == 0)
            {
                throw new FormatException("Invalid PNG: no image data (IDAT).");
            }

            if (colorType == 3 && palette == null)
            {
                throw new FormatException("Invalid PNG: palette image without PLTE chunk.");
            }

            if (trns != null && !ValidTrns(colorType, trns, palette))
            {
                trns = null; // malformed tRNS is ancillary: ignore it
            }

            var compressed = idat.ToArray();
            var channels = Channels(colorType);
            var raw = Zlib.Decompress(compressed, ExpectedRawSize(channels, bitDepth, interlace));

            var opaque = colorType != 4 && colorType != 6 && trns == null;
            if (opaque && interlace == 0 && !_forceDecode)
            {
                ValidateFilterBytes(raw, RowBytes(Width, channels, bitDepth), Height);
                FastPath(compressed, colorType, bitDepth, palette);
                UsedFastPath = true;
            }
            else
            {
                DecodePath(raw, colorType, bitDepth, interlace, palette, trns);
            }
        }

        private void FastPath(byte[] zlibData, int colorType, int bitDepth, byte[]? palette)
        {
            var colors = colorType == 2 ? 3 : 1;
            Components = colorType == 0 ? 1 : 3;
            _imageDict.Set("ColorSpace", ColorSpace(colorType, palette))
                .Set("BitsPerComponent", bitDepth)
                .Set("Filter", "FlateDecode")
                .Set("DecodeParms", new PdfDictionary()
                    .Set("Predictor", 15)
                    .Set("Colors", colors)
                    .Set("BitsPerComponent", bitDepth)
                    .Set("Columns", Width));
            _imageData = zlibData;
        }

        private void DecodePath(byte[] raw, int colorType, int bitDepth, int interlace, byte[]? palette, byte[]? trns)
        {
            var channels = Channels(colorType);
            var gray = colorType == 0 || colorType == 4;
            var outComponents = gray ? 1 : 3;
            var outBytesPerSample = bitDepth == 16 && colorType != 3 ? 2 : 1;
            var color = new byte[(long)Width * Height * outComponents * outBytesPerSample];
            var alpha = new byte[(long)Width * Height];
            var anyTransparent = false;

            var passes = interlace == 1 ? Adam7 : NoInterlace;
            var rawPos = 0;
            foreach (var pass in passes)
            {
                var pw = Width > pass[0] ? (Width - pass[0] + pass[2] - 1) / pass[2] : 0;
                var ph = Height > pass[1] ? (Height - pass[1] + pass[3] - 1) / pass[3] : 0;
                if (pw == 0 || ph == 0)
                {
                    continue;
                }

                var rowBytes = RowBytes(pw, channels, bitDepth);
                var bpp = Math.Max(1, channels * bitDepth / 8);
                var prev = new byte[rowBytes];
                var row = new byte[rowBytes];
                for (var y = 0; y < ph; y++)
                {
                    var filter = raw[rawPos];
                    Buffer.BlockCopy(raw, rawPos + 1, row, 0, rowBytes);
                    rawPos += rowBytes + 1;
                    Unfilter(filter, row, prev, bpp);

                    var outY = pass[1] + y * pass[3];
                    for (var x = 0; x < pw; x++)
                    {
                        var outX = pass[0] + x * pass[2];
                        var pixel = (long)outY * Width + outX;
                        var a = WritePixel(row, x, colorType, bitDepth, channels, palette, trns, color, pixel * outComponents * outBytesPerSample, outBytesPerSample);
                        alpha[pixel] = a;
                        anyTransparent |= a != 255;
                    }

                    var swap = prev;
                    prev = row;
                    row = swap;
                }
            }

            Components = outComponents;
            _imageDict.Set("ColorSpace", gray ? "DeviceGray" : "DeviceRGB")
                .Set("BitsPerComponent", outBytesPerSample * 8)
                .Set("Filter", "FlateDecode");
            _imageData = Zlib.Compress(color);
            // A fully opaque alpha channel adds nothing: drop the soft mask.
            _alphaData = anyTransparent ? Zlib.Compress(alpha) : null;
            DecodedColor = _forceDecode ? color : null;
            DecodedAlpha = _forceDecode ? alpha : null;
        }

        /// <summary>Writes one pixel's color samples and returns its 8-bit alpha.</summary>
        private static byte WritePixel(byte[] row, int x, int colorType, int bitDepth, int channels, byte[]? palette, byte[]? trns,
            byte[] color, long outPos, int outBytes)
        {
            switch (colorType)
            {
                case 3:
                {
                    var index = Sample(row, x, 0, channels, bitDepth);
                    if (index * 3 + 2 >= palette!.Length)
                    {
                        throw new FormatException("Invalid PNG: palette index " + index + " is out of range.");
                    }

                    color[outPos] = palette[index * 3];
                    color[outPos + 1] = palette[index * 3 + 1];
                    color[outPos + 2] = palette[index * 3 + 2];
                    return trns != null && index < trns.Length ? trns[index] : (byte)255;
                }

                case 0:
                case 4:
                {
                    var g = Sample(row, x, 0, channels, bitDepth);
                    PutSample(color, outPos, g, bitDepth, outBytes);
                    if (colorType == 4)
                    {
                        return High8(Sample(row, x, 1, channels, bitDepth), bitDepth);
                    }

                    return trns != null && g == ((trns[0] << 8) | trns[1]) ? (byte)0 : (byte)255;
                }

                default: // 2 or 6
                {
                    var r = Sample(row, x, 0, channels, bitDepth);
                    var g = Sample(row, x, 1, channels, bitDepth);
                    var b = Sample(row, x, 2, channels, bitDepth);
                    PutSample(color, outPos, r, bitDepth, outBytes);
                    PutSample(color, outPos + outBytes, g, bitDepth, outBytes);
                    PutSample(color, outPos + 2 * outBytes, b, bitDepth, outBytes);
                    if (colorType == 6)
                    {
                        return High8(Sample(row, x, 3, channels, bitDepth), bitDepth);
                    }

                    var key = trns != null &&
                              r == ((trns[0] << 8) | trns[1]) &&
                              g == ((trns[2] << 8) | trns[3]) &&
                              b == ((trns[4] << 8) | trns[5]);
                    return key ? (byte)0 : (byte)255;
                }
            }
        }

        private static int Sample(byte[] row, int x, int channel, int channels, int bitDepth)
        {
            switch (bitDepth)
            {
                case 8:
                    return row[x * channels + channel];
                case 16:
                    var p = (x * channels + channel) * 2;
                    return (row[p] << 8) | row[p + 1];
                default:
                    // Sub-byte depths only occur with one channel (gray or palette).
                    var bitPos = x * bitDepth;
                    var shift = 8 - bitDepth - (bitPos % 8);
                    return (row[bitPos / 8] >> shift) & ((1 << bitDepth) - 1);
            }
        }

        private static void PutSample(byte[] output, long pos, int value, int bitDepth, int outBytes)
        {
            if (outBytes == 2)
            {
                output[pos] = (byte)(value >> 8);
                output[pos + 1] = (byte)value;
            }
            else
            {
                output[pos] = bitDepth == 8 ? (byte)value : (byte)(value * 255 / ((1 << bitDepth) - 1));
            }
        }

        private static byte High8(int value, int bitDepth) => bitDepth == 16 ? (byte)(value >> 8) : (byte)value;

        private static void Unfilter(byte filter, byte[] row, byte[] prev, int bpp)
        {
            switch (filter)
            {
                case 0:
                    return;
                case 1: // Sub
                    for (var i = bpp; i < row.Length; i++)
                    {
                        row[i] = (byte)(row[i] + row[i - bpp]);
                    }

                    return;
                case 2: // Up
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = (byte)(row[i] + prev[i]);
                    }

                    return;
                case 3: // Average
                    for (var i = 0; i < row.Length; i++)
                    {
                        var left = i >= bpp ? row[i - bpp] : 0;
                        row[i] = (byte)(row[i] + ((left + prev[i]) >> 1));
                    }

                    return;
                case 4: // Paeth
                    for (var i = 0; i < row.Length; i++)
                    {
                        var a = i >= bpp ? row[i - bpp] : 0;
                        var b = prev[i];
                        var c = i >= bpp ? prev[i - bpp] : 0;
                        row[i] = (byte)(row[i] + Paeth(a, b, c));
                    }

                    return;
                default:
                    throw new FormatException("Invalid PNG: unknown row filter type " + filter + ".");
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static void ValidateFilterBytes(byte[] raw, int rowBytes, int height)
        {
            for (var y = 0; y < height; y++)
            {
                if (raw[y * (rowBytes + 1)] > 4)
                {
                    throw new FormatException("Invalid PNG: unknown row filter type in row " + y + ".");
                }
            }
        }

        private int ExpectedRawSize(int channels, int bitDepth, int interlace)
        {
            long total = 0;
            foreach (var pass in interlace == 1 ? Adam7 : NoInterlace)
            {
                var pw = Width > pass[0] ? (Width - pass[0] + pass[2] - 1) / pass[2] : 0;
                var ph = Height > pass[1] ? (Height - pass[1] + pass[3] - 1) / pass[3] : 0;
                if (pw > 0 && ph > 0)
                {
                    total += (long)ph * (RowBytes(pw, channels, bitDepth) + 1);
                }
            }

            if (total > int.MaxValue)
            {
                throw new NotSupportedException("PNG is too large to decode.");
            }

            return (int)total;
        }

        private static int RowBytes(int width, int channels, int bitDepth) => (int)(((long)width * channels * bitDepth + 7) / 8);

        private static PdfObject ColorSpace(int colorType, byte[]? palette)
        {
            switch (colorType)
            {
                case 0:
                    return new PdfName("DeviceGray");
                case 2:
                    return new PdfName("DeviceRGB");
                default:
                    var entries = palette!.Length / 3;
                    return new PdfArray(new PdfName("Indexed"), new PdfName("DeviceRGB"), new PdfInteger(entries - 1), PdfString.Hex(palette));
            }
        }

        private static int Channels(int colorType)
        {
            switch (colorType)
            {
                case 2: return 3;
                case 4: return 2;
                case 6: return 4;
                default: return 1;
            }
        }

        private static void ValidateDepth(int colorType, int bitDepth)
        {
            bool ok;
            switch (colorType)
            {
                case 0: ok = bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8 || bitDepth == 16; break;
                case 3: ok = bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8; break;
                case 2:
                case 4:
                case 6: ok = bitDepth == 8 || bitDepth == 16; break;
                default: ok = false; break;
            }

            if (!ok)
            {
                throw new FormatException("Invalid PNG: color type " + colorType + " with bit depth " + bitDepth + " is not allowed.");
            }
        }

        private static bool ValidTrns(int colorType, byte[] trns, byte[]? palette)
        {
            switch (colorType)
            {
                case 0: return trns.Length == 2;
                case 2: return trns.Length == 6;
                case 3: return palette != null && trns.Length <= palette.Length / 3;
                default: return false;
            }
        }

        private static uint ReadU32(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

        private static byte[] Slice(byte[] d, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(d, offset, result, 0, length);
            return result;
        }

        // Adam7 passes: xStart, yStart, xStep, yStep.
        private static readonly int[][] Adam7 =
        {
            new[] { 0, 0, 8, 8 }, new[] { 4, 0, 8, 8 }, new[] { 0, 4, 4, 8 }, new[] { 2, 0, 4, 4 },
            new[] { 0, 2, 2, 4 }, new[] { 1, 0, 2, 2 }, new[] { 0, 1, 1, 2 },
        };

        private static readonly int[][] NoInterlace = { new[] { 0, 0, 1, 1 } };
    }
}
