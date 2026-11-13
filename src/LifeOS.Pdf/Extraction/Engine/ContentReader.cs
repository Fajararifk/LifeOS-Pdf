using System;
using System.Collections.Generic;
using System.IO;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>One operator with its operands (§7.8.2). Inline images keep their raw data.</summary>
    internal sealed class ContentOperation
    {
        public ContentOperation(string op, PdfObject[] operands)
        {
            Op = op;
            Operands = operands;
        }

        public string Op { get; }

        public PdfObject[] Operands { get; }

        /// <summary>For "BI": the image dictionary and the bytes between ID and EI.</summary>
        public PdfDictionary? InlineDictionary { get; set; }

        public byte[]? InlineData { get; set; }

        public void WriteTo(PdfOutput output)
        {
            if (Op == "BI")
            {
                output.Write("BI");
                foreach (var e in InlineDictionary!.Entries)
                {
                    output.WriteByte((byte)' ');
                    PdfName.Write(output, e.Key);
                    output.WriteByte((byte)' ');
                    e.Value.WriteTo(output);
                }

                output.Write(" ID ");
                output.Write(InlineData!);
                output.Write("\nEI\n");
                return;
            }

            foreach (var operand in Operands)
            {
                operand.WriteTo(output);
                output.WriteByte((byte)' ');
            }

            output.Write(Op);
            output.WriteByte((byte)'\n');
        }
    }

    /// <summary>Splits a content stream into operations. Damaged operands are skipped, never fatal.</summary>
    internal static class ContentReader
    {
        public const int MaxOperations = 2000000;
        private const int MaxNesting = 32;

        public static List<ContentOperation> Read(byte[] data)
        {
            var result = new List<ContentOperation>();
            var lexer = new Lexer(data);
            var operands = new List<PdfObject>();
            while (true)
            {
                Token t;
                try
                {
                    t = lexer.Next();
                }
                catch (PdfReadException)
                {
                    break; // unterminated string at the end of a damaged stream
                }

                if (t.Kind == TokenKind.Eof)
                {
                    break;
                }

                if (t.Kind == TokenKind.Keyword)
                {
                    switch (t.Text)
                    {
                        case "true":
                            operands.Add(PdfBoolean.True);
                            continue;
                        case "false":
                            operands.Add(PdfBoolean.False);
                            continue;
                        case "null":
                            operands.Add(PdfNull.Instance);
                            continue;
                        case "BI":
                            var inline = ReadInlineImage(lexer, data);
                            if (inline != null)
                            {
                                result.Add(inline);
                            }

                            operands.Clear();
                            continue;
                    }

                    if (result.Count >= MaxOperations)
                    {
                        throw new PdfLimitException("A content stream has more than " + MaxOperations + " operations.");
                    }

                    result.Add(new ContentOperation(t.Text!, operands.ToArray()));
                    operands.Clear();
                    continue;
                }

                var value = Operand(lexer, t, 0);
                if (value != null && operands.Count < 64)
                {
                    operands.Add(value);
                }
            }

            return result;
        }

        private static PdfObject? Operand(Lexer lexer, Token t, int depth)
        {
            switch (t.Kind)
            {
                case TokenKind.Integer:
                    return new PdfInteger(t.Integer);
                case TokenKind.Real:
                    return new PdfReal(t.Real, t.Text!);
                case TokenKind.Name:
                    return t.Text!.Length == 0 ? null : new PdfName(t.Text);
                case TokenKind.String:
                    return PdfString.FromBytes(t.Bytes!, false);
                case TokenKind.HexString:
                    return PdfString.FromBytes(t.Bytes!, true);
                case TokenKind.ArrayStart:
                    var array = new PdfArray();
                    while (true)
                    {
                        var n = lexer.Next();
                        if (n.Kind == TokenKind.ArrayEnd || n.Kind == TokenKind.Eof || (n.Kind == TokenKind.Keyword && n.Text != "true" && n.Text != "false" && n.Text != "null"))
                        {
                            if (n.Kind == TokenKind.Keyword)
                            {
                                lexer.Position = n.Position; // operator inside an unclosed array: stop here
                            }

                            return array;
                        }

                        var item = depth < MaxNesting ? Operand(lexer, n, depth + 1) : null;
                        if (item != null)
                        {
                            array.Items.Add(item);
                        }
                        else if (n.Kind == TokenKind.Keyword)
                        {
                            array.Items.Add(n.Text == "true" ? PdfBoolean.True : n.Text == "false" ? (PdfObject)PdfBoolean.False : PdfNull.Instance);
                        }
                    }

                case TokenKind.DictStart:
                    var dict = new PdfDictionary();
                    while (true)
                    {
                        var k = lexer.Next();
                        if (k.Kind == TokenKind.DictEnd || k.Kind == TokenKind.Eof)
                        {
                            return dict;
                        }

                        if (k.Kind != TokenKind.Name)
                        {
                            if (k.Kind == TokenKind.Keyword)
                            {
                                lexer.Position = k.Position;
                                return dict;
                            }

                            continue;
                        }

                        var v = lexer.Next();
                        var value = depth < MaxNesting ? Operand(lexer, v, depth + 1) : null;
                        if (value != null && k.Text!.Length > 0)
                        {
                            dict.Set(k.Text, value);
                        }
                    }

                default:
                    return null;
            }
        }

        /// <summary>Reads "BI &lt;dict&gt; ID &lt;data&gt; EI" (§8.9.7).</summary>
        private static ContentOperation? ReadInlineImage(Lexer lexer, byte[] data)
        {
            var dict = new PdfDictionary();
            while (true)
            {
                var k = lexer.Next();
                if (k.Kind == TokenKind.Eof)
                {
                    return null;
                }

                if (k.IsKeyword("ID"))
                {
                    break;
                }

                if (k.Kind != TokenKind.Name)
                {
                    continue;
                }

                var value = Operand(lexer, lexer.Next(), 0);
                if (value != null)
                {
                    dict.Set(k.Text!, value);
                }
            }

            var start = (int)lexer.Position + 1; // one whitespace byte after ID
            if (start > data.Length)
            {
                return null;
            }

            int end;
            var length = dict.Get("L") ?? dict.Get("Length");
            if (length is PdfInteger li && li.Value >= 0 && start + li.Value <= data.Length)
            {
                end = start + (int)li.Value;
            }
            else
            {
                // "EI" preceded by whitespace and followed by whitespace or the end.
                end = -1;
                for (var i = start; i + 1 < data.Length; i++)
                {
                    if (data[i] == 'E' && data[i + 1] == 'I' && i > start && Lexer.IsWhitespace(data[i - 1]) &&
                        (i + 2 >= data.Length || Lexer.IsWhitespace(data[i + 2]) || Lexer.IsDelimiter(data[i + 2])))
                    {
                        end = i - 1;
                        break;
                    }
                }

                if (end < 0)
                {
                    return null;
                }
            }

            var bytes = new byte[end - start];
            Buffer.BlockCopy(data, start, bytes, 0, bytes.Length);
            lexer.Position = end;
            // Skip to after "EI".
            while (true)
            {
                var t = lexer.Next();
                if (t.Kind == TokenKind.Eof || t.IsKeyword("EI"))
                {
                    break;
                }
            }

            return new ContentOperation("BI", Array.Empty<PdfObject>()) { InlineDictionary = dict, InlineData = bytes };
        }

        /// <summary>Writes operations back to a content stream.</summary>
        public static byte[] Write(IEnumerable<ContentOperation> operations)
        {
            using (var ms = new MemoryStream())
            {
                var output = new PdfOutput(ms);
                foreach (var op in operations)
                {
                    op.WriteTo(output);
                }

                output.Flush();
                return ms.ToArray();
            }
        }
    }
}
