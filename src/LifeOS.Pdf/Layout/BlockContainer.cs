using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// Something that holds a vertical sequence of blocks: a <see cref="Section"/>, a
    /// header or footer, or a table cell. All of them share these Add methods.
    /// </summary>
    public abstract class BlockContainer
    {
        internal BlockContainer()
        {
        }

        public IReadOnlyList<Block> Blocks => BlockList;

        internal List<Block> BlockList { get; } = new List<Block>();

        internal virtual bool AllowsPageBreaks => true;

        public Paragraph AddParagraph(string? text = null, TextStyle? style = null)
        {
            var p = new Paragraph();
            if (text != null)
            {
                p.AddText(text, style);
            }
            else if (style != null)
            {
                p.Style = style;
            }

            BlockList.Add(p);
            return p;
        }

        /// <summary>Adds a heading (level 1–3) styled by <see cref="DocumentStyles"/>; it stays with the next block.</summary>
        public Paragraph AddHeading(string text, int level = 1)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (level < 1 || level > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1, 2 or 3.");
            }

            var p = new Paragraph { HeadingLevel = level };
            p.Format.KeepWithNext = true;
            p.AddText(text);
            BlockList.Add(p);
            return p;
        }

        public ListBlock AddList(ListKind kind = ListKind.Bullet)
        {
            var list = new ListBlock(kind);
            BlockList.Add(list);
            return list;
        }

        /// <summary>Adds a table. Define its columns with <see cref="Table.AddColumn"/> before adding rows.</summary>
        public Table AddTable()
        {
            var table = new Table();
            BlockList.Add(table);
            return table;
        }

        public ImageBlock AddImage(PdfImage image, double? width = null, ParagraphAlignment alignment = ParagraphAlignment.Left)
        {
            var block = new ImageBlock(image, width, alignment);
            BlockList.Add(block);
            return block;
        }

        /// <summary>Adds a QR code, <paramref name="size"/> points square (quiet zone included).</summary>
        public BarcodeBlock AddQrCode(Barcodes.QrCode qr, double size = 90, ParagraphAlignment alignment = ParagraphAlignment.Left)
        {
            if (qr == null)
            {
                throw new ArgumentNullException(nameof(qr));
            }

            Internal.PdfFormat.RequireNonNegative(size, nameof(size));
            var block = new BarcodeBlock(qr, null, size, size, alignment);
            BlockList.Add(block);
            return block;
        }

        /// <summary>Encodes <paramref name="text"/> as a QR code (error correction Medium) and adds it.</summary>
        public BarcodeBlock AddQrCode(string text, double size = 90, ParagraphAlignment alignment = ParagraphAlignment.Left) =>
            AddQrCode(Barcodes.QrCode.Encode(text), size, alignment);

        /// <summary>Adds a linear barcode; the height includes the text line when <see cref="BarcodeBlock.ShowText"/> is on.</summary>
        public BarcodeBlock AddBarcode(Barcodes.Barcode barcode, double width = 180, double height = 50, ParagraphAlignment alignment = ParagraphAlignment.Left)
        {
            if (barcode == null)
            {
                throw new ArgumentNullException(nameof(barcode));
            }

            Internal.PdfFormat.RequireNonNegative(width, nameof(width));
            Internal.PdfFormat.RequireNonNegative(height, nameof(height));
            var block = new BarcodeBlock(null, barcode, width, height, alignment);
            BlockList.Add(block);
            return block;
        }

        public SpacerBlock AddSpacer(double height)
        {
            var block = new SpacerBlock(height);
            BlockList.Add(block);
            return block;
        }

        public HorizontalLineBlock AddHorizontalLine(PdfPen? pen = null)
        {
            var block = new HorizontalLineBlock(pen ?? new PdfPen(PdfColor.FromHex("#D1D5DB"), 0.75));
            BlockList.Add(block);
            return block;
        }

        /// <summary>
        /// Adds a table of contents listing headings up to <paramref name="maxLevel"/> with dot leaders,
        /// page numbers and clickable entries. Only allowed in a section body.
        /// </summary>
        public TableOfContentsBlock AddTableOfContents(string? title = "Contents", int maxLevel = 3)
        {
            if (!AllowsPageBreaks)
            {
                throw new InvalidOperationException("A table of contents is only allowed in a section body, not in headers, footers or table cells.");
            }

            var block = new TableOfContentsBlock(title, maxLevel);
            BlockList.Add(block);
            return block;
        }

        /// <summary>Starts a new page. Only allowed in a section body.</summary>
        public void AddPageBreak()
        {
            if (!AllowsPageBreaks)
            {
                throw new InvalidOperationException("Page breaks are only allowed in a section body, not in headers, footers or table cells.");
            }

            BlockList.Add(new PageBreakBlock());
        }
    }
}
