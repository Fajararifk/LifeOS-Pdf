using System;
using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>Result of placing (part of) a block into the available space.</summary>
    internal sealed class LayoutResult
    {
        public static readonly LayoutResult Nothing = new LayoutResult(new List<DrawItem>(), 0, null) { PlacedNothing = true };

        public LayoutResult(List<DrawItem> items, double height, BlockLayouter? remainder)
        {
            Items = items;
            Height = height;
            Remainder = remainder;
        }

        public List<DrawItem> Items { get; }

        public double Height { get; }

        /// <summary>What still has to be placed (on the next column/page), or null when done.</summary>
        public BlockLayouter? Remainder { get; }

        /// <summary>Nothing fitted; the caller moves to a fresh column and tries again.</summary>
        public bool PlacedNothing { get; private set; }
    }

    /// <summary>
    /// Lays out one block. Implementations are immutable: continuing a split block
    /// returns a new layouter as <see cref="LayoutResult.Remainder"/>.
    /// </summary>
    internal abstract class BlockLayouter
    {
        public double SpaceBefore { get; protected set; }

        public double SpaceAfter { get; protected set; }

        public bool KeepWithNext { get; protected set; }

        public bool PageBreakBefore { get; protected set; }

        public virtual bool IsPageBreak => false;

        /// <summary>
        /// Places as much as fits in <paramref name="available"/>. When <paramref name="atTop"/> is true
        /// the area is a fresh column: the layouter must make progress or throw <see cref="LayoutException"/>.
        /// </summary>
        public abstract LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop);

        /// <summary>Narrowest and widest sensible width of the content (for automatic table columns).</summary>
        public abstract void ContentWidth(LayoutContext ctx, out double min, out double max);

        /// <summary>Height of the smallest first piece (used by keep-with-next).</summary>
        public virtual double FirstChunkHeight(LayoutContext ctx, double width) =>
            Layout(ctx, width, double.PositiveInfinity, true).Height;

        public double FullHeight(LayoutContext ctx, double width) => Layout(ctx, width, double.PositiveInfinity, true).Height;
    }

    internal sealed class PageBreakLayouter : BlockLayouter
    {
        public override bool IsPageBreak => true;

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop) =>
            new LayoutResult(new List<DrawItem>(), 0, null);

        public override void ContentWidth(LayoutContext ctx, out double min, out double max) => min = max = 0;
    }

    internal sealed class ParagraphLayouter : BlockLayouter
    {
        private readonly Paragraph _paragraph;
        private readonly TextStyle _baseStyle;
        private readonly double _leftIndent;
        private readonly string? _marker;
        private readonly double _markerX;
        private readonly int _startLine;
        private readonly LineCache _cache;

        public ParagraphLayouter(Paragraph paragraph, TextStyle baseStyle, LayoutContext ctx, double extraIndent = 0, string? marker = null, double markerX = 0)
            : this(paragraph, baseStyle, extraIndent, marker, markerX, 0, new LineCache())
        {
            var format = paragraph.Format;
            if (paragraph.HeadingLevel > 0)
            {
                var heading = ctx.Styles.Heading(paragraph.HeadingLevel);
                SpaceBefore = Math.Max(format.SpaceBefore, heading.SpaceBefore);
                SpaceAfter = heading.SpaceAfter;
            }
        }

        private ParagraphLayouter(Paragraph paragraph, TextStyle baseStyle, double extraIndent, string? marker, double markerX, int startLine, LineCache cache)
        {
            _paragraph = paragraph;
            _baseStyle = baseStyle;
            _leftIndent = paragraph.Format.LeftIndent + extraIndent;
            _marker = marker;
            _markerX = markerX;
            _startLine = startLine;
            _cache = cache;
            SpaceBefore = startLine == 0 ? paragraph.Format.SpaceBefore : 0;
            SpaceAfter = paragraph.Format.SpaceAfter;
            KeepWithNext = paragraph.Format.KeepWithNext;
            PageBreakBefore = startLine == 0 && paragraph.Format.PageBreakBefore;
        }

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop)
        {
            var lines = Lines(ctx, width);
            var format = _paragraph.Format;

            // Keep together: move the whole paragraph unless we are already at the top.
            if (format.KeepTogether && _startLine == 0 && !atTop && TotalHeight(lines, 0) > available)
            {
                return LayoutResult.Nothing;
            }

            var items = new List<DrawItem>();
            double y = 0;
            var i = _startLine;
            for (; i < lines.Count; i++)
            {
                var line = lines[i];
                if (y + line.Height > available + 0.001)
                {
                    if (i == _startLine)
                    {
                        if (!atTop)
                        {
                            return LayoutResult.Nothing;
                        }

                        throw new LayoutException("A line of text is " + Math.Round(line.Height, 1) +
                            " pt tall but only " + Math.Round(available, 1) + " pt are available on an empty page. Reduce the font size or line spacing, or enlarge the page.");
                    }

                    break;
                }

                if (i == 0 && (_paragraph.Anchor != null || _paragraph.HeadingLevel > 0))
                {
                    items.Add(new AnchorItem(_paragraph, _leftIndent, 0));
                }

                DrawLine(ctx, line, width, y, items, i == 0 && _marker != null);
                y += line.Height;
            }

            var remainder = i < lines.Count
                ? new ParagraphLayouter(_paragraph, _baseStyle, _leftIndent - _paragraph.Format.LeftIndent, _marker, _markerX, i, _cache) { SpaceAfter = SpaceAfter }
                : null;
            return new LayoutResult(items, y, remainder);
        }

        public override double FirstChunkHeight(LayoutContext ctx, double width)
        {
            var lines = Lines(ctx, width);
            return _startLine < lines.Count ? lines[_startLine].Height : 0;
        }

        public override void ContentWidth(LayoutContext ctx, out double min, out double max)
        {
            var atoms = BuildAtoms(ctx);
            min = 0;
            max = 0;
            double lineWidth = 0;
            foreach (var atom in atoms)
            {
                if (atom.Kind == AtomKind.Break)
                {
                    max = Math.Max(max, lineWidth);
                    lineWidth = 0;
                    continue;
                }

                var w = atom.Width;
                lineWidth += w;
                if (atom.Kind == AtomKind.Word)
                {
                    min = Math.Max(min, w);
                }
            }

            max = Math.Max(max, lineWidth);
            var indents = _leftIndent + _paragraph.Format.RightIndent + Math.Max(0, _paragraph.Format.FirstLineIndent);
            min += indents;
            max += indents;
        }

        private static double TotalHeight(List<TextLine> lines, int from)
        {
            double h = 0;
            for (var i = from; i < lines.Count; i++)
            {
                h += lines[i].Height;
            }

            return h;
        }

        private List<TextLine> Lines(LayoutContext ctx, double width)
        {
            // Page-number fields make the text depend on the page, so the page is part of the key.
            var key = width + "|" + ctx.PageNumber + "|" + ctx.PageCount;
            if (_cache.Key == key && _cache.Lines != null)
            {
                return _cache.Lines;
            }

            var format = _paragraph.Format;
            var limit = Math.Max(1, width - _leftIndent - format.RightIndent);
            var firstLimit = Math.Max(1, limit - format.FirstLineIndent);
            var lines = LineBreaker.Break(BuildAtoms(ctx), firstLimit, limit, ctx);
            foreach (var line in lines)
            {
                Measure(line, format.LineSpacing);
            }

            _cache.Key = key;
            _cache.Lines = lines;
            return lines;
        }

        private List<Atom> BuildAtoms(LayoutContext ctx)
        {
            var builder = new AtomBuilder(ctx);
            var paragraphStyle = _baseStyle.Merge(_paragraph.Style);
            foreach (var inline in _paragraph.Inlines)
            {
                var inlineStyle = inline.LinkStyled ? paragraphStyle.Merge(ctx.Styles.Link).Merge(inline.Style) : paragraphStyle.Merge(inline.Style);
                var style = new ResolvedStyle(inlineStyle, inline.Link);
                switch (inline.Kind)
                {
                    case InlineKind.LineBreak:
                        builder.AddBreak(style);
                        break;
                    case InlineKind.PageNumber:
                    case InlineKind.PageCount:
                        builder.Add(ctx.FieldText(inline.Kind), style);
                        break;
                    default:
                        builder.Add(inline.Text, style);
                        break;
                }
            }

            var atoms = builder.Finish();
            if (atoms.Count == 0)
            {
                // Empty paragraph: one blank line in the paragraph's font.
                atoms.Add(new Atom(AtomKind.Break, new ResolvedStyle(paragraphStyle)));
            }

            return atoms;
        }

        private static void Measure(TextLine line, double lineSpacing)
        {
            double ascent = 0, descent = 0, size = 0;
            foreach (var p in line.Pieces)
            {
                ascent = Math.Max(ascent, p.Font.Ascent);
                descent = Math.Max(descent, p.Font.Descent);
                size = Math.Max(size, p.Font.Size);
            }

            line.Height = size * lineSpacing;
            line.Baseline = (line.Height - (ascent + descent)) / 2 + ascent;
        }

        private void DrawLine(LayoutContext ctx, TextLine line, double width, double top, List<DrawItem> items, bool drawMarker)
        {
            var format = _paragraph.Format;
            var limit = Math.Max(1, width - _leftIndent - format.RightIndent - (line.IsFirst ? format.FirstLineIndent : 0));
            var x = _leftIndent + (line.IsFirst ? format.FirstLineIndent : 0);
            var lineWidth = line.Width;
            var baseline = top + line.Baseline;

            double extraPerSpace = 0;
            switch (format.Alignment)
            {
                case ParagraphAlignment.Center:
                    x += (limit - lineWidth) / 2;
                    break;
                case ParagraphAlignment.Right:
                    x += limit - lineWidth;
                    break;
                case ParagraphAlignment.Justify:
                    var spaces = 0;
                    foreach (var p in line.Pieces)
                    {
                        if (p.IsSpace)
                        {
                            spaces += p.Run.Glyphs.Length;
                        }
                    }

                    if (!line.IsHardEnd && spaces > 0 && limit > lineWidth)
                    {
                        extraPerSpace = (limit - lineWidth) / spaces;
                    }

                    break;
            }

            if (drawMarker)
            {
                var first = line.Pieces.Count > 0 ? line.Pieces[0] : LineBreaker.EmptyPiece(null, ctx);
                var font = ctx.Font(first.Style.PrimaryFace, first.Style.Size);
                items.Add(new TextItem(font, font.Map(_marker!), null, _markerX, baseline, first.Style.Color));
            }

            // Consecutive pieces with the same font and color become one text item.
            var i = 0;
            while (i < line.Pieces.Count)
            {
                var start = i;
                var piece = line.Pieces[i];
                var runs = new List<TextRun>();
                var extras = new List<double>();
                double segmentWidth = 0;
                while (i < line.Pieces.Count && SameAppearance(line.Pieces[i], piece))
                {
                    var p = line.Pieces[i];
                    runs.Add(p.Run);
                    for (var g = 0; g < p.Run.Glyphs.Length; g++)
                    {
                        extras.Add(p.IsSpace ? extraPerSpace : 0);
                    }

                    segmentWidth += p.Width + (p.IsSpace ? extraPerSpace * p.Run.Glyphs.Length : 0);
                    i++;
                }

                var run = Concat(runs);
                if (run.Codes.Length > 0)
                {
                    items.Add(new TextItem(piece.Font, run, extraPerSpace > 0 ? extras.ToArray() : null, x, baseline, piece.Style.Color));
                }

                AddDecorations(piece, x, segmentWidth, baseline, items);
                if (piece.Style.Link != null && segmentWidth > 0)
                {
                    items.Add(new LinkItem(piece.Style.Link, x, top, segmentWidth, line.Height));
                }

                x += segmentWidth;
            }
        }

        private static bool SameAppearance(Piece a, Piece b) =>
            ReferenceEquals(a.Font, b.Font) && a.Style.Color == b.Style.Color && a.Style.Link == b.Style.Link &&
            a.Style.Underline == b.Style.Underline && a.Style.Strikethrough == b.Style.Strikethrough;

        private static void AddDecorations(Piece piece, double x, double width, double baseline, List<DrawItem> items)
        {
            if (width <= 0 || (!piece.Style.Underline && !piece.Style.Strikethrough))
            {
                return;
            }

            var program = piece.Font.Face.Program;
            var size = piece.Font.Size;
            var pen = new PdfPen(piece.Style.Color, Math.Max(0.25, program.UnderlineThickness * size / 1000.0));
            if (piece.Style.Underline)
            {
                var y = baseline - program.UnderlinePosition * size / 1000.0;
                items.Add(new LineItem(pen, x, y, x + width, y));
            }

            if (piece.Style.Strikethrough)
            {
                // Through the middle of lowercase letters (about half the x-height ≈ 0.25 em).
                var y = baseline - size * 0.25;
                items.Add(new LineItem(pen, x, y, x + width, y));
            }
        }

        internal static TextRun Concat(List<TextRun> runs)
        {
            if (runs.Count == 1)
            {
                return runs[0];
            }

            var codes = new List<byte>();
            var glyphs = new List<int>();
            var cps = new List<int>();
            var adjustments = new List<double>();
            double width = 0;
            foreach (var r in runs)
            {
                codes.AddRange(r.Codes);
                glyphs.AddRange(r.Glyphs);
                cps.AddRange(r.CodePoints);
                adjustments.AddRange(r.Adjustments);
                width += r.Width;
            }

            return new TextRun(codes.ToArray(), glyphs.ToArray(), cps.ToArray(), width, adjustments.ToArray());
        }

        private sealed class LineCache
        {
            public string? Key { get; set; }

            public List<TextLine>? Lines { get; set; }
        }
    }

    internal sealed class ImageLayouter : BlockLayouter
    {
        private readonly ImageBlock _block;

        public ImageLayouter(ImageBlock block)
        {
            _block = block;
            SpaceBefore = block.SpaceBefore;
            SpaceAfter = block.SpaceAfter;
            KeepWithNext = block.KeepWithNext;
        }

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop)
        {
            NaturalSize(out var w, out var h);
            if (w > width)
            {
                h *= width / w;
                w = width;
            }

            if (h > available)
            {
                if (!atTop)
                {
                    return LayoutResult.Nothing;
                }

                if (available < 1)
                {
                    throw new LayoutException("No room for an image on an empty page; check the page margins.");
                }

                // Larger than an empty page: shrink to fit instead of cutting it off.
                w *= available / h;
                h = available;
            }

            var x = _block.Alignment == ParagraphAlignment.Center ? (width - w) / 2 : _block.Alignment == ParagraphAlignment.Right ? width - w : 0;
            return new LayoutResult(new List<DrawItem> { new ImageItem(_block.Image, x, 0, w, h) }, h, null);
        }

        public override void ContentWidth(LayoutContext ctx, out double min, out double max)
        {
            NaturalSize(out var w, out _);
            max = w;
            min = Math.Min(w, 24); // images shrink with their column
        }

        private void NaturalSize(out double w, out double h)
        {
            var image = _block.Image;
            var natural = image.PixelWidth * 72.0 / (image.DpiX ?? 96);
            w = _block.Width ?? natural;
            h = w * image.PixelHeight / image.PixelWidth;
        }
    }

    internal sealed class BarcodeLayouter : BlockLayouter
    {
        private readonly BarcodeBlock _block;

        public BarcodeLayouter(BarcodeBlock block)
        {
            _block = block;
            SpaceBefore = block.SpaceBefore;
            SpaceAfter = block.SpaceAfter;
        }

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop)
        {
            var w = Math.Min(_block.Width, width);
            // QR codes stay square when narrowed; barcodes keep their height.
            var h = _block.QrCode != null ? w : _block.Height;
            if (h > available)
            {
                if (!atTop)
                {
                    return LayoutResult.Nothing;
                }

                throw new LayoutException("A " + (_block.QrCode != null ? "QR code" : "barcode") + " of " + Math.Round(h, 1) +
                    " pt does not fit on an empty page (" + Math.Round(available, 1) + " pt). Make it smaller.");
            }

            var x = _block.Alignment == ParagraphAlignment.Center ? (width - w) / 2 : _block.Alignment == ParagraphAlignment.Right ? width - w : 0;
            return new LayoutResult(new List<DrawItem> { new CodeItem(_block, x, 0, w, h) }, h, null);
        }

        public override void ContentWidth(LayoutContext ctx, out double min, out double max)
        {
            max = _block.Width;
            // A barcode cannot shrink much without becoming unreadable.
            min = _block.QrCode != null ? Math.Min(_block.Width, 40) : _block.Width;
        }
    }

    internal sealed class SpacerLayouter : BlockLayouter
    {
        private readonly double _height;

        public SpacerLayouter(double height)
        {
            _height = height;
        }

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop) =>
            new LayoutResult(new List<DrawItem>(), Math.Max(0, Math.Min(_height, available)), null);

        public override void ContentWidth(LayoutContext ctx, out double min, out double max) => min = max = 0;
    }

    internal sealed class HorizontalLineLayouter : BlockLayouter
    {
        private readonly HorizontalLineBlock _block;

        public HorizontalLineLayouter(HorizontalLineBlock block)
        {
            _block = block;
            SpaceBefore = block.SpaceBefore;
            SpaceAfter = block.SpaceAfter;
        }

        public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop)
        {
            var h = Math.Max(_block.Pen.Width, 0.1);
            if (h > available && !atTop)
            {
                return LayoutResult.Nothing;
            }

            return new LayoutResult(new List<DrawItem> { new LineItem(_block.Pen, 0, h / 2, width, h / 2) }, h, null);
        }

        public override void ContentWidth(LayoutContext ctx, out double min, out double max) => min = max = 0;
    }

    /// <summary>Creates layouters for blocks and lays out containers without pagination.</summary>
    internal static class LayouterFactory
    {
        public static List<BlockLayouter> Create(IEnumerable<Block> blocks, TextStyle baseStyle, LayoutContext ctx)
        {
            var result = new List<BlockLayouter>();
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph p:
                        var style = p.HeadingLevel > 0 ? baseStyle.Merge(ctx.Styles.Heading(p.HeadingLevel).Text) : baseStyle;
                        result.Add(new ParagraphLayouter(p, style, ctx));
                        break;
                    case ListBlock list:
                        AddList(list, 0, baseStyle, ctx, result);
                        break;
                    case Table table:
                        result.Add(new TableLayouter(table, baseStyle, ctx));
                        break;
                    case ImageBlock image:
                        result.Add(new ImageLayouter(image));
                        break;
                    case BarcodeBlock code:
                        result.Add(new BarcodeLayouter(code));
                        break;
                    case SpacerBlock spacer:
                        result.Add(new SpacerLayouter(spacer.Height));
                        break;
                    case HorizontalLineBlock line:
                        result.Add(new HorizontalLineLayouter(line));
                        break;
                    case PageBreakBlock _:
                        result.Add(new PageBreakLayouter());
                        break;
                    case TableOfContentsBlock toc:
                        TocLayouter.Add(toc, baseStyle, ctx, result);
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Lays out blocks top to bottom with unlimited height (headers, footers, table cells).
        /// Space before the first and after the last block is dropped.
        /// </summary>
        public static List<DrawItem> Stack(List<BlockLayouter> layouters, LayoutContext ctx, double width, out double height)
        {
            var items = new List<DrawItem>();
            double y = 0;
            for (var i = 0; i < layouters.Count; i++)
            {
                var layouter = layouters[i];
                if (layouter.IsPageBreak)
                {
                    continue;
                }

                if (i > 0)
                {
                    y += layouter.SpaceBefore;
                }

                var result = layouter.Layout(ctx, width, double.PositiveInfinity, true);
                DrawItemList.TranslateAll(result.Items, 0, y);
                items.AddRange(result.Items);
                y += result.Height;
                if (i < layouters.Count - 1)
                {
                    y += layouter.SpaceAfter;
                }
            }

            height = y;
            return items;
        }

        public static void ContentWidth(List<BlockLayouter> layouters, LayoutContext ctx, out double min, out double max)
        {
            min = 0;
            max = 0;
            foreach (var l in layouters)
            {
                l.ContentWidth(ctx, out var a, out var b);
                min = Math.Max(min, a);
                max = Math.Max(max, b);
            }
        }

        private static void AddList(ListBlock list, int level, TextStyle baseStyle, LayoutContext ctx, List<BlockLayouter> result)
        {
            var number = list.StartNumber;
            var indent = list.Indent;
            foreach (var item in list.Items)
            {
                var marker = list.Kind == ListKind.Bullet
                    ? (level % 2 == 0 ? "•" : "–")
                    : number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                number++;
                var textIndent = indent * (level + 1);
                result.Add(new ParagraphLayouter(item.Paragraph, baseStyle, ctx, textIndent, marker, indent * level + item.Paragraph.Format.LeftIndent));
                foreach (var child in item.Children)
                {
                    AddList(child, level + 1, baseStyle, ctx, result);
                }
            }
        }
    }
}
