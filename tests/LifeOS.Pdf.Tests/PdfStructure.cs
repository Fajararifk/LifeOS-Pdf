using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LifeOS.Pdf.Internal;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>
    /// Independent structural checker for files we write: header, xref offsets,
    /// trailer, stream lengths and references. It deliberately does not reuse any
    /// writer code, so a bug in the writer cannot hide itself here.
    /// </summary>
    internal sealed class PdfStructure
    {
        private PdfStructure(string text, Dictionary<int, string> objects)
        {
            Text = text;
            Objects = objects;
        }

        /// <summary>The file as Latin-1 text (one char per byte).</summary>
        public string Text { get; }

        /// <summary>Object number → its source between "N 0 obj" and "endobj".</summary>
        public Dictionary<int, string> Objects { get; }

        public static PdfStructure Verify(byte[] pdf)
        {
            var text = Latin1(pdf);
            Assert.StartsWith("%PDF-1.7\n%", text);
            Assert.EndsWith("%%EOF\n", text);

            var startxrefMatch = Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$");
            Assert.True(startxrefMatch.Success, "startxref missing");
            var xrefOffset = int.Parse(startxrefMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.Equal("xref\n", text.Substring(xrefOffset, 5));

            var header = Regex.Match(text.Substring(xrefOffset), @"^xref\n0 (\d+)\n");
            Assert.True(header.Success, "xref subsection header missing");
            var count = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
            var entriesStart = xrefOffset + header.Length;

            Assert.Equal("0000000000 65535 f \n", text.Substring(entriesStart, 20));
            var objects = new Dictionary<int, string>();
            for (var n = 1; n < count; n++)
            {
                var entry = text.Substring(entriesStart + n * 20, 20);
                Assert.Matches(@"^\d{10} 00000 n \n$", entry);
                var offset = int.Parse(entry.Substring(0, 10), CultureInfo.InvariantCulture);
                var objHeader = n.ToString(CultureInfo.InvariantCulture) + " 0 obj\n";
                Assert.Equal(objHeader, text.Substring(offset, objHeader.Length));
                var end = text.IndexOf("\nendobj\n", offset, StringComparison.Ordinal);
                Assert.True(end > offset, "endobj missing for object " + n);
                objects[n] = text.Substring(offset + objHeader.Length, end - offset - objHeader.Length);
            }

            var trailerStart = entriesStart + count * 20;
            Assert.Equal("trailer\n", text.Substring(trailerStart, 8));
            var trailer = text.Substring(trailerStart);
            Assert.Contains("/Size " + count.ToString(CultureInfo.InvariantCulture), trailer);
            Assert.Matches(@"/Root \d+ 0 R", trailer);
            Assert.Matches(@"/ID \[<[0-9A-F]{32}> <[0-9A-F]{32}>\]", trailer);

            foreach (var obj in objects)
            {
                // Every reference must point at an object in the table.
                foreach (Match r in Regex.Matches(obj.Value, @"(\d+) 0 R"))
                {
                    var target = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
                    Assert.True(objects.ContainsKey(target), "Object " + obj.Key + " references missing object " + target);
                }

                // Stream /Length must equal the real data length.
                var streamIdx = obj.Value.IndexOf("\nstream\n", StringComparison.Ordinal);
                if (streamIdx >= 0)
                {
                    var length = int.Parse(Regex.Match(obj.Value, @"/Length (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                    var dataStart = streamIdx + "\nstream\n".Length;
                    var dataEnd = obj.Value.LastIndexOf("\nendstream", StringComparison.Ordinal);
                    Assert.Equal(length, dataEnd - dataStart);
                }
            }

            return new PdfStructure(text, objects);
        }

        /// <summary>Returns the decoded data of the stream in object <paramref name="number"/>.</summary>
        public byte[] StreamData(int number)
        {
            var src = Objects[number];
            var dataStart = src.IndexOf("\nstream\n", StringComparison.Ordinal) + "\nstream\n".Length;
            var dataEnd = src.LastIndexOf("\nendstream", StringComparison.Ordinal);
            var raw = new byte[dataEnd - dataStart];
            for (var i = 0; i < raw.Length; i++)
            {
                raw[i] = (byte)src[dataStart + i];
            }

            return src.Contains("/Filter /FlateDecode") ? Zlib.Decompress(raw) : raw;
        }

        /// <summary>Decoded content streams of every page, in page order.</summary>
        public List<string> PageContents()
        {
            var result = new List<string>();
            foreach (var obj in Objects)
            {
                var m = Regex.Match(obj.Value, @"^<< /Type /Page /Parent .* /Contents (\d+) 0 R( /Annots \[[^\]]*\])? >>$");
                if (m.Success)
                {
                    var data = StreamData(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                    result.Add(Encoding.ASCII.GetString(data));
                }
            }

            return result;
        }

        public int CountObjects(string fragment)
        {
            var n = 0;
            foreach (var obj in Objects.Values)
            {
                if (obj.Contains(fragment))
                {
                    n++;
                }
            }

            return n;
        }

        private static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            return new string(chars);
        }
    }
}
