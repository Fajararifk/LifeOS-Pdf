using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A part of the document with its own page setup, header and footer. Always starts on a new page.</summary>
    public sealed class Section : BlockContainer
    {
        internal Section(PageSetup setup)
        {
            PageSetup = setup;
        }

        public PageSetup PageSetup { get; }

        public HeaderFooter Header { get; } = new HeaderFooter();

        public HeaderFooter Footer { get; } = new HeaderFooter();

        /// <summary>Used on the section's first page when <see cref="PageSetup.DifferentFirstPage"/> is true.</summary>
        public HeaderFooter FirstPageHeader { get; } = new HeaderFooter();

        public HeaderFooter FirstPageFooter { get; } = new HeaderFooter();
    }
}
