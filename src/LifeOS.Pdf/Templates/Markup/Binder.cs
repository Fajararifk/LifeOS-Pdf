using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates.Expressions;

namespace LifeOS.Pdf.Templates.Markup
{
    /// <summary>Evaluates a template tree against data and builds a <see cref="FlowDocument"/>.</summary>
    internal sealed class Binder
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        private readonly TemplateOptions _options;
        private readonly ResourceCache _cache;
        private readonly Dictionary<string, FontFamily> _fonts;
        private FlowDocument _doc = null!;

        public Binder(TemplateOptions options, ResourceCache cache)
        {
            _options = options;
            _cache = cache;
            _fonts = new Dictionary<string, FontFamily>(options.Fonts, StringComparer.OrdinalIgnoreCase);
        }

        public FlowDocument Bind(TemplateNode root, object? data)
        {
            _doc = new FlowDocument();
            var scope = new Scope(data, _options);
            _doc.Info.Title = Attr(root, "title", scope);
            _doc.Info.Author = Attr(root, "author", scope);
            _doc.Info.Subject = Attr(root, "subject", scope);
            _doc.Info.Keywords = Attr(root, "keywords", scope);
            _doc.Info.Creator = Attr(root, "creator", scope);

            Each(root.Children, scope, (node, s) =>
            {
                if (node.IsText)
                {
                    return;
                }

                if (node.Name == "styles")
                {
                    BindStyles(node, s);
                }
                else
                {
                    BindSection(node, s);
                }
            });

            foreach (var fallback in _options.FallbackFonts)
            {
                if (!_doc.Styles.Normal.Fallbacks.Contains(fallback))
                {
                    _doc.Styles.Normal.Fallbacks.Add(fallback);
                }
            }

            if (_doc.Sections.Count == 0)
            {
                throw root.Error("The template produced no <section>. Add at least one <section> (check its if=\"…\" conditions).");
            }

            return _doc;
        }

        // ------------------------------------------------------------------ control flow

        /// <summary>Visits nodes, expanding foreach / if / else and if="…" attributes.</summary>
        private void Each(List<TemplateNode> nodes, Scope scope, Action<TemplateNode, Scope> visit)
        {
            bool? previous = null; // outcome of the last conditional sibling, for <else>
            foreach (var node in nodes)
            {
                if (node.Name == "set")
                {
                    // A variable visible to the following siblings (and their children).
                    scope = scope.With(node.Attributes["name"].Evaluate(scope), node.Source!.Evaluate(scope));
                    continue;
                }

                if (node.IsText)
                {
                    visit(node, scope);
                    continue;
                }

                if (node.Name == "else")
                {
                    if (previous == false)
                    {
                        Each(node.Children, scope, visit);
                    }

                    previous = null;
                    continue;
                }

                if (node.Name == "if")
                {
                    var test = Values.IsTruthy(node.Condition!.Evaluate(scope));
                    if (test)
                    {
                        Each(node.Children, scope, visit);
                    }

                    previous = test;
                    continue;
                }

                if (node.Condition != null && !Values.IsTruthy(node.Condition.Evaluate(scope)))
                {
                    previous = false;
                    continue;
                }

                previous = node.Condition != null ? true : (bool?)null;
                if (node.Name == "foreach")
                {
                    var name = node.Attributes.TryGetValue("as", out var a) ? a.Evaluate(scope) : "item";
                    var indexName = node.Attributes.TryGetValue("index", out var ix) ? ix.Evaluate(scope) : null;
                    var items = Filters.Items(node.Source!.Evaluate(scope));
                    for (var i = 0; i < items.Count; i++)
                    {
                        Each(node.Children, scope.With(name, items[i], indexName, i), visit);
                    }

                    continue;
                }

                visit(node, scope);
            }
        }

        // ------------------------------------------------------------------ document level

        private void BindStyles(TemplateNode styles, Scope scope)
        {
            Each(styles.Children, scope, (node, s) =>
            {
                switch (node.Name)
                {
                    case "font":
                        var name = Attr(node, "name", s);
                        if (string.IsNullOrEmpty(name) || !node.Attributes.ContainsKey("regular"))
                        {
                            throw node.Error("<font> needs name=\"…\" and regular=\"path/to/font.ttf\".");
                        }

                        _fonts[name!] = FontFamily.FromFaces(LoadFont(node, "regular", s)!, LoadFont(node, "bold", s), LoadFont(node, "italic", s), LoadFont(node, "boldItalic", s));
                        break;
                    case "normal":
                        _doc.Styles.Normal = _doc.Styles.Normal.Merge(Style(node, s));
                        break;
                    case "tableHeader":
                        _doc.Styles.TableHeader = _doc.Styles.TableHeader.Merge(Style(node, s));
                        break;
                    case "heading":
                        var level = (int)(Number(node, "level", s) ?? 1);
                        if (level < 1 || level > 3)
                        {
                            throw node.Error("Heading level must be 1, 2 or 3.");
                        }

                        var heading = level == 1 ? _doc.Styles.Heading1 : level == 2 ? _doc.Styles.Heading2 : _doc.Styles.Heading3;
                        heading.Text = heading.Text.Merge(Style(node, s));
                        heading.SpaceBefore = Length(node, "spaceBefore", s) ?? heading.SpaceBefore;
                        heading.SpaceAfter = Length(node, "spaceAfter", s) ?? heading.SpaceAfter;
                        break;
                }
            });
        }

