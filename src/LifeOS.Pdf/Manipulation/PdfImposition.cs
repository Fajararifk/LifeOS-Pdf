using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>
    /// Arranges pages on sheets: several pages per sheet (n-up), booklets for saddle-stitch
    /// printing, and letterheads or backgrounds drawn behind every page. Pages are placed as
    /// form XObjects, so they stay vector and sharp.
    /// </summary>
    public static class PdfImposition
    {
        /// <summary>
        /// <paramref name="columns"/> × <paramref name="rows"/> pages per sheet, each scaled to fit its cell
        /// and centred. Default sheet: A4, landscape when there are more columns than rows.
        /// </summary>
        public static PdfDocument NUp(PdfDocument source, int columns, int rows, PageSize? sheet = null, double margin = 18, double gap = 12, PdfPen? border = null)
        {
            RequireSource(source);
            if (columns < 1 || rows < 1 || columns * rows > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Columns and rows must be at least 1, with at most 64 pages per sheet.");
            }

            RequireSpacing(margin, gap);
            var size = sheet ?? (columns > rows ? PageSize.A4.Landscape : PageSize.A4);
            var pages = Snapshot(source);
            var result = new PdfDocument();
            CopyInfo(source, result);
            var cellW = (size.Width - 2 * margin - (columns - 1) * gap) / columns;
            var cellH = (size.Height - 2 * margin - (rows - 1) * gap) / rows;
            if (cellW < 10 || cellH < 10)
            {
                throw new ArgumentException("The margin and gap leave no room for the pages on a " + size + " sheet.");
            }

            PdfGraphics? g = null;
            for (var i = 0; i < pages.Count; i++)
            {
                var slot = i % (columns * rows);
                if (slot == 0)
                {
                    g = result.AddPage(size).Graphics;
                }

                var x = margin + slot % columns * (cellW + gap);
                var y = margin + slot / columns * (cellH + gap);
                Place(g!, pages[i], x, y, cellW, cellH, border);
            }

            return result;
        }

        /// <summary>
        /// A booklet for saddle-stitch printing: two pages side by side per sheet side, in the order
        /// that gives the right sequence after printing double-sided (flip on the short edge),
        /// folding and stapling. Blank pages are added to reach a multiple of 4.
        /// </summary>
        public static PdfDocument Booklet(PdfDocument source, PageSize? sheet = null)
        {
            RequireSource(source);
            var size = (sheet ?? PageSize.A4).Landscape;
            var pages = Snapshot(source);
            var n = (pages.Count + 3) / 4 * 4;
            var result = new PdfDocument();
            CopyInfo(source, result);
            var half = size.Width / 2;
            for (var s = 0; s < n / 4; s++)
            {
                // Front: last-unused | first-unused; back: second | second-to-last.
                Side(result, size, half, pages, n - 1 - 2 * s, 2 * s);
                Side(result, size, half, pages, 2 * s + 1, n - 2 - 2 * s);
            }

            return result;
        }

        /// <summary>
        /// Draws <paramref name="background"/> (e.g. the first page of a letterhead PDF) behind the content of
        /// each selected page, stretched to the page as displayed.
        /// </summary>
        public static void Overlay(PdfDocument document, PdfPage background, bool behind = true, PageRange? pages = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (background == null)
            {
                throw new ArgumentNullException(nameof(background));
            }

            var bg = Snapshot(background.Document)[IndexOf(background)];
            foreach (var page in PdfStamper.Selected(document, pages))
            {
                var g = page.Graphics;
                void Draw()
                {
                    using (g.DisplayFrame())
                    {
                        g.DrawPage(bg, 0, 0, g.DisplayWidth, g.DisplayHeight);
                    }
                }

                if (behind)
                {
                    g.DrawBehind(Draw);
                }
                else
                {
                    Draw();
                }
            }
        }

        private static void Side(PdfDocument result, PageSize size, double half, IReadOnlyList<PdfPage> pages, int left, int right)
        {
            var g = result.AddPage(size).Graphics;
            if (left < pages.Count)
            {
                Place(g, pages[left], 0, 0, half, size.Height, null);
            }

            if (right < pages.Count)
            {
                Place(g, pages[right], half, 0, half, size.Height, null);
            }
        }

        /// <summary>Scales a page (as displayed) to fit the cell, keeping its proportions, centred.</summary>
        private static void Place(PdfGraphics g, PdfPage page, double x, double y, double w, double h, PdfPen? border)
        {
            var sp = page.Source!;
            var box = sp.CropBox ?? sp.MediaBox;
            var pw = box[2] - box[0];
            var ph = box[3] - box[1];
            if (page.Rotation % 180 != 0)
            {
                var t = pw;
                pw = ph;
                ph = t;
            }

            var scale = Math.Min(w / pw, h / ph);
            var dw = pw * scale;
            var dh = ph * scale;
            var dx = x + (w - dw) / 2;
            var dy = y + (h - dh) / 2;
            g.DrawPage(page, dx, dy, dw, dh);
            if (border != null)
            {
                g.DrawRectangle(border, dx, dy, dw, dh);
            }
        }

        /// <summary>The pages as imported pages (a saved copy when some were drawn on or created here).</summary>
        private static IReadOnlyList<PdfPage> Snapshot(PdfDocument source)
        {
            foreach (var p in source.Pages)
            {
                if (!p.IsPristine)
                {
                    return PdfDocument.Open(source.ToArray()).Pages;
                }
            }

            return source.Pages;
        }

        private static int IndexOf(PdfPage page)
        {
            var pages = page.Document.Pages;
            for (var i = 0; i < pages.Count; i++)
            {
                if (ReferenceEquals(pages[i], page))
                {
                    return i;
                }
            }

            throw new ArgumentException("The page was removed from its document.", nameof(page));
        }

        private static void CopyInfo(PdfDocument from, PdfDocument to) => PdfSplitter.CopyInfo(from.Info, to.Info);

        private static void RequireSource(PdfDocument source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
        }

        private static void RequireSpacing(double margin, double gap)
        {
            if (margin < 0 || gap < 0 || double.IsNaN(margin) || double.IsNaN(gap) || double.IsInfinity(margin) || double.IsInfinity(gap))
            {
                throw new ArgumentOutOfRangeException(nameof(margin), "Margin and gap must be zero or positive.");
            }
        }
    }
}
