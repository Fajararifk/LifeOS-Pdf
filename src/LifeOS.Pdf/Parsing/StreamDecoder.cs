using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>
    /// Decodes stream data (ISO 32000-1 §7.4): Flate and LZW with predictors, ASCII85,
    /// ASCIIHex and RunLength. Every decoder stops at <c>limit</c> bytes so a small stream
    /// cannot expand into gigabytes. Image codecs (DCT, JPX, CCITT, JBIG2) are not decoded.
    /// </summary>
    internal static class StreamDecoder
    {
        public static byte[] Decode(PdfStream stream, PdfParser? parser, long limit)
        {
            var filters = new List<string>();
            var parms = new List<PdfDictionary?>();
            var filter = Resolve(stream.Dictionary.Get("Filter"), parser);
            var decodeParms = Resolve(stream.Dictionary.Get("DecodeParms"), parser);
            if (filter is PdfName single)
            {
                filters.Add(single.Value);
                parms.Add(Resolve(decodeParms is PdfArray pa && pa.Items.Count > 0 ? pa.Items[0] : decodeParms, parser) as PdfDictionary);
            }
            else if (filter is PdfArray array)
            {
                if (array.Items.Count > 16)
                {
                    throw new PdfReadException("A stream has " + array.Items.Count + " filters; at most 16 are supported.");
                }

                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (Resolve(array.Items[i], parser) is PdfName n)
                    {
                        filters.Add(n.Value);
                        var p = decodeParms is PdfArray dp && i < dp.Items.Count ? Resolve(dp.Items[i], parser) : null;
                        parms.Add(p as PdfDictionary);
                    }
                }
            }

            var data = stream.Data;
            for (var i = 0; i < filters.Count; i++)
            {
                data = Apply(filters[i], data, parms[i], parser, limit);
            }

            if (data.Length > limit)
            {
                throw TooLarge(limit);
            }

            return data;
        }

        public static bool CanDecode(PdfStream stream, PdfParser? parser)
        {
            var filter = Resolve(stream.Dictionary.Get("Filter"), parser);
            if (filter == null)
            {
                return true;
            }

            var names = filter is PdfArray a ? a.Items : new List<PdfObject> { filter };
            foreach (var item in names)
            {
                if (!(Resolve(item, parser) is PdfName n) || !IsSupported(n.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupported(string name) =>
            name == "FlateDecode" || name == "Fl" || name == "LZWDecode" || name == "LZW" || name == "ASCII85Decode" || name == "A85" ||
            name == "ASCIIHexDecode" || name == "AHx" || name == "RunLengthDecode" || name == "RL";

        private static PdfObject? Resolve(PdfObject? obj, PdfParser? parser) =>
            obj is PdfSourceReference r && parser != null ? parser.Resolve(r) : obj;

        private static byte[] Apply(string filter, byte[] data, PdfDictionary? parms, PdfParser? parser, long limit)
        {
            switch (filter)
            {
                case "FlateDecode":
                case "Fl":
                    return Predict(Inflate(data, limit), parms, parser, limit);
                case "LZWDecode":
                case "LZW":
                    var early = parms != null && Resolve(parms.Get("EarlyChange"), parser) is PdfInteger e ? (int)e.Value : 1;
                    return Predict(Lzw(data, early != 0, limit), parms, parser, limit);
                case "ASCII85Decode":
                case "A85":
                    return Ascii85(data);
                case "ASCIIHexDecode":
                case "AHx":
                    return AsciiHex(data);
                case "RunLengthDecode":
                case "RL":
                    return RunLength(data, limit);
                case "Crypt":
                    throw new PdfEncryptedException();
                default:
                    throw new PdfReadException("Filter /" + filter + " cannot be decoded by LifeOS.Pdf (image codecs are copied as they are).");
            }
        }

        private static PdfLimitException TooLarge(long limit) =>
            new PdfLimitException("A stream decompresses to more than " + limit + " bytes (PdfReadOptions.MaxStreamSize). The file may be a decompression bomb; raise the limit only for trusted files.");

        /// <summary>
        /// zlib inflate that tolerates a missing or broken header and a truncated end
        /// (common in real files): whatever decoded cleanly is returned.
        /// </summary>
        internal static byte[] Inflate(byte[] data, long limit)
        {
            var skip = data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0 ? 2 : 0;
            using (var output = new MemoryStream())
            {
                var buffer = new byte[16384];
                try
                {
                    using (var input = new MemoryStream(data, skip, data.Length - skip))
                    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                    {
                        int read;
                        while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + read > limit)
                            {
                                throw TooLarge(limit);
                            }

                            output.Write(buffer, 0, read);
                        }
                    }
                }
                catch (InvalidDataException) when (output.Length > 0)
                {
                    // Damaged tail: keep what was decoded.
                }
                catch (InvalidDataException ex)
                {
                    throw new PdfReadException("A Flate-compressed stream is damaged: " + ex.Message, ex);
                }

                return output.ToArray();
            }
        }

        private static byte[] Predict(byte[] data, PdfDictionary? parms, PdfParser? parser, long limit)
        {
            if (parms == null)
            {
                return data;
            }

            int Int(string key, int fallback) => Resolve(parms.Get(key), parser) is PdfInteger i ? (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, i.Value)) : fallback;
            var predictor = Int("Predictor", 1);
            if (predictor <= 1)
            {
                return data;
            }

            var colors = Int("Colors", 1);
            var bpc = Int("BitsPerComponent", 8);
            var columns = Int("Columns", 1);
            if (colors < 1 || colors > 32 || (bpc != 1 && bpc != 2 && bpc != 4 && bpc != 8 && bpc != 16) || columns < 1 || columns > 1 << 24)
            {
                throw new PdfReadException("Invalid predictor parameters (Colors " + colors + ", BitsPerComponent " + bpc + ", Columns " + columns + ").");
            }

            var bpp = Math.Max(1, colors * bpc / 8);
            var rowLength = (int)(((long)colors * bpc * columns + 7) / 8);
            if (predictor == 2)
            {
                return Tiff(data, rowLength, bpp, bpc);
            }

            using (var output = new MemoryStream())
            {
                var prior = new byte[rowLength];
                var row = new byte[rowLength];
                var pos = 0;
                while (pos < data.Length)
                {
                    var type = data[pos++];
                    var n = Math.Min(rowLength, data.Length - pos);
                    Array.Clear(row, 0, rowLength);
                    Buffer.BlockCopy(data, pos, row, 0, n);
                    pos += n;
                    for (var i = 0; i < rowLength; i++)
                    {
                        var left = i >= bpp ? row[i - bpp] : 0;
                        var up = prior[i];
                        var upLeft = i >= bpp ? prior[i - bpp] : 0;
                        switch (type)
                        {
                            case 1: row[i] = (byte)(row[i] + left); break;
                            case 2: row[i] = (byte)(row[i] + up); break;
                            case 3: row[i] = (byte)(row[i] + ((left + up) >> 1)); break;
                            case 4: row[i] = (byte)(row[i] + Paeth(left, up, upLeft)); break;
                        }
                    }

                    if (output.Length + n > limit)
                    {
                        throw TooLarge(limit);
                    }

                    output.Write(row, 0, n);
                    var t = prior;
                    prior = row;
                    row = t;
                }

                return output.ToArray();
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static byte[] Tiff(byte[] data, int rowLength, int bpp, int bpc)
        {
            if (bpc != 8)
            {
                return data; // only the common 8-bit case; other depths are left as they are
            }

            var output = (byte[])data.Clone();
            for (var rowStart = 0; rowStart < output.Length; rowStart += rowLength)
            {
                var end = Math.Min(output.Length, rowStart + rowLength);
                for (var i = rowStart + bpp; i < end; i++)
                {
                    output[i] = (byte)(output[i] + output[i - bpp]);
                }
            }

            return output;
        }

        private static byte[] Lzw(byte[] data, bool earlyChange, long limit)
        {
            using (var output = new MemoryStream())
            {
                var table = new List<byte[]>(4096);
                void Reset()
                {
                    table.Clear();
                    for (var i = 0; i < 256; i++)
                    {
                        table.Add(new[] { (byte)i });
                    }

                    table.Add(Array.Empty<byte>()); // 256 clear
                    table.Add(Array.Empty<byte>()); // 257 end
                }

                Reset();
                var codeLength = 9;
                long bitPos = 0;
                long totalBits = (long)data.Length * 8;
                byte[]? previous = null;
                while (bitPos + codeLength <= totalBits)
                {
                    var code = 0;
                    for (var i = 0; i < codeLength; i++, bitPos++)
                    {
                        code = (code << 1) | ((data[bitPos >> 3] >> (7 - (int)(bitPos & 7))) & 1);
                    }

                    if (code == 256)
                    {
                        Reset();
                        codeLength = 9;
                        previous = null;
                        continue;
                    }

                    if (code == 257)
                    {
                        break;
                    }

                    byte[] entry;
                    if (code < table.Count)
                    {
                        entry = table[code];
                        if (previous != null && table.Count < 4096)
                        {
                            table.Add(Append(previous, entry[0]));
                        }
                    }
                    else if (code == table.Count && previous != null && table.Count < 4096)
                    {
                        entry = Append(previous, previous[0]);
                        table.Add(entry);
                    }
                    else
                    {
                        break; // invalid code: stop, keep what was decoded
                    }

                    if (output.Length + entry.Length > limit)
                    {
                        throw TooLarge(limit);
                    }

                    output.Write(entry, 0, entry.Length);
                    previous = entry;
                    // §7.4.4.2: the code length grows one code early when EarlyChange is 1.
                    var next = table.Count + (earlyChange ? 1 : 0);
                    codeLength = next >= 2048 ? 12 : next >= 1024 ? 11 : next >= 512 ? 10 : 9;
                }

                return output.ToArray();
            }
        }

        private static byte[] Append(byte[] a, byte b)
        {
            var r = new byte[a.Length + 1];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            r[a.Length] = b;
            return r;
        }

        private static byte[] Ascii85(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                var group = new int[5];
                var n = 0;
                for (var i = 0; i < data.Length; i++)
                {
                    var c = data[i];
                    if (c == '~')
                    {
                        break;
                    }

                    if (Lexer.IsWhitespace(c))
                    {
                        continue;
                    }

                    if (c == 'z' && n == 0)
                    {
                        output.Write(new byte[4], 0, 4);
                        continue;
                    }

                    if (c < '!' || c > 'u')
                    {
                        throw new PdfReadException("Invalid character in ASCII85 data.");
                    }

                    group[n++] = c - '!';
                    if (n == 5)
                    {
                        WriteGroup(output, group, 4);
                        n = 0;
                    }
                }

                if (n > 1)
                {
                    for (var i = n; i < 5; i++)
                    {
                        group[i] = 84;
                    }

                    WriteGroup(output, group, n - 1);
                }

                return output.ToArray();
            }
        }

        private static void WriteGroup(MemoryStream output, int[] g, int count)
        {
            long value = 0;
            for (var i = 0; i < 5; i++)
            {
                value = value * 85 + g[i];
            }

            for (var i = 0; i < count; i++)
            {
                output.WriteByte((byte)(value >> (24 - 8 * i)));
            }
        }

        private static byte[] AsciiHex(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                var high = -1;
                foreach (var b in data)
                {
                    if (b == '>')
                    {
                        break;
                    }

                    var v = Lexer.Hex(b);
                    if (v < 0)
                    {
                        continue;
                    }

                    if (high < 0)
                    {
                        high = v;
                    }
                    else
                    {
                        output.WriteByte((byte)((high << 4) | v));
                        high = -1;
                    }
                }

                if (high >= 0)
                {
                    output.WriteByte((byte)(high << 4));
                }

                return output.ToArray();
            }
        }

        private static byte[] RunLength(byte[] data, long limit)
        {
            using (var output = new MemoryStream())
            {
                var i = 0;
                while (i < data.Length)
                {
                    int length = data[i++];
                    if (length == 128)
                    {
                        break;
                    }

                    var count = length < 128 ? length + 1 : 257 - length;
                    if (output.Length + count > limit)
                    {
                        throw TooLarge(limit);
                    }

                    if (length < 128)
                    {
                        var n = Math.Min(count, data.Length - i);
                        output.Write(data, i, n);
                        i += n;
                    }
                    else if (i < data.Length)
                    {
                        var b = data[i++];
                        for (var k = 0; k < count; k++)
                        {
                            output.WriteByte(b);
                        }
                    }
                }

                return output.ToArray();
            }
        }
    }
}
