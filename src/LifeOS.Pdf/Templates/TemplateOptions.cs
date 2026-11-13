using System;
using System.Collections.Generic;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;

namespace LifeOS.Pdf.Templates
{
    /// <summary>Settings for parsing and binding a <see cref="PdfTemplate"/>.</summary>
    public sealed class TemplateOptions
    {
        private Dictionary<string, FontFamily> _fonts = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PdfImage> _images = new Dictionary<string, PdfImage>(StringComparer.OrdinalIgnoreCase);
        private List<FontFamily> _fallbacks = new List<FontFamily>();

        /// <summary>Language of formatted numbers, dates and number words. Default Indonesian.</summary>
        public TemplateLanguage Language { get; set; } = TemplateLanguage.Indonesian;

        /// <summary>
        /// When true, a placeholder whose data does not exist throws <see cref="TemplateException"/>
        /// (useful while writing templates). Default false: missing values render as empty text.
        /// </summary>
        public bool Strict { get; set; }

        /// <summary>Folder that relative image and font paths in the template are resolved against.</summary>
        public string? BaseDirectory { get; set; }

        /// <summary>
        /// Allow absolute paths and paths leaving <see cref="BaseDirectory"/> ("..").
        /// Off by default so a template from an untrusted source cannot read arbitrary files.
        /// </summary>
        public bool AllowFileAccessOutsideBaseDirectory { get; set; }

        /// <summary>Font families usable by name in the template (family="noto", fallback="noto").</summary>
        public IDictionary<string, FontFamily> Fonts => _fonts;

        /// <summary>
        /// Fonts tried for characters the template's fonts do not have (e.g. Noto Sans for
        /// Vietnamese or Japanese names in a Helvetica template). Added after the template's own fallbacks.
        /// </summary>
        public IList<FontFamily> FallbackFonts => _fallbacks;

        /// <summary>Images usable by name in the template (&lt;image src="logo"/&gt;), e.g. a company logo.</summary>
        public IDictionary<string, PdfImage> Images => _images;

        /// <summary>A copy, so a parsed template is not affected by later changes to the caller's options.</summary>
        internal TemplateOptions Clone()
        {
            var copy = (TemplateOptions)MemberwiseClone();
            copy._fonts = new Dictionary<string, FontFamily>(_fonts, StringComparer.OrdinalIgnoreCase);
            copy._images = new Dictionary<string, PdfImage>(_images, StringComparer.OrdinalIgnoreCase);
            copy._fallbacks = new List<FontFamily>(_fallbacks);
            return copy;
        }
    }
}
