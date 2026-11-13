using System;
using System.Collections.Generic;
using System.IO;

namespace LifeOS.Pdf.Fonts.TrueType
{
    /// <summary>
    /// Builds a standalone TrueType file that contains only the glyphs a document uses.
    /// Glyph IDs are kept (unused glyphs become empty), so Identity-H content and
    /// /CIDToGIDMap /Identity stay valid without rewriting any text.
    /// </summary>
    internal static class TrueTypeSubsetter
    {
        private const int MaxCompositeDepth = 16;

        // Tables a CIDFontType2 needs (ISO 32000-1 §9.9). GSUB/GPOS/kern are not used by
        // PDF viewers for CID-keyed TrueType and are dropped.
        private static readonly string[] CopiedTables = { "cvt ", "fpgm", "prep", "OS/2", "name", "hhea", "maxp" };

        /// <param name="font">The parsed source font.</param>
        /// <param name="usedGlyphs">Glyphs to keep; null keeps every glyph (full embedding).</param>
        /// <param name="glyphToCodePoint">
        /// Characters the document uses, for a small 'cmap'. PDF itself addresses glyphs by ID,
        /// but the OpenType spec requires a cmap and strict consumers (e.g. OTS in browsers)
        /// reject fonts without one.
        /// </param>
        public static byte[] Subset(TrueTypeFont font, ICollection<int>? usedGlyphs, IDictionary<int, int>? glyphToCodePoint = null)
        {
            var keep = new bool[font.GlyphCount];
            if (usedGlyphs == null)
            {
                for (var i = 0; i < keep.Length; i++)
                {
                    keep[i] = true;
                }
            }
            else
            {
                var state = new byte[font.GlyphCount];
                Include(font, 0, keep, state, 0); // .notdef is always required
                foreach (var gid in usedGlyphs)
                {
                    Include(font, gid, keep, state, 0);
                }
            }

            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            BuildGlyf(font, keep, out var glyf, out var loca, out var shortLoca);
            tables["glyf"] = glyf;
            tables["loca"] = loca;
            tables["hmtx"] = BuildHmtx(font, keep);
            tables["post"] = BuildPost(font);

            var head = Copy(font, "head");
            BigEndian.Write32(head, 8, 0); // checkSumAdjustment, recomputed below
            BigEndian.Write16(head, 50, shortLoca ? 0 : 1);
            tables["head"] = head;

            foreach (var tag in CopiedTables)
            {
                if (font.HasTable(tag))
                {
                    tables[tag] = Copy(font, tag);
                }
            }

            tables["cmap"] = usedGlyphs == null && font.HasTable("cmap")
                ? Copy(font, "cmap")
                : BuildCmap(glyphToCodePoint ?? new Dictionary<int, int>(), keep);

            return WriteFont(tables);
        }

        /// <summary>
        /// cmap with a (3,1) format-4 subtable for BMP characters and, when needed, a (3,10)
        /// format-12 subtable for characters above U+FFFF. Built from scratch (OpenType 1.9 'cmap').
        /// </summary>
        internal static byte[] BuildCmap(IDictionary<int, int> glyphToCodePoint, bool[] keep)
        {
            var map = new SortedDictionary<int, int>(); // code point → glyph
            foreach (var pair in glyphToCodePoint)
            {
                if (pair.Key > 0 && pair.Key < keep.Length && keep[pair.Key] && pair.Value > 0 && pair.Value <= 0x10FFFF && !map.ContainsKey(pair.Value))
                {
                    map[pair.Value] = pair.Key;
                }
            }

            // Runs of consecutive code points mapping to consecutive glyphs.
            var runs = new List<int[]>(); // start, end, startGlyph
            foreach (var pair in map)
            {
                var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && pair.Key == last[1] + 1 && pair.Value == last[2] + (pair.Key - last[0]))
                {
                    last[1] = pair.Key;
                }
                else
                {
                    runs.Add(new[] { pair.Key, pair.Key, pair.Value });
                }
            }

            // Copies, so clamping to the BMP does not alter the runs used for format 12.
            var bmpRuns = runs.FindAll(r => r[0] < 0xFFFF).ConvertAll(r => new[] { r[0], Math.Min(r[1], 0xFFFE), r[2] });

            var format4 = BuildFormat4(bmpRuns);
            var needs12 = runs.Exists(r => r[1] > 0xFFFF);
            var format12 = needs12 ? BuildFormat12(runs) : null;

