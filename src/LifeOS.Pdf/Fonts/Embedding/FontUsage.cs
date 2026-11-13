using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts.Embedding
{
    /// <summary>
    /// Glyphs an embedded font uses across one document, and the Unicode text each
    /// glyph came from (for ToUnicode). The first code point seen for a glyph wins,
    /// which keeps output deterministic.
    /// </summary>
    internal sealed class FontUsage
    {
        public SortedDictionary<int, int> GlyphToCodePoint { get; } = new SortedDictionary<int, int>();

        /// <summary>Glyphs used (including .notdef, which has no single Unicode meaning).</summary>
        public SortedSet<int> Glyphs { get; } = new SortedSet<int>();

        public void Add(TextRun run)
        {
            for (var i = 0; i < run.Glyphs.Length; i++)
            {
                var gid = run.Glyphs[i];
                Glyphs.Add(gid);
                // .notdef stands for every missing character: giving it one Unicode value
                // would make copy-paste return a wrong letter (QA Fase 1), so it gets none.
                if (gid != 0 && !GlyphToCodePoint.ContainsKey(gid))
                {
                    GlyphToCodePoint[gid] = run.CodePoints[i];
                }
            }
        }
    }
}