        private void BindSection(TemplateNode node, Scope scope)
        {
            var setup = new PageSetup();
            var size = Attr(node, "size", scope);
            if (!string.IsNullOrEmpty(size))
            {
                setup.PageSize = ParsePageSize(node, size!);
            }

            var orientation = Attr(node, "orientation", scope);
            if (string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                setup.PageSize = setup.PageSize.Landscape;
            }
            else if (!string.IsNullOrEmpty(orientation) && !string.Equals(orientation, "portrait", StringComparison.OrdinalIgnoreCase))
            {
                throw node.Error("orientation must be portrait or landscape, not '" + orientation + "'.");
            }

            var margin = Attr(node, "margin", scope);
            if (!string.IsNullOrEmpty(margin))
            {
                setup.Margins = ParseMargins(node, margin!);
            }

            setup.Columns = (int)(Number(node, "columns", scope) ?? 1);
            setup.ColumnGap = Length(node, "columnGap", scope) ?? setup.ColumnGap;
            setup.HeaderSpacing = Length(node, "headerSpacing", scope) ?? setup.HeaderSpacing;
            setup.FooterSpacing = Length(node, "footerSpacing", scope) ?? setup.FooterSpacing;
            setup.DifferentFirstPage = Bool(node, "differentFirstPage", scope) ?? false;

            var section = _doc.AddSection(setup);
            var body = new List<KeyValuePair<TemplateNode, Scope>>();
            Each(node.Children, scope, (child, s) =>
            {
                switch (child.Name)
                {
                    case "header":
                        BindBlocks(section.Header, child.Children, s, ContainerDefaults.Body);
                        break;
                    case "footer":
                        BindBlocks(section.Footer, child.Children, s, ContainerDefaults.Body);
                        break;
                    case "firstHeader":
                        BindBlocks(section.FirstPageHeader, child.Children, s, ContainerDefaults.Body);
                        break;
                    case "firstFooter":
                        BindBlocks(section.FirstPageFooter, child.Children, s, ContainerDefaults.Body);
                        break;
                    default:
                        body.Add(new KeyValuePair<TemplateNode, Scope>(child, s));
                        break;
                }
            });

            BindExpanded(section, body, ContainerDefaults.Body);
        }

        // ------------------------------------------------------------------ blocks

        private void BindBlocks(BlockContainer container, List<TemplateNode> nodes, Scope scope, ContainerDefaults defaults)
        {
            var expanded = new List<KeyValuePair<TemplateNode, Scope>>();
            Each(nodes, scope, (node, s) => expanded.Add(new KeyValuePair<TemplateNode, Scope>(node, s)));
            BindExpanded(container, expanded, defaults);
        }

        /// <summary>Binds already expanded nodes; runs of inline content become implicit paragraphs.</summary>
        private void BindExpanded(BlockContainer container, List<KeyValuePair<TemplateNode, Scope>> nodes, ContainerDefaults defaults)
        {
            var pendingInline = new List<KeyValuePair<TemplateNode, Scope>>();

            void Flush()
            {
                if (pendingInline.Count == 0)
                {
                    return;
                }

                var builder = new InlineBuilder(this);
                foreach (var pair in pendingInline)
                {
                    builder.Visit(pair.Key, pair.Value, defaults.Style);
                }

                pendingInline.Clear();
                if (builder.HasContent)
                {
                    var p = container.AddParagraph();
                    p.Format.SpaceAfter = defaults.SpaceAfter;
                    if (defaults.Alignment.HasValue)
                    {
                        p.Format.Alignment = defaults.Alignment.Value;
                    }

                    builder.WriteTo(p);
                }
            }

            foreach (var pair in nodes)
            {
                var node = pair.Key;
                if (node.IsText || IsInline(node.Name))
                {
                    pendingInline.Add(pair);
                    continue;
                }

                Flush();
                BindBlock(container, node, pair.Value, defaults);
            }

            Flush();
        }

