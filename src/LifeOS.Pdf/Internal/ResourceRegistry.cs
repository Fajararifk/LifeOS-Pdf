using System.Collections.Generic;
using System.Globalization;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Fonts.Embedding;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Document-wide registry so each font, image and graphics state is written to the
    /// file exactly once, no matter how many pages use it. Names (F1, Im1, GS1, ...) are
    /// stable per document. Also collects which glyphs each embedded font needs.
    /// </summary>
    internal sealed class ResourceRegistry
    {
        private readonly Dictionary<PdfFontFace, string> _fontNames = new Dictionary<PdfFontFace, string>();
        private readonly Dictionary<PdfImage, string> _imageNames = new Dictionary<PdfImage, string>();
        private readonly Dictionary<string, string> _stateNames = new Dictionary<string, string>();

        public List<PdfFontFace> Fonts { get; } = new List<PdfFontFace>();

        public Dictionary<PdfFontFace, FontUsage> FontUsages { get; } = new Dictionary<PdfFontFace, FontUsage>();

        public List<PdfImage> Images { get; } = new List<PdfImage>();

        /// <summary>Graphics states in creation order: name → (fill alpha, stroke alpha).</summary>
        public List<KeyValuePair<string, OpacityState>> GraphicsStates { get; } = new List<KeyValuePair<string, OpacityState>>();

        public string FontName(PdfFontFace face, TextRun run)
        {
            if (!_fontNames.TryGetValue(face, out var name))
            {
                Fonts.Add(face);
                name = "F" + Fonts.Count.ToString(CultureInfo.InvariantCulture);
                _fontNames[face] = name;
                if (face.IsEmbedded)
                {
                    FontUsages[face] = new FontUsage();
                }
            }

            if (face.IsEmbedded)
            {
                FontUsages[face].Add(run);
            }

            return name;
        }

        public string ImageName(PdfImage image)
        {
            if (!_imageNames.TryGetValue(image, out var name))
            {
                Images.Add(image);
                name = "Im" + Images.Count.ToString(CultureInfo.InvariantCulture);
                _imageNames[image] = name;
            }

            return name;
        }

        public string GraphicsStateName(OpacityState state)
        {
            var key = PdfFormat.Number(state.Fill) + "/" + PdfFormat.Number(state.Stroke);
            if (!_stateNames.TryGetValue(key, out var name))
            {
                name = "GS" + (GraphicsStates.Count + 1).ToString(CultureInfo.InvariantCulture);
                _stateNames[key] = name;
                GraphicsStates.Add(new KeyValuePair<string, OpacityState>(name, state));
            }

            return name;
        }
    }

    internal readonly struct OpacityState
    {
        public OpacityState(double fill, double stroke)
        {
            Fill = fill;
            Stroke = stroke;
        }

        public double Fill { get; }

        public double Stroke { get; }
    }
}
