using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Templates.Expressions
{
    /// <summary>
    /// A parsed template expression. The language is deliberately small and side-effect free:
    /// paths, literals, arithmetic (+ − * /), comparisons, &amp;&amp; || !, parentheses and filters (<c>value | name:arg</c>).
    /// There are no method calls: evaluating an expression can never run code.
    /// </summary>
    internal abstract class Expression
    {
        public int Line { get; set; }

        public int Column { get; set; }

        public abstract object? Evaluate(Scope scope);

        /// <summary>Parses an expression; errors are reported at the given template position.</summary>
        public static Expression Parse(string text, int line, int column) => new ExpressionParser(text, line, column).ParseAll();
    }

    internal sealed class LiteralExpression : Expression
    {
        public LiteralExpression(object? value)
        {
            Value = value;
        }

        public object? Value { get; }

        public override object? Evaluate(Scope scope) => Value;
    }

    /// <summary>a.b[0].c — first segment is a variable (loop item) or a member of the data root.</summary>
    internal sealed class PathExpression : Expression
    {
        public PathExpression(string first)
        {
            First = first;
        }

        public string First { get; }

        /// <summary>Each segment is a member name (string) or an index expression.</summary>
        public List<object> Segments { get; } = new List<object>();

        public string Text
        {
            get
            {
                var sb = new StringBuilder(First);
                foreach (var s in Segments)
                {
                    sb.Append(s is string name ? "." + name : "[…]");
                }

                return sb.ToString();
            }
        }

        public override object? Evaluate(Scope scope)
        {
            object? value;
            if (!scope.TryGetVariable(First, out value))
            {
                if (!ValueAccess.TryGetMember(scope.Root, First, out value))
                {
                    return scope.Missing(this);
                }
            }

            foreach (var segment in Segments)
            {
                if (value == null)
                {
                    return null; // null-safe navigation: a.b.c is null when b is null
                }

                bool found;
                if (segment is string name)
                {
                    found = ValueAccess.TryGetMember(value, name, out value);
                }
                else
                {
                    found = ValueAccess.TryGetIndex(value, ((Expression)segment).Evaluate(scope), out value);
                }

                if (!found)
                {
                    return scope.Missing(this);
                }
            }

            return value;
        }
    }

    /// <summary>Member/index access on any expression: (Items | sort:'Date')[0].Name.</summary>
    internal sealed class AccessExpression : Expression
    {
        public AccessExpression(Expression target)
        {
            Target = target;
        }

        public Expression Target { get; }

        /// <summary>Member names (string) or index expressions.</summary>
        public List<object> Segments { get; } = new List<object>();

        public override object? Evaluate(Scope scope)
        {
            var value = Target.Evaluate(scope);
            foreach (var segment in Segments)
            {
                if (value == null)
                {
                    return null;
                }

                var found = segment is string name
                    ? ValueAccess.TryGetMember(value, name, out value)
                    : ValueAccess.TryGetIndex(value, ((Expression)segment).Evaluate(scope), out value);
                if (!found)
                {
                    return null;
                }
            }

            return value;
        }
    }

    internal sealed class UnaryExpression : Expression
    {
        public UnaryExpression(string op, Expression operand)
        {
            Op = op;
            Operand = operand;
        }

        public string Op { get; }

        public Expression Operand { get; }

        public override object? Evaluate(Scope scope)
        {
            var v = Operand.Evaluate(scope);
            if (Op == "!")
            {
                return !Values.IsTruthy(v);
            }

            var n = Values.ToNumber(v);
            return n.HasValue ? -n.Value : (object?)null;
        }
    }

    internal sealed class BinaryExpression : Expression
    {
        public BinaryExpression(string op, Expression left, Expression right)
        {
            Op = op;
            Left = left;
            Right = right;
        }

        public string Op { get; }

        public Expression Left { get; }

        public Expression Right { get; }

        public override object? Evaluate(Scope scope)
        {
            switch (Op)
            {
                case "&&":
                    return Values.IsTruthy(Left.Evaluate(scope)) && Values.IsTruthy(Right.Evaluate(scope));
                case "||":
                    return Values.IsTruthy(Left.Evaluate(scope)) || Values.IsTruthy(Right.Evaluate(scope));
            }

            var l = Left.Evaluate(scope);
            var r = Right.Evaluate(scope);
            switch (Op)
            {
                case "+":
                {
                    var a = Values.ToNumber(l);
                    var b = Values.ToNumber(r);
                    if (a.HasValue && b.HasValue && !(l is string) && !(r is string))
                    {
                        return a.Value + b.Value;
                    }

                    // With a string on either side, + concatenates.
                    return Values.ToText(l, scope.Options) + Values.ToText(r, scope.Options);
                }

                case "-":
                case "*":
                case "/":
                {
                    var a = Values.ToNumber(l);
                    var b = Values.ToNumber(r);
                    if (!a.HasValue || !b.HasValue)
                    {
                        return null;
                    }

                    try
                    {
                        // Division by zero gives an empty value rather than an error (e.g. a saving
                        // rate when income is 0); templates test for it with if="…".
                        return Op == "-" ? a.Value - b.Value : Op == "*" ? a.Value * b.Value : b.Value == 0 ? (object?)null : a.Value / b.Value;
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }
                }

                case "==":
                    return Values.AreEqual(l, r);
                case "!=":
                    return !Values.AreEqual(l, r);
                default:
                    var c = Values.Compare(l, r);
                    if (!c.HasValue)
                    {
                        return false; // incomparable values (e.g. null vs number) are never ordered
                    }

                    switch (Op)
                    {
                        case "<": return c < 0;
                        case ">": return c > 0;
                        case "<=": return c <= 0;
                        default: return c >= 0;
                    }
            }
        }
    }

    internal sealed class FilterExpression : Expression
    {
        public FilterExpression(Expression input, Filter filter, List<Expression> args)
        {
            Input = input;
            Filter = filter;
            Args = args;
        }

        public Expression Input { get; }

        public Filter Filter { get; }

        public List<Expression> Args { get; }

        public override object? Evaluate(Scope scope)
        {
            var input = Input.Evaluate(scope);
            var args = new object?[Args.Count];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = Args[i].Evaluate(scope);
            }

            try
            {
                return Filter.Apply(input, args, scope.Options);
            }
            catch (TemplateException)
            {
                throw;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                throw new TemplateException("Filter '" + Filter.Name + "' failed: " + ex.Message, Line, Column);
            }
        }
    }

    /// <summary>Recursive-descent parser. Precedence (loosest first): filter | &lt; || &lt; &amp;&amp; &lt; comparison &lt; + − &lt; * / &lt; unary.</summary>
    internal sealed class ExpressionParser
    {
        private readonly List<Token> _tokens;
        private readonly int _line;
        private readonly int _column;
        private int _pos;

        public ExpressionParser(string text, int line, int column)
        {
            _line = line;
            _column = column;
            _tokens = Tokenize(text);
        }

        public Expression ParseAll()
        {
            if (_tokens.Count == 1)
            {
                throw Error("Empty expression");
            }

            var e = ParsePipeline();
            if (Current.Kind != TokenKind.End)
            {
                throw Error("Unexpected '" + Current.Text + "'");
            }

            return e;
        }

        private Token Current => _tokens[_pos];

        private Expression ParseOr()
        {
            var e = ParseAnd();
            while (Current.Is("||"))
            {
                _pos++;
                e = Located(new BinaryExpression("||", e, ParseAnd()));
            }

            return e;
        }

        private Expression ParseAnd()
        {
            var e = ParseComparison();
            while (Current.Is("&&"))
            {
                _pos++;
                e = Located(new BinaryExpression("&&", e, ParseComparison()));
            }

            return e;
        }

        private Expression ParseComparison()
        {
            var e = ParseAdditive();
            if (Current.Kind == TokenKind.Symbol && (Current.Is("==") || Current.Is("!=") || Current.Is("<") || Current.Is(">") || Current.Is("<=") || Current.Is(">=")))
            {
                var op = Current.Text;
                _pos++;
                e = Located(new BinaryExpression(op, e, ParseAdditive()));
            }

            return e;
        }

        private Expression ParseAdditive()
        {
            var e = ParseMultiplicative();
            while (Current.Is("+") || Current.Is("-"))
            {
                var op = Current.Text;
                _pos++;
                e = Located(new BinaryExpression(op, e, ParseMultiplicative()));
            }

            return e;
        }

        private Expression ParseMultiplicative()
        {
            var e = ParseUnary();
            while (Current.Is("*") || Current.Is("/"))
            {
                var op = Current.Text;
                _pos++;
                e = Located(new BinaryExpression(op, e, ParseUnary()));
            }

            return e;
        }

        private Expression ParseUnary()
        {
            if (Current.Is("!") || Current.Is("-"))
            {
                var op = Current.Text;
                _pos++;
                return Located(new UnaryExpression(op, ParseUnary()));
            }

            return ParsePrimary();
        }

        /// <summary>
        /// Filters bind loosest (as in Liquid/Jinja): <c>a * b | rupiah</c> formats the product.
        /// Compare a filtered value with parentheses: <c>(Items | count) &gt; 3</c>.
        /// </summary>
        private Expression ParsePipeline()
        {
            var e = ParseOr();
            while (Current.Is("|"))
            {
                _pos++;
                if (Current.Kind != TokenKind.Identifier)
                {
                    throw Error("Expected a filter name after '|'");
                }

                var name = Current.Text;
                _pos++;
                if (!Filters.TryGet(name, out var filter))
                {
                    throw Error("Unknown filter '" + name + "'. Available filters: " + string.Join(", ", Filters.Names));
                }

                var args = new List<Expression>();
                while (Current.Is(":"))
                {
                    _pos++;
                    args.Add(ParsePrimary());
                }

                if (args.Count < filter.MinArgs || args.Count > filter.MaxArgs)
                {
                    throw Error("Filter '" + name + "' takes " + (filter.MinArgs == filter.MaxArgs ? filter.MinArgs.ToString(CultureInfo.InvariantCulture) : filter.MinArgs + "–" + filter.MaxArgs) +
                        " argument(s) but got " + args.Count + ". Usage: " + filter.Usage);
                }

                e = Located(new FilterExpression(e, filter, args));
            }

            return e;
        }

        private Expression ParsePrimary()
        {
            var t = Current;
            switch (t.Kind)
            {
                case TokenKind.Number:
                    _pos++;
                    return Located(new LiteralExpression(t.Number));
                case TokenKind.String:
                    _pos++;
                    return Located(new LiteralExpression(t.Text));
                case TokenKind.Identifier:
                    _pos++;
                    switch (t.Text)
                    {
                        case "true": return Located(new LiteralExpression(true));
                        case "false": return Located(new LiteralExpression(false));
                        case "null": return Located(new LiteralExpression(null));
                    }

                    var path = new PathExpression(t.Text);
                    ParseSegments(path.Segments);
                    return Located(path);
                default:
                    if (t.Is("("))
                    {
                        _pos++;
                        var inner = ParsePipeline();
                        if (!Current.Is(")"))
                        {
                            throw Error("Expected ')'");
                        }

                        _pos++;
                        if (!Current.Is(".") && !Current.Is("["))
                        {
                            return inner;
                        }

                        var access = new AccessExpression(inner);
                        ParseSegments(access.Segments);
                        return Located(access);
                    }

                    throw Error(t.Kind == TokenKind.End ? "Unexpected end of expression" : "Unexpected '" + t.Text + "'");
            }
        }

        /// <summary>Parses .name and [index] accessors.</summary>
        private void ParseSegments(List<object> segments)
        {
            while (true)
            {
                if (Current.Is("."))
                {
                    _pos++;
                    if (Current.Kind != TokenKind.Identifier)
                    {
                        throw Error("Expected a name after '.'");
                    }

                    segments.Add(Current.Text);
                    _pos++;
                }
                else if (Current.Is("["))
                {
                    _pos++;
                    segments.Add(ParsePipeline());
                    if (!Current.Is("]"))
                    {
                        throw Error("Expected ']'");
                    }

                    _pos++;
                }
                else
                {
                    return;
                }
            }
        }

        private Expression Located(Expression e)
        {
            e.Line = _line;
            e.Column = _column;
            return e;
        }

        private TemplateException Error(string message) => new TemplateException("Invalid expression: " + message + ".", _line, _column);

        private List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    var start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
                    {
                        i++;
                    }

                    var text = s.Substring(start, i - start);
                    if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
                    {
                        throw Error("Invalid number '" + text + "'");
                    }

                    tokens.Add(new Token(TokenKind.Number, text) { Number = n });
                    continue;
                }

                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    var start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, s.Substring(start, i - start)));
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= s.Length)
                        {
                            throw Error("Unterminated string");
                        }

                        if (s[i] == c)
                        {
                            i++;
                            break;
                        }

                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            i++;
                        }

                        sb.Append(s[i]);
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.String, sb.ToString()));
                    continue;
                }

                var two = i + 1 < s.Length ? s.Substring(i, 2) : string.Empty;
                if (two == "==" || two == "!=" || two == "<=" || two == ">=" || two == "&&" || two == "||")
                {
                    tokens.Add(new Token(TokenKind.Symbol, two));
                    i += 2;
                    continue;
                }

                if ("<>!+-*/.[]()|:".IndexOf(c) >= 0)
                {
                    tokens.Add(new Token(TokenKind.Symbol, c.ToString()));
                    i++;
                    continue;
                }

                throw Error("Unexpected character '" + c + "'");
            }

            tokens.Add(new Token(TokenKind.End, string.Empty));
            return tokens;
        }

        private enum TokenKind
        {
            Number,
            String,
            Identifier,
            Symbol,
            End,
        }

        private sealed class Token
        {
            public Token(TokenKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }

            public TokenKind Kind { get; }

            public string Text { get; }

            public decimal Number { get; set; }

            public bool Is(string symbol) => Kind == TokenKind.Symbol && Text == symbol;
        }
    }
}
