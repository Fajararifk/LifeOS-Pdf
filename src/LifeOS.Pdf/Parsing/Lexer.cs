using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LifeOS.Pdf.Parsing
{
    internal enum TokenKind
    {
        Eof,
        Integer,
        Real,
        Name,
        String,
        HexString,
        Keyword,
        ArrayStart,
        ArrayEnd,
        DictStart,
        DictEnd,
    }

    internal struct Token
    {
        public TokenKind Kind;
        public long Integer;
        public double Real;

        /// <summary>Name (bytes as Latin-1 characters) or keyword text.</summary>
        public string? Text;

        public byte[]? Bytes;

        /// <summary>Offset of the first byte of the token.</summary>
        public long Position;

        public bool IsKeyword(string keyword) => Kind == TokenKind.Keyword && Text == keyword;

        public override string ToString() => Kind + (Text != null ? " " + Text : Kind == TokenKind.Integer ? " " + Integer.ToString(CultureInfo.InvariantCulture) : string.Empty);
    }

    /// <summary>
    /// Tokenizer for PDF syntax (ISO 32000-1 §7.2). Works on a byte array; never reads
    /// outside it. Malformed numbers are read leniently (like other readers do), but
    /// strings that never end are an error.
    /// </summary>
    internal sealed class Lexer
    {
        private readonly byte[] _data;
        private readonly NameTable _names;

        public Lexer(byte[] data, long position = 0, NameTable? names = null)
        {
            _data = data;
            Position = position;
            _names = names ?? new NameTable();
        }

        public byte[] Data => _data;

        public long Position { get; set; }

        public int Length => _data.Length;

        public static bool IsWhitespace(int b) => b == 0x20 || b == 0x0A || b == 0x0D || b == 0x09 || b == 0x0C || b == 0x00;

        public static bool IsDelimiter(int b) =>
            b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';

        public static bool IsRegular(int b) => !IsWhitespace(b) && !IsDelimiter(b);

        public int Peek(long offset = 0)
        {
            var p = Position + offset;
            return p >= 0 && p < _data.Length ? _data[p] : -1;
        }

        public void SkipWhitespace()
        {
            while (Position < _data.Length)
            {
                var b = _data[Position];
                if (IsWhitespace(b))
                {
                    Position++;
                }
                else if (b == '%')
                {
                    while (Position < _data.Length && _data[Position] != '\n' && _data[Position] != '\r')
                    {
                        Position++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        public Token Next()
        {
            SkipWhitespace();
            var token = new Token { Position = Position };
            if (Position >= _data.Length)
            {
                token.Kind = TokenKind.Eof;
                return token;
            }

            var b = _data[Position];
            switch (b)
            {
                case (byte)'[':
                    Position++;
                    token.Kind = TokenKind.ArrayStart;
                    return token;
                case (byte)']':
                    Position++;
                    token.Kind = TokenKind.ArrayEnd;
                    return token;
                case (byte)'<':
                    if (Peek(1) == '<')
                    {
                        Position += 2;
                        token.Kind = TokenKind.DictStart;
                        return token;
                    }

                    Position++;
                    token.Kind = TokenKind.HexString;
                    token.Bytes = ReadHexString();
                    return token;
                case (byte)'>':
                    if (Peek(1) == '>')
                    {
                        Position += 2;
                        token.Kind = TokenKind.DictEnd;
                        return token;
                    }

                    // A stray '>' is ignored like whitespace.
                    Position++;
                    return Next();
                case (byte)'(':
                    Position++;
                    token.Kind = TokenKind.String;
                    token.Bytes = ReadLiteralString();
                    return token;
                case (byte)'/':
                    Position++;
                    token.Kind = TokenKind.Name;
                    token.Text = ReadName();
                    return token;
                case (byte)')':
                case (byte)'{':
                case (byte)'}':
                    Position++;
                    token.Kind = TokenKind.Keyword;
                    token.Text = ((char)b).ToString();
                    return token;
            }

            if ((b >= '0' && b <= '9') || b == '+' || b == '-' || b == '.')
            {
                return ReadNumber(token);
            }

            var start = Position;
            while (Position < _data.Length && IsRegular(_data[Position]))
            {
                Position++;
            }

            token.Kind = TokenKind.Keyword;
            token.Text = _names.Get(_data, start, (int)(Position - start));
            return token;
        }

        private Token ReadNumber(Token token)
        {
            // Fast path: a plain integer (the vast majority: object numbers, lengths, offsets).
            var p = Position;
            var negative = false;
            if (p < _data.Length && (_data[p] == '-' || _data[p] == '+'))
            {
                negative = _data[p] == '-';
                p++;
            }

            var digitsStart = p;
            long value = 0;
            while (p < _data.Length && _data[p] >= '0' && _data[p] <= '9' && p - digitsStart < 18)
            {
                value = value * 10 + (_data[p] - '0');
                p++;
            }

            if (p > digitsStart && (p >= _data.Length || !IsRegular(_data[p])))
            {
                Position = p;
                token.Kind = TokenKind.Integer;
                token.Integer = negative ? -value : value;
                token.Real = token.Integer;
                return token;
            }

            return ReadNumberSlow(token);
        }

        private Token ReadNumberSlow(Token token)
        {
            var start = Position;
            while (Position < _data.Length && IsRegular(_data[Position]))
            {
                Position++;
            }

            var text = Latin1(start, (int)(Position - start));
            // Leniency for producer bugs such as "--5" or "0.0.5": keep the first sign and dot.
            var sb = new StringBuilder(text.Length);
            var dot = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((c == '-' || c == '+') && sb.Length == 0)
                {
                    sb.Append(c);
                }
                else if (c == '.' && !dot)
                {
                    dot = true;
                    sb.Append(c);
                }
                else if (c >= '0' && c <= '9')
                {
                    sb.Append(c);
                }
                else if (c == '-' || c == '+' || c == '.')
                {
                    continue;
                }
                else
                {
                    // Not a number after all (e.g. a keyword starting with '.'): treat as keyword.
                    token.Kind = TokenKind.Keyword;
                    token.Text = text;
                    return token;
                }
            }

            var s = sb.ToString();
            if (!dot && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
            {
                token.Kind = TokenKind.Integer;
                token.Integer = l;
                token.Real = l;
                return token;
            }

            double.TryParse(s.Length == 0 || s == "-" || s == "+" || s == "." ? "0" : s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
            if (double.IsInfinity(d) || double.IsNaN(d))
            {
                d = 0;
            }

            token.Kind = TokenKind.Real;
            token.Real = d;
            token.Text = s.Length == 0 || s == "-" || s == "+" || s == "." ? "0" : s;
            return token;
        }

        private string ReadName()
        {
            // Without '#' escapes the name is exactly its bytes: intern it.
            var start = Position;
            var end = start;
            while (end < _data.Length && IsRegular(_data[end]) && _data[end] != '#')
            {
                end++;
            }

            if (end >= _data.Length || !IsRegular(_data[end]))
            {
                Position = end;
                return _names.Get(_data, start, (int)(end - start));
            }

            var sb = new StringBuilder();
            while (Position < _data.Length && IsRegular(_data[Position]))
            {
                var b = _data[Position++];
                if (b == '#' && Position + 1 < _data.Length && Hex(_data[Position]) >= 0 && Hex(_data[Position + 1]) >= 0)
                {
                    b = (byte)((Hex(_data[Position]) << 4) | Hex(_data[Position + 1]));
                    Position += 2;
                }

                sb.Append((char)b);
            }

            return sb.ToString();
        }

        private byte[] ReadHexString()
        {
            var ms = new MemoryStream();
            var high = -1;
            while (true)
            {
                if (Position >= _data.Length)
                {
                    throw new PdfReadException("Hex string is not closed (missing '>').");
                }

                var b = _data[Position++];
                if (b == '>')
                {
                    break;
                }

                var v = Hex(b);
                if (v < 0)
                {
                    continue; // whitespace and garbage are skipped
                }

                if (high < 0)
                {
                    high = v;
                }
                else
                {
                    ms.WriteByte((byte)((high << 4) | v));
                    high = -1;
                }
            }

            if (high >= 0)
            {
                ms.WriteByte((byte)(high << 4)); // odd digit count: last digit followed by 0 (§7.3.4.3)
            }

            return ms.ToArray();
        }

        private byte[] ReadLiteralString()
        {
            var ms = new MemoryStream();
            var depth = 1;
            while (true)
            {
                if (Position >= _data.Length)
                {
                    throw new PdfReadException("Literal string is not closed (missing ')').");
                }

                var b = _data[Position++];
                switch (b)
                {
                    case (byte)'(':
                        depth++;
                        ms.WriteByte(b);
                        break;
                    case (byte)')':
                        if (--depth == 0)
                        {
                            return ms.ToArray();
                        }

                        ms.WriteByte(b);
                        break;
                    case (byte)'\r':
                        // An unescaped end of line is read as a single LF (§7.3.4.2).
                        if (Peek() == '\n')
                        {
                            Position++;
                        }

                        ms.WriteByte((byte)'\n');
                        break;
                    case (byte)'\\':
                        ReadEscape(ms);
                        break;
                    default:
                        ms.WriteByte(b);
                        break;
                }
            }
        }

        private void ReadEscape(MemoryStream ms)
        {
            if (Position >= _data.Length)
            {
                return;
            }

            var c = _data[Position++];
            switch (c)
            {
                case (byte)'n': ms.WriteByte((byte)'\n'); return;
                case (byte)'r': ms.WriteByte((byte)'\r'); return;
                case (byte)'t': ms.WriteByte((byte)'\t'); return;
                case (byte)'b': ms.WriteByte(8); return;
                case (byte)'f': ms.WriteByte(12); return;
                case (byte)'\r':
                    if (Peek() == '\n')
                    {
                        Position++;
                    }

                    return; // line continuation
                case (byte)'\n':
                    return;
            }

            if (c >= '0' && c <= '7')
            {
                var value = c - '0';
                for (var i = 0; i < 2 && Position < _data.Length && _data[Position] >= '0' && _data[Position] <= '7'; i++)
                {
                    value = (value << 3) + (_data[Position++] - '0');
                }

                ms.WriteByte((byte)value);
                return;
            }

            ms.WriteByte(c); // \( \) \\ and unknown escapes: the character itself
        }

        internal static int Hex(int b)
        {
            if (b >= '0' && b <= '9')
            {
                return b - '0';
            }

            if (b >= 'a' && b <= 'f')
            {
                return b - 'a' + 10;
            }

            if (b >= 'A' && b <= 'F')
            {
                return b - 'A' + 10;
            }

            return -1;
        }

        public string Latin1(long start, int length)
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)_data[start + i];
            }

            return new string(chars);
        }

        /// <summary>Finds <paramref name="pattern"/> at or after <paramref name="from"/>; -1 when absent.</summary>
        public long IndexOf(byte[] pattern, long from, long to = -1)
        {
            var end = (to < 0 ? _data.Length : Math.Min(to, _data.Length)) - pattern.Length;
            for (var i = Math.Max(0, from); i <= end; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (_data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Finds the last <paramref name="pattern"/> starting before <paramref name="before"/>.</summary>
        public long LastIndexOf(byte[] pattern, long before, long notBefore = 0)
        {
            for (var i = Math.Min(before, _data.Length - pattern.Length); i >= notBefore; i--)
            {
                var match = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (_data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
