using System;
using System.Collections.Generic;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>
    /// Builds COS objects from tokens (§7.3). Nesting is limited by
    /// <see cref="PdfReadOptions.MaxDepth"/>; references become <see cref="PdfSourceReference"/>.
    /// </summary>
    internal sealed class ObjectParser
    {
        private static readonly byte[] EndStream = { (byte)'e', (byte)'n', (byte)'d', (byte)'s', (byte)'t', (byte)'r', (byte)'e', (byte)'a', (byte)'m' };

        private readonly Lexer _lexer;
        private readonly PdfParser _owner;
        private readonly int _maxDepth;
        private readonly bool _decrypt;
        private int _objectNumber = -1;
        private int _generation;

        public ObjectParser(Lexer lexer, PdfParser owner, int maxDepth, bool decrypt = false)
        {
            _lexer = lexer;
            _owner = owner;
            _maxDepth = maxDepth;
            _decrypt = decrypt;
        }

        public Lexer Lexer => _lexer;

        /// <summary>Parses the next object; end of input or a stray delimiter gives null.</summary>
        public PdfObject ParseObject(int depth = 0)
        {
            var token = _lexer.Next();
            return FromToken(token, depth);
        }

        private PdfObject FromToken(Token token, int depth)
        {
            switch (token.Kind)
            {
                case TokenKind.Integer:
                    return MaybeReference(token);
                case TokenKind.Real:
                    return new PdfReal(token.Real, token.Text!);
                case TokenKind.Name:
                    return token.Text!.Length == 0 ? (PdfObject)PdfNull.Instance : new PdfName(token.Text);
                case TokenKind.String:
                    return PdfString.FromBytes(Decrypt(token.Bytes!), false);
                case TokenKind.HexString:
                    return PdfString.FromBytes(Decrypt(token.Bytes!), true);
                case TokenKind.ArrayStart:
                    return ParseArray(depth + 1);
                case TokenKind.DictStart:
                    return ParseDictionary(depth + 1);
                case TokenKind.Keyword:
                    switch (token.Text)
                    {
                        case "true": return PdfBoolean.True;
                        case "false": return PdfBoolean.False;
                        default: return PdfNull.Instance; // "null" and unknown keywords
                    }

                default:
                    return PdfNull.Instance;
            }
        }

        private PdfObject MaybeReference(Token first)
        {
            var save = _lexer.Position;
            var second = _lexer.Next();
            if (second.Kind == TokenKind.Integer)
            {
                var third = _lexer.Next();
                if (third.IsKeyword("R"))
                {
                    if (first.Integer < 1 || first.Integer > _owner.Options.MaxObjects || second.Integer < 0 || second.Integer > 65535)
                    {
                        return PdfNull.Instance;
                    }

                    return new PdfSourceReference(_owner, (int)first.Integer, (int)second.Integer);
                }
            }

            _lexer.Position = save;
            return new PdfInteger(first.Integer);
        }

        private void CheckDepth(int depth)
        {
            if (depth > _maxDepth)
            {
                throw new PdfLimitException("Arrays/dictionaries are nested more than " + _maxDepth + " levels deep near offset " + _lexer.Position + " (PdfReadOptions.MaxDepth).");
            }
        }

        private PdfArray ParseArray(int depth)
        {
            CheckDepth(depth);
            var array = new PdfArray();
            while (true)
            {
                var token = _lexer.Next();
                if (token.Kind == TokenKind.ArrayEnd)
                {
                    return array;
                }

                if (token.Kind == TokenKind.Eof)
                {
                    throw new PdfReadException("Array is not closed (missing ']').");
                }

                if (token.Kind == TokenKind.DictEnd || token.IsKeyword("endobj") || token.IsKeyword("stream"))
                {
                    _lexer.Position = token.Position;
                    return array; // damaged: close the array here
                }

                array.Items.Add(FromToken(token, depth));
            }
        }

        private PdfDictionary ParseDictionary(int depth)
        {
            CheckDepth(depth);
            var dict = new PdfDictionary();
            while (true)
            {
                var token = _lexer.Next();
                if (token.Kind == TokenKind.DictEnd)
                {
                    return dict;
                }

                if (token.Kind == TokenKind.Eof)
                {
                    throw new PdfReadException("Dictionary is not closed (missing '>>').");
                }

                if (token.IsKeyword("endobj") || token.IsKeyword("stream"))
                {
                    _lexer.Position = token.Position;
                    return dict; // damaged: close the dictionary here
                }

                if (token.Kind != TokenKind.Name)
                {
                    continue; // skip junk where a key should be
                }

                var valueToken = _lexer.Next();
                if (valueToken.Kind == TokenKind.DictEnd)
                {
                    return dict;
                }

                var value = FromToken(valueToken, depth);
                if (!(value is PdfNull) && token.Text!.Length > 0)
                {
                    dict.Set(token.Text, value); // a null value means "absent" (§7.3.7)
                }
            }
        }

        /// <summary>Parses "n g obj … endobj" at the current position, including a stream body.</summary>
        public PdfObject ParseIndirect(out int number, out int generation)
        {
            var n = _lexer.Next();
            var g = _lexer.Next();
            var obj = _lexer.Next();
            if (n.Kind != TokenKind.Integer || g.Kind != TokenKind.Integer || !obj.IsKeyword("obj"))
            {
                throw new PdfReadException("Expected 'n g obj' at offset " + n.Position + ".");
            }

            number = (int)Math.Min(int.MaxValue, Math.Max(-1, n.Integer));
            generation = (int)Math.Min(int.MaxValue, Math.Max(-1, g.Integer));
            _objectNumber = number;
            _generation = generation;
            var value = ParseObject();
            if (value is PdfDictionary dict)
            {
                var save = _lexer.Position;
                var next = _lexer.Next();
                if (next.IsKeyword("stream"))
                {
                    return ReadStream(dict);
                }

                _lexer.Position = save;
            }

            return value;
        }

        private PdfStream ReadStream(PdfDictionary dict)
        {
            // After "stream": CRLF or LF (a lone CR is tolerated).
            var data = _lexer.Data;
            var start = _lexer.Position;
            if (start < data.Length && data[start] == '\r')
            {
                start++;
            }

            if (start < data.Length && data[start] == '\n')
            {
                start++;
            }

            long length = -1;
            var lengthObj = dict.Get("Length");
            if (lengthObj is PdfSourceReference r)
            {
                lengthObj = _owner.ResolveLength(r);
            }

            if (lengthObj is PdfInteger li)
            {
                length = li.Value;
            }

            if (length < 0 || start + length > data.Length || !EndsWithEndStream(start + length))
            {
                // Missing or wrong /Length: find "endstream" and trim the end-of-line before it.
                var end = _lexer.IndexOf(EndStream, start);
                if (end < 0)
                {
                    throw new PdfReadException("Stream starting at offset " + start + " has no 'endstream'.");
                }

                length = end - start;
                if (length > 0 && data[start + length - 1] == '\n')
                {
                    length--;
                }

                if (length > 0 && data[start + length - 1] == '\r')
                {
                    length--;
                }
            }

            var bytes = new byte[length];
            Buffer.BlockCopy(data, (int)start, bytes, 0, (int)length);
            _lexer.Position = start + length;
            var token = _lexer.Next();
            if (!token.IsKeyword("endstream"))
            {
                _lexer.Position = start + length;
            }

            return new PdfStream(dict, DecryptStream(bytes));
        }

        private byte[] Decrypt(byte[] data) =>
            _decrypt && _owner.Security != null ? _owner.Security.DecryptData(data, _objectNumber, _generation, stream: false) : data;

        private byte[] DecryptStream(byte[] data) =>
            _decrypt && _owner.Security != null ? _owner.Security.DecryptData(data, _objectNumber, _generation, stream: true) : data;

        private bool EndsWithEndStream(long position)
        {
            var save = _lexer.Position;
            _lexer.Position = position;
            _lexer.SkipWhitespace();
            var ok = _lexer.IndexOf(EndStream, _lexer.Position, _lexer.Position + EndStream.Length) == _lexer.Position;
            _lexer.Position = save;
            return ok;
        }
    }
}
