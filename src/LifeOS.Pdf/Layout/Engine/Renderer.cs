namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>
    /// Runs layout and draws the result into a <see cref="PdfDocument"/>.
    /// The total page count is not known until layout ends, so layout is repeated with
    /// the real count (at most a few times) until it is stable; header and footer are
    /// laid out per page with the actual numbers.
    /// </summary>
    internal sealed class Renderer
    {
        private const int MaxPasses = 4;

        private readonly FlowDocument _doc;

        public Renderer(FlowDocument doc)
        {
            _doc = doc;
        }

        public void Render(PdfDocument pdf)
        {
            var ctx = new LayoutContext(_doc.Styles);
            var guess = 1;
            var pages = Paginator.Paginate(_doc, ctx);
            for (var pass = 1; pass < MaxPasses && pages.Count != guess; pass++)
            {
                guess = pages.Count;
                ctx.PageCount = guess;
                pages = Paginator.Paginate(_doc, ctx);
            }

            ctx.PageCount = pages.Count;
            for (var i = 0; i < pages.Count; i++)
            {
                var layout = pages[i];
                var setup = layout.Section.PageSetup;
                var m = setup.Margins;
                var pdfPage = pdf.AddPage(setup.PageSize);
                var g = pdfPage.Graphics;
                var width = setup.PageSize.Width - m.Left - m.Right;
                ctx.PageNumber = i + 1;

                var header = Paginator.HeaderFor(layout.Section, layout.SectionPageIndex);
                if (!header.IsEmpty)
                {
                    var items = LayouterFactory.Stack(LayouterFactory.Create(header.BlockList, ctx.BaseStyle, ctx), ctx, width, out _);
                    DrawItemList.TranslateAll(items, m.Left, m.Top);
                    items.ForEach(item => item.Draw(g));
                }

                var footer = Paginator.FooterFor(layout.Section, layout.SectionPageIndex);
                if (!footer.IsEmpty)
                {
                    var items = LayouterFactory.Stack(LayouterFactory.Create(footer.BlockList, ctx.BaseStyle, ctx), ctx, width, out var height);
                    DrawItemList.TranslateAll(items, m.Left, setup.PageSize.Height - m.Bottom - height);
                    items.ForEach(item => item.Draw(g));
                }

                layout.Items.ForEach(item => item.Draw(g));
            }
        }
    }
}
