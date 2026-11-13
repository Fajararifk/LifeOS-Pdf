using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// A CMap read from a stream (Adobe Tech Note 5014 / ISO 32000-1 §9.7.5, §9.10.3): codespace
    /// ranges (how many bytes a code has), code → Unicode (ToUnicode: bfchar/bfrange) and
    /// code → CID (embedded encoding CMaps: cidchar/cidrange). Ranges are kept as ranges, so a
    /// hostile "0000–FFFF" range costs nothing.
    /// </summary>
    internal sealed class CMap
    {
        private const int MaxEntries = 200000;

        private readonly List<(int Length, uint Low, uint High)> _codespaces = new List<(int, uint, uint)>();
        private readonly Dictionary<ulong, string> _unicode = new Dictionary<ulong, string>();
        private readonly List<(int Length, uint Low, uint High, byte[]? Start, string[]? List)> _unicodeRanges = new List<(int, uint, uint, byte[]?, string[]?)>();
        private readonly Dictionary<ulong, int> _cid = new Dictionary<ulong, int>();
        private readonly List<(int Length, uint Low, uint High, int Cid)> _cidRanges = new List<(int, uint, uint, int)>();

        public bool HasCodespaces => _codespaces.Count > 0;

        public bool HasCids => _cid.Count > 0 || _cidRanges.Count > 0;

        public static CMap Parse(byte[] data)
        {
            var cmap = new CMap();
            var lexer = new Lexer(data);
            var operands = new List<Token>();
            var entries = 0;
            while (true)
            {
                var t = lexer.Next();
                if (t.Kind == TokenKind.Eof)
                {
                    break;
                }

                if (t.Kind != TokenKind.Keyword)
                {
                    if (t.Kind == TokenKind.ArrayStart)
                    {
                        // Only used inside bfrange: collect the array as one operand.
                        operands.Add(ReadArray(lexer, t));
                    }
                    else if (operands.Count < 10000)
                    {
                        operands.Add(t);
                    }

                    continue;
                }

                switch (t.Text)
                {
                    case "endcodespacerange":
                        for (var i = 0; i + 1 < operands.Count; i += 2)
                        {
                            if (operands[i].Bytes != null && operands[i + 1].Bytes != null && operands[i].Bytes!.Length >= 1 && operands[i].Bytes!.Length <= 4)
                            {
                                cmap._codespaces.Add((operands[i].Bytes!.Length, ToCode(operands[i].Bytes!), ToCode(operands[i + 1].Bytes!)));
                            }
                        }

                        break;
                    case "endbfchar":
                        for (var i = 0; i + 1 < operands.Count && entries < MaxEntries; i += 2, entries++)
                        {
                            var src = operands[i].Bytes;
                            if (src != null && src.Length <= 4)
                            {
                                var dst = operands[i + 1].Kind == TokenKind.Name ? GlyphList.ToUnicode(operands[i + 1].Text!) : Utf16(operands[i + 1].Bytes);
                                if (dst != null)
                                {
                                    cmap._unicode[Key(src.Length, ToCode(src))] = dst;
                                }
                            }
                        }

                        break;
                    case "endbfrange":
                        for (var i = 0; i + 2 < operands.Count && entries < MaxEntries; i += 3, entries++)
                        {
                            var lo = operands[i].Bytes;
                            var hi = operands[i + 1].Bytes;
                            if (lo == null || hi == null || lo.Length > 4 || lo.Length != hi.Length)
                            {
                                continue;
                            }

                            var dst = operands[i + 2];
                            if (dst.Kind == TokenKind.ArrayStart && dst.Text != null)
                            {
                                cmap._unicodeRanges.Add((lo.Length, ToCode(lo), ToCode(hi), null, dst.Text.Split('\u0001')));
                            }
                            else if (dst.Bytes != null && dst.Bytes.Length >= 1)
                            {
                                cmap._unicodeRanges.Add((lo.Length, ToCode(lo), ToCode(hi), dst.Bytes, null));
                            }
                        }

                        break;
                    case "endcidchar":
                        for (var i = 0; i + 1 < operands.Count && entries < MaxEntries; i += 2, entries++)
                        {
                            if (operands[i].Bytes is byte[] src && src.Length <= 4 && operands[i + 1].Kind == TokenKind.Integer)
                            {
                                cmap._cid[Key(src.Length, ToCode(src))] = (int)operands[i + 1].Integer;
                            }
                        }

                        break;
                    case "endcidrange":
                        for (var i = 0; i + 2 < operands.Count && entries < MaxEntries; i += 3, entries++)
                        {
                            if (operands[i].Bytes is byte[] lo && operands[i + 1].Bytes is byte[] hi && lo.Length <= 4 && lo.Length == hi.Length && operands[i + 2].Kind == TokenKind.Integer)
                            {
                                cmap._cidRanges.Add((lo.Length, ToCode(lo), ToCode(hi), (int)operands[i + 2].Integer));
                            }
                        }

                        break;
                }

                operands.Clear();
            }

            return cmap;
        }

        /// <summary>Reads "[ &lt;..&gt; &lt;..&gt; ]" into a token whose Text holds the decoded strings separated by \u0001.</summary>
        private static Token ReadArray(Lexer lexer, Token start)
        {
            var parts = new List<string>();
            while (true)
            {
                var t = lexer.Next();
                if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof || parts.Count > 65536)
                {
                    break;
                }

                if (t.Bytes != null)
                {
                    parts.Add(Utf16(t.Bytes) ?? string.Empty);
                }
            }

            start.Text = string.Join("\u0001", parts);
            return start;
        }

        private static ulong Key(int length, uint code) => ((ulong)length << 32) | code;

        private static uint ToCode(byte[] bytes)
        {
            uint v = 0;
            foreach (var b in bytes)
            {
                v = (v << 8) | b;
            }

            return v;
        }

        private static string? Utf16(byte[]? bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            if (bytes.Length == 1)
            {
                return ((char)bytes[0]).ToString();
            }

            var s = Encoding.BigEndianUnicode.GetString(bytes, 0, bytes.Length & ~1);
            return s;
        }

        /// <summary>Reads one code at <paramref name="pos"/> following the codespace ranges; returns its byte length.</summary>
        public int ReadCode(byte[] data, int pos, out uint code)
        {
            uint v = 0;
            for (var n = 1; n <= 4 && pos + n <= data.Length; n++)
            {
                v = (v << 8) | data[pos + n - 1];
                foreach (var cs in _codespaces)
                {
                    if (cs.Length == n && v >= cs.Low && v <= cs.High)
                    {
                        code = v;
                        return n;
                    }
                }
            }

            // No range matched: use the shortest codespace length (or one byte).
            var length = 4;
            foreach (var cs in _codespaces)
            {
                length = Math.Min(length, cs.Length);
            }

            if (_codespaces.Count == 0)
            {
                length = 1;
            }

            length = Math.Min(length, data.Length - pos);
            code = 0;
            for (var i = 0; i < length; i++)
            {
                code = (code << 8) | data[pos + i];
            }

            return Math.Max(1, length);
        }

        public string? Unicode(int length, uint code)
        {
            if (_unicode.TryGetValue(Key(length, code), out var s))
            {
                return s;
            }

            foreach (var r in _unicodeRanges)
            {
                if (r.Length != length || code < r.Low || code > r.High)
                {
                    continue;
                }

                var offset = code - r.Low;
                if (r.List != null)
                {
                    return offset < r.List.Length ? r.List[offset] : null;
                }

                // Increment the last byte of the destination (§9.10.3).
                var dst = (byte[])r.Start!.Clone();
                var carry = offset;
                for (var i = dst.Length - 1; i >= 0 && carry > 0; i--)
                {
                    var sum = dst[i] + carry;
                    dst[i] = (byte)sum;
                    carry = sum >> 8;
                }

                return Utf16(dst);
            }

            return null;
        }

        public int? Cid(int length, uint code)
        {
            if (_cid.TryGetValue(Key(length, code), out var cid))
            {
                return cid;
            }

            foreach (var r in _cidRanges)
            {
                if (r.Length == length && code >= r.Low && code <= r.High)
                {
                    return r.Cid + (int)(code - r.Low);
                }
            }

            return null;
        }
    }
}
