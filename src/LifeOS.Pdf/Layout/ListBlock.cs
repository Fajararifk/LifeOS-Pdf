using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Layout
{
    /// <summary>A bulleted or numbered list. Items are paragraphs; lists can be nested.</summary>
    public sealed class ListBlock : Block
    {
        internal ListBlock(ListKind kind)
        {
            Kind = kind;
        }

        public ListKind Kind { get; }

        /// <summary>First number of a numbered list. Default 1.</summary>
        public int StartNumber { get; set; } = 1;

        /// <summary>Distance from the marker to the item text, in points. Default 18.</summary>
        public double Indent { get; set; } = 18;

        internal List<ListItem> Items { get; } = new List<ListItem>();

        /// <summary>Adds an item and returns its paragraph for further formatting.</summary>
        public Paragraph AddItem(string text, TextStyle? style = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var p = new Paragraph();
            p.Format.SpaceAfter = 3;
            p.AddText(text, style);
            Items.Add(new ListItem(p));
            return p;
        }

        /// <summary>Adds a nested list under the last item.</summary>
        public ListBlock AddList(ListKind kind)
        {
            if (Items.Count == 0)
            {
                throw new InvalidOperationException("Add an item before adding a nested list.");
            }

            var child = new ListBlock(kind);
            Items[Items.Count - 1].Children.Add(child);
            return child;
        }
    }

    internal sealed class ListItem
    {
        public ListItem(Paragraph paragraph)
        {
            Paragraph = paragraph;
        }

        public Paragraph Paragraph { get; }

        public List<ListBlock> Children { get; } = new List<ListBlock>();
    }
}
