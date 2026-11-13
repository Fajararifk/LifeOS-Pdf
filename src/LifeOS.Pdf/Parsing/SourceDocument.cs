using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>A page of a parsed file with its inherited attributes resolved (§7.7.3.4).</summary>
    internal sealed class SourcePage
    {
        public SourcePage(SourceDocument document, int number, PdfDictionary dictionary)
        {
            Document = document;
            Number = number;
            Dictionary = dictionary;
        }

        public SourceDocument Document { get; }

        /// <summary>Object number of the page dictionary, or -1 for a (non-conforming) direct page.</summary>
        public int Number { get; }

        public PdfDictionary Dictionary { get; }

        /// <summary>Resources as found (direct dictionary or reference), possibly inherited; null when absent.</summary>
        public PdfObject? Resources { get; set; }

        /// <summary>MediaBox as left, bottom, right, top (normalized).</summary>
        public double[] MediaBox { get; set; } = { 0, 0, 612, 792 };

        public double[]? CropBox { get; set; }

        public int Rotate { get; set; }

        public PdfParser Parser => Document.Parser;
    }

    /// <summary>Document-level view of a parsed file: pages, info, outline and named destinations.</summary>
    internal sealed class SourceDocument
    {
        private Dictionary<string, PdfObject>? _names;

        private SourceDocument(PdfParser parser)
        {
            Parser = parser;
        }

        public PdfParser Parser { get; }

        public List<SourcePage> Pages { get; } = new List<SourcePage>();

        /// <summary>Page object number → index in <see cref="Pages"/>.</summary>
        public Dictionary<int, int> PageIndex { get; } = new Dictionary<int, int>();

        public static SourceDocument Load(PdfParser parser)
        {
            var doc = new SourceDocument(parser);
            parser.Source = doc;
            doc.LoadPages();
            return doc;
        }

        // ------------------------------------------------------------------ pages

        private void LoadPages()
        {
            var catalog = Parser.Catalog;
            var root = catalog.Get("Pages");
            var visited = new HashSet<int>();
            var stack = new Stack<(PdfObject Node, Inherited Attrs, int Depth)>();
            stack.Push((root ?? PdfNull.Instance, new Inherited(), 0));
            while (stack.Count > 0)
            {
                var (nodeRef, attrs, depth) = stack.Pop();
                if (depth > PdfReadOptions.MaxTreeDepth)
                {
                    throw new PdfLimitException("The page tree is more than " + PdfReadOptions.MaxTreeDepth + " levels deep.");
                }

                var number = -1;
                if (nodeRef is PdfSourceReference r)
                {
                    number = r.Number;
                    if (!visited.Add(number))
                    {
                        continue; // a node reached twice (cycle or shared kid) is used once
                    }
                }

                if (!(Parser.Resolve(nodeRef) is PdfDictionary node))
                {
                    continue;
                }

                var here = attrs.With(node);
                var type = node.Get("Type") as PdfName;
                if (Parser.Resolve(node.Get("Kids")) is PdfArray kids && (type == null || type.Value != "Page"))
                {
                    // Push in reverse so pages come out in document order.
                    for (var i = kids.Items.Count - 1; i >= 0; i--)
                    {
                        stack.Push((kids.Items[i], here, depth + 1));
                    }

                    continue;
                }

                if (type != null && type.Value != "Page")
                {
                    continue;
                }

                AddPage(number, node, here);
            }

            if (Pages.Count == 0)
            {
                throw new PdfReadException("The PDF has no readable pages (the page tree is empty or damaged).");
            }
        }

        private void AddPage(int number, PdfDictionary node, Inherited attrs)
        {
            if (Pages.Count >= Parser.Options.MaxPages)
            {
                throw new PdfLimitException("The PDF has more than " + Parser.Options.MaxPages + " pages (PdfReadOptions.MaxPages).");
            }

            var page = new SourcePage(this, number, node)
            {
                Resources = attrs.Resources,
                MediaBox = Rect(attrs.MediaBox) ?? new double[] { 0, 0, 612, 792 },
                Rotate = attrs.Rotate,
            };
            var crop = Rect(attrs.CropBox);
            if (crop != null)
            {
                // The crop box is clipped to the media box (§14.11.2).
                var m = page.MediaBox;
                var c = new[] { Math.Max(crop[0], m[0]), Math.Max(crop[1], m[1]), Math.Min(crop[2], m[2]), Math.Min(crop[3], m[3]) };
                if (c[2] - c[0] >= 1 && c[3] - c[1] >= 1 && !(c[0] == m[0] && c[1] == m[1] && c[2] == m[2] && c[3] == m[3]))
                {
                    page.CropBox = c;
                }
            }

            if (number > 0)
            {
                PageIndex[number] = Pages.Count;
            }

            Pages.Add(page);
        }

        public double[]? Rect(PdfObject? obj)
        {
            if (!(Parser.Resolve(obj) is PdfArray a) || a.Items.Count < 4)
            {
                return null;
            }

            var v = new double[4];
            for (var i = 0; i < 4; i++)
            {
                var n = Number(a.Items[i]);
                if (n == null || double.IsNaN(n.Value) || double.IsInfinity(n.Value))
                {
                    return null;
                }

                v[i] = n.Value;
            }

            var r = new[] { Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]) };
            return r[2] - r[0] >= 1 && r[3] - r[1] >= 1 ? r : null;
        }

        public double? Number(PdfObject? obj)
        {
            switch (Parser.Resolve(obj))
            {
                case PdfInteger i: return i.Value;
                case PdfReal r: return r.Value;
                default: return null;
            }
        }

        private sealed class Inherited
        {
            public PdfObject? Resources;
            public PdfObject? MediaBox;
            public PdfObject? CropBox;
            public int Rotate;

            public Inherited With(PdfDictionary node)
            {
                var copy = (Inherited)MemberwiseClone();
                copy.Resources = node.Get("Resources") ?? Resources;
                copy.MediaBox = node.Get("MediaBox") ?? MediaBox;
                copy.CropBox = node.Get("CropBox") ?? CropBox;
                if (node.Get("Rotate") != null)
                {
                    copy.Rotate = 0;
                    if (node.Get("Rotate") is PdfInteger ri)
                    {
                        copy.Rotate = NormalizeRotation(ri.Value);
                    }
                    else if (node.Get("Rotate") is PdfReal rr)
                    {
                        copy.Rotate = NormalizeRotation((long)Math.Round(rr.Value));
                    }
                }

                return copy;
            }
        }

        /// <summary>Rotation in 0, 90, 180, 270; values that are not multiples of 90 are ignored like viewers do.</summary>
        public static int NormalizeRotation(long value)
        {
            if (value % 90 != 0)
            {
                return 0;
            }

            var r = (int)(value % 360);
            return r < 0 ? r + 360 : r;
        }

        // ------------------------------------------------------------------ info

        public string? InfoText(string key) =>
            Parser.Resolve(Parser.Trailer.Get("Info")) is PdfDictionary info && Parser.Resolve(info.Get(key)) is PdfString s ? TextString(s.Bytes) : null;

        public DateTimeOffset? InfoDate(string key)
        {
            var text = InfoText(key);
            return text == null ? (DateTimeOffset?)null : ParseDate(text);
        }

        private static readonly char[] PdfDocHigh =
        {
            '•', '†', '‡', '…', '—', '–', 'ƒ', '⁄', '‹', '›', '−', '‰', '„', '“', '”', '‘',
            '’', '‚', '™', 'ﬁ', 'ﬂ', 'Ł', 'Œ', 'Š', 'Ÿ', 'Ž', 'ı', 'ł', 'œ', 'š', 'ž', '�',
            '€',
        };

        private static readonly char[] PdfDocLow = { '˘', 'ˇ', 'ˆ', '˙', '˝', '˛', '˚', '˜' };

        /// <summary>Decodes a text string (§7.9.2.2): UTF-16BE or UTF-8 with BOM, else PDFDocEncoding.</summary>
        public static string TextString(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                chars[i] = b >= 0x80 && b <= 0xA0 ? PdfDocHigh[b - 0x80] : b >= 0x18 && b <= 0x1F ? PdfDocLow[b - 0x18] : (char)b;
            }

            return new string(chars);
        }

        /// <summary>Parses "D:YYYYMMDDHHmmSSOHH'mm'" leniently (§7.9.4); null when unreadable.</summary>
        public static DateTimeOffset? ParseDate(string text)
        {
            var s = text.Trim();
            if (s.StartsWith("D:", StringComparison.Ordinal))
            {
                s = s.Substring(2);
            }

            int Part(int start, int length, int fallback) =>
                s.Length >= start + length && int.TryParse(s.Substring(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : fallback;

            if (s.Length < 4 || !int.TryParse(s.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                return null;
            }

            var month = Part(4, 2, 1);
            var day = Part(6, 2, 1);
            var hour = Part(8, 2, 0);
            var minute = Part(10, 2, 0);
            var second = Part(12, 2, 0);
            var offset = TimeSpan.Zero;
            if (s.Length > 14 && (s[14] == '+' || s[14] == '-'))
            {
                var oh = Part(15, 2, 0);
                var om = s.Length >= 20 ? Part(18, 2, 0) : 0;
                offset = new TimeSpan(oh, om, 0);
                if (s[14] == '-')
                {
                    offset = offset.Negate();
                }
            }

            try
            {
                return new DateTimeOffset(year, month, day, hour, minute, Math.Min(second, 59), offset);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ destinations

        /// <summary>
        /// Turns a destination (explicit array, name, string, or a dictionary with /D) into an explicit
        /// array, looking names up in the catalog's /Dests and /Names /Dests. Null when it cannot be resolved.
        /// </summary>
        public PdfArray? ExplicitDestination(PdfObject? dest)
        {
            for (var hops = 0; hops < 4; hops++)
            {
                var d = Parser.Resolve(dest);
                switch (d)
                {
                    case PdfArray array:
                        return array.Items.Count > 0 ? array : null;
                    case PdfDictionary dict:
                        dest = dict.Get("D");
                        continue;
                    case PdfName name:
                        dest = Lookup(name.Value);
                        continue;
                    case PdfString str:
                        dest = Lookup(Latin1(str.Bytes));
                        continue;
                    default:
                        return null;
                }
            }

            return null;
        }

        /// <summary>The destination of an outline item or link: /Dest, or /A with a GoTo action.</summary>
        public PdfArray? DestinationOf(PdfDictionary item)
        {
            if (item.Get("Dest") != null)
            {
                return ExplicitDestination(item.Get("Dest"));
            }

            if (Parser.Resolve(item.Get("A")) is PdfDictionary action && action.Get("S") is PdfName s && s.Value == "GoTo")
            {
                return ExplicitDestination(action.Get("D"));
            }

            return null;
        }

        private PdfObject? Lookup(string name)
        {
            if (_names == null)
            {
                _names = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
                var catalog = Parser.Catalog;
                if (Parser.Resolve(catalog.Get("Dests")) is PdfDictionary old)
                {
                    foreach (var e in old.Entries)
                    {
                        _names[e.Key] = e.Value;
                    }
                }

                if (Parser.Resolve(catalog.Get("Names")) is PdfDictionary names)
                {
                    ReadNameTree(names.Get("Dests"));
                }
            }

            return _names.TryGetValue(name, out var v) ? v : null;
        }

        private void ReadNameTree(PdfObject? root)
        {
            var visited = new HashSet<int>();
            var stack = new Stack<(PdfObject Node, int Depth)>();
            stack.Push((root ?? PdfNull.Instance, 0));
            while (stack.Count > 0 && _names!.Count < PdfReadOptions.MaxOutlineItems)
            {
                var (nodeRef, depth) = stack.Pop();
                if (depth > PdfReadOptions.MaxTreeDepth || (nodeRef is PdfSourceReference r && !visited.Add(r.Number)))
                {
                    continue;
                }

                if (!(Parser.Resolve(nodeRef) is PdfDictionary node))
                {
                    continue;
                }

                if (Parser.Resolve(node.Get("Names")) is PdfArray pairs)
                {
                    for (var i = 0; i + 1 < pairs.Items.Count; i += 2)
                    {
                        if (Parser.Resolve(pairs.Items[i]) is PdfString key)
                        {
                            var k = Latin1(key.Bytes);
                            if (!_names.ContainsKey(k))
                            {
                                _names[k] = pairs.Items[i + 1];
                            }
                        }
                    }
                }

                if (Parser.Resolve(node.Get("Kids")) is PdfArray kids)
                {
                    foreach (var kid in kids.Items)
                    {
                        stack.Push((kid, depth + 1));
                    }
                }
            }
        }

        private static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            return new string(chars);
        }

        // ------------------------------------------------------------------ outline

        /// <summary>Reads the outline tree with cycle, depth and count limits.</summary>
        public List<SourceOutlineItem> ReadOutline()
        {
            var result = new List<SourceOutlineItem>();
            if (!(Parser.Resolve(Parser.Catalog.Get("Outlines")) is PdfDictionary root))
            {
                return result;
            }

            var visited = new HashSet<int>();
            var total = 0;
            var stack = new Stack<(PdfObject? First, List<SourceOutlineItem> Target, int Depth)>();
            stack.Push((root.Get("First"), result, 0));
            while (stack.Count > 0)
            {
                var (first, target, depth) = stack.Pop();
                if (depth > PdfReadOptions.MaxTreeDepth)
                {
                    continue;
                }

                var current = first;
                while (current != null && total < PdfReadOptions.MaxOutlineItems)
                {
                    if (current is PdfSourceReference r && !visited.Add(r.Number))
                    {
                        break; // cycle in /Next
                    }

                    if (!(Parser.Resolve(current) is PdfDictionary item))
                    {
                        break;
                    }

                    total++;
                    var title = Parser.Resolve(item.Get("Title")) is PdfString t ? TextString(t.Bytes) : string.Empty;
                    var node = new SourceOutlineItem(title.Replace("\r", " ").Replace("\n", " ").Trim(), DestinationOf(item))
                    {
                        IsOpen = Number(item.Get("Count")) > 0,
                    };
                    if (Number(item.Get("F")) is double f)
                    {
                        node.Italic = ((int)f & 1) != 0;
                        node.Bold = ((int)f & 2) != 0;
                    }

                    if (Parser.Resolve(item.Get("C")) is PdfArray c && c.Items.Count == 3)
                    {
                        node.Color = new double[] { Number(c.Items[0]) ?? 0, Number(c.Items[1]) ?? 0, Number(c.Items[2]) ?? 0 };
                    }

                    target.Add(node);
                    if (item.Get("First") != null)
                    {
                        stack.Push((item.Get("First"), node.Children, depth + 1));
                    }

                    current = item.Get("Next");
                }
            }

            return result;
        }
    }

    internal sealed class SourceOutlineItem
    {
        public SourceOutlineItem(string title, PdfArray? destination)
        {
            Title = title;
            Destination = destination;
        }

        public string Title { get; }

        public PdfArray? Destination { get; }

        public bool IsOpen { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public double[]? Color { get; set; }

        public List<SourceOutlineItem> Children { get; } = new List<SourceOutlineItem>();
    }
}
