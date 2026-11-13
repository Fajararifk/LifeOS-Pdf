using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Layout
{
    /// <summary>Formatting of a heading level.</summary>
    public sealed class HeadingStyle
    {
        internal HeadingStyle(double size, double spaceBefore, double spaceAfter)
        {
            Text = new TextStyle { Size = size, Bold = true };
            SpaceBefore = spaceBefore;
            SpaceAfter = spaceAfter;
        }

        /// <summary>Character style, merged over <see cref="DocumentStyles.Normal"/>.</summary>
        public TextStyle Text { get; set; }

        public double SpaceBefore { get; set; }

        public double SpaceAfter { get; set; }
    }
}
