using System;
using System.Collections.Generic;
using LifeOS.Pdf;
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

    /// <summary>OCR result for one image.</summary>
    public sealed class OcrPageResult
    {
        public OcrPageResult(string text, IReadOnlyList<OcrWord> words)
        {
            Text = text;
            Words = words;
        }

        public string Text { get; }

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
        public OcrPageResult Recognize(byte[] image)
        {
            using var pix = Pix.LoadFromMemory(image);
            using var page = _engine.Process(pix);
            var text = page.GetText() ?? string.Empty;
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
                            words.Add(new OcrWord(word, rect.X1, rect.Y1, rect.Width, rect.Height));
                        }
                    }
                }
                while (iterator.Next(PageIteratorLevel.Word));
            }

            return new OcrPageResult(text, words);
        }

        /// <summary>Recognizes just the text from an image.</summary>
        public string RecognizeText(byte[] image) => Recognize(image).Text;

        public void Dispose() => _engine.Dispose();
    }
}
