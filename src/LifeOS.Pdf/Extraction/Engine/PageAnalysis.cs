using System;
using System.Collections.Generic;
using System.IO;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>Everything extraction needs from one page, computed once and cached on the page.</summary>
    internal sealed class PageAnalysis
    {
        private List<TextLine>? _lines;

        private PageAnalysis(PageGeometry geometry)
        {
            Geometry = geometry;
        }

        public PageGeometry Geometry { get; }

        /// <summary>Glyphs in content order, with the raw interpreter data alongside (same index).</summary>
        public List<TextGlyph> Glyphs { get; } = new List<TextGlyph>();

        public List<GlyphInfo> Raw { get; } = new List<GlyphInfo>();

        public List<(ImageInfo Info, PdfRect Bounds)> Images { get; } = new List<(ImageInfo, PdfRect)>();

        /// <summary>Ruling lines in display coordinates (zero-height or zero-width rectangles).</summary>
        public List<PdfRect> Rulings { get; } = new List<PdfRect>();

        public PdfParser? Parser { get; private set; }

        /// <summary>Lines with default options (cached).</summary>
        public List<TextLine> Lines => _lines ?? (_lines = TextAssembler.Lines(Glyphs, null, true, false));

        /// <summary>Analyses an imported, unchanged page (callers snapshot other pages first).</summary>
        public static PageAnalysis Run(PdfPage page)
        {
            var sp = page.Source ?? throw new InvalidOperationException("Internal error: extraction needs an imported page.");
            var parser = sp.Parser;
            var geometry = new PageGeometry(sp.CropBox ?? sp.MediaBox, page.Rotation);
            var analysis = new PageAnalysis(geometry) { Parser = parser };
            var interpreter = new ContentInterpreter(parser, parser.Options.MaxStreamSize);
            interpreter.Run(ContentReader.Read(ContentBytes(sp)), parser.Resolve(sp.Resources) as PdfDictionary, ContentInterpreter.Identity());

            foreach (var g in interpreter.Glyphs)
            {
                // Box from the baseline start/end and the top-left corner (a parallelogram).
                var bounds = geometry.Quad(g.X0, g.Y0, g.X1, g.Y1, g.TopX, g.TopY, g.X1 + g.TopX - g.X0, g.Y1 + g.TopY - g.Y0);
                geometry.ToDisplay(g.X0, g.Y0, out var ax, out var ay);
                geometry.ToDisplay(g.X1, g.Y1, out var bx, out var by);
                double dx = bx - ax, dy = by - ay;
                if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
                {
                    // Zero-width glyph: take the direction from the glyph's up vector instead.
                    geometry.ToDisplay(g.TopX, g.TopY, out var tx, out var ty);
                    dx = -(ty - ay);
                    dy = tx - ax;
                }

                var orientation = Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? 0 : 2) : (dy >= 0 ? 1 : 3);
                analysis.Glyphs.Add(new TextGlyph(g.Text, bounds, g.Font.BaseFont, Math.Round(g.FontSize, 2), g.Font.Bold, g.Font.Italic)
                {
                    Orientation = orientation,
                    Invisible = g.RenderMode == 3 || g.RenderMode == 7,
                });
                analysis.Raw.Add(g);
            }

            foreach (var image in interpreter.Images)
            {
                var c = image.Ctm;
                analysis.Images.Add((image, geometry.Quad(c[4], c[5], c[0] + c[4], c[1] + c[5], c[2] + c[4], c[3] + c[5], c[0] + c[2] + c[4], c[1] + c[3] + c[5])));
            }

            foreach (var s in interpreter.Segments)
            {
                analysis.Rulings.Add(geometry.Rect(s[0], s[1], s[2], s[3]));
            }

            return analysis;
        }

        /// <summary>The page's content streams decoded and joined (§7.8.2: split at token boundaries).</summary>
        public static byte[] ContentBytes(SourcePage sp)
        {
            var parser = sp.Parser;
            using (var ms = new MemoryStream())
            {
                foreach (var part in ImportedPageWriter.ContentParts(sp))
                {
                    var stream = (PdfStream)parser.Resolve(part);
                    byte[] data;
                    try
                    {
                        data = StreamDecoder.Decode(stream, parser, parser.Options.MaxStreamSize);
                    }
                    catch (PdfReadException ex) when (!(ex is PdfLimitException) && !(ex is PdfEncryptedException))
                    {
                        continue; // an undecodable part is skipped, the rest still counts
                    }

                    if (ms.Length + data.Length > parser.Options.MaxStreamSize)
                    {
                        throw new PdfLimitException("The page content is larger than PdfReadOptions.MaxStreamSize.");
                    }

                    ms.Write(data, 0, data.Length);
                    ms.WriteByte((byte)'\n');
                }

                return ms.ToArray();
            }
        }
    }
}
