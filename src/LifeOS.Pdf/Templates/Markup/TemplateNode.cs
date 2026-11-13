using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Templates.Expressions;

namespace LifeOS.Pdf.Templates.Markup
{
    /// <summary>
    /// A parsed template element or text node. The tree is immutable after parsing, so a
    /// template can be bound many times (and from several threads).
    /// </summary>
    internal sealed class TemplateNode
    {
        public const string TextName = "#text";

        public TemplateNode(string name, int line, int column)
        {
            Name = name;
            Line = line;
            Column = column;
        }

        public string Name { get; }

        public int Line { get; }

        public int Column { get; }

        /// <summary>Text node content (only for <see cref="TextName"/>).</summary>
        public Interpolated? Text { get; set; }

        public Dictionary<string, Interpolated> Attributes { get; } = new Dictionary<string, Interpolated>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Condition from the <c>if</c> attribute (or the <c>test</c> of an &lt;if&gt; element).</summary>
        public Expression? Condition { get; set; }

        /// <summary>List expression of a &lt;foreach in="…"&gt;.</summary>
        public Expression? Source { get; set; }

        public List<TemplateNode> Children { get; } = new List<TemplateNode>();

        public bool IsText => Name == TextName;

        public bool IsWhitespace => IsText && Text!.IsWhitespace;

        public TemplateException Error(string message) => new TemplateException(message, Line, Column);
    }

    /// <summary>Text with <c>{{ expression }}</c> placeholders.</summary>
    internal sealed class Interpolated
    {
        private Interpolated(List<object> parts)
        {
            Parts = parts;
        }

        /// <summary>Literal strings and <see cref="Expression"/>s, in order.</summary>
        public List<object> Parts { get; }

        public bool IsWhitespace
        {
            get
            {
                foreach (var p in Parts)
                {
                    if (!(p is string s) || s.Trim().Length > 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>The single expression when the text is exactly one placeholder, else null.</summary>
        public Expression? SingleExpression => Parts.Count == 1 ? Parts[0] as Expression : null;

        public static Interpolated Parse(string text, int line, int column)
        {
            var parts = new List<object>();
            var pos = 0;
            while (pos < text.Length)
            {
                var open = text.IndexOf("{{", pos, StringComparison.Ordinal);
                if (open < 0)
                {
                    parts.Add(text.Substring(pos));
                    break;
                }

                if (open > pos)
                {
                    parts.Add(text.Substring(pos, open - pos));
                }

                var close = FindClose(text, open + 2);
                if (close < 0)
                {
                    throw new TemplateException("Placeholder '{{' is not closed with '}}'.", line, column);
                }

                parts.Add(Expression.Parse(text.Substring(open + 2, close - open - 2), line, column));
                pos = close + 2;
            }

            return new Interpolated(parts);
        }

        public static Interpolated Literal(string text) => new Interpolated(new List<object> { text });

        public string Evaluate(Scope scope)
        {
            if (Parts.Count == 1 && Parts[0] is string only)
            {
                return only;
            }

            var sb = new StringBuilder();
            foreach (var p in Parts)
            {
                sb.Append(p is Expression e ? Values.ToText(e.Evaluate(scope), scope.Options) : (string)p);
            }

            return sb.ToString();
        }

        /// <summary>The raw value for a single placeholder (keeps byte[], images, numbers); otherwise the text.</summary>
        public object? EvaluateRaw(Scope scope) => SingleExpression != null ? SingleExpression.Evaluate(scope) : Evaluate(scope);

        /// <summary>Finds the closing braces, skipping over quoted strings inside the expression.</summary>
        private static int FindClose(string text, int from)
        {
            char quote = '\0';
            for (var i = from; i < text.Length - 1; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == '}' && text[i + 1] == '}')
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
