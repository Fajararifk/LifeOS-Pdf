using System.Collections.Generic;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf.Manipulation
{
    /// <summary>Copies bookmarks between documents, following pages that were copied.</summary>
    internal static class OutlineCopier
    {
        /// <summary>
        /// Adds copies of <paramref name="items"/> whose target page is in <paramref name="map"/> through
        /// <paramref name="add"/>. Items pointing elsewhere are left out; their children move up a level.
        /// Named destinations (which do not travel between documents) go to <paramref name="fallback"/>.
        /// </summary>
        public static void Copy(IReadOnlyList<PdfOutlineItem> items, IDictionary<PdfPage, PdfPage> map, PdfPage? fallback,
            System.Func<string, PdfDestination, PdfOutlineItem> add)
        {
            foreach (var item in items)
            {
                PdfDestination? dest = null;
                if (item.Destination.Page != null && map.TryGetValue(item.Destination.Page, out var target))
                {
                    dest = item.Destination.WithPage(target);
                }
                else if (item.Destination.Page == null && fallback != null)
                {
                    dest = PdfDestination.ToPage(fallback);
                }

                if (dest == null)
                {
                    Copy(item.Children, map, fallback, add); // promote the children
                    continue;
                }

                var copy = add(item.Title, dest);
                copy.IsOpen = item.IsOpen;
                copy.Bold = item.Bold;
                copy.Italic = item.Italic;
                copy.Color = item.Color;
                Copy(item.Children, map, fallback, copy.Add);
            }
        }
    }
}
