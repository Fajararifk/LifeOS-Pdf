namespace LifeOS.Pdf.Navigation
{
    /// <summary>How pages are arranged when the document opens (§7.7.2 /PageLayout).</summary>
    public enum PageLayout
    {
        /// <summary>Viewer default.</summary>
        Default,

        /// <summary>One page at a time.</summary>
        SinglePage,

        /// <summary>Continuous single column.</summary>
        OneColumn,

        /// <summary>Continuous two columns, odd pages on the left.</summary>
        TwoColumnLeft,

        /// <summary>Continuous two columns, odd pages on the right (book with a cover).</summary>
        TwoColumnRight,

        /// <summary>Two pages at a time, odd pages on the left.</summary>
        TwoPageLeft,

        /// <summary>Two pages at a time, odd pages on the right.</summary>
        TwoPageRight,
    }
}
