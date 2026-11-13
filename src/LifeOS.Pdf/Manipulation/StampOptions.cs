using System;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>Options for page numbers and Bates numbers.</summary>
    public sealed class StampOptions
    {
        private double _margin = 28;

        public StampPosition Position { get; set; } = StampPosition.BottomCenter;

        /// <summary>Distance from the page edges in points (default 28 ≈ 1 cm).</summary>
        public double Margin
        {
            get => _margin;
            set => _margin = value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(Margin), value, "Margin must be zero or positive.");
        }

        /// <summary>Font (default Helvetica 9 pt).</summary>
        public PdfFont Font { get; set; } = new PdfFont(StandardFont.Helvetica, 9);

        public PdfColor Color { get; set; } = PdfColor.FromHex("#374151");

        /// <summary>Pages to stamp (default all). Numbering counts only these pages.</summary>
        public PageRange? Pages { get; set; }

        /// <summary>Number shown on the first stamped page (default 1).</summary>
        public int StartNumber { get; set; } = 1;
    }
}
