using System;
using System.Collections.Generic;
using System.Text;

namespace LifeOS.Pdf.Fonts.TrueType
{
    /// <summary>
    /// A parsed TrueType font (glyf outlines). Holds the original bytes and the
    /// locations of its tables; parsing validates every offset up front.
    /// Spec: OpenType 1.9 / Apple TrueType Reference Manual.
    /// </summary>
    internal sealed class TrueTypeFont
    {
        private const uint TrueTypeVersion = 0x00010000;
        private const uint AppleTrue = 0x74727565; // 'true'
        private const uint Ttcf = 0x74746366;      // 'ttcf'
        private const uint Otto = 0x4F54544F;      // 'OTTO' (CFF outlines)
        private const uint Woff = 0x774F4646;      // 'wOFF'
        private const uint Woff2 = 0x774F4632;     // 'wOF2'

        private ushort[] _advances = Array.Empty<ushort>();
        private uint[] _loca = Array.Empty<uint>();

        private TrueTypeFont(byte[] data)
        {
            Data = data;
        }

        public byte[] Data { get; }

        /// <summary>Table tag → (offset, length) within <see cref="Data"/>.</summary>
        public Dictionary<string, TableRecord> Tables { get; } = new Dictionary<string, TableRecord>(StringComparer.Ordinal);

        public int UnitsPerEm { get; private set; }

        public int GlyphCount { get; private set; }

        public short XMin { get; private set; }

        public short YMin { get; private set; }

        public short XMax { get; private set; }

        public short YMax { get; private set; }

        public short Ascender { get; private set; }

        public short Descender { get; private set; }

        public short CapHeight { get; private set; }

        public double ItalicAngle { get; private set; }

        public bool IsFixedPitch { get; private set; }

        public bool IsBold { get; private set; }

        public bool IsItalic { get; private set; }

        public int WeightClass { get; private set; } = 400;

        /// <summary>OS/2 fsType embedding permissions (0 = installable).</summary>
        public ushort FsType { get; private set; }

        public string PostScriptName { get; private set; } = string.Empty;

        public string FamilyName { get; private set; } = string.Empty;

        public CmapTable Cmap { get; private set; } = null!;

        /// <param name="data">Font file bytes (.ttf or .ttc).</param>
        /// <param name="collectionIndex">Font index inside a .ttc; 0 for a .ttf.</param>
        /// <param name="requireCmap">
        /// False to parse fonts without a 'cmap' table, such as our own subsets (CID-keyed
        /// TrueType in PDF addresses glyphs by ID and does not need one).
        /// </param>
        public static TrueTypeFont Parse(byte[] data, int collectionIndex, bool requireCmap = true)
        {
            if (data.Length < 12)
            {
                throw new FormatException("Invalid font file: too short.");
            }

            long offsetTable = 0;
            var tag = BigEndian.U32(data, 0);
            if (tag == Ttcf)
            {
                var numFonts = BigEndian.U32(data, 8);
                if (collectionIndex < 0 || collectionIndex >= numFonts)
                {
                    throw new ArgumentOutOfRangeException(nameof(collectionIndex), collectionIndex,
                        "The font collection contains " + numFonts + " font(s); use an index from 0 to " + (numFonts - 1) + ".");
                }

                offsetTable = BigEndian.U32(data, 12 + collectionIndex * 4L);
                tag = BigEndian.U32(data, offsetTable);
            }
            else if (collectionIndex != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(collectionIndex), collectionIndex, "Only .ttc collections have more than one font; use 0.");
            }

            if (tag == Otto)
            {
                throw new NotSupportedException("OpenType fonts with CFF (PostScript) outlines are not supported yet. Use a TrueType (.ttf) version of the font.");
            }

            if (tag == Woff || tag == Woff2)
            {
                throw new NotSupportedException("WOFF/WOFF2 web fonts are not supported. Convert the font to .ttf first.");
            }

            if (tag != TrueTypeVersion && tag != AppleTrue)
            {
                throw new FormatException("Not a TrueType font file.");
            }

            var font = new TrueTypeFont(data);
            font.ReadDirectory(offsetTable);
            font.ReadHead();
            font.ReadMaxp();
            font.ReadHhea();
            font.ReadLoca();
            font.ReadOs2();
            font.ReadPost();
            font.ReadName();
            if (requireCmap || font.HasTable("cmap"))
            {
                var cmap = font.Table("cmap");
                font.Cmap = CmapTable.Parse(data, cmap.Offset, cmap.Length);
            }

            return font;
        }

        public bool HasTable(string tag) => Tables.ContainsKey(tag);

        public TableRecord Table(string tag)
        {
            if (!Tables.TryGetValue(tag, out var record))
            {
                throw new FormatException("Invalid font file: required table '" + tag.Trim() + "' is missing.");
            }

            return record;
        }

