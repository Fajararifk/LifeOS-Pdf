using System;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// A table of contents: the section headings with dot leaders and page numbers.
    /// Entries are links to the headings. Page numbers are found by laying the document
    /// out again until they no longer change.
    /// </summary>
    public sealed class TableOfContentsBlock : Block
    {
        internal TableOfContentsBlock(string? title, int maxLevel)
        {
            if (maxLevel < 1 || maxLevel > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLevel), maxLevel, "The table of contents level must be 1, 2 or 3.");
            }

            Title = title;
            MaxLevel = maxLevel;
        }

        /// <summary>Title above the entries, or null for none.</summary>
        public string? Title { get; set; }

        /// <summary>Deepest heading level listed (1–3).</summary>
        public int MaxLevel { get; }

        /// <summary>Indent per heading level in points.</summary>
        public double LevelIndent { get; set; } = 14;

        /// <summary>Show dots between an entry and its page number.</summary>
        public bool DotLeaders { get; set; } = true;
    }
}
