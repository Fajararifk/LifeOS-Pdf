using System.Collections;
using System.Collections.Generic;

namespace LifeOS.Pdf.Navigation
{
    /// <summary>The top level of the document's bookmarks (outline).</summary>
    public sealed class PdfOutline : IEnumerable<PdfOutlineItem>
    {
        private readonly List<PdfOutlineItem> _items = new List<PdfOutlineItem>();

        public IReadOnlyList<PdfOutlineItem> Items => _items;

        public int Count => _items.Count;

        /// <summary>Adds a top-level bookmark and returns it (add children to it with <see cref="PdfOutlineItem.Add"/>).</summary>
        public PdfOutlineItem Add(string title, PdfDestination destination)
        {
            var item = new PdfOutlineItem(title, destination);
            _items.Add(item);
            return item;
        }

        public void Clear() => _items.Clear();

        public IEnumerator<PdfOutlineItem> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