        /// <summary>Advance width in font units.</summary>
        public int AdvanceWidth(int glyphId)
        {
            if (glyphId < 0 || glyphId >= GlyphCount || _advances.Length == 0)
            {
                return 0;
            }

            return glyphId < _advances.Length ? _advances[glyphId] : _advances[_advances.Length - 1];
        }

        /// <summary>Byte range of a glyph's outline inside the 'glyf' table (length 0 = empty glyph).</summary>
        public void GlyphRange(int glyphId, out long offset, out int length)
        {
            var glyf = Table("glyf");
            var start = _loca[glyphId];
            var end = _loca[glyphId + 1];
            offset = glyf.Offset + start;
            length = (int)(end - start);
        }

        /// <summary>Glyph IDs referenced by a composite glyph (empty for simple glyphs).</summary>
        public List<int> ComponentGlyphs(int glyphId)
        {
            var result = new List<int>();
            GlyphRange(glyphId, out var offset, out var length);
            if (length < 10 || BigEndian.I16(Data, offset) >= 0)
            {
                return result; // empty or simple glyph
            }

            const int ArgsAreWords = 0x0001, HaveScale = 0x0008, MoreComponents = 0x0020, HaveXYScale = 0x0040, HaveTwoByTwo = 0x0080;
            var end = offset + length;
            var p = offset + 10;
            int flags;
            do
            {
                if (p + 4 > end)
                {
                    throw new FormatException("Invalid font file: composite glyph " + glyphId + " is truncated.");
                }

                flags = BigEndian.U16(Data, p);
                result.Add(BigEndian.U16(Data, p + 2));
                p += 4;
                p += (flags & ArgsAreWords) != 0 ? 4 : 2;
                if ((flags & HaveScale) != 0)
                {
                    p += 2;
                }
                else if ((flags & HaveXYScale) != 0)
                {
                    p += 4;
                }
                else if ((flags & HaveTwoByTwo) != 0)
                {
                    p += 8;
                }
            }
            while ((flags & MoreComponents) != 0);

            return result;
        }

        private void ReadDirectory(long offsetTable)
        {
            var numTables = BigEndian.U16(Data, offsetTable + 4);
            if (numTables == 0 || numTables > 200)
            {
                throw new FormatException("Invalid font file: unexpected number of tables (" + numTables + ").");
            }

            for (var i = 0; i < numTables; i++)
            {
                var rec = offsetTable + 12 + i * 16L;
                var tag = BigEndian.Tag(Data, rec);
                var offset = BigEndian.U32(Data, rec + 8);
                var length = BigEndian.U32(Data, rec + 12);
                BigEndian.Require(Data, offset, length, "table '" + tag + "'");
                Tables[tag] = new TableRecord(offset, length);
            }
        }

        private void ReadHead()
        {
            var head = Table("head");
            BigEndian.Require(Data, head.Offset, 54, "head table");
            UnitsPerEm = BigEndian.U16(Data, head.Offset + 18);
            if (UnitsPerEm < 16 || UnitsPerEm > 16384)
            {
                throw new FormatException("Invalid font file: unitsPerEm " + UnitsPerEm + " is out of range.");
            }

            XMin = BigEndian.I16(Data, head.Offset + 36);
            YMin = BigEndian.I16(Data, head.Offset + 38);
            XMax = BigEndian.I16(Data, head.Offset + 40);
            YMax = BigEndian.I16(Data, head.Offset + 42);
            var macStyle = BigEndian.U16(Data, head.Offset + 44);
            IsBold = (macStyle & 1) != 0;
            IsItalic = (macStyle & 2) != 0;
        }

        private void ReadMaxp()
        {
            var maxp = Table("maxp");
            GlyphCount = BigEndian.U16(Data, maxp.Offset + 4);
            if (GlyphCount == 0)
            {
                throw new FormatException("Invalid font file: the font has no glyphs.");
            }
        }

        private void ReadHhea()
        {
            var hhea = Table("hhea");
            BigEndian.Require(Data, hhea.Offset, 36, "hhea table");
            Ascender = BigEndian.I16(Data, hhea.Offset + 4);
            Descender = BigEndian.I16(Data, hhea.Offset + 6);
            var numberOfHMetrics = BigEndian.U16(Data, hhea.Offset + 34);
            if (numberOfHMetrics == 0 || numberOfHMetrics > GlyphCount)
            {
                throw new FormatException("Invalid font file: hhea.numberOfHMetrics is " + numberOfHMetrics + ".");
            }

            var hmtx = Table("hmtx");
            if (hmtx.Length < numberOfHMetrics * 4L)
            {
                throw new FormatException("Invalid font file: hmtx table is too short.");
            }

            _advances = new ushort[numberOfHMetrics];
            for (var i = 0; i < numberOfHMetrics; i++)
            {
                _advances[i] = BigEndian.U16(Data, hmtx.Offset + i * 4L);
            }
        }

