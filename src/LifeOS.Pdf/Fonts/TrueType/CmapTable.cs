using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts.TrueType
{
    /// <summary>
    /// Unicode → glyph ID lookup from the 'cmap' table. Supports formats 0, 4, 6 and 12,
    /// which cover practically every TrueType font. Lookups are done on the parsed
    /// structures (no giant dictionary), so a hostile format-12 group spanning the
    /// whole Unicode range costs nothing.
    /// </summary>
    internal sealed class CmapTable
    {
        private readonly Func<int, int> _lookup;

        private CmapTable(Func<int, int> lookup, bool symbol)
        {
            _lookup = lookup;
            IsSymbol = symbol;
        }

        /// <summary>True for (3,0) symbol fonts whose codes live at U+F000–U+F0FF.</summary>
        public bool IsSymbol { get; }

        /// <summary>Glyph ID for the code point, or 0 (.notdef) when the font has none.</summary>
        public int GlyphId(int codePoint)
        {
            if (codePoint < 0 || codePoint > 0x10FFFF)
            {
                return 0;
            }

            var gid = _lookup(codePoint);
            if (gid == 0 && IsSymbol && codePoint <= 0xFF)
            {
                // Symbol fonts map their byte codes into the private use area.
                gid = _lookup(0xF000 + codePoint);
            }

            return gid;
        }

        public static CmapTable Parse(byte[] d, long tableOffset, long tableLength)
        {
            BigEndian.Require(d, tableOffset, Math.Max(4, tableLength), "cmap table");
            var count = BigEndian.U16(d, tableOffset + 2);

            // Preference: full Unicode first, then BMP, then symbol.
            var candidates = new List<KeyValuePair<int, long>>();
            for (var i = 0; i < count; i++)
            {
                var rec = tableOffset + 4 + i * 8;
                var platform = BigEndian.U16(d, rec);
                var encoding = BigEndian.U16(d, rec + 2);
                var offset = BigEndian.U32(d, rec + 4);
                if (offset >= tableLength)
                {
                    continue;
                }

                int rank;
                if (platform == 3 && encoding == 10)
                {
                    rank = 0;
                }
                else if (platform == 0 && (encoding == 4 || encoding == 6))
                {
                    rank = 1;
                }
                else if (platform == 3 && encoding == 1)
                {
                    rank = 2;
                }
                else if (platform == 0 && encoding <= 3)
                {
                    rank = 3;
                }
                else if (platform == 3 && encoding == 0)
                {
                    rank = 4;
                }
                else
                {
                    continue;
                }

                candidates.Add(new KeyValuePair<int, long>(rank, tableOffset + offset));
            }

            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var candidate in candidates)
            {
                var lookup = ParseSubtable(d, candidate.Value, tableOffset + tableLength);
                if (lookup != null)
                {
                    return new CmapTable(lookup, candidate.Key == 4);
                }
            }

            throw new NotSupportedException("The font has no Unicode character map (cmap) that this library can read.");
        }

        private static Func<int, int>? ParseSubtable(byte[] d, long o, long tableEnd)
        {
            var format = BigEndian.U16(d, o);
            switch (format)
            {
                case 0:
                {
                    BigEndian.Require(d, o, 6 + 256, "cmap format 0");
                    var map = new byte[256];
                    Array.Copy(d, o + 6, map, 0, 256);
                    return cp => cp < 256 ? map[cp] : 0;
                }

                case 4:
                    return ParseFormat4(d, o);

                case 6:
                {
                    var first = BigEndian.U16(d, o + 6);
                    var entryCount = BigEndian.U16(d, o + 8);
                    BigEndian.Require(d, o + 10, entryCount * 2L, "cmap format 6");
                    var ids = new ushort[entryCount];
                    for (var i = 0; i < entryCount; i++)
                    {
                        ids[i] = BigEndian.U16(d, o + 10 + i * 2);
                    }

                    return cp => cp >= first && cp < first + entryCount ? ids[cp - first] : 0;
                }

                case 12:
                    return ParseFormat12(d, o);

                default:
                    return null; // e.g. format 2 (legacy CJK), 13, 14: try the next subtable
            }
        }

        private static Func<int, int> ParseFormat4(byte[] d, long o)
        {
            var length = BigEndian.U16(d, o + 2);
            BigEndian.Require(d, o, length, "cmap format 4");
            var segCount = BigEndian.U16(d, o + 6) / 2;
            var endCodes = o + 14;
            var startCodes = endCodes + segCount * 2 + 2;
            var deltas = startCodes + segCount * 2;
            var rangeOffsets = deltas + segCount * 2;
            BigEndian.Require(d, endCodes, segCount * 8L + 2, "cmap format 4 segments");

            var end = new ushort[segCount];
            var start = new ushort[segCount];
            var delta = new short[segCount];
            var range = new ushort[segCount];
            for (var i = 0; i < segCount; i++)
            {
                end[i] = BigEndian.U16(d, endCodes + i * 2);
                start[i] = BigEndian.U16(d, startCodes + i * 2);
                delta[i] = BigEndian.I16(d, deltas + i * 2);
                range[i] = BigEndian.U16(d, rangeOffsets + i * 2);
            }

            var tableEnd = o + length;
            return cp =>
            {
                if (cp > 0xFFFF)
                {
                    return 0;
                }

                // Segments are sorted by end code: binary search the first end >= cp.
                int lo = 0, hi = segCount - 1, seg = -1;
                while (lo <= hi)
                {
                    var mid = (lo + hi) / 2;
                    if (end[mid] >= cp)
                    {
                        seg = mid;
                        hi = mid - 1;
                    }
                    else
                    {
                        lo = mid + 1;
                    }
                }

                if (seg < 0 || start[seg] > cp)
                {
                    return 0;
                }

                if (range[seg] == 0)
                {
                    return (cp + delta[seg]) & 0xFFFF;
                }

                // idRangeOffset is relative to its own position in the array.
                var glyphAddress = rangeOffsets + seg * 2 + range[seg] + (cp - start[seg]) * 2L;
                if (glyphAddress + 2 > tableEnd || glyphAddress + 2 > d.Length)
                {
                    return 0;
                }

                var glyph = BigEndian.U16(d, glyphAddress);
                return glyph == 0 ? 0 : (glyph + delta[seg]) & 0xFFFF;
            };
        }

        private static Func<int, int> ParseFormat12(byte[] d, long o)
        {
            var groups = BigEndian.U32(d, o + 12);
            if (groups > 1_000_000)
            {
                throw new FormatException("Invalid font file: cmap format 12 has too many groups.");
            }

            BigEndian.Require(d, o + 16, groups * 12L, "cmap format 12 groups");
            var starts = new uint[groups];
            var ends = new uint[groups];
            var glyphs = new uint[groups];
            for (var i = 0; i < groups; i++)
            {
                var g = o + 16 + i * 12L;
                starts[i] = BigEndian.U32(d, g);
                ends[i] = BigEndian.U32(d, g + 4);
                glyphs[i] = BigEndian.U32(d, g + 8);
            }

            // The spec requires sorted groups; sort anyway (keeping the three arrays aligned)
            // so binary search is correct even for sloppy fonts.
            var order = new int[groups];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort((uint[])starts.Clone(), order);
            starts = Reorder(starts, order);
            ends = Reorder(ends, order);
            glyphs = Reorder(glyphs, order);

            return cp =>
            {
                var c = (uint)cp;
                int lo = 0, hi = starts.Length - 1;
                while (lo <= hi)
                {
                    var mid = (lo + hi) / 2;
                    if (c < starts[mid])
                    {
                        hi = mid - 1;
                    }
                    else if (c > ends[mid])
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        var gid = glyphs[mid] + (c - starts[mid]);
                        return gid > 0xFFFF ? 0 : (int)gid;
                    }
                }

                return 0;
            };
        }

        private static uint[] Reorder(uint[] values, int[] order)
        {
            var result = new uint[values.Length];
            for (var i = 0; i < order.Length; i++)
            {
                result[i] = values[order[i]];
            }

            return result;
        }
    }
}