        private void BindBlock(BlockContainer container, TemplateNode node, Scope scope, ContainerDefaults defaults)
        {
            switch (node.Name)
            {
                case "p":
                case "h1":
                case "h2":
                case "h3":
                {
                    var p = container.AddParagraph();
                    if (node.Name != "p")
                    {
                        p.HeadingLevel = node.Name[1] - '0';
                        p.Format.KeepWithNext = true;
                    }
                    else
                    {
                        p.Format.SpaceAfter = defaults.SpaceAfter;
                        if (defaults.Alignment.HasValue)
                        {
                            p.Format.Alignment = defaults.Alignment.Value;
                        }
                    }

                    ApplyParagraphFormat(node, scope, p.Format);
                    var builder = new InlineBuilder(this);
                    var style = Combine(defaults.Style, Style(node, scope));
                    Each(node.Children, scope, (child, s) => builder.Visit(child, s, style));
                    builder.WriteTo(p);
                    break;
                }

                case "list":
                    BindList(container.AddList(ListKindOf(node, scope)), node, scope, defaults, 0);
                    break;
                case "table":
                    BindTable(container, node, scope);
                    break;
                case "image":
                    BindImage(container, node, scope);
                    break;
                case "qrcode":
                case "barcode":
                    BindCode(container, node, scope);
                    break;
                case "hr":
                {
                    var color = Color(node, "color", scope) ?? PdfColor.FromHex("#D1D5DB");
                    var line = container.AddHorizontalLine(new PdfPen(color, Length(node, "thickness", scope) ?? 0.75));
                    line.SpaceBefore = Length(node, "spaceBefore", scope) ?? line.SpaceBefore;
                    line.SpaceAfter = Length(node, "spaceAfter", scope) ?? line.SpaceAfter;
                    break;
                }

                case "spacer":
                    container.AddSpacer(Length(node, "height", scope) ?? 12);
                    break;
                case "pagebreak":
                    try
                    {
                        container.AddPageBreak();
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw node.Error(ex.Message);
                    }

                    break;
                case "header":
                case "footer":
                case "firstHeader":
                case "firstFooter":
                    throw node.Error("<" + node.Name + "> belongs directly inside <section>.");
                default:
                    throw node.Error("<" + node.Name + "> is not allowed here.");
            }
        }

        private void BindList(ListBlock list, TemplateNode node, Scope scope, ContainerDefaults defaults, int depth)
        {
            list.StartNumber = (int)(Number(node, "start", scope) ?? 1);
            list.Indent = Length(node, "indent", scope) ?? list.Indent;
            var listStyle = Combine(defaults.Style, Style(node, scope));
            Each(node.Children, scope, (item, s) =>
            {
                if (item.IsText)
                {
                    return;
                }

                if (item.Name != "item")
                {
                    throw item.Error("Only <item> is allowed inside <list>.");
                }

                var p = list.AddItem(string.Empty);
                ApplyParagraphFormat(item, s, p.Format);
                var builder = new InlineBuilder(this);
                var style = Combine(listStyle, Style(item, s));
                var nested = new List<KeyValuePair<TemplateNode, Scope>>();
                Each(item.Children, s, (child, cs) =>
                {
                    if (child.Name == "list")
                    {
                        nested.Add(new KeyValuePair<TemplateNode, Scope>(child, cs));
                    }
                    else
                    {
                        builder.Visit(child, cs, style);
                    }
                });
                builder.WriteTo(p);
                foreach (var child in nested)
                {
                    if (depth > 20)
                    {
                        throw child.Key.Error("Lists are nested too deeply.");
                    }

                    BindList(list.AddList(ListKindOf(child.Key, child.Value)), child.Key, child.Value, new ContainerDefaults(listStyle, null, 3), depth + 1);
                }
            });
        }

        private void BindTable(BlockContainer container, TemplateNode node, Scope scope)
        {
            var table = container.AddTable();
            table.Width = Length(node, "width", scope);
            table.HeaderRows = (int)(Number(node, "headerRows", scope) ?? 0);
            table.AlternateRowBackground = Color(node, "zebra", scope);
            if (node.Attributes.ContainsKey("headerBackground"))
            {
                table.HeaderBackground = Color(node, "headerBackground", scope);
            }

            if (node.Attributes.ContainsKey("border"))
            {
                table.Border = Pen(node, "border", scope);
            }

            if (node.Attributes.ContainsKey("grid"))
            {
                table.GridLines = Pen(node, "grid", scope);
            }

            var padding = Attr(node, "padding", scope);
            if (!string.IsNullOrEmpty(padding))
            {
                table.CellPadding = ParseMargins(node, padding!);
            }

            table.SpaceBefore = Length(node, "spaceBefore", scope) ?? table.SpaceBefore;
            table.SpaceAfter = Length(node, "spaceAfter", scope) ?? table.SpaceAfter;
            table.KeepWithNext = Bool(node, "keepWithNext", scope) ?? false;
            var tableStyle = Style(node, scope);
            table.Style = tableStyle;

            var occupied = new Dictionary<int, HashSet<int>>(); // row → columns covered by earlier spans
            var aligns = new List<ParagraphAlignment?>();
            Each(node.Children, scope, (child, s) =>
            {
                if (child.IsText)
                {
                    return;
                }

                if (child.Name == "column")
                {
                    if (table.Rows.Count > 0)
                    {
                        throw child.Error("<column> elements must come before the first <row>.");
                    }

                    var align = Alignment(child, "align", s);
                    table.AddColumn(ParseColumnWidth(child, Attr(child, "width", s) ?? "*"), align);
                    aligns.Add(align);
                    return;
                }

                if (child.Name != "row")
                {
                    throw child.Error("Only <column> and <row> are allowed inside <table>.");
                }

                if (table.Columns.Count == 0)
                {
                    throw child.Error("Declare the table's <column> elements before its rows.");
                }

                BindRow(table, child, s, occupied, aligns);
            });
        }

