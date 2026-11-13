using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using LifeOS.Pdf.Templates.Expressions;

namespace LifeOS.Pdf.Templates.Markup
{
    /// <summary>
    /// Reads template XML into a validated <see cref="TemplateNode"/> tree. Unknown elements
    /// and attributes are reported with their line and column, so typos never fail silently.
    /// DTDs are prohibited (no XXE, no entity expansion attacks).
    /// </summary>
    internal static class TemplateReader
    {
        private static readonly string[] StyleAttributes = { "family", "size", "color", "bold", "italic", "underline", "strike", "kerning" };
        private static readonly string[] ParagraphAttributes =
            { "align", "spaceBefore", "spaceAfter", "indent", "rightIndent", "firstIndent", "lineSpacing", "keepWithNext", "keepTogether", "pageBreakBefore" };

        private static readonly string[] Blocks = { "h1", "h2", "h3", "p", "list", "table", "image", "hr", "spacer", "pagebreak" };
        private static readonly string[] Inlines = { TemplateNode.TextName, "b", "i", "u", "s", "span", "br", "pageNumber", "pageCount" };
        private static readonly string[] Control = { "foreach", "if", "else", "set" };

        /// <summary>Element → (allowed attributes, allowed children).</summary>
        private static readonly Dictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.Ordinal)
        {
            ["document"] = new Rule(new[] { "title", "author", "subject", "keywords", "creator" }, new[] { "styles", "section" }),
            ["styles"] = new Rule(new string[0], new[] { "font", "normal", "heading", "tableHeader" }),
            ["font"] = new Rule(new[] { "name", "regular", "bold", "italic", "boldItalic" }, new string[0]),
            ["normal"] = new Rule(StyleAttributes.Concat(new[] { "fallback" }).ToArray(), new string[0]),
            ["tableHeader"] = new Rule(StyleAttributes, new string[0]),
            ["heading"] = new Rule(StyleAttributes.Concat(new[] { "level", "spaceBefore", "spaceAfter" }).ToArray(), new string[0]),
            ["section"] = new Rule(new[] { "size", "orientation", "margin", "columns", "columnGap", "headerSpacing", "footerSpacing", "differentFirstPage" },
                new[] { "header", "footer", "firstHeader", "firstFooter" }.Concat(Blocks).Concat(Inlines).ToArray()),
            ["header"] = Container(),
            ["footer"] = Container(),
            ["firstHeader"] = Container(),
            ["firstFooter"] = Container(),
            ["h1"] = InlineContainer(),
            ["h2"] = InlineContainer(),
            ["h3"] = InlineContainer(),
            ["p"] = InlineContainer(),
            ["b"] = new Rule(new string[0], Inlines),
            ["i"] = new Rule(new string[0], Inlines),
            ["u"] = new Rule(new string[0], Inlines),
            ["s"] = new Rule(new string[0], Inlines),
            ["span"] = new Rule(StyleAttributes, Inlines),
            ["br"] = new Rule(new string[0], new string[0]),
            ["pageNumber"] = new Rule(StyleAttributes, new string[0]),
            ["pageCount"] = new Rule(StyleAttributes, new string[0]),
            ["list"] = new Rule(new[] { "kind", "start", "indent" }.Concat(StyleAttributes).ToArray(), new[] { "item" }),
            ["item"] = new Rule(ParagraphAttributes.Concat(StyleAttributes).ToArray(), Inlines.Concat(new[] { "list" }).ToArray()),
            ["table"] = new Rule(new[] { "width", "headerRows", "zebra", "headerBackground", "border", "grid", "padding", "spaceBefore", "spaceAfter", "keepWithNext" }
                .Concat(StyleAttributes).ToArray(), new[] { "column", "row" }),
            ["column"] = new Rule(new[] { "width", "align" }, new string[0]),
            ["row"] = new Rule(new[] { "bg", "minHeight" }.Concat(StyleAttributes).ToArray(), new[] { "cell" }),
            ["cell"] = new Rule(new[] { "colspan", "rowspan", "bg", "valign", "align", "padding" }.Concat(StyleAttributes).ToArray(), Blocks.Concat(Inlines).ToArray()),
            ["image"] = new Rule(new[] { "src", "width", "align", "spaceBefore", "spaceAfter" }, new string[0]),
            ["hr"] = new Rule(new[] { "color", "thickness", "spaceBefore", "spaceAfter" }, new string[0]),
            ["spacer"] = new Rule(new[] { "height" }, new string[0]),
            ["pagebreak"] = new Rule(new string[0], new string[0]),
        };