            var headerSize = 4 + 8 * (needs12 ? 2 : 1);
            var cmap = new byte[headerSize + format4.Length + (format12?.Length ?? 0)];
            BigEndian.Write16(cmap, 0, 0);
            BigEndian.Write16(cmap, 2, needs12 ? 2 : 1);
            BigEndian.Write16(cmap, 4, 3);
            BigEndian.Write16(cmap, 6, 1);
            BigEndian.Write32(cmap, 8, (uint)headerSize);
            if (format12 != null)
            {
                BigEndian.Write16(cmap, 12, 3);
                BigEndian.Write16(cmap, 14, 10);
                BigEndian.Write32(cmap, 16, (uint)(headerSize + format4.Length));
                Array.Copy(format12, 0, cmap, headerSize + format4.Length, format12.Length);
            }

            Array.Copy(format4, 0, cmap, headerSize, format4.Length);
            return cmap;
        }

        private static byte[] BuildFormat4(List<int[]> runs)
        {
            // The spec requires a final segment 0xFFFF → .notdef.
            var segments = new List<int[]>(runs) { new[] { 0xFFFF, 0xFFFF, 0 } };
            var segCount = segments.Count;
            var length = 16 + segCount * 8;
            var t = new byte[length];
            var entrySelector = 0;
            while (1 << (entrySelector + 1) <= segCount)
            {
                entrySelector++;
            }

            var searchRange = 2 * (1 << entrySelector);
            BigEndian.Write16(t, 0, 4);
            BigEndian.Write16(t, 2, length);
            BigEndian.Write16(t, 4, 0);
            BigEndian.Write16(t, 6, segCount * 2);
            BigEndian.Write16(t, 8, searchRange);
            BigEndian.Write16(t, 10, entrySelector);
            BigEndian.Write16(t, 12, segCount * 2 - searchRange);
            var ends = 14;
            var starts = ends + segCount * 2 + 2; // +2: reservedPad
            var deltas = starts + segCount * 2;
            for (var i = 0; i < segCount; i++)
            {
                var s = segments[i];
                BigEndian.Write16(t, ends + i * 2, s[1]);
                BigEndian.Write16(t, starts + i * 2, s[0]);
                // idDelta: glyph = (code + delta) mod 65536. The last segment maps 0xFFFF → 0.
                var delta = i == segCount - 1 ? 1 : (s[2] - s[0]) & 0xFFFF;
                BigEndian.Write16(t, deltas + i * 2, delta);
                // idRangeOffset stays 0 for every segment.
            }

            return t;
        }

        private static byte[] BuildFormat12(List<int[]> runs)
        {
            var t = new byte[16 + runs.Count * 12];
            BigEndian.Write16(t, 0, 12);
            BigEndian.Write32(t, 4, (uint)t.Length);
            BigEndian.Write32(t, 12, (uint)runs.Count);
            for (var i = 0; i < runs.Count; i++)
            {
                BigEndian.Write32(t, 16 + i * 12, (uint)runs[i][0]);
                BigEndian.Write32(t, 20 + i * 12, (uint)runs[i][1]);
                BigEndian.Write32(t, 24 + i * 12, (uint)runs[i][2]);
            }

            return t;
        }

        /// <summary>
        /// Depth-first include of a glyph and its composite components.
        /// state: 0 = unseen, 1 = on the current path, 2 = done. A glyph met again while
        /// on the path is a cycle; recursion depth is capped, so a hostile font can
        /// neither loop forever nor overflow the stack, and each glyph is expanded once.
        /// </summary>
        private static void Include(TrueTypeFont font, int gid, bool[] keep, byte[] state, int level)
        {
            if (gid < 0 || gid >= keep.Length || state[gid] == 2)
            {
                return;
            }

            if (state[gid] == 1 || level > MaxCompositeDepth)
            {
                throw new FormatException("Invalid font file: composite glyph " + gid + " is cyclic or nested too deeply.");
            }

            state[gid] = 1;
            keep[gid] = true;
            foreach (var component in font.ComponentGlyphs(gid))
            {
                Include(font, component, keep, state, level + 1);
            }

            state[gid] = 2;
        }

