using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Header or footer of a section, repeated on every page.</summary>
    public sealed class HeaderFooter : BlockContainer
    {
        internal HeaderFooter()
        {
        }

        public bool IsEmpty => BlockList.Count == 0;

        internal override bool AllowsPageBreaks => false;
    }
}
