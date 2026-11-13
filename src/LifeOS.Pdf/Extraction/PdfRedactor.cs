using System;
using System.Collections.Generic;
using LifeOS.Pdf.Extraction.Engine;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>
    /// True redaction: content inside the areas is removed from the file — text (glyph by glyph,
    /// the rest of the line stays in place), images and inline images that touch an area, form
    /// XObjects (rewritten), and annotations — then the areas are covered with a box. Coordinates
    /// are those of the page as displayed (top-left origin), as returned by search and extraction.
    /// <code>
    /// var doc = PdfDocument.Open("ktp-scan-text.pdf");
    /// PdfRedactor.RedactText(doc, "3174012345678901");
    /// doc.Save("redacted.pdf");
    /// </code>
    /// Limitations: vector graphics under an area are covered, not removed; an image touching an area
    /// is removed completely.
    /// </summary>
    public static class PdfRedactor
    {
        /// <summary>Redacts one area. Returns the number of glyphs, images and forms removed or rewritten.</summary>
        public static int Redact(PdfPage page, PdfRect area, RedactionOptions? options = null) => Redact(page, new[] { area }, options);

        /// <summary>Redacts several areas of one page.</summary>
        public static int Redact(PdfPage page, IEnumerable<PdfRect> areas, RedactionOptions? options = null)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            if (areas == null)
            {
                throw new ArgumentNullException(nameof(areas));
            }

            options = options ?? new RedactionOptions();
            var list = new List<PdfRect>(areas);
            if (list.Count == 0)
            {
                return 0;
            }

            if (!page.IsPristine)
            {
                // Content drawn in memory must be redacted too: continue from a saved copy of the page.
                var owner = page.Document;
                var index = IndexOf(owner, page);
                page.AdoptSource(PdfDocument.Open(owner.ToArray()).Pages[index]);
            }

            var sp = page.Source!;
            var parser = sp.Parser;
            var geometry = new PageGeometry(sp.CropBox ?? sp.MediaBox, page.Rotation);
            var redactor = new Redactor(parser, geometry, list);
            var changes = new Dictionary<string, PdfStream?>();
            var ops = ContentReader.Read(PageAnalysis.ContentBytes(sp));
            var rewritten = redactor.Rewrite(ops, parser.Resolve(sp.Resources) as PdfDictionary, ContentInterpreter.Identity(), changes, 0);
            if (rewritten != null)
            {
                var replacement = new ContentOverride(ContentReader.Write(rewritten));
                foreach (var pair in changes)
                {
                    replacement.XObjects[pair.Key] = pair.Value;
                }

                page.ContentOverride = replacement;
            }

            var removed = redactor.Removed + RemoveAnnotations(page, geometry, list);
            page.ExtractionCache = null;

            if (options.FillColor.HasValue)
            {
                var g = page.Graphics;
                using (g.DisplayFrame())
                {
                    foreach (var area in list)
                    {
                        g.FillRectangle(options.FillColor.Value, area.X, area.Y, area.Width, area.Height);
                    }
                }
            }

            return removed;
        }

        /// <summary>Finds <paramref name="text"/> on every page and redacts each match. Returns the number of matches.</summary>
        public static int RedactText(PdfDocument document, string text, bool ignoreCase = true, RedactionOptions? options = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            options = options ?? new RedactionOptions();
            var matches = document.Search(text, ignoreCase);
            var byPage = new Dictionary<int, List<PdfRect>>();
            foreach (var m in matches)
            {
                if (!byPage.TryGetValue(m.PageIndex, out var rects))
                {
                    byPage[m.PageIndex] = rects = new List<PdfRect>();
                }

                foreach (var r in m.Rects)
                {
                    var p = options.Padding;
                    // Shrink horizontally a little inside the padding so neighbouring letters stay untouched.
                    rects.Add(new PdfRect(r.X + 0.5, r.Y - p, Math.Max(0.1, r.Width - 1), r.Height + 2 * p));
                }
            }

            foreach (var pair in byPage)
            {
                Redact(document.Pages[pair.Key], pair.Value, options);
            }

            if (options.ScrubMetadata)
            {
                var info = document.Info;
                info.Title = Scrub(info.Title, text, ignoreCase, options.Replacement);
                info.Author = Scrub(info.Author, text, ignoreCase, options.Replacement);
                info.Subject = Scrub(info.Subject, text, ignoreCase, options.Replacement);
                info.Keywords = Scrub(info.Keywords, text, ignoreCase, options.Replacement);
                info.Creator = Scrub(info.Creator, text, ignoreCase, options.Replacement);
                ScrubOutline(document.Outline.Items, text, ignoreCase, options.Replacement);
            }

            return matches.Count;
        }

        private static void ScrubOutline(IReadOnlyList<Navigation.PdfOutlineItem> items, string text, bool ignoreCase, string replacement)
        {
            foreach (var item in items)
            {
                item.Title = Scrub(item.Title, text, ignoreCase, replacement)!;
                ScrubOutline(item.Children, text, ignoreCase, replacement);
            }
        }

        private static string? Scrub(string? value, string text, bool ignoreCase, string replacement)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(text))
            {
                return value;
            }

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var sb = new System.Text.StringBuilder();
            var start = 0;
            int at;
            while ((at = value!.IndexOf(text, start, comparison)) >= 0)
            {
                sb.Append(value, start, at - start).Append(replacement);
                start = at + text.Length;
            }

            return sb.Append(value, start, value.Length - start).ToString();
        }

        private static int RemoveAnnotations(PdfPage page, PageGeometry geometry, List<PdfRect> areas)
        {
            var removed = 0;
            var sp = page.Source!;
            if (sp.Parser.Resolve(sp.Dictionary.Get("Annots")) is PdfArray annots)
            {
                foreach (var item in annots.Items)
                {
                    if (item is PdfSourceReference r && sp.Parser.Resolve(item) is PdfDictionary annot && sp.Document.Rect(annot.Get("Rect")) is double[] rect)
                    {
                        var box = geometry.Rect(rect[0], rect[1], rect[2], rect[3]);
                        if (areas.Exists(a => a.Intersects(box)) && page.RemovedAnnotations.Add(r.Number))
                        {
                            removed++;
                        }
                    }
                }
            }

            return removed;
        }

        private static int IndexOf(PdfDocument document, PdfPage page)
        {
            for (var i = 0; i < document.Pages.Count; i++)
            {
                if (ReferenceEquals(document.Pages[i], page))
                {
                    return i;
                }
            }

            throw new ArgumentException("The page was removed from its document.", nameof(page));
        }
    }
}
