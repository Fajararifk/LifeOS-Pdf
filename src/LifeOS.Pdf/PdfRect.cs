using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf
{
    /// <summary>
    /// A rectangle in points with the origin at the top-left corner (the convention of the whole
    /// public API). Used for text, image and table positions and for redaction areas.
    /// </summary>
    public readonly struct PdfRect : IEquatable<PdfRect>
    {
        public PdfRect(double x, double y, double width, double height)
        {
            PdfFormat.RequireFinite(x, nameof(x));
            PdfFormat.RequireFinite(y, nameof(y));
            PdfFormat.RequireNonNegative(width, nameof(width));
            PdfFormat.RequireNonNegative(height, nameof(height));
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public double Right => X + Width;

        public double Bottom => Y + Height;

        /// <summary>The smallest rectangle containing both corners.</summary>
        public static PdfRect FromPoints(double x1, double y1, double x2, double y2) =>
            new PdfRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

        /// <summary>True when the rectangles overlap (touching edges do not count).</summary>
        public bool Intersects(PdfRect other) =>
            X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

        public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

        /// <summary>True when <paramref name="other"/> lies completely inside this rectangle.</summary>
        public bool Contains(PdfRect other) => other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

        public PdfRect Union(PdfRect other) =>
            FromPoints(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

        /// <summary>Area of the overlap (0 when the rectangles do not overlap).</summary>
        public double OverlapArea(PdfRect other)
        {
            var w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
            var h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
            return w > 0 && h > 0 ? w * h : 0;
        }

        public bool Equals(PdfRect other) => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object? obj) => obj is PdfRect r && Equals(r);

        public override int GetHashCode() => ((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Width.GetHashCode()) * 397 ^ Height.GetHashCode();

        public override string ToString() =>
            "[" + PdfFormat.Number(X) + ", " + PdfFormat.Number(Y) + ", " + PdfFormat.Number(Width) + " × " + PdfFormat.Number(Height) + "]";
    }
}
