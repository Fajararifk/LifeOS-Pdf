using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LifeOS.Pdf.Annotations;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Navigation;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Writes the interactive parts of a document: destinations, outline, name trees,
    /// page labels, viewer preferences, annotations and embedded files (ISO 32000-1 §12).
    /// </summary>
    internal sealed class InteractiveWriter
    {
        private readonly PdfDocument _document;
        private readonly Dictionary<PdfPage, PdfIndirectObject> _pages;
        private readonly Func<PdfIndirectObject> _new;
        private readonly Dictionary<PdfFontFace, PdfIndirectObject> _fonts;

        public InteractiveWriter(PdfDocument document, Dictionary<PdfPage, PdfIndirectObject> pages, Func<PdfIndirectObject> newObject,
            Dictionary<PdfFontFace, PdfIndirectObject> fonts)
        {
            _document = document;
            _pages = pages;
            _new = newObject;
            _fonts = fonts;
        }

        /// <summary>A destination as written in /Dest: an explicit array, or a named-destination string.</summary>
        public PdfObject Destination(PdfDestination destination)
        {
            if (destination.Name != null)
            {
                if (!_document.NamedDestinations.ContainsKey(destination.Name))
                {
                    throw new InvalidOperationException("A link or bookmark points to the named destination '" + destination.Name +
                        "', which is not defined. Add it to PdfDocument.NamedDestinations (or set a paragraph Anchor).");
                }

                return PdfString.Text(destination.Name);
            }

            var page = destination.Page!;
            if (!_pages.TryGetValue(page, out var pageObj))
            {
                throw new InvalidOperationException("A link or bookmark points to a page that does not belong to this document.");
            }

            var array = new PdfArray(pageObj.Reference);
            switch (destination.View)
            {
                case DestinationView.Fit:
                    array.Items.Add(new PdfName("Fit"));
                    break;
                case DestinationView.FitWidth:
                    array.Items.Add(new PdfName("FitH"));
                    array.Items.Add(Coordinate(destination.Top.HasValue ? page.OriginY + page.Height - destination.Top.Value : (double?)null));
                    break;
                default:
                    array.Items.Add(new PdfName("XYZ"));
                    array.Items.Add(Coordinate(destination.Left.HasValue ? page.OriginX + destination.Left.Value : (double?)null));
                    array.Items.Add(Coordinate(destination.Top.HasValue ? page.OriginY + page.Height - destination.Top.Value : (double?)null));
                    array.Items.Add(Coordinate(destination.Zoom));
                    break;
            }

            return array;
        }

        /// <summary>Annotation objects of a page, or null when it has none.</summary>
        public PdfArray? Annotations(PdfPage page)
        {
            if (page.Annotations.Count == 0)
            {
                return null;
            }

            var pageRef = _pages[page].Reference;
            var array = new PdfArray();
            foreach (var annotation in page.Annotations)
            {
                var obj = _new();
                var left = page.OriginX + annotation.X;
                var bottom = page.OriginY + page.Height - annotation.Y - annotation.Height;
                var dict = new PdfDictionary()
                    .Set("Type", "Annot")
                    .Set("Subtype", annotation.Subtype)
                    .Set("Rect", PdfArray.OfNumbers(left, bottom, left + annotation.Width, bottom + annotation.Height))
                    .Set("P", pageRef);
                if (annotation.Flags != 0)
                {
                    dict.Set("F", annotation.Flags);
                }

                if (!string.IsNullOrEmpty(annotation.Contents))
                {
                    dict.Set("Contents", PdfString.Text(annotation.Contents!));
                }

                if (!string.IsNullOrEmpty(annotation.Author))
                {
                    dict.Set("T", PdfString.Text(annotation.Author!));
                }

                if (annotation.Color.HasValue)
                {
                    var c = annotation.Color.Value;
                    dict.Set("C", PdfArray.OfNumbers(c.R / 255.0, c.G / 255.0, c.B / 255.0));
                }

                switch (annotation)
                {
                    case LinkAnnotation link:
                        dict.Set("Border", PdfArray.OfNumbers(0, 0, 0)); // invisible link area
                        if (link.Uri != null)
                        {
                            dict.Set("A", new PdfDictionary().Set("Type", "Action").Set("S", "URI").Set("URI", PdfString.Text(link.Uri)));
                        }
                        else
                        {
                            dict.Set("Dest", Destination(link.Destination!));
                        }

                        break;
                    case NoteAnnotation note:
                        dict.Set("Name", note.Icon.ToString());
                        dict.Set("Open", note.Open ? PdfBoolean.True : PdfBoolean.False);
                        break;
                    case HighlightAnnotation _:
                        // QuadPoints in the order Acrobat writes: upper-left, upper-right, lower-left, lower-right.
                        var top = bottom + annotation.Height;
                        dict.Set("QuadPoints", PdfArray.OfNumbers(left, top, left + annotation.Width, top, left, bottom, left + annotation.Width, bottom));
                        break;
                }

                var appearance = annotation.CreateAppearance();
                if (appearance != null)
                {
                    dict.Set("AP", new PdfDictionary().Set("N", AppearanceStream(appearance, annotation.Width, annotation.Height)));
                }

                obj.Value = dict;
                array.Items.Add(obj.Reference);
            }

            return array;
        }

        /// <summary>Adds outline, names, page labels, viewer preferences and open action to the catalog.</summary>
        public void Complete(PdfDictionary catalog)
        {
            var prefs = _document.ViewerPreferences;
            var outline = Outline();
            if (outline != null)
            {
                catalog.Set("Outlines", outline);
            }

            var names = new PdfDictionary();
            if (_document.NamedDestinations.Count > 0)
            {
                var entries = new List<KeyValuePair<string, PdfObject>>();
                foreach (var pair in _document.NamedDestinations)
                {
                    PdfDestination.ValidateName(pair.Key);
                    if (pair.Value.Name != null)
                    {
                        throw new InvalidOperationException("Named destination '" + pair.Key + "' must point to a page, not to another name.");
                    }

                    entries.Add(new KeyValuePair<string, PdfObject>(pair.Key, Destination(pair.Value)));
                }

                names.Set("Dests", NameTree(entries));
            }

            if (_document.Attachments.Count > 0)
            {
                var entries = _document.Attachments.Select(a => new KeyValuePair<string, PdfObject>(a.Name, FileSpecification(a))).ToList();
                names.Set("EmbeddedFiles", NameTree(entries));
            }

            if (names.Count > 0)
            {
                catalog.Set("Names", names);
            }

            if (_document.PageLabels.Count > 0)
            {
                catalog.Set("PageLabels", PageLabels());
            }

            var pageMode = prefs.PageMode == PageMode.Default && outline != null ? PageMode.Outlines : prefs.PageMode;
            switch (pageMode)
            {
                case PageMode.None: catalog.Set("PageMode", "UseNone"); break;
                case PageMode.Outlines: catalog.Set("PageMode", "UseOutlines"); break;
                case PageMode.Thumbnails: catalog.Set("PageMode", "UseThumbs"); break;
                case PageMode.Attachments: catalog.Set("PageMode", "UseAttachments"); break;
                case PageMode.FullScreen: catalog.Set("PageMode", "FullScreen"); break;
            }

            if (prefs.PageLayout != PageLayout.Default)
            {
                catalog.Set("PageLayout", prefs.PageLayout.ToString());
            }

            if (prefs.InitialZoom != InitialZoom.Default || prefs.InitialPage != 0)
            {
                if (prefs.InitialPage >= _document.Pages.Count)
                {
                    throw new InvalidOperationException("ViewerPreferences.InitialPage is " + prefs.InitialPage + " but the document has " + _document.Pages.Count + " page(s).");
                }

                var page = _document.Pages[prefs.InitialPage];
                PdfDestination open;
                switch (prefs.InitialZoom)
                {
                    case InitialZoom.FitPage: open = PdfDestination.FitPage(page); break;
                    case InitialZoom.FitWidth: open = PdfDestination.FitWidth(page); break;
                    case InitialZoom.Percent: open = PdfDestination.ToPage(page, zoom: prefs.ZoomPercent / 100.0); break;
                    default: open = PdfDestination.ToPage(page); break;
                }

                catalog.Set("OpenAction", Destination(open));
            }

            var vp = new PdfDictionary();
            if (prefs.DisplayDocTitle && !string.IsNullOrEmpty(_document.Info.Title))
            {
                vp.Set("DisplayDocTitle", PdfBoolean.True);
            }

            if (prefs.HideToolbar)
            {
                vp.Set("HideToolbar", PdfBoolean.True);
            }

            if (prefs.HideMenubar)
            {
                vp.Set("HideMenubar", PdfBoolean.True);
            }

            if (prefs.HideWindowUI)
            {
                vp.Set("HideWindowUI", PdfBoolean.True);
            }

            if (prefs.FitWindow)
            {
                vp.Set("FitWindow", PdfBoolean.True);
            }

            if (prefs.CenterWindow)
            {
                vp.Set("CenterWindow", PdfBoolean.True);
            }

            if (vp.Count > 0)
            {
                catalog.Set("ViewerPreferences", vp);
            }
        }

        private PdfReference? Outline()
        {
            if (_document.Outline.Count == 0)
            {
                return null;
            }

            var root = _new();
            var dict = new PdfDictionary().Set("Type", "Outlines");
            WriteChildren(root, dict, _document.Outline.Items, 0);
            dict.Set("Count", _document.Outline.Items.Sum(i => 1 + (i.IsOpen ? VisibleDescendants(i) : 0)));
            root.Value = dict;
            return root.Reference;
        }

        private void WriteChildren(PdfIndirectObject parent, PdfDictionary parentDict, IReadOnlyList<PdfOutlineItem> items, int depth)
        {
            if (depth > 64)
            {
                throw new InvalidOperationException("Bookmarks are nested more than 64 levels deep.");
            }

            var objects = items.Select(_ => _new()).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var dict = new PdfDictionary()
                    .Set("Title", PdfString.Text(item.Title))
                    .Set("Parent", parent.Reference)
                    .Set("Dest", Destination(item.Destination));
                if (i > 0)
                {
                    dict.Set("Prev", objects[i - 1].Reference);
                }

                if (i < items.Count - 1)
                {
                    dict.Set("Next", objects[i + 1].Reference);
                }

                if (item.Children.Count > 0)
                {
                    WriteChildren(objects[i], dict, item.Children, depth + 1);
                    var visible = VisibleDescendants(item);
                    // §12.3.3: positive = open (visible descendants), negative = closed.
                    dict.Set("Count", item.IsOpen ? visible : -visible);
                }

                if (item.Color.HasValue)
                {
                    var c = item.Color.Value;
                    dict.Set("C", PdfArray.OfNumbers(c.R / 255.0, c.G / 255.0, c.B / 255.0));
                }

                var flags = (item.Italic ? 1 : 0) | (item.Bold ? 2 : 0);
                if (flags != 0)
                {
                    dict.Set("F", flags);
                }

                objects[i].Value = dict;
            }

            if (objects.Count > 0)
            {
                parentDict.Set("First", objects[0].Reference);
                parentDict.Set("Last", objects[objects.Count - 1].Reference);
            }
        }

        /// <summary>Descendants shown when the item is open: each child plus the visible descendants of open children.</summary>
        private static int VisibleDescendants(PdfOutlineItem item) =>
            item.Children.Sum(child => 1 + (child.IsOpen ? VisibleDescendants(child) : 0));

        /// <summary>A name tree with a single leaf (§7.9.6); keys sorted by their encoded bytes.</summary>
        private static PdfDictionary NameTree(List<KeyValuePair<string, PdfObject>> entries)
        {
            var sorted = entries.Select(e => new { Key = PdfString.Text(e.Key), e.Value })
                .OrderBy(e => e.Key.Bytes, ByteComparer.Instance)
                .ToList();
            var names = new PdfArray();
            foreach (var e in sorted)
            {
                names.Items.Add(e.Key);
                names.Items.Add(e.Value);
            }

            return new PdfDictionary().Set("Names", names);
        }

        private PdfDictionary PageLabels()
        {
            var nums = new PdfArray();
            var ranges = _document.PageLabels.Ranges.ToList();
            if (ranges[0].Key != 0)
            {
                // Viewers expect a range starting at the first page.
                nums.Items.Add(new PdfInteger(0));
                nums.Items.Add(new PdfDictionary().Set("S", "D"));
            }

            foreach (var range in ranges)
            {
                if (range.Key >= _document.Pages.Count)
                {
                    throw new InvalidOperationException("A page label range starts at page index " + range.Key + " but the document has " + _document.Pages.Count + " page(s).");
                }

                var dict = new PdfDictionary();
                switch (range.Value.Style)
                {
                    case PageLabelStyle.Decimal: dict.Set("S", "D"); break;
                    case PageLabelStyle.UpperRoman: dict.Set("S", "R"); break;
                    case PageLabelStyle.LowerRoman: dict.Set("S", "r"); break;
                    case PageLabelStyle.UpperLetters: dict.Set("S", "A"); break;
                    case PageLabelStyle.LowerLetters: dict.Set("S", "a"); break;
                }

                if (!string.IsNullOrEmpty(range.Value.Prefix))
                {
                    dict.Set("P", PdfString.Text(range.Value.Prefix!));
                }

                if (range.Value.FirstNumber != 1)
                {
                    dict.Set("St", range.Value.FirstNumber);
                }

                nums.Items.Add(new PdfInteger(range.Key));
                nums.Items.Add(dict);
            }

            return new PdfDictionary().Set("Nums", nums);
        }

        private PdfReference FileSpecification(Attachments.PdfAttachment attachment)
        {
            byte[] checksum;
            using (var md5 = MD5.Create())
            {
                checksum = md5.ComputeHash(attachment.Data);
            }

            var parameters = new PdfDictionary().Set("Size", attachment.Size).Set("CheckSum", PdfString.Hex(checksum));
            if (attachment.ModificationDate.HasValue)
            {
                parameters.Set("ModDate", PdfString.Text(PdfFormat.Date(attachment.ModificationDate.Value)));
            }

            var fileDict = new PdfDictionary().Set("Type", "EmbeddedFile").Set("Filter", "FlateDecode").Set("Params", parameters);
            if (attachment.MimeType != null)
            {
                fileDict.Set("Subtype", attachment.MimeType); // a name; '/' is escaped as #2F by the writer
            }

            var file = _new();
            file.Value = new PdfStream(fileDict, Zlib.Compress(attachment.Data));

            var spec = _new();
            var specDict = new PdfDictionary()
                .Set("Type", "Filespec")
                .Set("F", PdfString.Text(AsciiFileName(attachment.Name)))
                .Set("UF", PdfString.Text(attachment.Name))
                .Set("EF", new PdfDictionary().Set("F", file.Reference).Set("UF", file.Reference));
            if (!string.IsNullOrEmpty(attachment.Description))
            {
                specDict.Set("Desc", PdfString.Text(attachment.Description!));
            }

            spec.Value = specDict;
            return spec.Reference;
        }

        /// <summary>/F must be a portable byte string: non-ASCII characters become '_' (the full name is in /UF).</summary>
        private static string AsciiFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(c >= 0x20 && c < 0x7F ? c : '_');
            }

            return sb.ToString();
        }

        private PdfReference AppearanceStream(Appearance appearance, double width, double height)
        {
            var resources = new PdfDictionary();
            if (appearance.Fonts.Count > 0)
            {
                var fonts = new PdfDictionary();
                foreach (var pair in appearance.Fonts)
                {
                    fonts.Set(pair.Key, _fonts[pair.Value].Reference);
                }

                resources.Set("Font", fonts);
            }

            if (appearance.BlendMode != null)
            {
                resources.Set("ExtGState", new PdfDictionary().Set("GS0", new PdfDictionary().Set("Type", "ExtGState").Set("BM", appearance.BlendMode)));
            }

            var dict = new PdfDictionary()
                .Set("Type", "XObject")
                .Set("Subtype", "Form")
                .Set("BBox", PdfArray.OfNumbers(0, 0, width, height))
                .Set("Resources", resources)
                .Set("Filter", "FlateDecode");
            var obj = _new();
            obj.Value = new PdfStream(dict, Zlib.Compress(Encoding.ASCII.GetBytes(appearance.Content.ToString())));
            return obj.Reference;
        }

        private static PdfObject Coordinate(double? value) => value.HasValue ? new PdfReal(value.Value) : (PdfObject)PdfNull.Instance;

        private sealed class ByteComparer : IComparer<byte[]>
        {
            public static readonly ByteComparer Instance = new ByteComparer();

            public int Compare(byte[]? x, byte[]? y)
            {
                var a = x ?? Array.Empty<byte>();
                var b = y ?? Array.Empty<byte>();
                for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
                {
                    if (a[i] != b[i])
                    {
                        return a[i].CompareTo(b[i]);
                    }
                }

                return a.Length.CompareTo(b.Length);
            }
        }
    }
}
