using System;
using System.Collections.Generic;
using LifeOS.Pdf.Images.Png;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// Turns an image XObject or inline image (§8.9) into a file: JPEG and JPEG 2000 data are
    /// returned as they are; Flate/LZW/RunLength/ASCII images in Gray, RGB, CMYK, Indexed,
    /// ICCBased, CalGray/CalRGB or Separation become PNG, with /Decode, image masks, soft masks
    /// and stencil masks applied. JBIG2 and CCITT data are returned raw.
    /// </summary>
    internal static class ImageDecoder
    {
        private const long MaxPixels = 100000000;

        private static readonly Dictionary<string, string> InlineKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BPC"] = "BitsPerComponent", ["CS"] = "ColorSpace", ["D"] = "Decode", ["DP"] = "DecodeParms", ["F"] = "Filter",
            ["H"] = "Height", ["IM"] = "ImageMask", ["I"] = "Interpolate", ["W"] = "Width",
        };

        private static readonly Dictionary<string, string> InlineNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["G"] = "DeviceGray", ["RGB"] = "DeviceRGB", ["CMYK"] = "DeviceCMYK", ["I"] = "Indexed",
            ["AHx"] = "ASCIIHexDecode", ["A85"] = "ASCII85Decode", ["LZW"] = "LZWDecode", ["Fl"] = "FlateDecode",
            ["RL"] = "RunLengthDecode", ["CCF"] = "CCITTFaxDecode", ["DCT"] = "DCTDecode",
        };

        /// <summary>The stream of an image, with inline abbreviations expanded.</summary>
        public static PdfStream StreamOf(ImageInfo info)
        {
            if (info.Stream != null)
            {
                return info.Stream;
            }

            var dict = new PdfDictionary();
            foreach (var e in info.Dictionary.Entries)
            {
                dict.Set(InlineKeys.TryGetValue(e.Key, out var full) ? full : e.Key, Expand(e.Value));
            }

            return new PdfStream(dict, info.InlineData ?? Array.Empty<byte>());
        }

        private static PdfObject Expand(PdfObject value)
        {
            switch (value)
            {
                case PdfName n when InlineNames.TryGetValue(n.Value, out var full):
                    return new PdfName(full);
                case PdfArray a:
                    var copy = new PdfArray();
                    foreach (var item in a.Items)
                    {
                        copy.Items.Add(Expand(item));
                    }

                    return copy;
                default:
                    return value;
            }
        }

        public static ExtractedImageFormat Format(PdfStream stream, PdfParser? parser)
        {
            var last = LastFilter(stream, parser);
            switch (last)
            {
                case "DCTDecode": return ExtractedImageFormat.Jpeg;
                case "JPXDecode": return ExtractedImageFormat.Jpeg2000;
                case "JBIG2Decode": return ExtractedImageFormat.Jbig2;
                case "CCITTFaxDecode": return ExtractedImageFormat.Ccitt;
                default: return StreamDecoder.CanDecode(stream, parser) ? ExtractedImageFormat.Png : ExtractedImageFormat.Unsupported;
            }
        }

        private static string? LastFilter(PdfStream stream, PdfParser? parser)
        {
            var filter = R(stream.Dictionary.Get("Filter"), parser);
            if (filter is PdfName n)
            {
                return n.Value;
            }

            if (filter is PdfArray a && a.Items.Count > 0 && R(a.Items[a.Items.Count - 1], parser) is PdfName ln)
            {
                return ln.Value;
            }

            return null;
        }

        /// <summary>The file bytes for <paramref name="format"/>.</summary>
        public static byte[] Bytes(PdfStream stream, ExtractedImageFormat format, PdfParser? parser, long limit)
        {
            switch (format)
            {
                case ExtractedImageFormat.Jpeg:
                case ExtractedImageFormat.Jpeg2000:
                case ExtractedImageFormat.Jbig2:
                case ExtractedImageFormat.Ccitt:
                    return WithoutLastFilter(stream, parser, limit);
                case ExtractedImageFormat.Png:
                    return Png(stream, parser, limit);
                default:
                    throw new PdfReadException("This image uses a filter LifeOS.Pdf cannot decode.");
            }
        }

        /// <summary>Applies every filter except the last (image codec) one — usually there are none.</summary>
        private static byte[] WithoutLastFilter(PdfStream stream, PdfParser? parser, long limit)
        {
            if (R(stream.Dictionary.Get("Filter"), parser) is PdfArray a && a.Items.Count > 1)
            {
                var dict = new PdfDictionary();
                var filters = new PdfArray();
                for (var i = 0; i < a.Items.Count - 1; i++)
                {
                    filters.Items.Add(a.Items[i]);
                }

                dict.Set("Filter", filters);
                if (R(stream.Dictionary.Get("DecodeParms"), parser) is PdfArray p)
                {
                    var parms = new PdfArray();
                    for (var i = 0; i < Math.Min(p.Items.Count, a.Items.Count - 1); i++)
                    {
                        parms.Items.Add(p.Items[i]);
                    }

                    dict.Set("DecodeParms", parms);
                }

                return StreamDecoder.Decode(new PdfStream(dict, stream.Data), parser, limit);
            }

            return stream.Data;
        }

        private static byte[] Png(PdfStream stream, PdfParser? parser, long limit)
        {
            var d = stream.Dictionary;
            var width = (int)Num(d.Get("Width"), parser);
            var height = (int)Num(d.Get("Height"), parser);
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            {
                throw new PdfLimitException("The image size " + width + "×" + height + " is invalid or larger than " + MaxPixels + " pixels.");
            }

            var data = StreamDecoder.Decode(stream, parser, limit);
            var mask = R(d.Get("ImageMask"), parser) is PdfBoolean im && im.Value;
            var bpc = mask ? 1 : (int)Num(d.Get("BitsPerComponent"), parser, 8);
            if (bpc != 1 && bpc != 2 && bpc != 4 && bpc != 8 && bpc != 16)
            {
                throw new PdfReadException("Unsupported BitsPerComponent " + bpc + ".");
            }

            var space = mask ? new Space(1, null, 0, null) : ColorSpace(R(d.Get("ColorSpace"), parser), parser, 0);
            var decode = R(d.Get("Decode"), parser) as PdfArray;
            var samples = Unpack(data, width, height, space.Components, bpc);
            var pixels = ToColor(samples, width * height, space, bpc, decode, parser, mask);
            var channels = space.OutputChannels;

            // Transparency: soft mask (gray image) or explicit stencil mask.
            byte[]? alpha = null;
            if (R(d.Get("SMask"), parser) is PdfStream smask)
            {
                alpha = Gray(smask, parser, limit, width, height, stencil: false);
            }
            else if (R(d.Get("Mask"), parser) is PdfStream stencil)
            {
                alpha = Gray(stencil, parser, limit, width, height, stencil: true);
            }

            if (alpha != null)
            {
                var withAlpha = new byte[width * height * (channels + 1)];
                for (var i = 0; i < width * height; i++)
                {
                    Buffer.BlockCopy(pixels, i * channels, withAlpha, i * (channels + 1), channels);
                    withAlpha[i * (channels + 1) + channels] = alpha[i];
                }

                return PngWriter.Write(width, height, withAlpha, channels + 1);
            }

            return PngWriter.Write(width, height, pixels, channels);
        }

        /// <summary>A mask image as one alpha byte per pixel of the base image (resampled if sizes differ).</summary>
        private static byte[] Gray(PdfStream mask, PdfParser? parser, long limit, int width, int height, bool stencil)
        {
            var d = mask.Dictionary;
            var mw = (int)Num(d.Get("Width"), parser);
            var mh = (int)Num(d.Get("Height"), parser);
            if (mw <= 0 || mh <= 0 || (long)mw * mh > MaxPixels)
            {
                throw new PdfLimitException("Invalid mask size.");
            }

            var bpc = stencil ? 1 : (int)Num(d.Get("BitsPerComponent"), parser, 8);
            var samples = Unpack(StreamDecoder.Decode(mask, parser, limit), mw, mh, 1, bpc);
            var max = (1 << Math.Min(bpc, 8)) - 1;
            var decode = R(d.Get("Decode"), parser) as PdfArray;
            var invert = decode != null && decode.Items.Count >= 2 && Num(decode.Items[0], parser) > Num(decode.Items[1], parser);
            var result = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var sy = (int)((long)y * mh / height);
                for (var x = 0; x < width; x++)
                {
                    var sx = (int)((long)x * mw / width);
                    var v = samples[sy * mw + sx] * 255 / max;
                    if (invert)
                    {
                        v = 255 - v;
                    }

                    // Stencil masks: sample 0 = painted (opaque), 1 = masked out (§8.9.6.3).
                    result[y * width + x] = (byte)(stencil ? 255 - v : v);
                }
            }

            return result;
        }

        private sealed class Space
        {
            public Space(int components, string? kind, int hival, byte[]? lookup, Space? baseSpace = null)
            {
                Components = components;
                Kind = kind;
                Hival = hival;
                Lookup = lookup;
                Base = baseSpace;
            }

            public int Components { get; }

            /// <summary>"Gray", "RGB", "CMYK", "Indexed", "Separation" (or null for masks).</summary>
            public string? Kind { get; }

            public int Hival { get; }

            public byte[]? Lookup { get; }

            public Space? Base { get; }

            public int OutputChannels => Kind == "RGB" || Kind == "CMYK" || (Kind == "Indexed" && Base!.OutputChannels == 3) ? 3 : 1;
        }

        private static Space ColorSpace(PdfObject? cs, PdfParser? parser, int depth)
        {
            if (depth > 4)
            {
                throw new PdfReadException("Colour spaces are nested too deeply.");
            }

            if (cs is PdfName n)
            {
                switch (n.Value)
                {
                    case "DeviceGray": case "CalGray": case "G": return new Space(1, "Gray", 0, null);
                    case "DeviceRGB": case "CalRGB": case "RGB": return new Space(3, "RGB", 0, null);
                    case "DeviceCMYK": case "CMYK": return new Space(4, "CMYK", 0, null);
                }
            }

            if (cs is PdfArray a && a.Items.Count > 0 && R(a.Items[0], parser) is PdfName family)
            {
                switch (family.Value)
                {
                    case "CalGray": return new Space(1, "Gray", 0, null);
                    case "CalRGB": return new Space(3, "RGB", 0, null);
                    case "ICCBased":
                        var icc = a.Items.Count > 1 ? R(a.Items[1], parser) as PdfStream : null;
                        var n2 = icc != null ? (int)Num(icc.Dictionary.Get("N"), parser, 3) : 3;
                        return n2 == 1 ? new Space(1, "Gray", 0, null) : n2 == 4 ? new Space(4, "CMYK", 0, null) : new Space(3, "RGB", 0, null);
                    case "Indexed":
                    case "I":
                        if (a.Items.Count >= 4)
                        {
                            var baseSpace = ColorSpace(R(a.Items[1], parser), parser, depth + 1);
                            var hival = (int)Math.Max(0, Math.Min(255, Num(a.Items[2], parser)));
                            var lookupObj = R(a.Items[3], parser);
                            var lookup = lookupObj is PdfString ls ? ls.Bytes : lookupObj is PdfStream st ? StreamDecoder.Decode(st, parser, 1 << 20) : null;
                            if (lookup != null && baseSpace.Kind != "Indexed")
                            {
                                return new Space(1, "Indexed", hival, lookup, baseSpace);
                            }
                        }

                        break;
                    case "Separation":
                        return new Space(1, "Separation", 0, null);
                    case "DeviceGray":
                    case "DeviceRGB":
                    case "DeviceCMYK":
                        return ColorSpace(family, parser, depth + 1);
                }
            }

            throw new PdfReadException("Unsupported colour space for image extraction" + (cs is PdfArray arr && arr.Items.Count > 0 ? " (" + R(arr.Items[0], parser) + ")" : string.Empty) + ".");
        }

        /// <summary>Samples per component (0 … 2^bpc−1, 16-bit reduced to 8), rows padded to whole bytes (§8.9.3).</summary>
        private static int[] Unpack(byte[] data, int width, int height, int components, int bpc)
        {
            var perRow = width * components;
            var rowBytes = ((long)perRow * bpc + 7) / 8;
            var result = new int[(long)perRow * height];
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * rowBytes;
                for (var i = 0; i < perRow; i++)
                {
                    int v;
                    if (bpc == 8)
                    {
                        var p = rowStart + i;
                        v = p < data.Length ? data[p] : 0;
                    }
                    else if (bpc == 16)
                    {
                        var p = rowStart + i * 2;
                        v = p < data.Length ? data[p] : 0;
                    }
                    else
                    {
                        var bit = (long)i * bpc;
                        var p = rowStart + bit / 8;
                        var shift = 8 - bpc - (int)(bit % 8);
                        v = p < data.Length ? (data[p] >> shift) & ((1 << bpc) - 1) : 0;
                    }

                    result[(long)y * perRow + i] = v;
                }
            }

            return result;
        }

        private static byte[] ToColor(int[] samples, int pixels, Space space, int bpc, PdfArray? decode, PdfParser? parser, bool mask)
        {
            var max = bpc == 16 ? 255 : (1 << bpc) - 1;
            var n = space.Components;
            var output = new byte[pixels * space.OutputChannels];

            // Decode ranges (§8.9.5.2): sample → [dmin, dmax].
            var dmin = new double[n];
            var dmax = new double[n];
            for (var c = 0; c < n; c++)
            {
                dmin[c] = 0;
                dmax[c] = space.Kind == "Indexed" ? max : 1;
                if (decode != null && decode.Items.Count >= 2 * (c + 1))
                {
                    dmin[c] = Num(decode.Items[2 * c], parser);
                    dmax[c] = Num(decode.Items[2 * c + 1], parser);
                }
            }

            double Value(int pixel, int c) => dmin[c] + samples[(long)pixel * n + c] * (dmax[c] - dmin[c]) / max;
            byte B(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255)));

            for (var p = 0; p < pixels; p++)
            {
                switch (space.Kind)
                {
                    case null: // stencil mask: 0 = paint (black), 1 = background (white)
                        output[p] = B(Value(p, 0));
                        break;
                    case "Gray":
                        output[p] = B(Value(p, 0));
                        break;
                    case "Separation":
                        output[p] = B(1 - Value(p, 0)); // tint 1 = full ink = dark
                        break;
                    case "RGB":
                        output[p * 3] = B(Value(p, 0));
                        output[p * 3 + 1] = B(Value(p, 1));
                        output[p * 3 + 2] = B(Value(p, 2));
                        break;
                    case "CMYK":
                        double cy = Value(p, 0), m = Value(p, 1), ye = Value(p, 2), k = Value(p, 3);
                        output[p * 3] = B((1 - cy) * (1 - k));
                        output[p * 3 + 1] = B((1 - m) * (1 - k));
                        output[p * 3 + 2] = B((1 - ye) * (1 - k));
                        break;
                    case "Indexed":
                        var index = (int)Math.Max(0, Math.Min(space.Hival, Math.Round(Value(p, 0))));
                        var bs = space.Base!;
                        var entry = index * bs.Components;
                        double L(int c) => entry + c < space.Lookup!.Length ? space.Lookup[entry + c] / 255.0 : 0;
                        if (bs.Kind == "RGB")
                        {
                            output[p * 3] = B(L(0));
                            output[p * 3 + 1] = B(L(1));
                            output[p * 3 + 2] = B(L(2));
                        }
                        else if (bs.Kind == "CMYK")
                        {
                            output[p * 3] = B((1 - L(0)) * (1 - L(3)));
                            output[p * 3 + 1] = B((1 - L(1)) * (1 - L(3)));
                            output[p * 3 + 2] = B((1 - L(2)) * (1 - L(3)));
                        }
                        else
                        {
                            output[p] = B(bs.Kind == "Separation" ? 1 - L(0) : L(0));
                        }

                        break;
                }
            }

            return output;
        }

        private static PdfObject? R(PdfObject? o, PdfParser? parser) => o is PdfSourceReference && parser != null ? parser.Resolve(o) : o;

        private static double Num(PdfObject? o, PdfParser? parser, double fallback = 0)
        {
            switch (R(o, parser))
            {
                case PdfInteger i: return i.Value;
                case PdfReal r: return r.Value;
                default: return fallback;
            }
        }
    }
}
