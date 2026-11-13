using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts.TrueType
{
    /// <summary>
    /// A TrueType font used as an embedded CIDFontType2 with Identity-H encoding:
    /// every glyph is written as its 2-byte glyph ID (CID = GID).
    /// </summary>
    internal sealed class TrueTypeFontProgram : IFontProgram
    {
        // OS/2 fsType bits (OpenType spec).
        internal const ushort RestrictedLicense = 0x0002;
        internal const ushort NoSubsetting = 0x0100;
        internal const ushort BitmapOnly = 0x0200;

        public TrueTypeFontProgram(TrueTypeFont font)
        {
            if ((font.FsType & RestrictedLicense) != 0 && (font.FsType & 0x000C) == 0)
            {
                throw new NotSupportedException("The license of font '" + font.PostScriptName +
                    "' does not allow embedding (OS/2 fsType = Restricted License). Choose another font.");
            }

            if ((font.FsType & BitmapOnly) != 0)
            {
                throw new NotSupportedException("Font '" + font.PostScriptName +
                    "' only allows bitmap embedding, which is not supported. Choose another font.");
            }

            Font = font;
        }

        public TrueTypeFont Font { get; }

        public string PostScriptName => Font.PostScriptName;

        public string FamilyName => Font.FamilyName.Length > 0 ? Font.FamilyName : Font.PostScriptName;

        public bool IsBold => Font.IsBold;

        public bool IsItalic => Font.IsItalic;

        public bool IsFixedPitch => Font.IsFixedPitch;

        public bool IsEmbedded => true;

        public int GlyphCount => Font.GlyphCount;

        public double Ascent => ToPdfUnits(Font.Ascender);

        public double Descent => ToPdfUnits(Font.Descender);

        public double CapHeight => ToPdfUnits(Font.CapHeight);

        /// <summary>The font may only be embedded in full.</summary>
        public bool MustEmbedFully => (Font.FsType & NoSubsetting) != 0;

        public bool HasGlyph(int codePoint) => Font.Cmap.GlyphId(codePoint) != 0;

        /// <summary>
        /// Width of a glyph in 1/1000 em, rounded to a whole number. The same value is
        /// written to the /W array, so measuring and the viewer's layout always agree.
        /// </summary>
        public int GlyphWidth(int glyphId) => (int)Math.Round(ToPdfUnits(Font.AdvanceWidth(glyphId)), MidpointRounding.AwayFromZero);

        public TextRun Map(string text)
        {
            var codePoints = CodePointReader.Read(text);
            var codes = new byte[codePoints.Count * 2];
            var glyphs = new int[codePoints.Count];
            double width = 0;
            for (var i = 0; i < codePoints.Count; i++)
            {
                var gid = Font.Cmap.GlyphId(codePoints[i]);
                glyphs[i] = gid;
                codes[i * 2] = (byte)(gid >> 8);
                codes[i * 2 + 1] = (byte)gid;
                width += GlyphWidth(gid);
            }

            return new TextRun(codes, glyphs, codePoints.ToArray(), width);
        }

        private double ToPdfUnits(double fontUnits) => fontUnits * 1000.0 / Font.UnitsPerEm;
    }
}