        private void BindRow(Table table, TemplateNode node, Scope scope, Dictionary<int, HashSet<int>> occupied, List<ParagraphAlignment?> aligns)
        {
            var row = table.AddRow();
            var r = table.Rows.Count - 1;
            row.Background = Color(node, "bg", scope);
            row.MinHeight = Length(node, "minHeight", scope) ?? 0;
            var rowStyle = Style(node, scope);
            var columns = table.Columns.Count;
            var cursor = 0;
            Each(node.Children, scope, (cellNode, s) =>
            {
                if (cellNode.IsText)
                {
                    return;
                }

                if (cellNode.Name != "cell")
                {
                    throw cellNode.Error("Only <cell> is allowed inside <row>.");
                }

                while (cursor < columns && occupied.TryGetValue(r, out var taken) && taken.Contains(cursor))
                {
                    cursor++;
                }

                if (cursor >= columns)
                {
                    throw cellNode.Error("This row has more cells than the table has columns (" + columns + "). Remember that colspan/rowspan cells take up several positions.");
                }

                var cell = row.Cells[cursor];
                var colSpan = (int)(Number(cellNode, "colspan", s) ?? 1);
                var rowSpan = (int)(Number(cellNode, "rowspan", s) ?? 1);
                if (colSpan < 1 || rowSpan < 1)
                {
                    throw cellNode.Error("colspan and rowspan must be 1 or more.");
                }

                colSpan = Math.Min(colSpan, columns - cursor);
                cell.ColSpan = colSpan;
                cell.RowSpan = rowSpan;
                for (var rr = r; rr < r + rowSpan; rr++)
                {
                    for (var cc = cursor; cc < cursor + colSpan; cc++)
                    {
                        if (rr == r && cc == cursor)
                        {
                            continue;
                        }

                        if (!occupied.TryGetValue(rr, out var set))
                        {
                            occupied[rr] = set = new HashSet<int>();
                        }

                        set.Add(cc);
                    }
                }

                cell.Background = Color(cellNode, "bg", s);
                var valign = Attr(cellNode, "valign", s);
                if (!string.IsNullOrEmpty(valign))
                {
                    cell.VerticalAlignment = ParseEnum<VerticalAlignment>(cellNode, "valign", valign!);
                }

                var padding = Attr(cellNode, "padding", s);
                if (!string.IsNullOrEmpty(padding))
                {
                    cell.Padding = ParseMargins(cellNode, padding!);
                }

                var align = Alignment(cellNode, "align", s) ?? aligns[cursor];
                var style = Combine(rowStyle, Style(cellNode, s));
                BindBlocks(cell, cellNode.Children, s, new ContainerDefaults(style, align, 0));
                cursor += colSpan;
            });
        }

        private void BindImage(BlockContainer container, TemplateNode node, Scope scope)
        {
            if (!node.Attributes.TryGetValue("src", out var src))
            {
                throw node.Error("<image> needs a src=\"…\" attribute (file path, image name, data: URI or {{ data field }}).");
            }

            var image = ResolveImage(node, src.EvaluateRaw(scope));
            if (image == null)
            {
                return; // empty src: an optional image (e.g. a logo) that the data does not provide
            }

            var block = container.AddImage(image, Length(node, "width", scope), Alignment(node, "align", scope) ?? ParagraphAlignment.Left);
            block.SpaceBefore = Length(node, "spaceBefore", scope) ?? block.SpaceBefore;
            block.SpaceAfter = Length(node, "spaceAfter", scope) ?? block.SpaceAfter;
        }

        /// <summary>&lt;qrcode value="…"/&gt; and &lt;barcode type="…" value="…"/&gt;. An empty value draws nothing.</summary>
        private void BindCode(BlockContainer container, TemplateNode node, Scope scope)
        {
            var value = Attr(node, "value", scope);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            BarcodeBlock block;
            try
            {
                if (node.Name == "qrcode")
                {
                    var ecc = Attr(node, "ecc", scope);
                    var level = Barcodes.QrErrorCorrection.Medium;
                    switch ((ecc ?? "M").ToUpperInvariant())
                    {
                        case "L": level = Barcodes.QrErrorCorrection.Low; break;
                        case "M": level = Barcodes.QrErrorCorrection.Medium; break;
                        case "Q": level = Barcodes.QrErrorCorrection.Quartile; break;
                        case "H": level = Barcodes.QrErrorCorrection.High; break;
                        default: throw node.Error("ecc must be L, M, Q or H, not '" + ecc + "'.");
                    }

                    block = container.AddQrCode(Barcodes.QrCode.Encode(value!, level), Length(node, "size", scope) ?? 90, Alignment(node, "align", scope) ?? ParagraphAlignment.Left);
                }
                else
                {
                    var type = Attr(node, "type", scope) ?? "code128";
                    block = container.AddBarcode(Barcodes.Barcode.Create(type, value!), Length(node, "width", scope) ?? 180, Length(node, "height", scope) ?? 50,
                        Alignment(node, "align", scope) ?? ParagraphAlignment.Left);
                    block.ShowText = Bool(node, "showText", scope) ?? true;
                }
            }
            catch (ArgumentException ex)
            {
                throw node.Error("Cannot create the " + node.Name + ": " + ex.Message);
            }

            block.Color = Color(node, "color", scope) ?? block.Color;
            block.SpaceBefore = Length(node, "spaceBefore", scope) ?? block.SpaceBefore;
            block.SpaceAfter = Length(node, "spaceAfter", scope) ?? block.SpaceAfter;
        }