        public static TemplateNode Read(TextReader text)
        {
            XDocument xml;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                };
                using (var reader = XmlReader.Create(text, settings))
                {
                    xml = XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                }
            }
            catch (XmlException ex)
            {
                throw new TemplateException("The template is not valid XML: " + ex.Message, ex.LineNumber, ex.LinePosition);
            }

            var root = xml.Root!;
            if (root.Name.LocalName != "document")
            {
                throw Error(root, "The root element must be <document>, not <" + root.Name.LocalName + ">.");
            }

            return Convert(root, null);
        }

        private static TemplateNode Convert(XElement element, string[]? inheritedChildren)
        {
            var name = element.Name.LocalName;
            var info = (IXmlLineInfo)element;
            var node = new TemplateNode(name, info.LineNumber, info.LinePosition);

            if (Control.Contains(name))
            {
                ReadControl(element, node);
            }
            else
            {
                if (!Rules.TryGetValue(name, out var rule))
                {
                    throw Error(element, "Unknown element <" + name + ">.");
                }

                foreach (var attribute in element.Attributes())
                {
                    var attrName = attribute.Name.LocalName;
                    if (attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    var position = (IXmlLineInfo)attribute;
                    if (attrName == "if")
                    {
                        node.Condition = ParseExpression(attribute.Value, position);
                    }
                    else if (rule.Attributes.Contains(attrName, StringComparer.Ordinal))
                    {
                        node.Attributes[attrName] = Interpolated.Parse(attribute.Value, position.LineNumber, position.LinePosition);
                    }
                    else
                    {
                        throw new TemplateException("Unknown attribute '" + attrName + "' on <" + name + ">. Allowed: " +
                            string.Join(", ", rule.Attributes.Concat(new[] { "if" })) + ".", position.LineNumber, position.LinePosition);
                    }
                }
            }

            // <foreach>/<if>/<else> may contain exactly what their parent may contain.
            var allowed = Control.Contains(name) ? inheritedChildren : Rules[name].Children;
            var previousWasConditional = false;
            foreach (var child in element.Nodes())
            {
                if (child is XText text)
                {
                    var textInfo = (IXmlLineInfo)text;
                    var textNode = new TemplateNode(TemplateNode.TextName, textInfo.LineNumber, textInfo.LinePosition)
                    {
                        Text = Interpolated.Parse(text.Value, textInfo.LineNumber, textInfo.LinePosition),
                    };
                    if (!textNode.IsWhitespace)
                    {
                        if (allowed != null && !allowed.Contains(TemplateNode.TextName))
                        {
                            throw Error(text, "Text is not allowed directly inside <" + name + ">.");
                        }

                        previousWasConditional = false;
                    }

                    node.Children.Add(textNode);
                    continue;
                }

                if (!(child is XElement childElement))
                {
                    continue;
                }

                var childName = childElement.Name.LocalName;
                if (!Control.Contains(childName) && !Rules.ContainsKey(childName))
                {
                    throw Error(childElement, "Unknown element <" + childName + ">." +
                        (allowed != null ? " Allowed here: " + string.Join(", ", allowed.Where(a => a != TemplateNode.TextName).Select(a => "<" + a + ">")) + "." : string.Empty));
                }

                // Control elements may contain whatever their parent may contain.
                if (allowed != null && !Control.Contains(childName) && !allowed.Contains(childName))
                {
                    throw Error(childElement, "<" + childName + "> is not allowed inside <" + name + ">." +
                        (allowed.Length > 0 ? " Allowed here: " + string.Join(", ", allowed.Where(a => a != TemplateNode.TextName).Select(a => "<" + a + ">")) + "." : string.Empty));
                }

                if (childName == "else" && !previousWasConditional)
                {
                    throw Error(childElement, "<else> must directly follow an <if> element or an element with an if=\"…\" attribute.");
                }

                var converted = Convert(childElement, allowed);
                previousWasConditional = childName == "if" || (converted.Condition != null && childName != "else");
                node.Children.Add(converted);
            }

            return node;
        }

        private static void ReadControl(XElement element, TemplateNode node)
        {
            var name = element.Name.LocalName;
            var allowedAttributes = name == "foreach" ? new[] { "in", "as", "index" } : name == "if" ? new[] { "test" } : name == "set" ? new[] { "name", "value" } : new string[0];
            foreach (var attribute in element.Attributes())
            {
                var attrName = attribute.Name.LocalName;
                var position = (IXmlLineInfo)attribute;
                if (!allowedAttributes.Contains(attrName))
                {
                    throw new TemplateException("Unknown attribute '" + attrName + "' on <" + name + ">." +
                        (allowedAttributes.Length > 0 ? " Allowed: " + string.Join(", ", allowedAttributes) + "." : string.Empty), position.LineNumber, position.LinePosition);
                }

                switch (attrName)
                {
                    case "in":
                        node.Source = ParseExpression(attribute.Value, position);
                        break;
                    case "test":
                        node.Condition = ParseExpression(attribute.Value, position);
                        break;
                    case "value":
                        node.Source = ParseExpression(attribute.Value, position);
                        break;
                    default:
                        if (!IsIdentifier(attribute.Value))
                        {
                            throw new TemplateException("'" + attribute.Value + "' is not a valid variable name for " + attrName + "=.", position.LineNumber, position.LinePosition);
                        }

                        node.Attributes[attrName] = Interpolated.Literal(attribute.Value);
                        break;
                }
            }

            if (name == "foreach" && node.Source == null)
            {
                throw Error(element, "<foreach> needs an in=\"…\" attribute naming the list, e.g. <foreach in=\"Items\" as=\"item\">.");
            }

            if (name == "set" && (node.Source == null || !node.Attributes.ContainsKey("name")))
            {
                throw Error(element, "<set> needs name=\"…\" and value=\"…\", e.g. <set name=\"Subtotal\" value=\"Items | sum:'Amount'\"/>.");
            }

            if (name == "set" && element.Nodes().Any())
            {
                throw Error(element, "<set> cannot have content.");
            }

            if (name == "if" && node.Condition == null)
            {
                throw Error(element, "<if> needs a test=\"…\" attribute, e.g. <if test=\"Discount > 0\">.");
            }
        }

        /// <summary>Attribute expressions may be written with or without the {{ }} braces.</summary>
        private static Expression ParseExpression(string value, IXmlLineInfo position)
        {
            var text = value.Trim();
            if (text.StartsWith("{{", StringComparison.Ordinal) && text.EndsWith("}}", StringComparison.Ordinal))
            {
                text = text.Substring(2, text.Length - 4);
            }

            return Expression.Parse(text, position.LineNumber, position.LinePosition);
        }

        private static bool IsIdentifier(string s) =>
            s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_');

        private static TemplateException Error(XObject node, string message)
        {
            var info = (IXmlLineInfo)node;
            return new TemplateException(message, info.LineNumber, info.LinePosition);
        }

        private static Rule Container() => new Rule(new string[0], Blocks.Concat(Inlines).ToArray());

        private static Rule InlineContainer() => new Rule(ParagraphAttributes.Concat(StyleAttributes).ToArray(), Inlines);

        private sealed class Rule
        {
            public Rule(string[] attributes, string[] children)
            {
                Attributes = attributes;
                Children = children;
            }

            public string[] Attributes { get; }

            public string[] Children { get; }
        }
    }
}
