using System;
using System.Collections;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Navigation
{
    /// <summary>A bookmark in the viewer's outline panel (ISO 32000-1 §12.3.3). Can contain child bookmarks.</summary>
    public sealed class PdfOutlineItem : IEnumerable<PdfOutlineItem>
    {
        private readonly List<PdfOutlineItem> _children = new List<PdfOutlineItem>();

        internal PdfOutlineItem(string title, PdfDestination destination)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        }

        /// <summary>Text shown in the panel (any Unicode text).</summary>
        public string Title { get; set; }

        public PdfDestination Destination { get; set; }

        /// <summary>Children are shown expanded when the document opens. Default true.</summary>
        public bool IsOpen { get; set; } = true;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>Text color in the panel (supported by Acrobat and some other viewers).</summary>
        public PdfColor? Color { get; set; }

        public IReadOnlyList<PdfOutlineItem> Children => _children;

        /// <summary>Adds a child bookmark and returns it.</summary>
        public PdfOutlineItem Add(string title, PdfDestination destination)
        {
            var item = new PdfOutlineItem(title, destination);
            _children.Add(item);
            return item;
        }

        public IEnumerator<PdfOutlineItem> GetEnumerator() => _children.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
