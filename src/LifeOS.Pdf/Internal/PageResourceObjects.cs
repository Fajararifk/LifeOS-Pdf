using System.Collections.Generic;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Internal
{
    /// <summary>The shared resource objects of a document being written; builds a page's /Resources.</summary>
    internal sealed class PageResourceObjects
    {
        private readonly Dictionary<PdfFontFace, PdfIndirectObject> _fonts;
        private readonly Dictionary<PdfImage, PdfIndirectObject> _images;
        private readonly Dictionary<string, PdfIndirectObject> _states;
        private readonly Dictionary<SourcePage, PdfIndirectObject> _forms;

        public PageResourceObjects(Dictionary<PdfFontFace, PdfIndirectObject> fonts, Dictionary<PdfImage, PdfIndirectObject> images,
            Dictionary<string, PdfIndirectObject> states, Dictionary<SourcePage, PdfIndirectObject> forms)
        {
            _fonts = fonts;
            _images = images;
            _states = states;
            _forms = forms;
        }

        /// <summary>Resources used by what was drawn on <paramref name="page"/> through its graphics.</summary>
        public PdfDictionary For(PdfPage page)
        {
            var resources = new PdfDictionary();
            if (page.Fonts.Count > 0)
            {
                var fonts = new PdfDictionary();
                foreach (var entry in page.Fonts)
                {
                    fonts.Set(entry.Key, _fonts[entry.Value].Reference);
                }

                resources.Set("Font", fonts);
            }

            if (page.Images.Count > 0 || page.Forms.Count > 0)
            {
                var xobjects = new PdfDictionary();
                foreach (var entry in page.Images)
                {
                    xobjects.Set(entry.Key, _images[entry.Value].Reference);
                }

                foreach (var entry in page.Forms)
                {
                    xobjects.Set(entry.Key, _forms[entry.Value].Reference);
                }

                resources.Set("XObject", xobjects);
            }

            if (page.GraphicsStates.Count > 0)
            {
                var states = new PdfDictionary();
                foreach (var name in page.GraphicsStates)
                {
                    states.Set(name, _states[name].Reference);
                }

                resources.Set("ExtGState", states);
            }

            return resources;
        }
    }
}
