using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>
    /// Expands a <see cref="TableOfContentsBlock"/> into one layouter per entry, like a list.
    /// Each entry is an ordinary paragraph (so it wraps and splits normally) whose last line
    /// gets dot leaders and a right-aligned page number.
    /// </summary>
    internal static class TocLayouter
    {
        private const double NumberColumn = 36;

        public static void Add(TableOfContentsBlock toc, TextStyle baseStyle, LayoutContext ctx, List<BlockLayouter> result)
        {
            if (!string.IsNullOrEmpty(toc.Title))
            {
                var title = new Paragraph { Style = ctx.Styles.TableOfContentsTitle };
                title.Format.SpaceAfter = 10;
                title.Format.KeepWithNext = true;
                title.AddText(toc.Title!);
                result.Add(new ParagraphLayouter(title, baseStyle, ctx));
            }

            foreach (var heading in ctx.Headings)
            {
                if (heading.Level > toc.MaxLevel)
                {
                    continue;
                }

                var entry = new Paragraph();
                entry.Format.LeftIndent = (heading.Level - 1) * toc.LevelIndent;
                entry.Format.RightIndent = NumberColumn;
                entry.Format.SpaceBefore = heading.Level == 1 ? 4 : 1;
                entry.Format.SpaceAfter = 1;
                var style = heading.Level == 1 ? new TextStyle { Bold = true } : null;
                entry.Inlines.Add(new Inline(InlineKind.Text, heading.Paragraph.PlainText, style) { Link = "#" + heading.Name });

                ctx.HeadingPages.TryGetValue(heading.Paragraph, out var page);
                var number = page > 0 ? page.ToString(CultureInfo.InvariantCulture) : "?";
                var resolved = new ResolvedStyle(baseStyle.Merge(style));
                result.Add(new EntryLayouter(new ParagraphLayouter(entry, baseStyle, ctx), resolved, number, "#" + heading.Name, toc.DotLeaders));
            }
        }

        private sealed class EntryLayouter : BlockLayouter
        {
            private readonly ParagraphLayouter _inner;
            private readonly ResolvedStyle _style;
            private readonly string _number;
            private readonly string _link;
            private readonly bool _dots;

            public EntryLayouter(ParagraphLayouter inner, ResolvedStyle style, string number, string link, bool dots)
            {
                _inner = inner;
                _style = style;
                _number = number;
                _link = link;
                _dots = dots;
                SpaceBefore = inner.SpaceBefore;
                SpaceAfter = inner.SpaceAfter;
                KeepWithNext = inner.KeepWithNext;
            }

            public override LayoutResult Layout(LayoutContext ctx, double width, double available, bool atTop)
            {
                var result = _inner.Layout(ctx, width, available, atTop);
                if (result.PlacedNothing)
                {
                    return result;
                }

                if (result.Remainder != null)
                {
                    var rest = new EntryLayouter((ParagraphLayouter)result.Remainder, _style, _number, _link, _dots);
                    return new LayoutResult(result.Items, result.Height, rest);
                }

                // The entry ends in this chunk: find the end of its last line.
                TextItem? last = null;
                double lastEnd = 0;
                foreach (var item in result.Items)
                {
                    if (item is TextItem t)
                    {
                        var end = t.X + t.Run.Width * t.Font.Size / 1000.0;
                        if (last == null || t.Baseline > last.Baseline + 0.01 || (Math.Abs(t.Baseline - last.Baseline) <= 0.01 && end > lastEnd))
                        {
                            last = t;
                            lastEnd = end;
                        }
                    }
                }

                if (last == null)
                {
                    return result;
                }

                var items = new List<DrawItem>(result.Items);
                var font = ctx.Font(_style.PrimaryFace, _style.Size);
                var numberRun = font.Map(_number);
                var numberWidth = numberRun.Width * font.Size / 1000.0;
                var numberX = width - numberWidth;
                items.Add(new TextItem(font, numberRun, null, numberX, last.Baseline, _style.Color));
                items.Add(new LinkItem(_link, numberX, last.Baseline - font.Size, numberWidth, font.Size * 1.25));

                if (_dots)
                {
                    var unit = font.Map(" .");
                    var unitWidth = unit.Width * font.Size / 1000.0;
                    var room = numberX - 4 - (lastEnd + 2);
                    var count = unitWidth > 0 ? (int)Math.Floor(room / unitWidth) : 0;
                    if (count > 0)
                    {
                        var dots = new StringBuilder(count * 2);
                        for (var i = 0; i < count; i++)
                        {
                            dots.Append(" .");
                        }

                        var run = font.Map(dots.ToString());
                        var x = numberX - 4 - run.Width * font.Size / 1000.0;
                        items.Add(new TextItem(font, run, null, x, last.Baseline, PdfColor.FromHex("#9CA3AF")));
                    }
                }

                return new LayoutResult(items, result.Height, null);
            }

            public override void ContentWidth(LayoutContext ctx, out double min, out double max) => _inner.ContentWidth(ctx, out min, out max);

            public override double FirstChunkHeight(LayoutContext ctx, double width) => _inner.FirstChunkHeight(ctx, width);
        }
    }
}