        // ------------------------------------------------------------------ inline content

        private static bool IsInline(string name) =>
            name == "b" || name == "i" || name == "u" || name == "s" || name == "span" || name == "br" || name == "pageNumber" || name == "pageCount";

        /// <summary>
        /// Collects text runs for one paragraph. Template whitespace is collapsed like HTML
        /// (runs → one space, trimmed at the edges); values inserted from data keep their
        /// own spacing and line breaks.
        /// </summary>
        private sealed class InlineBuilder
        {
            private readonly Binder _binder;
            private readonly List<Segment> _segments = new List<Segment>();
            private bool _lastWasSpace = true;

            public InlineBuilder(Binder binder)
            {
                _binder = binder;
            }

            public bool HasContent => _segments.Any(s => s.Kind != SegmentKind.Text || s.Text.Trim().Length > 0);

            public void Visit(TemplateNode node, Scope scope, TextStyle? style)
            {
                if (node.IsText)
                {
                    foreach (var part in node.Text!.Parts)
                    {
                        if (part is string literal)
                        {
                            AddText(Whitespace.Replace(literal, " "), style, collapse: true);
                        }
                        else
                        {
                            var value = Values.ToText(((Expression)part).Evaluate(scope), scope.Options);
                            AddText(value, style, collapse: false);
                        }
                    }

                    return;
                }

                switch (node.Name)
                {
                    case "br":
                        TrimTrailingSpace();
                        _segments.Add(new Segment(SegmentKind.LineBreak, string.Empty, style));
                        _lastWasSpace = true;
                        return;
                    case "pageNumber":
                    case "pageCount":
                        _segments.Add(new Segment(node.Name == "pageNumber" ? SegmentKind.PageNumber : SegmentKind.PageCount, string.Empty,
                            Combine(style, _binder.Style(node, scope))));
                        _lastWasSpace = false;
                        return;
                }

                TextStyle? childStyle;
                switch (node.Name)
                {
                    case "b": childStyle = Combine(style, new TextStyle { Bold = true }); break;
                    case "i": childStyle = Combine(style, new TextStyle { Italic = true }); break;
                    case "u": childStyle = Combine(style, new TextStyle { Underline = true }); break;
                    case "s": childStyle = Combine(style, new TextStyle { Strikethrough = true }); break;
                    case "span": childStyle = Combine(style, _binder.Style(node, scope)); break;
                    default: throw node.Error("<" + node.Name + "> is a block element and cannot be used inside a paragraph.");
                }

                _binder.Each(node.Children, scope, (child, s) => Visit(child, s, childStyle));
            }

            public void WriteTo(Paragraph p)
            {
                TrimTrailingSpace();
                foreach (var s in _segments)
                {
                    switch (s.Kind)
                    {
                        case SegmentKind.Text:
                            if (s.Text.Length > 0)
                            {
                                p.AddText(s.Text, s.Style);
                            }

                            break;
                        case SegmentKind.LineBreak:
                            p.AddLineBreak();
                            break;
                        case SegmentKind.PageNumber:
                            p.AddPageNumber(s.Style);
                            break;
                        case SegmentKind.PageCount:
                            p.AddPageCount(s.Style);
                            break;
                    }
                }
            }

            private void AddText(string text, TextStyle? style, bool collapse)
            {
                if (collapse && _lastWasSpace && text.StartsWith(" ", StringComparison.Ordinal))
                {
                    text = text.Substring(1);
                }

                if (text.Length == 0)
                {
                    return;
                }

                _segments.Add(new Segment(SegmentKind.Text, text, style));
                _lastWasSpace = text.EndsWith(" ", StringComparison.Ordinal) || text.EndsWith("\n", StringComparison.Ordinal);
            }

            private void TrimTrailingSpace()
            {
                for (var i = _segments.Count - 1; i >= 0; i--)
                {
                    var s = _segments[i];
                    if (s.Kind != SegmentKind.Text)
                    {
                        return;
                    }

                    var trimmed = s.Text.TrimEnd(' ');
                    _segments[i] = new Segment(SegmentKind.Text, trimmed, s.Style);
                    if (trimmed.Length > 0)
                    {
                        return;
                    }
                }
            }

