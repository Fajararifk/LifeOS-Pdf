using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts.TrueType
{
    /// <summary>
    /// Pair kerning from the legacy 'kern' table (format 0) and from GPOS lookups of the
    /// 'kern' feature (PairPos format 1 and 2, also behind Extension lookups).
    /// Kerning is an enhancement: any malformed data disables kerning for the font
    /// instead of failing (requirement Fase 2 NFR-05).
    /// </summary>
    internal sealed class KerningTable
    {
        private const int MaxSubtables = 2000;
        private const int MaxPairs = 1_000_000;

        private readonly Dictionary<uint, short> _pairs = new Dictionary<uint, short>();
        private readonly List<ClassPairs> _classPairs = new List<ClassPairs>();

        private KerningTable()
        {
        }

        public static KerningTable Empty { get; } = new KerningTable();

        public bool IsEmpty => _pairs.Count == 0 && _classPairs.Count == 0;

        /// <summary>Adjustment in font units between two glyphs (negative = closer together).</summary>
        public int Get(int left, int right)
        {
            if (_pairs.TryGetValue(Key(left, right), out var value))
            {
                return value;
            }

            foreach (var cp in _classPairs)
            {
                var v = cp.Get(left, right);
                if (v != 0)
                {
                    return v;
                }
            }

            return 0;
        }

        public static KerningTable Load(TrueTypeFont font)
        {
            try
            {
                var table = new KerningTable();
                if (font.Tables.TryGetValue("GPOS", out var gpos))
                {
                    table.ReadGpos(font.Data, gpos.Offset, gpos.Offset + gpos.Length);
                }

                // The legacy table only when GPOS had no kerning (fonts often carry both).
                if (table.IsEmpty && font.Tables.TryGetValue("kern", out var kern))
                {
                    table.ReadKern(font.Data, kern.Offset, kern.Offset + kern.Length);
                }

                return table;
            }
            catch (FormatException)
            {
                return Empty;
            }
            catch (IndexOutOfRangeException)
            {
                return Empty;
            }
        }

        private static uint Key(int left, int right) => ((uint)left << 16) | (uint)(right & 0xFFFF);

        private void ReadKern(byte[] d, long start, long end)
        {
            var version = U16(d, start, end);
            if (version != 0)
            {
                return; // Apple's version 1 'kern' is not used by Windows/PDF tooling
            }

            var count = U16(d, start + 2, end);
            var pos = start + 4;
            for (var i = 0; i < count && i < MaxSubtables; i++)
            {
                var length = U16(d, pos + 2, end);
                var coverage = U16(d, pos + 4, end);
                var format = coverage >> 8;
                var horizontal = (coverage & 1) != 0;
                var minimum = (coverage & 2) != 0;
                var crossStream = (coverage & 4) != 0;
                if (format == 0 && horizontal && !minimum && !crossStream)
                {
                    var nPairs = U16(d, pos + 6, end);
                    for (var p = 0; p < nPairs && _pairs.Count < MaxPairs; p++)
                    {
                        var rec = pos + 14 + p * 6L;
                        var key = Key(U16(d, rec, end), U16(d, rec + 2, end));
                        if (!_pairs.ContainsKey(key))
                        {
                            _pairs[key] = unchecked((short)U16(d, rec + 4, end));
                        }
                    }
                }

                if (length < 6)
                {
                    break;
                }

                pos += length;
            }
        }

        private void ReadGpos(byte[] d, long start, long end)
        {
            var featureList = start + U16(d, start + 6, end);
            var lookupList = start + U16(d, start + 8, end);

            var lookups = new SortedSet<int>();
            var featureCount = U16(d, featureList, end);
            for (var i = 0; i < featureCount; i++)
            {
                var rec = featureList + 2 + i * 6L;
                if (Tag(d, rec, end) != "kern")
                {
                    continue;
                }

                var feature = featureList + U16(d, rec + 4, end);
                var indexCount = U16(d, feature + 2, end);
                for (var k = 0; k < indexCount; k++)
                {
                    lookups.Add(U16(d, feature + 4 + k * 2L, end));
                }
            }

            var lookupCount = U16(d, lookupList, end);
            var subtables = 0;
            foreach (var index in lookups)
            {
                if (index >= lookupCount)
                {
                    continue;
                }

                var lookup = lookupList + U16(d, lookupList + 2 + index * 2L, end);
                var type = U16(d, lookup, end);
                var subCount = U16(d, lookup + 4, end);
                for (var s = 0; s < subCount; s++)
                {
                    if (++subtables > MaxSubtables)
                    {
                        return;
                    }

                    var sub = lookup + U16(d, lookup + 6 + s * 2L, end);
                    var subType = type;
                    if (type == 9)
                    {
                        // Extension: format(2) + extensionLookupType(2) + offset32.
                        subType = U16(d, sub + 2, end);
                        sub += U32(d, sub + 4, end);
                    }

                    if (subType == 2)
                    {
                        ReadPairPos(d, sub, end);
                    }
                }
            }
        }

        private void ReadPairPos(byte[] d, long sub, long end)
        {
            var format = U16(d, sub, end);
            var coverage = ReadCoverage(d, sub + U16(d, sub + 2, end), end);
            var valueFormat1 = U16(d, sub + 4, end);
            var valueFormat2 = U16(d, sub + 6, end);
            var size1 = ValueRecordSize(valueFormat1);
            var size2 = ValueRecordSize(valueFormat2);
            var xAdvanceOffset = XAdvanceOffset(valueFormat1);
            if (xAdvanceOffset < 0)
            {
                return; // this subtable does not change advances
            }

            if (format == 1)
            {
                var pairSetCount = U16(d, sub + 8, end);
                foreach (var entry in coverage)
                {
                    if (entry.Value >= pairSetCount)
                    {
                        continue;
                    }

                    var pairSet = sub + U16(d, sub + 10 + entry.Value * 2L, end);
                    var pairCount = U16(d, pairSet, end);
                    var recordSize = 2 + size1 + size2;
                    for (var p = 0; p < pairCount && _pairs.Count < MaxPairs; p++)
                    {
                        var rec = pairSet + 2 + p * (long)recordSize;
                        var key = Key(entry.Key, U16(d, rec, end));
                        if (!_pairs.ContainsKey(key))
                        {
                            _pairs[key] = unchecked((short)U16(d, rec + 2 + xAdvanceOffset, end));
                        }
                    }
                }
            }
            else if (format == 2)
            {
                var classDef1 = ReadClassDef(d, sub + U16(d, sub + 8, end), end);
                var classDef2 = ReadClassDef(d, sub + U16(d, sub + 10, end), end);
                var class1Count = U16(d, sub + 12, end);
                var class2Count = U16(d, sub + 14, end);
                if ((long)class1Count * class2Count > MaxPairs)
                {
                    throw new FormatException("GPOS class matrix too large.");
                }

                var values = new short[class1Count * class2Count];
                var recordSize = size1 + size2;
                for (var c1 = 0; c1 < class1Count; c1++)
                {
                    for (var c2 = 0; c2 < class2Count; c2++)
                    {
                        var rec = sub + 16 + ((long)c1 * class2Count + c2) * recordSize;
                        values[c1 * class2Count + c2] = unchecked((short)U16(d, rec + xAdvanceOffset, end));
                    }
                }

                _classPairs.Add(new ClassPairs(new HashSet<int>(coverage.Keys), classDef1, classDef2, class2Count, values));
            }
        }

        /// <summary>Glyph → coverage index.</summary>
        private static Dictionary<int, int> ReadCoverage(byte[] d, long o, long end)
        {
            var result = new Dictionary<int, int>();
            var format = U16(d, o, end);
            var count = U16(d, o + 2, end);
            if (format == 1)
            {
                for (var i = 0; i < count; i++)
                {
                    result[U16(d, o + 4 + i * 2L, end)] = i;
                }
            }
            else if (format == 2)
            {
                for (var i = 0; i < count; i++)
                {
                    var rec = o + 4 + i * 6L;
                    int first = U16(d, rec, end), last = U16(d, rec + 2, end), startIndex = U16(d, rec + 4, end);
                    if (last < first || result.Count + (last - first) > MaxPairs)
                    {
                        throw new FormatException("Bad coverage range.");
                    }

                    for (var g = first; g <= last; g++)
                    {
                        result[g] = startIndex + g - first;
                    }
                }
            }

            return result;
        }

        private static Dictionary<int, int> ReadClassDef(byte[] d, long o, long end)
        {
            var result = new Dictionary<int, int>();
            var format = U16(d, o, end);
            if (format == 1)
            {
                var startGlyph = U16(d, o + 2, end);
                var count = U16(d, o + 4, end);
                for (var i = 0; i < count; i++)
                {
                    result[startGlyph + i] = U16(d, o + 6 + i * 2L, end);
                }
            }
            else if (format == 2)
            {
                var count = U16(d, o + 2, end);
                for (var i = 0; i < count; i++)
                {
                    var rec = o + 4 + i * 6L;
                    int first = U16(d, rec, end), last = U16(d, rec + 2, end), cls = U16(d, rec + 4, end);
                    if (last < first || result.Count + (last - first) > MaxPairs)
                    {
                        throw new FormatException("Bad class range.");
                    }

                    for (var g = first; g <= last; g++)
                    {
                        result[g] = cls;
                    }
                }
            }

            return result;
        }

        private static int ValueRecordSize(int valueFormat)
        {
            var bits = 0;
            for (var v = valueFormat & 0xFF; v != 0; v >>= 1)
            {
                bits += v & 1;
            }

            return bits * 2;
        }

        /// <summary>Byte offset of XAdvance (0x0004) inside a value record, or −1 if absent.</summary>
        private static int XAdvanceOffset(int valueFormat)
        {
            if ((valueFormat & 0x0004) == 0)
            {
                return -1;
            }

            return ((valueFormat & 0x0001) != 0 ? 2 : 0) + ((valueFormat & 0x0002) != 0 ? 2 : 0);
        }

        private static int U16(byte[] d, long o, long end)
        {
            if (o < 0 || o + 2 > end || o + 2 > d.Length)
            {
                throw new FormatException("Kerning data out of range.");
            }

            return (d[o] << 8) | d[o + 1];
        }

        private static long U32(byte[] d, long o, long end) => ((long)U16(d, o, end) << 16) | (uint)U16(d, o + 2, end);

        private static string Tag(byte[] d, long o, long end)
        {
            U16(d, o + 2, end); // bounds check for 4 bytes
            return new string(new[] { (char)d[o], (char)d[o + 1], (char)d[o + 2], (char)d[o + 3] });
        }

        private sealed class ClassPairs
        {
            private readonly HashSet<int> _coverage;
            private readonly Dictionary<int, int> _class1;
            private readonly Dictionary<int, int> _class2;
            private readonly int _class2Count;
            private readonly short[] _values;

            public ClassPairs(HashSet<int> coverage, Dictionary<int, int> class1, Dictionary<int, int> class2, int class2Count, short[] values)
            {
                _coverage = coverage;
                _class1 = class1;
                _class2 = class2;
                _class2Count = class2Count;
                _values = values;
            }

            public int Get(int left, int right)
            {
                if (!_coverage.Contains(left))
                {
                    return 0;
                }

                _class1.TryGetValue(left, out var c1); // missing = class 0
                _class2.TryGetValue(right, out var c2);
                var index = c1 * _class2Count + c2;
                return c2 < _class2Count && index < _values.Length ? _values[index] : 0;
            }
        }
    }
}
