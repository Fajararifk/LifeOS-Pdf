using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>How the width of a table column is determined.</summary>
    public readonly struct ColumnWidth
    {
        private ColumnWidth(ColumnWidthKind kind, double value)
        {
            Kind = kind;
            Value = value;
        }

        public ColumnWidthKind Kind { get; }

        public double Value { get; }

        /// <summary>Follows the content: as wide as needed, shrinking (wrapping) when space is short.</summary>
        public static ColumnWidth Auto => new ColumnWidth(ColumnWidthKind.Auto, 0);

        /// <summary>Exactly <paramref name="points"/> wide.</summary>
        public static ColumnWidth Fixed(double points)
        {
            PdfFormat.RequireNonNegative(points, nameof(points));
            return new ColumnWidth(ColumnWidthKind.Fixed, points);
        }

        /// <summary>A percentage (0–100) of the table width.</summary>
        public static ColumnWidth Percent(double percent)
        {
            PdfFormat.RequireNonNegative(percent, nameof(percent));
            return new ColumnWidth(ColumnWidthKind.Percent, percent);
        }

        /// <summary>A share of the space left after the other columns (weights like CSS fr).</summary>
        public static ColumnWidth Relative(double weight = 1)
        {
            PdfFormat.RequireFinite(weight, nameof(weight));
            if (weight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "Weight must be greater than zero.");
            }

            return new ColumnWidth(ColumnWidthKind.Relative, weight);
        }
    }
}