        private static void BuildGlyf(TrueTypeFont font, bool[] keep, out byte[] glyf, out byte[] loca, out bool shortLoca)
        {
            var offsets = new uint[keep.Length + 1];
            using (var ms = new MemoryStream())
            {
                for (var gid = 0; gid < keep.Length; gid++)
                {
                    offsets[gid] = (uint)ms.Length;
                    if (!keep[gid])
                    {
                        continue;
                    }

                    font.GlyphRange(gid, out var offset, out var length);
                    if (length > 0)
                    {
                        ms.Write(font.Data, (int)offset, length);
                        // Keep every glyph 4-byte aligned (also makes short loca possible).
                        while (ms.Length % 4 != 0)
                        {
                            ms.WriteByte(0);
                        }
                    }
                }

                offsets[keep.Length] = (uint)ms.Length;
                glyf = ms.ToArray();
            }

            shortLoca = glyf.Length / 2 <= ushort.MaxValue;
            loca = new byte[offsets.Length * (shortLoca ? 2 : 4)];
            for (var i = 0; i < offsets.Length; i++)
            {
                if (shortLoca)
                {
                    BigEndian.Write16(loca, i * 2, (int)(offsets[i] / 2));
                }
                else
                {
                    BigEndian.Write32(loca, i * 4, offsets[i]);
                }
            }
        }

        private static byte[] BuildHmtx(TrueTypeFont font, bool[] keep)
        {
            var hmtx = Copy(font, "hmtx");
            var numberOfHMetrics = BigEndian.U16(font.Data, font.Table("hhea").Offset + 34);
            // Zero the metrics of dropped glyphs: smaller after compression, same layout.
            for (var gid = 0; gid < keep.Length; gid++)
            {
                if (keep[gid])
                {
                    continue;
                }

                var pos = gid < numberOfHMetrics ? gid * 4 : numberOfHMetrics * 4 + (gid - numberOfHMetrics) * 2;
                var size = gid < numberOfHMetrics ? 4 : 2;
                if (pos + size <= hmtx.Length)
                {
                    Array.Clear(hmtx, pos, size);
                }
            }

            return hmtx;
        }

        private static byte[] BuildPost(TrueTypeFont font)
        {
            // Version 3.0: same header, no glyph names (PDF does not need them).
            var post = new byte[32];
            if (font.HasTable("post") && font.Table("post").Length >= 32)
            {
                Array.Copy(font.Data, font.Table("post").Offset, post, 0, 32);
            }

            BigEndian.Write32(post, 0, 0x00030000);
            return post;
        }

        private static byte[] Copy(TrueTypeFont font, string tag)
        {
            var record = font.Table(tag);
            var bytes = new byte[record.Length];
            Array.Copy(font.Data, record.Offset, bytes, 0, record.Length);
            return bytes;
        }

        private static byte[] WriteFont(SortedDictionary<string, byte[]> tables)
        {
            var numTables = tables.Count;
            var entrySelector = 0;
            while (1 << (entrySelector + 1) <= numTables)
            {
                entrySelector++;
            }

            var searchRange = (1 << entrySelector) * 16;
            var headerSize = 12 + 16 * numTables;
            var total = headerSize;
            foreach (var t in tables.Values)
            {
                total += Pad4(t.Length);
            }

            var output = new byte[total];
            BigEndian.Write32(output, 0, 0x00010000);
            BigEndian.Write16(output, 4, numTables);
            BigEndian.Write16(output, 6, searchRange);
            BigEndian.Write16(output, 8, entrySelector);
            BigEndian.Write16(output, 10, numTables * 16 - searchRange);

            var record = 12;
            var offset = headerSize;
            var headOffset = -1;
            foreach (var table in tables)
            {
                for (var i = 0; i < 4; i++)
                {
                    output[record + i] = (byte)table.Key[i];
                }

                BigEndian.Write32(output, record + 4, Checksum(table.Value, 0, table.Value.Length));
                BigEndian.Write32(output, record + 8, (uint)offset);
                BigEndian.Write32(output, record + 12, (uint)table.Value.Length);
                Array.Copy(table.Value, 0, output, offset, table.Value.Length);
                if (table.Key == "head")
                {
                    headOffset = offset;
                }

                record += 16;
                offset += Pad4(table.Value.Length);
            }

            // head.checkSumAdjustment makes the whole file sum to 0xB1B0AFBA.
            var adjustment = unchecked(0xB1B0AFBAu - Checksum(output, 0, output.Length));
            BigEndian.Write32(output, headOffset + 8, adjustment);
            return output;
        }

        internal static uint Checksum(byte[] data, int offset, int length)
        {
            uint sum = 0;
            var end = offset + length;
            for (var i = offset; i < end; i += 4)
            {
                uint word = 0;
                for (var j = 0; j < 4; j++)
                {
                    word = (word << 8) | (i + j < end ? data[i + j] : (byte)0);
                }

                sum = unchecked(sum + word);
            }

            return sum;
        }

        private static int Pad4(int n) => (n + 3) & ~3;
    }
}
