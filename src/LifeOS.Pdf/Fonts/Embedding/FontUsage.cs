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

        public void Add(TextRun run)
        {
            for (var i = 0; i < run.Glyphs.Length; i++)
            {
                if (!GlyphToCodePoint.ContainsKey(run.Glyphs[i]))
                {
                    GlyphToCodePoint[run.Glyphs[i]] = run.CodePoints[i];
                }
            }
        }
    }
}
