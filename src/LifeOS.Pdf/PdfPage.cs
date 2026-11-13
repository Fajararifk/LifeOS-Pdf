using System;
using System.Collections.Generic;
using LifeOS.Pdf.Annotations;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Forms;
using LifeOS.Pdf.Forms.Internal;
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
        internal Parsing.SourcePage? Source { get; private set; }

        /// <summary>Lower-left corner of the media box (non-zero only for some imported pages).</summary>
        internal double OriginX { get; private set; }

        internal double OriginY { get; private set; }

        /// <summary>An imported page with nothing added: it can be copied or drawn as it is in the file.</summary>
        internal bool IsPristine => Source != null && Graphics.ContentLength == 0 && Annotations.Count == 0 && ContentOverride == null && RemovedAnnotations.Count == 0 && FormWidgets.Count == 0;

        /// <summary>Rewritten content of an imported page (redaction) and the XObjects it replaces (null = removed).</summary>
        internal Extraction.Engine.ContentOverride? ContentOverride { get; set; }

        /// <summary>Annotations of the source page that are not copied (object numbers; redaction).</summary>
        internal HashSet<int> RemovedAnnotations { get; } = new HashSet<int>();

        /// <summary>
        /// Makes this page a copy of <paramref name="saved"/> (the same page in a saved snapshot): used before
        /// rewriting content, so that drawing added in memory is part of what gets rewritten.
        /// </summary>
        internal void AdoptSource(PdfPage saved)
        {
            Source = saved.Source;
            OriginX = saved.OriginX;
            OriginY = saved.OriginY;
            Graphics.Clear();
            Fonts.Clear();
            Images.Clear();
            Forms.Clear();
            GraphicsStates.Clear();
            Annotations.Clear();
            ContentOverride = null;
            RemovedAnnotations.Clear();
            ExtractionCache = null;
        }

        // ------------------------------------------------------------------ extraction (Fase 7)

        internal Extraction.Engine.PageAnalysis? ExtractionCache { get; set; }

        internal int ExtractionRotation { get; set; }

        /// <summary>
        /// The page text in reading order (lines top to bottom; see <see cref="Extraction.TextExtractionOptions"/>
        /// for areas, layout-preserving output and column detection).
        /// </summary>
        public string ExtractText(Extraction.TextExtractionOptions? options = null)
        {
            var lines = GetTextLines(options);
            return Extraction.Engine.TextAssembler.Text(new List<Extraction.TextLine>(lines), options?.Layout ?? false);
        }

        /// <summary>Lines of text in reading order, with their words and positions (page as displayed, top-left origin).</summary>
        public IReadOnlyList<Extraction.TextLine> GetTextLines(Extraction.TextExtractionOptions? options = null)
        {
            var analysis = Extraction.Engine.Analyzer.For(this);
            if (options == null)
            {
                return analysis.Lines;
            }

            return Extraction.Engine.TextAssembler.Lines(analysis.Glyphs, options.Area, options.IncludeInvisibleText, options.DetectColumns);
        }

        /// <summary>All words in reading order.</summary>
        public IReadOnlyList<Extraction.TextWord> GetWords(Extraction.TextExtractionOptions? options = null)
        {
            var words = new List<Extraction.TextWord>();
            foreach (var line in GetTextLines(options))
            {
                words.AddRange(line.Words);
            }

            return words;
        }

        /// <summary>Images drawn on the page (XObjects inside forms included; inline images too), in drawing order.</summary>
        public IReadOnlyList<Extraction.ExtractedImage> GetImages()
        {
            var analysis = Extraction.Engine.Analyzer.For(this);
            var images = new List<Extraction.ExtractedImage>();
            foreach (var (info, bounds) in analysis.Images)
            {
                images.Add(new Extraction.ExtractedImage(info, bounds, analysis.Parser));
            }

            return images;
        }

        /// <summary>Tables on the page (ruled grids and text aligned in columns), top to bottom.</summary>
        public IReadOnlyList<Extraction.ExtractedTable> ExtractTables(Extraction.TableExtractionOptions? options = null) =>
            Extraction.Engine.TableDetector.Find(Extraction.Engine.Analyzer.For(this), options ?? new Extraction.TableExtractionOptions());

        /// <summary>Every glyph in the order the page draws them.</summary>
        public IReadOnlyList<Extraction.TextGlyph> GetGlyphs() => Extraction.Engine.Analyzer.For(this).Glyphs;

        /// <summary>Finds <paramref name="text"/> on this page (whitespace in the query matches any space or line break).</summary>
        public IReadOnlyList<Extraction.SearchResult> Search(string text, bool ignoreCase = true)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var index = 0;
            for (var i = 0; i < _document.Pages.Count; i++)
            {
                if (ReferenceEquals(_document.Pages[i], this))
                {
                    index = i;
                }
            }

            return Extraction.Engine.TextSearch.Find(Extraction.Engine.Analyzer.For(this).Lines, index, text, ignoreCase);
        }

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

        /// <summary>Form field widgets on this page, in creation order.</summary>
        internal List<FormWidget> FormWidgets { get; } = new List<FormWidget>();

        /// <summary>Object numbers of source widget annotations owned by the form (not copied).</summary>
        internal HashSet<int> FormWidgetNumbers { get; } = new HashSet<int>();

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

        // ------------------------------------------------------------------ form fields (Fase 8)

        /// <summary>Adds a text field (single- or multi-line) at the given area.</summary>
        public PdfTextField AddTextField(string name, double x, double y, double width, double height)
        {
            RequireArea(x, y, width, height);
            var field = new PdfTextField(name);
            field.AddWidget(this, x, y, width, height);
            Document.Form.Add(field);
            return field;
        }

        /// <summary>Adds a checkbox.</summary>
        public PdfCheckBox AddCheckBox(string name, double x, double y, double width, double height)
        {
            RequireArea(x, y, width, height);
            var field = new PdfCheckBox(name);
            field.AddWidget(this, x, y, width, height);
            Document.Form.Add(field);
            return field;
        }

        /// <summary>Adds a radio button to a group; buttons sharing a name form one field.</summary>
        public PdfRadioButton AddRadioButton(string groupName, string onValue, double x, double y, double width, double height)
        {
            RequireArea(x, y, width, height);
            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            if (string.IsNullOrEmpty(onValue))
            {
                throw new ArgumentException("A radio button needs an on-state name.", nameof(onValue));
            }

            var existing = Document.Form.GetField(groupName);
            PdfRadioButton field;
            if (existing != null)
            {
                if (!(existing is PdfRadioButton radio))
                {
                    throw new ArgumentException("Field '" + groupName + "' already exists and is not a radio group.", nameof(groupName));
                }

                field = radio;
            }
            else
            {
                field = new PdfRadioButton(groupName);
                Document.Form.Add(field);
            }

            field.AddWidget(this, x, y, width, height).OnState = onValue;
            return field;
        }

        /// <summary>Adds an editable combo box.</summary>
        public PdfChoiceField AddComboBox(string name, double x, double y, double width, double height, params string[] options)
        {
            RequireArea(x, y, width, height);
            var field = new PdfChoiceField(name, combo: true);
            foreach (var option in options)
            {
                field.AddOption(new PdfChoiceOption(option));
            }

            field.AddWidget(this, x, y, width, height);
            Document.Form.Add(field);
            return field;
        }

        /// <summary>Adds a list box.</summary>
        public PdfChoiceField AddListBox(string name, double x, double y, double width, double height, params string[] options)
        {
            RequireArea(x, y, width, height);
            var field = new PdfChoiceField(name, combo: false) { MultiSelect = true };
            foreach (var option in options)
            {
                field.AddOption(new PdfChoiceOption(option));
            }

            field.AddWidget(this, x, y, width, height);
            Document.Form.Add(field);
            return field;
        }

        /// <summary>Adds a push button with a caption.</summary>
        public PdfButton AddButton(string name, double x, double y, double width, double height, string caption)
        {
            RequireArea(x, y, width, height);
            var field = new PdfButton(name) { Caption = caption ?? string.Empty };
            field.AddWidget(this, x, y, width, height);
            Document.Form.Add(field);
            return field;
        }

        /// <summary>Adds a signature field (placeholder; signing arrives in Fase 9).</summary>
        public PdfSignatureField AddSignatureField(string name, double x, double y, double width, double height)
        {
            RequireArea(x, y, width, height);
            var field = new PdfSignatureField(name);
            field.AddWidget(this, x, y, width, height);
            Document.Form.Add(field);
            return field;
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
