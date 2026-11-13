using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Paragraph formatting (spacing, indents, alignment, pagination).</summary>
    public sealed class ParagraphFormat
    {
        private double _lineSpacing = 1.2;
        private double _spaceBefore;
        private double _spaceAfter = 6;
        private double _leftIndent;
        private double _rightIndent;

        public ParagraphAlignment Alignment { get; set; } = ParagraphAlignment.Left;

        /// <summary>Distance between baselines as a multiple of the font size. Default 1.2.</summary>
        public double LineSpacing
        {
            get => _lineSpacing;
            set
            {
                PdfFormat.RequireFinite(value, nameof(LineSpacing));
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(LineSpacing), value, "Line spacing must be greater than zero.");
                }

                _lineSpacing = value;
            }
        }

        /// <summary>Space above, in points. Suppressed at the top of a page, column or cell.</summary>
        public double SpaceBefore
        {
            get => _spaceBefore;
            set => _spaceBefore = NonNegative(value, nameof(SpaceBefore));
        }

        /// <summary>Space below, in points. Default 6. Ignored after the last block of a cell, header or footer.</summary>
        public double SpaceAfter
        {
            get => _spaceAfter;
            set => _spaceAfter = NonNegative(value, nameof(SpaceAfter));
        }

        public double LeftIndent
        {
            get => _leftIndent;
            set => _leftIndent = NonNegative(value, nameof(LeftIndent));
        }

        public double RightIndent
        {
            get => _rightIndent;
            set => _rightIndent = NonNegative(value, nameof(RightIndent));
        }

        /// <summary>Extra indent of the first line (may be negative for a hanging indent).</summary>
        public double FirstLineIndent { get; set; }

        /// <summary>Do not split the paragraph across pages or columns.</summary>
        public bool KeepTogether { get; set; }

        /// <summary>Keep on the same page as the start of the next block (e.g. a heading).</summary>
        public bool KeepWithNext { get; set; }

        /// <summary>Always start on a new page.</summary>
        public bool PageBreakBefore { get; set; }

        private static double NonNegative(double value, string name)
        {
            PdfFormat.RequireNonNegative(value, name);
            return value;
        }
    }
}
