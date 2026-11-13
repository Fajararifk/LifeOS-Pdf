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

        private string? _anchor;

        /// <summary>
        /// Name other links can jump to with <c>"#name"</c> (ASCII letters, digits, '-', '_', '.', ':').
        /// The paragraph's position becomes a named destination of the PDF.
        /// </summary>
        public string? Anchor
        {
            get => _anchor;
            set
            {
                if (value != null)
                {
                    Navigation.PdfDestination.ValidateName(value);
                }

                _anchor = value;
            }
        }

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

        /// <summary>
        /// Adds clickable text. <paramref name="target"/> is a web address (http, https, mailto, tel)
        /// or <c>"#name"</c> for a paragraph whose <see cref="Anchor"/> is <c>name</c>.
        /// The text uses <see cref="DocumentStyles.Link"/> with <paramref name="style"/> on top.
        /// </summary>
        public Paragraph AddLink(string text, string target, TextStyle? style = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            Inlines.Add(new Inline(InlineKind.Text, text, style) { Link = NormalizeTarget(target), LinkStyled = true });
            return this;
        }

        /// <summary>Plain text of the paragraph (fields left out), used for bookmarks and the table of contents.</summary>
        internal string PlainText
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var inline in Inlines)
                {
                    sb.Append(inline.Kind == InlineKind.Text ? inline.Text : inline.Kind == InlineKind.LineBreak ? " " : string.Empty);
                }

                return sb.ToString().Trim();
            }
        }

        internal static string NormalizeTarget(string target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.StartsWith("#", StringComparison.Ordinal))
            {
                Navigation.PdfDestination.ValidateName(target.Substring(1));
                return target;
            }

            return Annotations.LinkAnnotation.NormalizeUri(target);
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

        /// <summary>"#name" for an internal link, an escaped URI, or null.</summary>
        public string? Link { get; set; }

        /// <summary>Apply <see cref="DocumentStyles.Link"/> (blue, underlined).</summary>
        public bool LinkStyled { get; set; }
    }
}
