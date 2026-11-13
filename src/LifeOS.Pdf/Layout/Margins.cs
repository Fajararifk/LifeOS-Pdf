using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Distances in points on each side (page margins, cell padding).</summary>
    public readonly struct Margins
    {
        public Margins(double all)
            : this(all, all, all, all)
        {
        }

        public Margins(double horizontal, double vertical)
            : this(horizontal, vertical, horizontal, vertical)
        {
        }

        public Margins(double left, double top, double right, double bottom)
        {
            PdfFormat.RequireNonNegative(left, nameof(left));
            PdfFormat.RequireNonNegative(top, nameof(top));
            PdfFormat.RequireNonNegative(right, nameof(right));
            PdfFormat.RequireNonNegative(bottom, nameof(bottom));
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public double Left { get; }

        public double Top { get; }

        public double Right { get; }

        public double Bottom { get; }

        /// <summary>Millimetres → points, the unit page margins are usually given in.</summary>
        public static Margins FromMillimeters(double left, double top, double right, double bottom)
        {
            const double k = 72 / 25.4;
            return new Margins(left * k, top * k, right * k, bottom * k);
        }

        public override string ToString() =>
            PdfFormat.Number(Left) + ", " + PdfFormat.Number(Top) + ", " + PdfFormat.Number(Right) + ", " + PdfFormat.Number(Bottom);
    }
}
