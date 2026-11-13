using System;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>Options for text and image watermarks.</summary>
    public sealed class WatermarkOptions
    {
        private double _opacity = 0.2;
        private double _scale = 0.5;

        /// <summary>Font face for text watermarks (default Helvetica Bold).</summary>
        public PdfFontFace Font { get; set; } = PdfFontFace.Standard(StandardFont.HelveticaBold);

        /// <summary>Font size in points; null fits the text across the page (default).</summary>
        public double? FontSize { get; set; }

        public PdfColor Color { get; set; } = PdfColor.FromHex("#DC2626");

        /// <summary>0 (invisible) to 1 (opaque); default 0.2.</summary>
        public double Opacity
        {
            get => _opacity;
            set => _opacity = value >= 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Opacity), value, "Opacity must be between 0 and 1.");
        }

        /// <summary>Counter-clockwise angle in degrees (default 45: bottom-left to top-right).</summary>
        public double Angle { get; set; } = 45;

        /// <summary>Image watermarks: width as a fraction of the page width (default 0.5).</summary>
        public double Scale
        {
            get => _scale;
            set => _scale = value > 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Scale), value, "Scale must be greater than 0 and at most 1.");
        }

        /// <summary>Draw behind the page content instead of on top (text stays selectable and readable).</summary>
        public bool Behind { get; set; }

        /// <summary>Pages to mark (default all).</summary>
        public PageRange? Pages { get; set; }
    }
}
