using System;
using System.Collections.Generic;
using LifeOS.Pdf.Annotations;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf
{
    /// <summary>One page of a <see cref="PdfDocument"/>. Create it with <see cref="PdfDocument.AddPage()"/>.</summary>
    public sealed class PdfPage
    {
        private readonly PdfDocument _document;

        private int _rotation;

        internal PdfPage(PdfDocument document, PageSize size, Parsing.SourcePage? source = null)
        {
            _document = document;
            Size = size;
            Source = source;
            Graphics = new PdfGraphics(this);
            if (source != null)
            {
                _rotation = source.Rotate;
                OriginX = source.MediaBox[0];
                OriginY = source.MediaBox[1];
            }
        }

        /// <summary>
        /// Clockwise rotation when the page is displayed: 0, 90, 180 or 270 (other multiples of 90 are
        /// normalized). Drawing coordinates are always those of the unrotated page.
        /// </summary>
        public int Rotation
        {
            get => _rotation;
            set
            {
                if (value % 90 != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Rotation must be a multiple of 90 degrees.");
                }

                _rotation = ((value % 360) + 360) % 360;
            }
        }

        /// <summary>True when the page comes from an opened PDF (its original content is kept).</summary>
        public bool IsImported => Source != null;

        /// <summary>The page this one was read from, or null for a page created here.</summary>
        internal Parsing.SourcePage? Source { get; }

        /// <summary>Lower-left corner of the media box (non-zero only for some imported pages).</summary>
        internal double OriginX { get; }

        internal double OriginY { get; }

        /// <summary>An imported page with nothing added: it can be copied or drawn as it is in the file.</summary>
        internal bool IsPristine => Source != null && Graphics.ContentLength == 0 && Annotations.Count == 0;

        /// <summary>True when something was drawn on the page through <see cref="Graphics"/>.</summary>
        internal bool HasNewContent => Graphics.ContentLength > 0;

        public PageSize Size { get; }

        /// <summary>Page width in points.</summary>
        public double Width => Size.Width;

        /// <summary>Page height in points.</summary>
        public double Height => Size.Height;

        /// <summary>Draw on this page (origin top-left, units in points).</summary>
        public PdfGraphics Graphics { get; }

        /// <summary>Resource name → font face, only the fonts this page uses.</summary>
        internal SortedDictionary<string, PdfFontFace> Fonts { get; } = new SortedDictionary<string, PdfFontFace>(StringComparer.Ordinal);

        /// <summary>Resource name → image, only the images this page uses.</summary>
        internal SortedDictionary<string, PdfImage> Images { get; } = new SortedDictionary<string, PdfImage>(StringComparer.Ordinal);

        /// <summary>Resource names of the graphics states (opacity) this page uses.</summary>
        internal SortedSet<string> GraphicsStates { get; } = new SortedSet<string>(StringComparer.Ordinal);

        internal PdfDocument Document => _document;

        /// <summary>Annotations in the order they were added (links, notes, highlights, stamps).</summary>
        internal List<Annotation> Annotations { get; } = new List<Annotation>();

        /// <summary>Makes the area (top-left x, y, width, height in points) a link to a web address (http, https, mailto, tel).</summary>
        public void AddLink(double x, double y, double width, double height, string uri)
        {
            RequireArea(x, y, width, height);
            Annotations.Add(new LinkAnnotation(x, y, width, height, LinkAnnotation.NormalizeUri(uri), null));
        }

        /// <summary>Makes the area a link to a place in this document.</summary>
        public void AddLink(double x, double y, double width, double height, PdfDestination destination)
        {
            RequireArea(x, y, width, height);
            Annotations.Add(new LinkAnnotation(x, y, width, height, null, destination ?? throw new ArgumentNullException(nameof(destination))));
        }

        /// <summary>Adds a sticky note icon at (x, y); the text appears when it is opened. Notes are not printed.</summary>
        public void AddNote(double x, double y, string text, string? author = null, PdfColor? color = null, NoteIcon icon = NoteIcon.Comment, bool open = false)
        {
            RequireArea(x, y, 0, 0);
            Annotations.Add(new NoteAnnotation(x, y, icon, open) { Contents = text ?? throw new ArgumentNullException(nameof(text)), Author = author, Color = color });
        }

        /// <summary>Highlights an area (like a marker pen over text), optionally with a comment.</summary>
        public void AddHighlight(double x, double y, double width, double height, PdfColor? color = null, string? comment = null, string? author = null)
        {
            RequireArea(x, y, width, height);
            Annotations.Add(new HighlightAnnotation(x, y, width, height) { Color = color, Contents = comment, Author = author });
        }

        /// <summary>Adds a rubber stamp such as "LUNAS", "DRAFT" or "APPROVED" in the box.</summary>
        public void AddStamp(double x, double y, double width, double height, string text, PdfColor? color = null)
        {
            RequireArea(x, y, width, height);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("A stamp needs text.", nameof(text));
            }

            // Font size: as large as fits in the box, at most half its height.
            var face = PdfFontFace.Standard(StandardFont.HelveticaBold);
            var unit = new PdfFont(face, 1).MeasureWidth(text);
            var size = Math.Max(4, Math.Min(height * 0.5, unit > 0 ? width * 0.82 / unit : height * 0.5));
            var font = new PdfFont(face, size);
            var name = _document.Resources.FontName(face, font.Map(text));
            Annotations.Add(new StampAnnotation(x, y, width, height, text, font, name) { Color = color, Contents = text });
        }

        private static void RequireArea(double x, double y, double width, double height)
        {
            PdfFormat.RequireFinite(x, nameof(x));
            PdfFormat.RequireFinite(y, nameof(y));
            PdfFormat.RequireNonNegative(width, nameof(width));
            PdfFormat.RequireNonNegative(height, nameof(height));
        }

        internal string UseFont(PdfFontFace face, TextRun run)
        {
            var name = _document.Resources.FontName(face, run);
            Fonts[name] = face;
            return name;
        }

        /// <summary>Resource name → imported page drawn as a form XObject.</summary>
        internal SortedDictionary<string, Parsing.SourcePage> Forms { get; } = new SortedDictionary<string, Parsing.SourcePage>(StringComparer.Ordinal);

        internal string UseForm(Parsing.SourcePage page)
        {
            var name = _document.Resources.FormName(page);
            Forms[name] = page;
            return name;
        }

        internal string UseImage(PdfImage image)
        {
            var name = _document.Resources.ImageName(image);
            Images[name] = image;
            return name;
        }

        internal string UseOpacity(double fill, double stroke)
        {
            var name = _document.Resources.GraphicsStateName(new OpacityState(fill, stroke));
            GraphicsStates.Add(name);
            return name;
        }
    }
}
