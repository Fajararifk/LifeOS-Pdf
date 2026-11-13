using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Fonts.Embedding
{
    /// <summary>
    /// Builds the ToUnicode CMap (ISO 32000-1 §9.10.3) that lets viewers copy, search
    /// and extract text drawn with an embedded font: 2-byte glyph code → UTF-16BE.
    /// </summary>
    internal static class ToUnicodeCMap
    {
        // bfchar blocks may hold at most 100 entries (Adobe CMap spec, Technical Note #5014).
        private const int MaxEntriesPerBlock = 100;

        public static byte[] Build(IDictionary<int, int> glyphToCodePoint)
        {
            var sb = new StringBuilder();
            sb.Append("/CIDInit /ProcSet findresource begin\n");
            sb.Append("12 dict begin\n");
            sb.Append("begincmap\n");
            sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
            sb.Append("/CMapName /Adobe-Identity-UCS def\n");
            sb.Append("/CMapType 2 def\n");
            sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

            var entries = new List<KeyValuePair<int, int>>(glyphToCodePoint);
            for (var start = 0; start < entries.Count; start += MaxEntriesPerBlock)
            {
                var count = System.Math.Min(MaxEntriesPerBlock, entries.Count - start);
                sb.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
                for (var i = start; i < start + count; i++)
                {
                    sb.Append('<').Append(entries[i].Key.ToString("X4", CultureInfo.InvariantCulture)).Append("> <");
                    sb.Append(Utf16Hex(entries[i].Value)).Append(">\n");
                }

                sb.Append("endbfchar\n");
            }

            sb.Append("endcmap\n");
            sb.Append("CMapName currentdict /CMap defineresource pop\n");
            sb.Append("end\nend\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static string Utf16Hex(int codePoint)
        {
            var s = char.ConvertFromUtf32(codePoint); // surrogate pair for > U+FFFF
            var sb = new StringBuilder(8);
            foreach (var c in s)
            {
                sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
