using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf
{
    /// <summary>A page size in points (1 pt = 1/72 inch).</summary>
    public readonly struct PageSize : IEquatable<PageSize>
    {
        private const double PointsPerMillimeter = 72.0 / 25.4;

        public PageSize(double width, double height)
        {
            PdfFormat.RequireFinite(width, nameof(width));
            PdfFormat.RequireFinite(height, nameof(height));
            // ISO 32000-1 Annex C: page dimensions must stay within 3–14400 units.
            if (width < 3 || width > 14400)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Page width must be between 3 and 14400 points.");
            }

            if (height < 3 || height > 14400)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Page height must be between 3 and 14400 points.");
            }

            Width = width;
            Height = height;
        }

        // For pages read from a file: their size is whatever the file says (it was not created here).
        private PageSize(double width, double height, bool unchecked_)
        {
            Width = width;
            Height = height;
        }

        internal static PageSize Unchecked(double width, double height) => new PageSize(width, height, true);

        public double Width { get; }

        public double Height { get; }

        public static PageSize A3 => FromMillimeters(297, 420);

        public static PageSize A4 => FromMillimeters(210, 297);

        public static PageSize A5 => FromMillimeters(148, 210);

        public static PageSize Letter => new PageSize(612, 792);

        public static PageSize Legal => new PageSize(612, 1008);

        /// <summary>The same size rotated so it is wider than tall.</summary>
        public PageSize Landscape => Width >= Height ? this : new PageSize(Height, Width);

        /// <summary>The same size rotated so it is taller than wide.</summary>
        public PageSize Portrait => Height >= Width ? this : new PageSize(Height, Width);

        public static PageSize FromMillimeters(double width, double height) =>
            new PageSize(width * PointsPerMillimeter, height * PointsPerMillimeter);

        public static PageSize FromInches(double width, double height) => new PageSize(width * 72, height * 72);

        public bool Equals(PageSize other) => Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object? obj) => obj is PageSize other && Equals(other);

        public override int GetHashCode() => Width.GetHashCode() * 397 ^ Height.GetHashCode();

        public override string ToString() => PdfFormat.Number(Width) + " x " + PdfFormat.Number(Height) + " pt";
    }
}
