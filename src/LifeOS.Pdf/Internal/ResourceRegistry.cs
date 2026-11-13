using System.Collections.Generic;
using System.Globalization;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Document-wide registry so each font and image is written to the file exactly once,
    /// no matter how many pages use it. Names (F1, Im1, ...) are stable per document.
    /// </summary>
    internal sealed class ResourceRegistry
    {
        private readonly Dictionary<StandardFont, string> _fontNames = new Dictionary<StandardFont, string>();
        private readonly Dictionary<PdfImage, string> _imageNames = new Dictionary<PdfImage, string>();

        public List<StandardFont> Fonts { get; } = new List<StandardFont>();

        public List<PdfImage> Images { get; } = new List<PdfImage>();

        public string FontName(StandardFont font)
        {
            if (!_fontNames.TryGetValue(font, out var name))
            {
                Fonts.Add(font);
                name = "F" + Fonts.Count.ToString(CultureInfo.InvariantCulture);
                _fontNames[font] = name;
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
    }
}
