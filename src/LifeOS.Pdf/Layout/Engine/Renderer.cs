using System;
using System.Collections.Generic;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>
    /// Runs layout and draws the result into a <see cref="PdfDocument"/>.
    /// The total page count (and, with a table of contents, the page of every heading) is not
    /// known until layout ends, so layout is repeated with the previous results (at most a few
    /// times) until they are stable; header and footer are laid out per page with the actual numbers.
    /// </summary>
    internal sealed class Renderer
    {
        private const int MaxPasses = 6;

        private readonly FlowDocument _doc;

        public Renderer(FlowDocument doc)
        {
            _doc = doc;
        }

        public void Render(PdfDocument pdf)
        {
            var ctx = new LayoutContext(_doc.Styles);
            var hasToc = CollectHeadings(ctx);
            var pages = Paginator.Paginate(_doc, ctx);
            for (var pass = 1; pass < MaxPasses; pass++)
            {
                var changed = pages.Count != ctx.PageCount;
                ctx.PageCount = pages.Count;
                if (hasToc)
                {
                    var found = HeadingPages(pages);
                    if (!SameMap(found, ctx.HeadingPages))
                    {
                        changed = true;
                        ctx.HeadingPages.Clear();
                        foreach (var pair in found)
                        {
                            ctx.HeadingPages[pair.Key] = pair.Value;
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }

                pages = Paginator.Paginate(_doc, ctx);
            }

            var autoNames = new Dictionary<Paragraph, string>();
            if (hasToc)
            {
                foreach (var heading in ctx.Headings)
                {
                    autoNames[heading.Paragraph] = heading.Name;
                }
            }

            var outline = new OutlineBuilder(pdf.Outline);
            var placed = new HashSet<Paragraph>();
            var anchorOwners = new Dictionary<string, Paragraph>(StringComparer.Ordinal);

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
                    DrawAll(items, g);
                }

                var footer = Paginator.FooterFor(layout.Section, layout.SectionPageIndex);
                if (!footer.IsEmpty)
                {
                    var items = LayouterFactory.Stack(LayouterFactory.Create(footer.BlockList, ctx.BaseStyle, ctx), ctx, width, out var height);
                    DrawItemList.TranslateAll(items, m.Left, setup.PageSize.Height - m.Bottom - height);
                    DrawAll(items, g);
                }

                DrawAll(layout.Items, g);

                foreach (var item in layout.Items)
                {
                    if (!(item is AnchorItem anchor) || !placed.Add(anchor.Paragraph))
                    {
                        continue; // a heading repeated (e.g. in a table header row) only counts once
                    }

                    var p = anchor.Paragraph;
                    // A little room above the text so the viewer does not cut off its top.
                    var top = Math.Max(0, anchor.Y - 6);
                    var name = p.Anchor ?? (autoNames.TryGetValue(p, out var auto) ? auto : null);
                    if (name != null)
                    {
                        if (anchorOwners.TryGetValue(name, out var owner) && !ReferenceEquals(owner, p))
                        {
                            throw new LayoutException("The anchor '" + name + "' is used by more than one paragraph. Anchor names must be unique.");
                        }

                        anchorOwners[name] = p;
                        pdf.NamedDestinations[name] = PdfDestination.ToPage(pdfPage, top: top);
                    }

                    if (p.HeadingLevel > 0 && _doc.GenerateOutline)
                    {
                        var title = p.PlainText;
                        if (title.Length > 0)
                        {
                            outline.Add(p.HeadingLevel, title, PdfDestination.ToPage(pdfPage, top: top));
                        }
                    }
                }
            }
        }

        private static void DrawAll(List<DrawItem> items, Drawing.PdfGraphics g)
        {
            foreach (var item in items)
            {
                item.Draw(g);
            }
        }

        /// <summary>Lists body headings for the table of contents; true when the document has one.</summary>
        private bool CollectHeadings(LayoutContext ctx)
        {
            var hasToc = false;
            var index = 0;
            foreach (var section in _doc.Sections)
            {
                foreach (var block in section.BlockList)
                {
                    if (block is TableOfContentsBlock)
                    {
                        hasToc = true;
                    }
                    else if (block is Paragraph p && p.HeadingLevel > 0)
                    {
                        index++;
                        ctx.Headings.Add(new HeadingInfo(p, p.Anchor ?? "_heading-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    }
                }
            }

            if (!hasToc)
            {
                ctx.Headings.Clear();
            }

            return hasToc;
        }

        private static Dictionary<Paragraph, int> HeadingPages(List<PageLayout> pages)
        {
            var result = new Dictionary<Paragraph, int>();
            for (var i = 0; i < pages.Count; i++)
            {
                foreach (var item in pages[i].Items)
                {
                    if (item is AnchorItem anchor && !result.ContainsKey(anchor.Paragraph))
                    {
                        result[anchor.Paragraph] = i + 1;
                    }
                }
            }

            return result;
        }

        private static bool SameMap(Dictionary<Paragraph, int> a, Dictionary<Paragraph, int> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var v) || v != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Nests bookmarks by heading level (a level 3 under the last level 2, and so on).</summary>
        private sealed class OutlineBuilder
        {
            private readonly PdfOutline _root;
            private readonly List<(int Level, PdfOutlineItem Item)> _stack = new List<(int Level, PdfOutlineItem Item)>();

            public OutlineBuilder(PdfOutline root)
            {
                _root = root;
            }

            public void Add(int level, string title, PdfDestination destination)
            {
                while (_stack.Count > 0 && _stack[_stack.Count - 1].Level >= level)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                }

                var item = _stack.Count == 0 ? _root.Add(title, destination) : _stack[_stack.Count - 1].Item.Add(title, destination);
                item.IsOpen = level == 1; // chapters show their sections; deeper levels start closed
                _stack.Add((level, item));
            }
        }
    }
}
