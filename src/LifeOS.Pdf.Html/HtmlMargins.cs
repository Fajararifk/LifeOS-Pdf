using System;

namespace LifeOS.Pdf.Html
{
    /// <summary>Page margins in points (1 pt = 1/72 inch), like every other size in LifeOS.Pdf.</summary>
    public sealed class HtmlMargins
    {
        public HtmlMargins(double top, double right, double bottom, double left)
        {
            Top = Check(top, nameof(top));
            Right = Check(right, nameof(right));
            Bottom = Check(bottom, nameof(bottom));
            Left = Check(left, nameof(left));
        }

        /// <summary>The same margin on all four sides.</summary>
        public static HtmlMargins Uniform(double points) => new HtmlMargins(points, points, points, points);

        /// <summary>No margins.</summary>
        public static HtmlMargins None => Uniform(0);

        public double Top { get; }

        public double Right { get; }

        public double Bottom { get; }

        public double Left { get; }

        private static double Check(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "A margin must be a finite number of points, 0 or more.");
            }

            return value;
        }
    }
}