            private enum SegmentKind
            {
                Text,
                LineBreak,
                PageNumber,
                PageCount,
            }

            private readonly struct Segment
            {
                public Segment(SegmentKind kind, string text, TextStyle? style)
                {
                    Kind = kind;
                    Text = text;
                    Style = style;
                }

                public SegmentKind Kind { get; }

                public string Text { get; }

                public TextStyle? Style { get; }
            }
        }

        // ------------------------------------------------------------------ attribute parsing

        private string? Attr(TemplateNode node, string name, Scope scope) =>
            node.Attributes.TryGetValue(name, out var value) ? value.Evaluate(scope).Trim() : null;

        private decimal? Number(TemplateNode node, string name, Scope scope)
        {
            var text = Attr(node, name, scope);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
            {
                throw node.Error(name + "=\"" + text + "\" is not a number.");
            }

            return n;
        }

        private bool? Bool(TemplateNode node, string name, Scope scope)
        {
            var text = Attr(node, name, scope);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            switch (text!.ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "1":
                    return true;
                case "false":
                case "no":
                case "0":
                    return false;
                default:
                    throw node.Error(name + "=\"" + text + "\" must be true or false.");
            }
        }

        /// <summary>A length: plain number = points, or with unit pt, mm, cm, in.</summary>
        private double? Length(TemplateNode node, string name, Scope scope)
        {
            var text = Attr(node, name, scope);
            return string.IsNullOrEmpty(text) ? (double?)null : ParseLength(node, name, text!);
        }

        private static double ParseLength(TemplateNode node, string name, string text)
        {
            var t = text.Trim().ToLowerInvariant();
            double factor = 1;
            foreach (var unit in new[] { ("mm", 72 / 25.4), ("cm", 72 / 2.54), ("in", 72.0), ("pt", 1.0) })
            {
                if (t.EndsWith(unit.Item1, StringComparison.Ordinal))
                {
                    factor = unit.Item2;
                    t = t.Substring(0, t.Length - unit.Item1.Length).Trim();
                    break;
                }
            }

            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || double.IsInfinity(v))
            {
                throw node.Error(name + "=\"" + text + "\" is not a valid length. Use e.g. 12, 12pt, 5mm, 1.5cm or 1in.");
            }

