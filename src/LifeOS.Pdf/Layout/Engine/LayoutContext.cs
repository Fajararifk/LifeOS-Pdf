using System.Collections.Generic;
using System.Globalization;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>State shared by all layouters during one layout pass.</summary>
    internal sealed class LayoutContext
    {
        private readonly Dictionary<(PdfFontFace Face, double Size), PdfFont> _fonts = new Dictionary<(PdfFontFace Face, double Size), PdfFont>();

        public LayoutContext(DocumentStyles styles)
        {
            Styles = styles;
            // Built-in defaults below the user's Normal style, so a partial Normal still resolves.
            BaseStyle = new TextStyle
            {
                Family = FontFamily.Helvetica,
                Size = 10,
                Bold = false,
                Italic = false,
                Underline = false,
                Strikethrough = false,
                Color = PdfColor.Black,
                Kerning = true,
            }.Merge(styles.Normal);
        }

        public DocumentStyles Styles { get; }

        /// <summary>Normal style with every property set.</summary>
        public TextStyle BaseStyle { get; }

        /// <summary>Number of the page being laid out (1-based).</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Total page count (exact in the final pass, an estimate before).</summary>
        public int PageCount { get; set; } = 1;

        public string FieldText(InlineKind kind) =>
            (kind == InlineKind.PageNumber ? PageNumber : PageCount).ToString(CultureInfo.InvariantCulture);

        /// <summary>Headings of the body, in document order (for the table of contents).</summary>
        public List<HeadingInfo> Headings { get; } = new List<HeadingInfo>();

        /// <summary>Page number of each heading from the previous layout pass.</summary>
        public Dictionary<Paragraph, int> HeadingPages { get; } = new Dictionary<Paragraph, int>();

        public PdfFont Font(PdfFontFace face, double size)
        {
            var key = (face, size);
            if (!_fonts.TryGetValue(key, out var font))
            {
                font = new PdfFont(face, size);
                _fonts[key] = font;
            }

            return font;
        }
    }

    /// <summary>A heading that can appear in the table of contents.</summary>
    internal sealed class HeadingInfo
    {
        public HeadingInfo(Paragraph paragraph, string name)
        {
            Paragraph = paragraph;
            Name = name;
        }

        public Paragraph Paragraph { get; }

        public int Level => Paragraph.HeadingLevel;

        /// <summary>Named destination the table of contents links to.</summary>
        public string Name { get; }
    }

    /// <summary>A text style with every property resolved.</summary>
    internal sealed class ResolvedStyle
    {
        public ResolvedStyle(TextStyle s, string? link = null)
        {
            Link = link;
            Family = s.Family ?? FontFamily.Helvetica;
            Fallbacks = new List<FontFamily>(s.Fallbacks);
            Size = s.Size ?? 10;
            Bold = s.Bold ?? false;
            Italic = s.Italic ?? false;
            Underline = s.Underline ?? false;
            Strikethrough = s.Strikethrough ?? false;
            Color = s.Color ?? PdfColor.Black;
            Kerning = s.Kerning ?? true;
            PrimaryFace = Family.GetFace(Bold, Italic);
        }

        public FontFamily Family { get; }

        public List<FontFamily> Fallbacks { get; }

        public double Size { get; }

        public bool Bold { get; }

        public bool Italic { get; }

        public bool Underline { get; }

        public bool Strikethrough { get; }

        public PdfColor Color { get; }

        public bool Kerning { get; }

        public PdfFontFace PrimaryFace { get; }

        /// <summary>Link target of the text ("#name" or a URI), or null.</summary>
        public string? Link { get; }

        /// <summary>Font fallback (requirement FR-07): the first family that has the glyph.</summary>
        public PdfFontFace FaceFor(int codePoint)
        {
            if (codePoint <= 0x20 || PrimaryFace.HasGlyph(codePoint))
            {
                return PrimaryFace;
            }

            foreach (var family in Fallbacks)
            {
                var face = family.GetFace(Bold, Italic);
                if (face.HasGlyph(codePoint))
                {
                    return face;
                }
            }

            return PrimaryFace; // draws .notdef, like PdfGraphics does
        }
    }
}
