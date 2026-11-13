using System;
using System.Collections.Generic;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Fonts.Standard;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// What text extraction needs from a font dictionary (§9.6–9.7, §9.10): how many bytes each
    /// character code takes, the code's width, and its Unicode text.
    /// </summary>
    internal sealed class FontReader
    {
        private readonly PdfParser? _parser;
        private readonly CMap? _toUnicode;
        private readonly CMap? _encodingCMap;
        private readonly string?[] _simpleUnicode = new string?[256];
        private readonly double[] _simpleWidths = new double[256];
        private readonly Dictionary<int, double>? _cidWidths;
        private readonly double _defaultWidth;
        private readonly bool _identityCodes;

        public FontReader(PdfDictionary font, PdfParser? parser)
        {
            _parser = parser;
            var subtype = (R(font.Get("Subtype")) as PdfName)?.Value ?? "Type1";
            BaseFont = StripSubset((R(font.Get("BaseFont")) as PdfName)?.Value ?? (R(font.Get("Name")) as PdfName)?.Value ?? "Unknown");
            if (R(font.Get("ToUnicode")) is PdfStream tu)
            {
                _toUnicode = TryCMap(tu);
            }

            var descriptor = R(font.Get("FontDescriptor")) as PdfDictionary;
            IsComposite = subtype == "Type0";
            if (IsComposite)
            {
                var descendant = R(font.Get("DescendantFonts")) is PdfArray d && d.Items.Count > 0 ? R(d.Items[0]) as PdfDictionary : null;
                descriptor = descendant != null ? R(descendant.Get("FontDescriptor")) as PdfDictionary : descriptor;
                var encoding = R(font.Get("Encoding"));
                if (encoding is PdfStream es)
                {
                    _encodingCMap = TryCMap(es);
                }
                else
                {
                    // Identity-H/V and predefined CMaps: two-byte codes; for Identity, code = CID.
                    _identityCodes = true;
                }

                _defaultWidth = descendant != null && Number(descendant.Get("DW")) is double dw ? dw : 1000;
                _cidWidths = descendant != null ? CidWidths(R(descendant.Get("W")) as PdfArray) : new Dictionary<int, double>();
            }
            else
            {
                ReadSimple(font, subtype, descriptor);
            }

            var flags = descriptor != null && Number(descriptor.Get("Flags")) is double f ? (int)f : 0;
            var lower = BaseFont.ToLowerInvariant();
            Bold = lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy") || (flags & (1 << 18)) != 0 ||
                (descriptor != null && Number(descriptor.Get("FontWeight")) >= 600);
            Italic = lower.Contains("italic") || lower.Contains("oblique") || (flags & (1 << 6)) != 0 ||
                (descriptor != null && Number(descriptor.Get("ItalicAngle")) is double a && Math.Abs(a) > 0.5);
            if (descriptor != null)
            {
                Ascent = Number(descriptor.Get("Ascent")) is double asc && asc > 0 ? asc : 750;
                Descent = Number(descriptor.Get("Descent")) is double desc && desc < 0 ? desc : -250;
            }
        }

        public string BaseFont { get; }

        public bool IsComposite { get; }

        public bool Bold { get; }

        public bool Italic { get; }

        /// <summary>Ascent/descent in 1/1000 em (for glyph boxes).</summary>
        public double Ascent { get; } = 750;

        public double Descent { get; } = -250;

        /// <summary>Type3 glyph space → text space (only [0] and [3] are used: width and height scale).</summary>
        public double[]? FontMatrix { get; private set; }

        /// <summary>Reads one code; returns the number of bytes used.</summary>
        public int ReadCode(byte[] data, int pos, out uint code)
        {
            if (!IsComposite)
            {
                code = data[pos];
                return 1;
            }

            if (_encodingCMap != null && _encodingCMap.HasCodespaces)
            {
                return _encodingCMap.ReadCode(data, pos, out code);
            }

            if (_toUnicode != null && _toUnicode.HasCodespaces && !_identityCodes)
            {
                return _toUnicode.ReadCode(data, pos, out code);
            }

            if (pos + 1 < data.Length)
            {
                code = (uint)((data[pos] << 8) | data[pos + 1]);
                return 2;
            }

            code = data[pos];
            return 1;
        }

        /// <summary>Advance width in 1/1000 of text space (already converted for Type3 fonts).</summary>
        public double Width(uint code, int length)
        {
            if (!IsComposite)
            {
                return _simpleWidths[code & 0xFF];
            }

            var cid = Cid(code, length);
            return _cidWidths!.TryGetValue(cid, out var w) ? w : _defaultWidth;
        }

        public string Unicode(uint code, int length)
        {
            var mapped = _toUnicode?.Unicode(length, code);
            if (mapped != null)
            {
                return mapped;
            }

            if (!IsComposite)
            {
                return _simpleUnicode[code & 0xFF] ?? (code >= 32 && code < 127 ? ((char)code).ToString() : "�");
            }

            return "�";
        }

        /// <summary>True for the single-byte code 32, which receives word spacing (§9.3.3).</summary>
        public bool IsWordSpace(uint code, int length) => length == 1 && code == 32;

        private int Cid(uint code, int length)
        {
            if (_encodingCMap != null && _encodingCMap.HasCids)
            {
                return _encodingCMap.Cid(length, code) ?? 0;
            }

            return (int)code;
        }

        private void ReadSimple(PdfDictionary font, string subtype, PdfDictionary? descriptor)
        {
            // Base encoding (Annex D): explicit, else the font's own; symbolic fonts keep their codes.
            var flags = descriptor != null && Number(descriptor.Get("Flags")) is double f ? (int)f : 0;
            var symbolic = (flags & 4) != 0 && (flags & 32) == 0;
            var standard = StandardMetrics(BaseFont);
            ushort[]? table = symbolic || BaseFont.StartsWith("Symbol", StringComparison.Ordinal) || BaseFont.StartsWith("ZapfDingbats", StringComparison.Ordinal)
                ? null
                : subtype == "TrueType" ? Encodings.WinAnsi : Encodings.Standard;
            Dictionary<int, string>? differences = null;
            var encoding = R(font.Get("Encoding"));
            if (encoding is PdfName en)
            {
                table = BaseTable(en.Value) ?? table;
            }
            else if (encoding is PdfDictionary ed)
            {
                if (R(ed.Get("BaseEncoding")) is PdfName be)
                {
                    table = BaseTable(be.Value) ?? table;
                }

                if (R(ed.Get("Differences")) is PdfArray diffs)
                {
                    differences = new Dictionary<int, string>();
                    var code = 0;
                    foreach (var item in diffs.Items)
                    {
                        switch (R(item))
                        {
                            case PdfInteger i:
                                code = (int)i.Value;
                                break;
                            case PdfName n when code >= 0 && code < 256:
                                differences[code++] = n.Value;
                                break;
                        }
                    }
                }
            }

            for (var c = 0; c < 256; c++)
            {
                string? text = null;
                if (differences != null && differences.TryGetValue(c, out var glyph))
                {
                    text = GlyphList.ToUnicode(glyph);
                }

                if (text == null && table != null && table[c] != 0)
                {
                    text = ((char)table[c]).ToString();
                }

                _simpleUnicode[c] = text;
            }

            // Widths: /Widths + /FirstChar, else the standard-14 metrics, else /MissingWidth.
            var missing = descriptor != null && Number(descriptor.Get("MissingWidth")) is double mw ? mw : 0;
            var first = Number(font.Get("FirstChar")) is double fc ? (int)fc : 0;
            var widths = R(font.Get("Widths")) as PdfArray;
            if (subtype == "Type3" && R(font.Get("FontMatrix")) is PdfArray fm && fm.Items.Count == 6)
            {
                FontMatrix = new double[6];
                for (var i = 0; i < 6; i++)
                {
                    FontMatrix[i] = Number(fm.Items[i]) ?? 0;
                }
            }

            var scale = FontMatrix != null ? FontMatrix[0] * 1000 : 1;
            for (var c = 0; c < 256; c++)
            {
                double w;
                if (widths != null && c - first >= 0 && c - first < widths.Items.Count && Number(widths.Items[c - first]) is double given)
                {
                    w = given * scale;
                }
                else if (standard != null)
                {
                    var text = _simpleUnicode[c];
                    w = text != null && text.Length == 1 && WinAnsiEncoding.TryEncode(text[0], out var winCode) && winCode >= 32
                        ? standard.Widths[winCode - 32]
                        : standard.UsesBuiltInEncoding && c >= 32 ? standard.Widths[c - 32] : missing;
                }
                else
                {
                    w = missing;
                }

                _simpleWidths[c] = w;
            }
        }

        private static ushort[]? BaseTable(string name)
        {
            switch (name)
            {
                case "WinAnsiEncoding": return Encodings.WinAnsi;
                case "MacRomanEncoding": return Encodings.MacRoman;
                case "StandardEncoding": return Encodings.Standard;
                default: return null; // MacExpertEncoding and unknown names: keep the default
            }
        }

        private static StandardFontData.Entry? StandardMetrics(string baseFont)
        {
            var name = baseFont.Replace(",", "-");
            switch (name)
            {
                case "Arial": name = "Helvetica"; break;
                case "Arial-Bold": case "Arial-BoldMT": name = "Helvetica-Bold"; break;
                case "TimesNewRoman": case "TimesNewRomanPSMT": name = "Times-Roman"; break;
                case "CourierNew": case "CourierNewPSMT": name = "Courier"; break;
            }

            foreach (StandardFont f in Enum.GetValues(typeof(StandardFont)))
            {
                var entry = StandardFontData.Get(f);
                if (string.Equals(entry.BaseFont, name, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private Dictionary<int, double> CidWidths(PdfArray? w)
        {
            // /W: "c [w1 w2 …]" or "cFirst cLast w" (§9.7.4.3).
            var result = new Dictionary<int, double>();
            if (w == null)
            {
                return result;
            }

            var i = 0;
            while (i < w.Items.Count && result.Count < 200000)
            {
                var first = Number(w.Items[i]);
                if (first == null)
                {
                    break;
                }

                if (i + 1 < w.Items.Count && R(w.Items[i + 1]) is PdfArray list)
                {
                    for (var k = 0; k < list.Items.Count && result.Count < 200000; k++)
                    {
                        result[(int)first.Value + k] = Number(list.Items[k]) ?? _defaultWidth;
                    }

                    i += 2;
                }
                else if (i + 2 < w.Items.Count)
                {
                    var last = Number(w.Items[i + 1]) ?? first.Value;
                    var width = Number(w.Items[i + 2]) ?? _defaultWidth;
                    for (var c = (int)first.Value; c <= (int)last && result.Count < 200000; c++)
                    {
                        result[c] = width;
                    }

                    i += 3;
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        private CMap? TryCMap(PdfStream stream)
        {
            try
            {
                var limit = _parser?.Options.MaxStreamSize ?? 16 * 1024 * 1024;
                return CMap.Parse(StreamDecoder.Decode(stream, _parser, Math.Min(limit, 16 * 1024 * 1024)));
            }
            catch (PdfReadException ex) when (!(ex is PdfLimitException))
            {
                return null; // a damaged CMap: fall back to the encoding
            }
        }

        private static string StripSubset(string name) =>
            name.Length > 7 && name[6] == '+' && IsUpper(name, 6) ? name.Substring(7) : name;

        private static bool IsUpper(string s, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (s[i] < 'A' || s[i] > 'Z')
                {
                    return false;
                }
            }

            return true;
        }

        private PdfObject? R(PdfObject? o) => o is PdfSourceReference && _parser != null ? _parser.Resolve(o) : o;

        private double? Number(PdfObject? o)
        {
            switch (R(o))
            {
                case PdfInteger i: return i.Value;
                case PdfReal r: return r.Value;
                default: return null;
            }
        }
    }
}
