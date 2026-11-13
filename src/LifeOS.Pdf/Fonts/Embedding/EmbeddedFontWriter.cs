using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using LifeOS.Pdf.Fonts.TrueType;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Fonts.Embedding
{
    /// <summary>
    /// Writes a TrueType font as a composite font (ISO 32000-1 §9.7):
    /// Type0 (Identity-H) → CIDFontType2 (/CIDToGIDMap /Identity) → FontDescriptor →
    /// FontFile2 (subset), plus a ToUnicode CMap.
    /// </summary>
    internal static class EmbeddedFontWriter
    {
        public static void Write(PdfIndirectObject type0, TrueTypeFontProgram program, FontUsage usage, Func<PdfIndirectObject> newObject)
        {
            var font = program.Font;
            var glyphs = new List<int>(usage.Glyphs);
            var fontFile = TrueTypeSubsetter.Subset(font, program.MustEmbedFully ? null : glyphs, usage.GlyphToCodePoint);

            // Subset tag (§9.9.2): six uppercase letters, derived from the content so it is
            // deterministic and differs whenever the glyph set differs.
            var baseFont = (program.MustEmbedFully ? string.Empty : SubsetTag(font.PostScriptName, glyphs) + "+") + font.PostScriptName;

            var cidFont = newObject();
            var descriptor = newObject();
            var fontFileObj = newObject();
            var toUnicode = newObject();

            fontFileObj.Value = new PdfStream(
                new PdfDictionary().Set("Length1", fontFile.Length).Set("Filter", "FlateDecode"),
                Zlib.Compress(fontFile));

            descriptor.Value = new PdfDictionary()
                .Set("Type", "FontDescriptor")
                .Set("FontName", baseFont)
                .Set("Flags", Flags(program))
                .Set("FontBBox", PdfArray.OfNumbers(Scale(font, font.XMin), Scale(font, font.YMin), Scale(font, font.XMax), Scale(font, font.YMax)))
                .Set("ItalicAngle", new PdfReal(font.ItalicAngle))
                .Set("Ascent", new PdfReal(Math.Round(program.Ascent)))
                .Set("Descent", new PdfReal(Math.Round(program.Descent)))
                .Set("CapHeight", new PdfReal(Math.Round(program.CapHeight)))
                // StemV is not stored in TrueType; estimate from weight like most producers do.
                .Set("StemV", new PdfInteger(10 + (long)(220 * Math.Pow((font.WeightClass - 50) / 900.0, 2))))
                .Set("FontFile2", fontFileObj.Reference);

            cidFont.Value = new PdfDictionary()
                .Set("Type", "Font")
                .Set("Subtype", "CIDFontType2")
                .Set("BaseFont", baseFont)
                .Set("CIDSystemInfo", new PdfDictionary()
                    .Set("Registry", PdfString.Text("Adobe"))
                    .Set("Ordering", PdfString.Text("Identity"))
                    .Set("Supplement", 0))
                .Set("FontDescriptor", descriptor.Reference)
                .Set("DW", program.GlyphWidth(0))
                .Set("W", WidthArray(program, glyphs))
                .Set("CIDToGIDMap", "Identity");

            toUnicode.Value = new PdfStream(
                new PdfDictionary().Set("Filter", "FlateDecode"),
                Zlib.Compress(ToUnicodeCMap.Build(usage.GlyphToCodePoint)));

            type0.Value = new PdfDictionary()
                .Set("Type", "Font")
                .Set("Subtype", "Type0")
                .Set("BaseFont", baseFont)
                .Set("Encoding", "Identity-H")
                .Set("DescendantFonts", new PdfArray(cidFont.Reference))
                .Set("ToUnicode", toUnicode.Reference);
        }

        /// <summary>/W as runs of consecutive glyph IDs: [first [w1 w2 …] first [w …] …].</summary>
        private static PdfArray WidthArray(TrueTypeFontProgram program, List<int> glyphs)
        {
            var w = new PdfArray();
            var i = 0;
            while (i < glyphs.Count)
            {
                var run = new PdfArray();
                var first = glyphs[i];
                var expected = first;
                while (i < glyphs.Count && glyphs[i] == expected)
                {
                    run.Items.Add(new PdfInteger(program.GlyphWidth(glyphs[i])));
                    i++;
                    expected++;
                }

                w.Items.Add(new PdfInteger(first));
                w.Items.Add(run);
            }

            return w;
        }

        private static long Flags(TrueTypeFontProgram program)
        {
            // Bit 3 Symbolic: required for CID-keyed fonts whose glyphs are not in the Standard Latin set.
            long flags = 4;
            if (program.IsFixedPitch)
            {
                flags |= 1;
            }

            if (program.IsItalic)
            {
                flags |= 64;
            }

            return flags;
        }

        private static double Scale(TrueTypeFont font, int units) => Math.Round(units * 1000.0 / font.UnitsPerEm);

        private static string SubsetTag(string name, List<int> glyphs)
        {
            var sb = new StringBuilder(name);
            foreach (var g in glyphs)
            {
                sb.Append(',').Append(g);
            }

            byte[] hash;
            using (var md5 = MD5.Create())
            {
                hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            }

            var tag = new char[6];
            for (var i = 0; i < 6; i++)
            {
                tag[i] = (char)('A' + hash[i] % 26);
            }

            return new string(tag);
        }
    }
}
