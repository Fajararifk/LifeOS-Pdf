using System.Collections.Generic;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// Character formatting. Every property is optional: unset properties are inherited
    /// (document Normal style → heading/paragraph style → text span).
    /// </summary>
    public sealed class TextStyle
    {
        public FontFamily? Family { get; set; }

        /// <summary>
        /// Families tried in order for characters the main family has no glyph for
        /// (e.g. Helvetica for Latin with Noto Sans as fallback for Vietnamese).
        /// Inherited as a whole when empty.
        /// </summary>
        public IList<FontFamily> Fallbacks { get; } = new List<FontFamily>();

        /// <summary>Font size in points.</summary>
        public double? Size { get; set; }

        public bool? Bold { get; set; }

        public bool? Italic { get; set; }

        public bool? Underline { get; set; }

        public bool? Strikethrough { get; set; }

        public PdfColor? Color { get; set; }

        /// <summary>Pair kerning for TrueType fonts. Default true.</summary>
        public bool? Kerning { get; set; }

        /// <summary>A copy that can be changed without affecting this style.</summary>
        public TextStyle Clone()
        {
            var copy = new TextStyle
            {
                Family = Family,
                Size = Size,
                Bold = Bold,
                Italic = Italic,
                Underline = Underline,
                Strikethrough = Strikethrough,
                Color = Color,
                Kerning = Kerning,
            };
            foreach (var f in Fallbacks)
            {
                copy.Fallbacks.Add(f);
            }

            return copy;
        }

        /// <summary>Returns this style with the set properties of <paramref name="other"/> applied on top.</summary>
        internal TextStyle Merge(TextStyle? other)
        {
            if (other == null)
            {
                return this;
            }

            var result = Clone();
            result.Family = other.Family ?? Family;
            result.Size = other.Size ?? Size;
            result.Bold = other.Bold ?? Bold;
            result.Italic = other.Italic ?? Italic;
            result.Underline = other.Underline ?? Underline;
            result.Strikethrough = other.Strikethrough ?? Strikethrough;
            result.Color = other.Color ?? Color;
            result.Kerning = other.Kerning ?? Kerning;
            if (other.Fallbacks.Count > 0)
            {
                result.Fallbacks.Clear();
                foreach (var f in other.Fallbacks)
                {
                    result.Fallbacks.Add(f);
                }
            }

            return result;
        }
    }
}
