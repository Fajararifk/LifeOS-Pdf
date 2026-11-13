using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>The body content of one output page.</summary>
    internal sealed class PageLayout
    {
        public PageLayout(Section section, int sectionPageIndex)
        {
            Section = section;
            SectionPageIndex = sectionPageIndex;
        }

        public Section Section { get; }

        public int SectionPageIndex { get; }

        public List<DrawItem> Items { get; } = new List<DrawItem>();
    }

    /// <summary>
    /// Flows section content into columns and pages: spacing, keep-with-next chains,
    /// keep-together, page breaks. Guarantees progress: a block offered a fresh column
    /// either places something or throws <see cref="LayoutException"/>, so layout always ends.
    /// </summary>
    internal static class Paginator
    {
        private const int MaxKeepChain = 5;

        public static List<PageLayout> Paginate(FlowDocument doc, LayoutContext ctx)
        {
            var pages = new List<PageLayout>();
            foreach (var section in doc.Sections)
            {
                PaginateSection(section, ctx, pages);
            }

            return pages;
        }

        /// <summary>Height of a header/footer, measured with wide placeholder page numbers.</summary>
        internal static double MeasureHeaderFooter(HeaderFooter hf, LayoutContext ctx, double width)
        {
            if (hf.IsEmpty)
            {
                return 0;
            }

            int number = ctx.PageNumber, count = ctx.PageCount;
            ctx.PageNumber = 8888;
            ctx.PageCount = 8888;
            try
            {
                LayouterFactory.Stack(LayouterFactory.Create(hf.BlockList, ctx.BaseStyle, ctx), ctx, width, out var height);
                return height;
            }
            finally
            {
                ctx.PageNumber = number;
                ctx.PageCount = count;
            }
        }

        internal static HeaderFooter HeaderFor(Section s, int sectionPageIndex) =>
            s.PageSetup.DifferentFirstPage && sectionPageIndex == 0 ? s.FirstPageHeader : s.Header;

        internal static HeaderFooter FooterFor(Section s, int sectionPageIndex) =>
            s.PageSetup.DifferentFirstPage && sectionPageIndex == 0 ? s.FirstPageFooter : s.Footer;

        private static void PaginateSection(Section section, LayoutContext ctx, List<PageLayout> pages)
        {
            var setup = section.PageSetup;
            var m = setup.Margins;
            var pageWidth = setup.PageSize.Width;
            var pageHeight = setup.PageSize.Height;
            var contentWidth = pageWidth - m.Left - m.Right;
            if (contentWidth < 1 || pageHeight - m.Top - m.Bottom < 1)
            {
                throw new LayoutException("The page margins (" + m + ") are larger than the " + setup.PageSize + " page. Reduce the margins.");
            }

            var columns = Math.Max(1, setup.Columns);
            var columnWidth = (contentWidth - setup.ColumnGap * (columns - 1)) / columns;
            if (columnWidth < 1)
            {
                throw new LayoutException(columns + " columns with a gap of " + setup.ColumnGap + " pt do not fit in " +
                    Math.Round(contentWidth, 1) + " pt of page width. Use fewer columns or a smaller gap.");
            }

            var headerHeights = new[] { MeasureHeaderFooter(HeaderFor(section, 0), ctx, contentWidth), MeasureHeaderFooter(HeaderFor(section, 1), ctx, contentWidth) };
            var footerHeights = new[] { MeasureHeaderFooter(FooterFor(section, 0), ctx, contentWidth), MeasureHeaderFooter(FooterFor(section, 1), ctx, contentWidth) };

            PageLayout page = null!;
            double bodyTop = 0, columnHeight = 0, y = 0;
            var column = 0;
            var sectionPage = 0;

            void NewPage()
            {
                page = new PageLayout(section, sectionPage);
                pages.Add(page);
                ctx.PageNumber = pages.Count;
                var k = sectionPage == 0 ? 0 : 1;
                bodyTop = m.Top + (headerHeights[k] > 0 ? headerHeights[k] + setup.HeaderSpacing : 0);
                var bodyBottom = pageHeight - m.Bottom - (footerHeights[k] > 0 ? footerHeights[k] + setup.FooterSpacing : 0);
                columnHeight = bodyBottom - bodyTop;
                if (columnHeight < 1)
                {
                    throw new LayoutException("Header and footer leave no room for content on the page. Make them smaller or reduce the margins.");
                }

                sectionPage++;
                column = 0;
                y = 0;
            }

            void NextColumn()
            {
                if (column + 1 < columns)
                {
                    column++;
                    y = 0;
                }
                else
                {
                    NewPage();
                }
            }

            NewPage();
            var layouters = LayouterFactory.Create(section.BlockList, ctx.BaseStyle, ctx);
            for (var i = 0; i < layouters.Count; i++)
            {
                var block = layouters[i];
                if (block.IsPageBreak)
                {
                    if (!(column == 0 && y == 0 && page.Items.Count == 0))
                    {
                        NewPage();
                    }

                    continue;
                }

                if (block.PageBreakBefore && !(column == 0 && y == 0))
                {
                    NewPage();
                }

                while (true)
                {
                    var atTop = y == 0;
                    var space = atTop ? 0 : block.SpaceBefore;
                    var available = columnHeight - y - space;

                    if (!atTop && block.KeepWithNext && i + 1 < layouters.Count)
                    {
                        var need = ChainHeight(layouters, i, ctx, columnWidth, 0);
                        if (need > available + 0.001 && need <= columnHeight)
                        {
                            NextColumn();
                            continue;
                        }
                    }

                    var result = block.Layout(ctx, columnWidth, available, atTop);
                    if (result.PlacedNothing)
                    {
                        NextColumn();
                        continue;
                    }

                    var x = m.Left + column * (columnWidth + setup.ColumnGap);
                    DrawItemList.TranslateAll(result.Items, x, bodyTop + y + space);
                    page.Items.AddRange(result.Items);
                    y += space + result.Height;

                    if (result.Remainder != null)
                    {
                        block = result.Remainder;
                        NextColumn();
                        continue;
                    }

                    y += block.SpaceAfter;
                    break;
                }
            }
        }

        /// <summary>Height a keep-with-next block needs: itself plus the first piece of what follows.</summary>
        private static double ChainHeight(List<BlockLayouter> layouters, int i, LayoutContext ctx, double width, int depth)
        {
            var h = layouters[i].FullHeight(ctx, width) + layouters[i].SpaceAfter;
            if (i + 1 >= layouters.Count || layouters[i + 1].IsPageBreak)
            {
                return h;
            }

            var next = layouters[i + 1];
            h += next.SpaceBefore;
            h += next.KeepWithNext && depth < MaxKeepChain
                ? ChainHeight(layouters, i + 1, ctx, width, depth + 1)
                : next.FirstChunkHeight(ctx, width);
            return h;
        }
    }
}
