using System;
using System.IO;
using LifeOS.Pdf.Fonts.Standard;
using LifeOS.Pdf.Fonts.TrueType;

namespace LifeOS.Pdf.Fonts
{
    /// <summary>
    /// A typeface, independent of size: one of the 14 standard fonts, or a TrueType
    /// font loaded from a file. Load a file once and reuse the face for every size
    /// and page; it is embedded (subset to the glyphs used) once per document.
    /// </summary>
    public sealed class PdfFontFace
    {
        private static readonly PdfFontFace?[] StandardFaces = new PdfFontFace?[14];
        private static readonly object Gate = new object();

        private PdfFontFace(IFontProgram program)
        {
            Program = program;
        }

        public string PostScriptName => Program.PostScriptName;

        public string FamilyName => Program.FamilyName;

        public bool IsBold => Program.IsBold;

        public bool IsItalic => Program.IsItalic;

        /// <summary>False for the 14 standard fonts (viewers supply them), true for loaded fonts.</summary>
        public bool IsEmbedded => Program.IsEmbedded;

        public int GlyphCount => Program.GlyphCount;

        internal IFontProgram Program { get; }

        /// <summary>The standard font this face represents, or null for a loaded font.</summary>
        internal StandardFont? StandardFont => (Program as StandardFontProgram)?.Font;

        /// <summary>Returns the face for a standard font. The same instance is returned every time.</summary>
        public static PdfFontFace Standard(StandardFont font)
        {
            var index = (int)font;
            if (index < 0 || index >= StandardFaces.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown standard font.");
            }

            lock (Gate)
            {
                return StandardFaces[index] ?? (StandardFaces[index] = new PdfFontFace(new StandardFontProgram(font)));
            }
        }

        /// <summary>Loads a TrueType font (.ttf) or one font of a collection (.ttc).</summary>
        public static PdfFontFace FromFile(string path, int collectionIndex = 0)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return FromBytesNoCopy(File.ReadAllBytes(path), collectionIndex);
        }

        public static PdfFontFace FromStream(Stream stream, int collectionIndex = 0)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return FromBytesNoCopy(ms.ToArray(), collectionIndex);
            }
        }

        public static PdfFontFace FromBytes(byte[] data, int collectionIndex = 0)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return FromBytesNoCopy((byte[])data.Clone(), collectionIndex);
        }

        /// <summary>True when the font has a real glyph (not .notdef) for the code point.</summary>
        public bool HasGlyph(int codePoint) => Program.HasGlyph(codePoint);

        /// <summary>True when every visible character of <paramref name="text"/> has a glyph.</summary>
        public bool HasGlyph(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            foreach (var cp in CodePointReader.Read(text))
            {
                if (!Program.HasGlyph(cp))
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString() => PostScriptName;

        private static PdfFontFace FromBytesNoCopy(byte[] data, int collectionIndex)
        {
            return new PdfFontFace(new TrueTypeFontProgram(TrueTypeFont.Parse(data, collectionIndex)));
        }
    }
}
