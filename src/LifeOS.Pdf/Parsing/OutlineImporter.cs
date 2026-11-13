using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Navigation;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>Turns the bookmarks of an opened file into an editable <see cref="PdfOutline"/>.</summary>
    internal static class OutlineImporter
    {
        public static void Import(SourceDocument source, PdfDocument doc)
        {
            List<SourceOutlineItem> items;
            try
            {
                items = source.ReadOutline();
            }
            catch (PdfReadException ex) when (!(ex is PdfLimitException) && !(ex is PdfEncryptedException))
            {
                return; // damaged bookmarks are dropped; the pages still open
            }

            var pages = PageMap(doc);
            var stack = new Stack<(List<SourceOutlineItem> Items, PdfOutlineItem? Parent)>();
            stack.Push((items, null));
            while (stack.Count > 0)
            {
                var (list, parent) = stack.Pop();
                foreach (var item in list)
                {
                    var destination = ToDestination(source, item.Destination, pages) ?? PdfDestination.ToPage(doc.Pages[0]);
                    var title = item.Title.Length > 0 ? item.Title : "—";
                    var added = parent == null ? doc.Outline.Add(title, destination) : parent.Add(title, destination);
                    added.IsOpen = item.IsOpen;
                    added.Bold = item.Bold;
                    added.Italic = item.Italic;
                    if (item.Color != null)
                    {
                        added.Color = new PdfColor(Byte(item.Color[0]), Byte(item.Color[1]), Byte(item.Color[2]));
                    }

                    if (item.Children.Count > 0)
                    {
                        stack.Push((item.Children, added));
                    }
                }
            }
        }

        private static byte Byte(double v) => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);

        /// <summary>
        /// Converts an explicit destination array (§12.3.2.2) to a <see cref="PdfDestination"/> on the
        /// matching imported page; views without an equivalent fall back to the closest one.
        /// </summary>
        public static Dictionary<SourcePage, PdfPage> PageMap(PdfDocument doc)
        {
            var map = new Dictionary<SourcePage, PdfPage>();
            foreach (var p in doc.Pages)
            {
                if (p.Source != null && !map.ContainsKey(p.Source))
                {
                    map[p.Source] = p;
                }
            }

            return map;
        }

        public static PdfDestination? ToDestination(SourceDocument source, PdfArray? dest, Dictionary<SourcePage, PdfPage> pages)
        {
            if (dest == null || dest.Items.Count == 0)
            {
                return null;
            }

            PdfPage? page = null;
            if (dest.Items[0] is PdfSourceReference r && source.PageIndex.TryGetValue(r.Number, out var index))
            {
                pages.TryGetValue(source.Pages[index], out page);
            }
            else if (source.Parser.Resolve(dest.Items[0]) is PdfInteger pi && pi.Value >= 0 && pi.Value < source.Pages.Count)
            {
                pages.TryGetValue(source.Pages[(int)pi.Value], out page); // remote-style page index, seen in some files
            }

            if (page == null)
            {
                return null;
            }

            var view = dest.Items.Count > 1 && source.Parser.Resolve(dest.Items[1]) is PdfName n ? n.Value : "Fit";
            double? Arg(int i) => i < dest.Items.Count ? source.Number(dest.Items[i]) : null;
            double? Top(double? t) => t.HasValue ? page.OriginY + page.Height - t.Value : (double?)null;
            double? Left(double? l) => l.HasValue ? l.Value - page.OriginX : (double?)null;
            switch (view)
            {
                case "XYZ":
                    var zoom = Arg(4);
                    return PdfDestination.ToPage(page, Clamp(Top(Arg(3)), page.Height), Clamp(Left(Arg(2)), page.Width), zoom.HasValue && zoom.Value > 0 ? zoom : null);
                case "FitH":
                case "FitBH":
                    return PdfDestination.FitWidth(page, Clamp(Top(Arg(2)), page.Height));
                case "FitR":
                    return PdfDestination.ToPage(page, Clamp(Top(Arg(5)), page.Height), Clamp(Left(Arg(2)), page.Width));
                default:
                    return PdfDestination.FitPage(page);
            }
        }

        private static double? Clamp(double? v, double max) => v.HasValue ? Math.Max(0, Math.Min(max, v.Value)) : (double?)null;
    }
}
