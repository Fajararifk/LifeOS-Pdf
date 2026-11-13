using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Writes a file with object streams (§7.5.7) and a cross-reference stream (§7.5.8):
    /// every non-stream object is packed, 100 per object stream, and compressed — typically
    /// 20–40% smaller for documents with many small dictionaries (pages, fonts, annotations).
    /// Requires a PDF 1.5+ reader (all current viewers).
    /// </summary>
    internal static class CompactSerializer
    {
        private const int PerStream = 100;

        public static byte[] Serialize(List<PdfIndirectObject> objects, PdfIndirectObject catalog, PdfIndirectObject info, byte[] header)
        {
            var packed = new List<PdfIndirectObject>();
            var direct = new List<PdfIndirectObject>();
            foreach (var obj in objects)
            {
                if (obj.Value == null)
                {
                    throw new InvalidOperationException("Internal error: object " + obj.Number + " has no value.");
                }

                (obj.Value is PdfStream ? direct : packed).Add(obj);
            }

            // Location of each object: (stream number, index) for packed ones.
            var nextNumber = objects.Count + 1;
            var where = new Dictionary<int, (int Stream, int Index)>();
            var streams = new List<PdfIndirectObject>();
            for (var start = 0; start < packed.Count; start += PerStream)
            {
                var count = Math.Min(PerStream, packed.Count - start);
                var stream = new PdfIndirectObject(nextNumber++);
                using (var body = new MemoryStream())
                {
                    var bodyOut = new PdfOutput(body);
                    var offsets = new long[count];
                    for (var i = 0; i < count; i++)
                    {
                        var obj = packed[start + i];
                        offsets[i] = bodyOut.Position;
                        obj.Value!.WriteTo(bodyOut);
                        bodyOut.WriteByte((byte)'\n');
                        where[obj.Number] = (stream.Number, i);
                    }

                    bodyOut.Flush();
                    using (var all = new MemoryStream())
                    {
                        var headOut = new PdfOutput(all);
                        for (var i = 0; i < count; i++)
                        {
                            headOut.WriteInteger(packed[start + i].Number);
                            headOut.WriteByte((byte)' ');
                            headOut.WriteInteger(offsets[i]);
                            headOut.WriteByte((byte)(i == count - 1 ? '\n' : ' '));
                        }

                        var first = headOut.Position;
                        headOut.Write(body.ToArray());
                        headOut.Flush();
                        stream.Value = new PdfStream(
                            new PdfDictionary().Set("Type", "ObjStm").Set("N", count).Set("First", first).Set("Filter", "FlateDecode"),
                            Zlib.Compress(all.ToArray()));
                    }
                }

                streams.Add(stream);
            }

            using (var ms = new MemoryStream())
            {
                var output = new PdfOutput(ms);
                output.Write(header);
                var offsetOf = new Dictionary<int, long>();
                foreach (var obj in direct)
                {
                    WriteObject(output, obj, offsetOf);
                }

                foreach (var obj in streams)
                {
                    WriteObject(output, obj, offsetOf);
                }

                var xrefNumber = nextNumber;
                var size = xrefNumber + 1;
                var xrefOffset = output.Position;
                offsetOf[xrefNumber] = xrefOffset;

                // /W [1 4 2]: type, offset or stream number, generation or index.
                var table = new byte[size * 7];
                for (var n = 0; n < size; n++)
                {
                    var p = n * 7;
                    if (n == 0)
                    {
                        table[p] = 0;
                        table[p + 5] = 0xFF;
                        table[p + 6] = 0xFF;
                    }
                    else if (where.TryGetValue(n, out var loc))
                    {
                        table[p] = 2;
                        Put(table, p + 1, loc.Stream, 4);
                        Put(table, p + 5, loc.Index, 2);
                    }
                    else
                    {
                        table[p] = 1;
                        Put(table, p + 1, offsetOf[n], 4);
                    }
                }

                // File identifier derived from the content: same input → same ID.
                byte[] id;
                output.Flush();
                using (var md5 = MD5.Create())
                {
                    md5.TransformBlock(ms.GetBuffer(), 0, (int)ms.Length, null, 0);
                    md5.TransformFinalBlock(table, 0, table.Length);
                    id = md5.Hash!;
                }

                var dict = new PdfDictionary()
                    .Set("Type", "XRef")
                    .Set("Size", size)
                    .Set("W", new PdfArray(new PdfInteger(1), new PdfInteger(4), new PdfInteger(2)))
                    .Set("Root", catalog.Reference)
                    .Set("Info", info.Reference)
                    .Set("ID", new PdfArray(PdfString.Hex(id), PdfString.Hex(id)))
                    .Set("Filter", "FlateDecode");
                output.WriteInteger(xrefNumber);
                output.Write(" 0 obj\n");
                new PdfStream(dict, Zlib.Compress(table)).WriteTo(output);
                output.Write("\nendobj\nstartxref\n");
                output.WriteInteger(xrefOffset);
                output.Write("\n%%EOF\n");
                output.Flush();
                return ms.ToArray();
            }
        }

        private static void WriteObject(PdfOutput output, PdfIndirectObject obj, Dictionary<int, long> offsetOf)
        {
            offsetOf[obj.Number] = output.Position;
            output.WriteInteger(obj.Number);
            output.Write(" 0 obj\n");
            obj.Value!.WriteTo(output);
            output.Write("\nendobj\n");
        }

        private static void Put(byte[] table, int pos, long value, int width)
        {
            if (value < 0 || value >= 1L << (8 * width))
            {
                throw new InvalidOperationException("The file is too large for a compact cross-reference stream; save without UseObjectStreams.");
            }

            for (var i = width - 1; i >= 0; i--)
            {
                table[pos + i] = (byte)value;
                value >>= 8;
            }
        }
    }
}