            return v * factor;
        }

        private static Margins ParseMargins(TemplateNode node, string text)
        {
            var parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => ParseLength(node, "margin", p)).ToArray();
            switch (parts.Length)
            {
                case 1: return new Margins(parts[0]);
                case 2: return new Margins(parts[1], parts[0]);                         // vertical horizontal (CSS order)
                case 4: return new Margins(parts[3], parts[0], parts[1], parts[2]);     // top right bottom left (CSS order)
                default: throw node.Error("Use 1, 2 or 4 values for margins/padding (CSS order: top right bottom left), not '" + text + "'.");
            }
        }

        private static PageSize ParsePageSize(TemplateNode node, string text)
        {
            switch (text.Trim().ToUpperInvariant())
            {
                case "A3": return PageSize.A3;
                case "A4": return PageSize.A4;
                case "A5": return PageSize.A5;
                case "LETTER": return PageSize.Letter;
                case "LEGAL": return PageSize.Legal;
            }

            var parts = text.ToLowerInvariant().Split('x');
            if (parts.Length == 2)
            {
                try
                {
                    return new PageSize(ParseLength(node, "size", parts[0]), ParseLength(node, "size", parts[1]));
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw node.Error(ex.Message);
                }
            }

            throw node.Error("Unknown page size '" + text + "'. Use A3, A4, A5, Letter, Legal or a size like \"210mm x 297mm\".");
        }

        private static ColumnWidth ParseColumnWidth(TemplateNode node, string text)
        {
            var t = text.Trim().ToLowerInvariant();
            if (t == "auto")
            {
                return ColumnWidth.Auto;
            }

            if (t.EndsWith("*", StringComparison.Ordinal))
            {
                var weight = t.Length == 1 ? "1" : t.Substring(0, t.Length - 1);
                if (double.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0)
                {
                    return ColumnWidth.Relative(w);
                }
            }
            else if (t.EndsWith("%", StringComparison.Ordinal))
            {
                if (double.TryParse(t.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p >= 0)
                {
                    return ColumnWidth.Percent(p);
                }
            }
            else
            {
                return ColumnWidth.Fixed(ParseLength(node, "width", t));
            }

            throw node.Error("Column width '" + text + "' is not valid. Use auto, *, 2*, 30% or a length such as 80 or 25mm.");
        }

        private PdfColor? Color(TemplateNode node, string name, Scope scope)
        {
            var text = Attr(node, name, scope);
            if (string.IsNullOrEmpty(text) || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            switch (text!.ToLowerInvariant())
            {
                case "black": return PdfColor.Black;
                case "white": return PdfColor.White;
                case "gray":
                case "grey": return PdfColor.Gray;
                case "red": return PdfColor.Red;
                case "green": return PdfColor.Green;
                case "blue": return PdfColor.Blue;
            }

            try
            {
                return PdfColor.FromHex(text);
            }
            catch (FormatException)
            {
                throw node.Error(name + "=\"" + text + "\" is not a color. Use #RRGGBB, #RGB or black, white, gray, red, green, blue.");
            }
        }

        /// <summary>"none", a color ("#999") or "color width" ("#999 0.5").</summary>
        private PdfPen? Pen(TemplateNode node, string name, Scope scope)
        {
            var text = Attr(node, name, scope);
            if (string.IsNullOrEmpty(text) || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = text!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var colorNode = new TemplateNode(node.Name, node.Line, node.Column);
            colorNode.Attributes["c"] = Interpolated.Literal(parts[0]);
            var color = Color(colorNode, "c", scope) ?? PdfColor.Black;
            var width = parts.Length > 1 ? ParseLength(node, name, parts[1]) : 0.75;
            return new PdfPen(color, width);
        }

        private ParagraphAlignment? Alignment(TemplateNode node, string name, Scope scope)
        {
            var text = Attr(node, name, scope);
            return string.IsNullOrEmpty(text) ? (ParagraphAlignment?)null : ParseEnum<ParagraphAlignment>(node, name, text!);
        }

        private static T ParseEnum<T>(TemplateNode node, string name, string text)
            where T : struct
        {
            var value = text.Trim();
            if (string.Equals(value, "middle", StringComparison.OrdinalIgnoreCase) && typeof(T) == typeof(ParagraphAlignment))
            {
                value = "Center";
            }

            if (Enum.TryParse<T>(value, true, out var result) && Enum.IsDefined(typeof(T), result))
            {
                return result;
            }

            throw node.Error(name + "=\"" + text + "\" is not valid. Use one of: " + string.Join(", ", Enum.GetNames(typeof(T)).Select(n => n.ToLowerInvariant())) + ".");
        }

        private ListKind ListKindOf(TemplateNode node, Scope scope)
        {
            var kind = Attr(node, "kind", scope);
            return string.IsNullOrEmpty(kind) ? ListKind.Bullet : ParseEnum<ListKind>(node, "kind", kind!);
        }

        private void ApplyParagraphFormat(TemplateNode node, Scope scope, ParagraphFormat format)
        {
            var align = Alignment(node, "align", scope);
            if (align.HasValue)
            {
                format.Alignment = align.Value;
            }

            format.SpaceBefore = Length(node, "spaceBefore", scope) ?? format.SpaceBefore;
            format.SpaceAfter = Length(node, "spaceAfter", scope) ?? format.SpaceAfter;
            format.LeftIndent = Length(node, "indent", scope) ?? format.LeftIndent;
            format.RightIndent = Length(node, "rightIndent", scope) ?? format.RightIndent;
            var first = Attr(node, "firstIndent", scope);
            if (!string.IsNullOrEmpty(first))
            {
                var negative = first!.StartsWith("-", StringComparison.Ordinal);
                var length = ParseLength(node, "firstIndent", negative ? first.Substring(1) : first);
                format.FirstLineIndent = negative ? -length : length;
            }

            var spacing = Number(node, "lineSpacing", scope);
            if (spacing.HasValue)
            {
                format.LineSpacing = (double)spacing.Value;
            }

            format.KeepWithNext = Bool(node, "keepWithNext", scope) ?? format.KeepWithNext;
            format.KeepTogether = Bool(node, "keepTogether", scope) ?? format.KeepTogether;
            format.PageBreakBefore = Bool(node, "pageBreakBefore", scope) ?? format.PageBreakBefore;
        }

        /// <summary>Character style from family/size/color/bold/… attributes (null when none are set).</summary>
        private TextStyle? Style(TemplateNode node, Scope scope)
        {
            TextStyle? style = null;
            TextStyle S() => style ?? (style = new TextStyle());

            var family = Attr(node, "family", scope);
            if (!string.IsNullOrEmpty(family))
            {
                S().Family = Family(node, family!);
            }

            var size = Length(node, "size", scope);
            if (size.HasValue)
            {
                if (size.Value <= 0)
                {
                    throw node.Error("size must be greater than zero.");
                }

                S().Size = size;
            }

            var color = Color(node, "color", scope);
            if (color.HasValue)
            {
                S().Color = color;
            }

            var bold = Bool(node, "bold", scope);
            if (bold.HasValue)
            {
                S().Bold = bold;
            }

            var italic = Bool(node, "italic", scope);
            if (italic.HasValue)
            {
                S().Italic = italic;
            }

            var underline = Bool(node, "underline", scope);
            if (underline.HasValue)
            {
                S().Underline = underline;
            }

            var strike = Bool(node, "strike", scope);
            if (strike.HasValue)
            {
                S().Strikethrough = strike;
            }

            var kerning = Bool(node, "kerning", scope);
            if (kerning.HasValue)
            {
                S().Kerning = kerning;
            }

            var fallback = Attr(node, "fallback", scope);
            if (!string.IsNullOrEmpty(fallback))
            {
                foreach (var name in fallback!.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    S().Fallbacks.Add(Family(node, name));
                }
            }

            return style;
        }

        private FontFamily Family(TemplateNode node, string name)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "helvetica":
                case "sans":
                    return FontFamily.Helvetica;
                case "times":
                case "serif":
                    return FontFamily.Times;
                case "courier":
                case "mono":
                    return FontFamily.Courier;
            }

            if (_fonts.TryGetValue(name.Trim(), out var family))
            {
                return family;
            }

            throw node.Error("Unknown font family '" + name + "'. Use helvetica, times, courier, a <font name=\"…\"> declared in <styles>, or a font added to TemplateOptions.Fonts." +
                (_fonts.Count > 0 ? " Declared: " + string.Join(", ", _fonts.Keys) + "." : string.Empty));
        }

        private static TextStyle? Combine(TextStyle? a, TextStyle? b) => a == null ? b : b == null ? a : a.Merge(b);

        // ------------------------------------------------------------------ files

        private PdfFontFace? LoadFont(TemplateNode node, string attribute, Scope scope)
        {
            var path = Attr(node, attribute, scope);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var full = ResolvePath(node, path!);
            try
            {
                return _cache.Font(full);
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                throw node.Error("Cannot load font '" + path + "': " + ex.Message);
            }
        }

        private PdfImage? ResolveImage(TemplateNode node, object? value)
        {
            try
            {
                switch (value)
                {
                    case null:
                        return null;
                    case PdfImage image:
                        return image;
                    case byte[] bytes:
                        return bytes.Length == 0 ? null : PdfImage.FromBytes(bytes);
                }

                var text = Values.ToText(value, _options).Trim();
                if (text.Length == 0)
                {
                    return null;
                }

                if (_options.Images.TryGetValue(text, out var named))
                {
                    return named;
                }

                if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = text.IndexOf(',');
                    if (comma < 0 || text.Substring(0, comma).IndexOf(";base64", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        throw node.Error("Only base64 data: URIs are supported (data:image/png;base64,…).");
                    }

                    return PdfImage.FromBytes(Convert.FromBase64String(text.Substring(comma + 1)));
                }

                return _cache.Image(ResolvePath(node, text));
            }
            catch (TemplateException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                throw node.Error("Cannot load image: " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves a relative path against the base directory. Absolute paths and paths that
        /// leave the base directory are refused unless explicitly allowed (untrusted templates).
        /// </summary>
        private string ResolvePath(TemplateNode node, string path)
        {
            if (_options.AllowFileAccessOutsideBaseDirectory)
            {
                return Path.GetFullPath(Path.Combine(_options.BaseDirectory ?? Directory.GetCurrentDirectory(), path));
            }

            if (string.IsNullOrEmpty(_options.BaseDirectory))
            {
                throw node.Error("'" + path + "' is a file path, but the template has no base directory. Load the template with PdfTemplate.Load(path) or set TemplateOptions.BaseDirectory.");
            }

            if (Path.IsPathRooted(path))
            {
                throw node.Error("Absolute path '" + path + "' is not allowed. Use a path relative to the template folder, or set TemplateOptions.AllowFileAccessOutsideBaseDirectory.");
            }

            var root = Path.GetFullPath(_options.BaseDirectory!);
            var rootWithSlash = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                throw node.Error("Path '" + path + "' leaves the template folder. Keep images and fonts inside it, or set TemplateOptions.AllowFileAccessOutsideBaseDirectory.");
            }

            return full;
        }

        private sealed class ContainerDefaults
        {
            public static readonly ContainerDefaults Body = new ContainerDefaults(null, null, 6);

            public ContainerDefaults(TextStyle? style, ParagraphAlignment? alignment, double spaceAfter)
            {
                Style = style;
                Alignment = alignment;
                SpaceAfter = spaceAfter;
            }

            public TextStyle? Style { get; }

            public ParagraphAlignment? Alignment { get; }

            public double SpaceAfter { get; }
        }
    }

    /// <summary>Fonts and images loaded from files, shared by all renders of one template.</summary>
    internal sealed class ResourceCache
    {
        private readonly Dictionary<string, PdfFontFace> _fonts = new Dictionary<string, PdfFontFace>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PdfImage> _images = new Dictionary<string, PdfImage>(StringComparer.OrdinalIgnoreCase);

        public PdfFontFace Font(string path)
        {
            lock (_fonts)
            {
                if (!_fonts.TryGetValue(path, out var face))
                {
                    face = PdfFontFace.FromFile(path);
                    _fonts[path] = face;
                }

                return face;
            }
        }

        public PdfImage Image(string path)
        {
            lock (_images)
            {
                if (!_images.TryGetValue(path, out var image))
                {
                    image = PdfImage.FromFile(path);
                    _images[path] = image;
                }

                return image;
            }
        }
    }
}