        private void ReadLoca()
        {
            var head = Table("head");
            var longFormat = BigEndian.I16(Data, head.Offset + 50) == 1;
            var loca = Table("loca");
            var glyf = Table("glyf");
            var entries = GlyphCount + 1;
            if (loca.Length < entries * (longFormat ? 4L : 2L))
            {
                throw new FormatException("Invalid font file: loca table is too short.");
            }

            _loca = new uint[entries];
            for (var i = 0; i < entries; i++)
            {
                var value = longFormat ? BigEndian.U32(Data, loca.Offset + i * 4L) : BigEndian.U16(Data, loca.Offset + i * 2L) * 2u;
                if (value > glyf.Length || (i > 0 && value < _loca[i - 1]))
                {
                    throw new FormatException("Invalid font file: loca offsets are out of order or outside the glyf table.");
                }

                _loca[i] = value;
            }
        }

        private void ReadOs2()
        {
            CapHeight = (short)(Ascender * 0.7);
            if (!Tables.TryGetValue("OS/2", out var os2) || os2.Length < 78)
            {
                return; // optional in old Mac fonts
            }

            var version = BigEndian.U16(Data, os2.Offset);
            WeightClass = BigEndian.U16(Data, os2.Offset + 4);
            FsType = BigEndian.U16(Data, os2.Offset + 8);
            var fsSelection = BigEndian.U16(Data, os2.Offset + 62);
            IsItalic |= (fsSelection & 0x0001) != 0;
            IsBold |= (fsSelection & 0x0020) != 0 || WeightClass >= 600;

            var typoAscender = BigEndian.I16(Data, os2.Offset + 68);
            var typoDescender = BigEndian.I16(Data, os2.Offset + 70);
            if (typoAscender > 0 && typoDescender < 0 && Ascender == 0)
            {
                Ascender = typoAscender;
                Descender = typoDescender;
            }

            if (version >= 2 && os2.Length >= 90)
            {
                var cap = BigEndian.I16(Data, os2.Offset + 88);
                if (cap > 0)
                {
                    CapHeight = cap;
                }
            }
        }

        private void ReadPost()
        {
            if (!Tables.TryGetValue("post", out var post) || post.Length < 16)
            {
                return;
            }

            var fixedAngle = unchecked((int)BigEndian.U32(Data, post.Offset + 4));
            ItalicAngle = fixedAngle / 65536.0;
            IsFixedPitch = BigEndian.U32(Data, post.Offset + 12) != 0;
        }

        private void ReadName()
        {
            if (Tables.TryGetValue("name", out var name) && name.Length >= 6)
            {
                var count = BigEndian.U16(Data, name.Offset + 2);
                var storage = name.Offset + BigEndian.U16(Data, name.Offset + 4);
                string? ps = null, family = null, typoFamily = null;
                for (var i = 0; i < count; i++)
                {
                    var rec = name.Offset + 6 + i * 12L;
                    if (rec + 12 > name.Offset + name.Length)
                    {
                        break;
                    }

                    var platform = BigEndian.U16(Data, rec);
                    var encoding = BigEndian.U16(Data, rec + 2);
                    var nameId = BigEndian.U16(Data, rec + 6);
                    var length = BigEndian.U16(Data, rec + 8);
                    var offset = storage + BigEndian.U16(Data, rec + 10);
                    if (nameId != 1 && nameId != 6 && nameId != 16)
                    {
                        continue;
                    }

                    if (offset + length > Data.Length)
                    {
                        continue;
                    }

                    string? value = null;
                    if (platform == 3 || platform == 0)
                    {
                        value = Encoding.BigEndianUnicode.GetString(Data, (int)offset, length & ~1);
                    }
                    else if (platform == 1 && encoding == 0)
                    {
                        value = Encoding.ASCII.GetString(Data, (int)offset, length);
                    }

                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    // Prefer the Windows (platform 3) record when several exist.
                    if (nameId == 6 && (ps == null || platform == 3))
                    {
                        ps = value;
                    }
                    else if (nameId == 1 && (family == null || platform == 3))
                    {
                        family = value;
                    }
                    else if (nameId == 16 && (typoFamily == null || platform == 3))
                    {
                        typoFamily = value;
                    }
                }

                FamilyName = typoFamily ?? family ?? string.Empty;
                PostScriptName = SanitizePostScriptName(ps ?? FamilyName);
            }

            if (PostScriptName.Length == 0)
            {
                PostScriptName = "EmbeddedFont";
            }
        }

        /// <summary>Removes characters that are not allowed in a PDF font name (no spaces, no delimiters).</summary>
        internal static string SanitizePostScriptName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (c > 0x20 && c < 0x7F && "()<>[]{}/%#".IndexOf(c) < 0)
                {
                    sb.Append(c);
                }
            }

            return sb.Length > 63 ? sb.ToString(0, 63) : sb.ToString();
        }
    }

    internal readonly struct TableRecord
    {
        public TableRecord(long offset, long length)
        {
            Offset = offset;
            Length = length;
        }

        public long Offset { get; }

        public long Length { get; }
    }
}
