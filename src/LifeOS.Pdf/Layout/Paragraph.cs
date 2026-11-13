using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// A paragraph of text with mixed formatting, page-number fields and line breaks.
    /// Methods return the paragraph so calls can be chained.
    /// </summary>
    public sealed class Paragraph : Block
    {
        internal Paragraph()
        {
        }

        /// <summary>Character style for the whole paragraph (spans can override).</summary>
        public TextStyle? Style { get; set; }

        public ParagraphFormat Format { get; } = new ParagraphFormat();

        internal List<Inline> Inlines { get; } = new List<Inline>();

        /// <summary>Heading level 1–3, or 0 for body text.</summary>
        internal int HeadingLevel { get; set; }

        public Paragraph AddText(string text, TextStyle? style = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            Inlines.Add(new Inline(InlineKind.Text, text, style));
            return this;
        }

        /// <summary>Number of the page the text ends up on.</summary>
        public Paragraph AddPageNumber(TextStyle? style = null)
        {
            Inlines.Add(new Inline(InlineKind.PageNumber, string.Empty, style));
            return this;
        }

        /// <summary>Total number of pages in the document.</summary>
        public Paragraph AddPageCount(TextStyle? style = null)
        {
            Inlines.Add(new Inline(InlineKind.PageCount, string.Empty, style));
            return this;
        }

        /// <summary>Starts a new line within the paragraph (no paragraph spacing).</summary>
        public Paragraph AddLineBreak()
        {
            Inlines.Add(new Inline(InlineKind.LineBreak, "\n", null));
            return this;
        }
    }

    internal enum InlineKind
    {
        Text,
        PageNumber,
        PageCount,
        LineBreak,
    }

    internal sealed class Inline
    {
        public Inline(InlineKind kind, string text, TextStyle? style)
        {
            Kind = kind;
            Text = text;
            Style = style;
        }

        public InlineKind Kind { get; }

        public string Text { get; }

        public TextStyle? Style { get; }
    }
}
