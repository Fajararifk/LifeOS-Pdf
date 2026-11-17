using System;
using System.Collections.Generic;
using LifeOS.Pdf;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Rendering;
using Tesseract;

namespace LifeOS.Pdf.Ocr
{
    /// <summary>A recognized word with its position (top-left, points).</summary>
    public sealed class OcrWord
    {
        public OcrWord(string text, double x, double y, double width, double height)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public string Text { get; }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }
    }

    /// <summary>OCR result for one image or page.</summary>
    public sealed class OcrPageResult
    {
        public OcrPageResult(string text, IReadOnlyList<OcrWord> words)
        {
            Text = text;
            Words = words;
        }

        public string Text { get; }

        /// <summary>Words with their boxes, in points with the origin at the top-left.</summary>
        public IReadOnlyList<OcrWord> Words { get; }
    }

    /// <summary>Optical character recognition via Tesseract (Apache-2.0).</summary>
    public sealed class OcrEngine : IDisposable
    {
        private readonly TesseractEngine _engine;

        public OcrEngine(string tessDataPath, string language = "eng")
        {
            _engine = new TesseractEngine(tessDataPath, language, EngineMode.Default);
        }

        /// <summary>Recognizes text from an image (PNG/JPEG bytes).</summary>
        public OcrPageResult Recognize(byte[] image, OcrOptions? options = null)
        {
            options ??= new OcrOptions();
            using var pix = Pix.LoadFromMemory(image);
            using var page = _engine.Process(pix, options.PageSegmentation);
            var text = page.GetText() ?? string.Empty;

            var scale = options.Dpi > 0 ? 72.0 / options.Dpi : 1.0; // pixels → points
            var words = new List<OcrWord>();
            using (var iterator = page.GetIterator())
            {
                iterator.Begin();
                do
                {
                    if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                    {
                        var word = iterator.GetText(PageIteratorLevel.Word);
                        if (!string.IsNullOrWhiteSpace(word))
                        {
                            words.Add(new OcrWord(word!, rect.X1 * scale, rect.Y1 * scale, rect.Width * scale, rect.Height * scale));
                        }
                    }
                }
                while (iterator.Next(PageIteratorLevel.Word));
            }

            return new OcrPageResult(text, words);
        }

        /// <summary>Recognizes just the text from an image.</summary>
        public string RecognizeText(byte[] image, OcrOptions? options = null) => Recognize(image, options).Text;

        /// <summary>Renders a PDF page (PDFium) and OCRs it; positions are in points.</summary>
        public OcrPageResult RecognizePdf(PdfDocument document, int pageIndex, OcrOptions? options = null)
        {
            options ??= new OcrOptions { Dpi = 200 };
            var image = new PdfRenderer().Render(document.ToArray(), pageIndex, new RenderOptions { Dpi = (int)Math.Round(options.Dpi) });
            return Recognize(image.Png, options);
        }

        /// <summary>Renders and OCRs every page, in order.</summary>
        public IReadOnlyList<OcrPageResult> RecognizeAll(PdfDocument document, OcrOptions? options = null)
        {
            options ??= new OcrOptions { Dpi = 200 };
            var bytes = document.ToArray();
            var renderer = new PdfRenderer();
            var results = new List<OcrPageResult>(document.Pages.Count);
            for (var i = 0; i < document.Pages.Count; i++)
            {
                var image = renderer.Render(bytes, i, new RenderOptions { Dpi = (int)Math.Round(options.Dpi) });
                results.Add(Recognize(image.Png, options));
            }

            return results;
        }

        /// <summary>
        /// Adds an invisible OCR text layer (rendering mode 3) to every page so the scanned content
        /// becomes selectable and searchable. The page is rendered as-is; the visible content is not
        /// changed. Use this on opened scanned PDFs.
        /// </summary>
        public void MakeSearchable(PdfDocument document, OcrOptions? options = null)
        {
            options ??= new OcrOptions { Dpi = 200 };
            var bytes = document.ToArray(); // serialize once; drawing below adds overlays
            var renderer = new PdfRenderer();
            var font = new PdfFont(StandardFont.Helvetica, 10);

            for (var i = 0; i < document.Pages.Count; i++)
            {
                var image = renderer.Render(bytes, i, new RenderOptions { Dpi = (int)Math.Round(options.Dpi) });
                var result = Recognize(image.Png, options);
                var page = document.Pages[i];
                foreach (var word in result.Words)
                {
                    if (string.IsNullOrWhiteSpace(word.Text))
                    {
                        continue;
                    }

                    var size = Math.Max(4, word.Height);
                    page.Graphics.DrawInvisibleText(word.Text, font.WithSize(size), word.X, word.Y);
                }
            }
        }

        public void Dispose() => _engine.Dispose();
    }
}
