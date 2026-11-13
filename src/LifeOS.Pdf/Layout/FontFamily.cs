using System;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// A family of faces (regular, bold, italic, bold italic). Missing variants fall back
    /// to the closest available one, ending at Regular.
    /// </summary>
    public sealed class FontFamily
    {
        private static readonly Lazy<FontFamily> HelveticaFamily = new Lazy<FontFamily>(() => Standard("Helvetica",
            StandardFont.Helvetica, StandardFont.HelveticaBold, StandardFont.HelveticaOblique, StandardFont.HelveticaBoldOblique));

        private static readonly Lazy<FontFamily> TimesFamily = new Lazy<FontFamily>(() => Standard("Times",
            StandardFont.TimesRoman, StandardFont.TimesBold, StandardFont.TimesItalic, StandardFont.TimesBoldItalic));

        private static readonly Lazy<FontFamily> CourierFamily = new Lazy<FontFamily>(() => Standard("Courier",
            StandardFont.Courier, StandardFont.CourierBold, StandardFont.CourierOblique, StandardFont.CourierBoldOblique));

        private FontFamily(string name, PdfFontFace regular, PdfFontFace? bold, PdfFontFace? italic, PdfFontFace? boldItalic)
        {
            Name = name;
            Regular = regular;
            Bold = bold ?? regular;
            Italic = italic ?? regular;
            BoldItalic = boldItalic ?? bold ?? italic ?? regular;
        }

        public string Name { get; }

        public PdfFontFace Regular { get; }

        public PdfFontFace Bold { get; }

        public PdfFontFace Italic { get; }

        public PdfFontFace BoldItalic { get; }

        /// <summary>The standard Helvetica family (no embedding, WinAnsi characters).</summary>
        public static FontFamily Helvetica => HelveticaFamily.Value;

        public static FontFamily Times => TimesFamily.Value;

        public static FontFamily Courier => CourierFamily.Value;

        public static FontFamily FromFaces(PdfFontFace regular, PdfFontFace? bold = null, PdfFontFace? italic = null, PdfFontFace? boldItalic = null)
        {
            if (regular == null)
            {
                throw new ArgumentNullException(nameof(regular));
            }

            return new FontFamily(regular.FamilyName, regular, bold, italic, boldItalic);
        }

        /// <summary>Loads TrueType files; only the regular face is required.</summary>
        public static FontFamily FromFiles(string regular, string? bold = null, string? italic = null, string? boldItalic = null)
        {
            if (regular == null)
            {
                throw new ArgumentNullException(nameof(regular));
            }

            return FromFaces(
                PdfFontFace.FromFile(regular),
                bold == null ? null : PdfFontFace.FromFile(bold),
                italic == null ? null : PdfFontFace.FromFile(italic),
                boldItalic == null ? null : PdfFontFace.FromFile(boldItalic));
        }

        public PdfFontFace GetFace(bool bold, bool italic) => bold ? (italic ? BoldItalic : Bold) : (italic ? Italic : Regular);

        public override string ToString() => Name;

        private static FontFamily Standard(string name, StandardFont regular, StandardFont bold, StandardFont italic, StandardFont boldItalic) =>
            new FontFamily(name, PdfFontFace.Standard(regular), PdfFontFace.Standard(bold), PdfFontFace.Standard(italic), PdfFontFace.Standard(boldItalic));
    }
}
