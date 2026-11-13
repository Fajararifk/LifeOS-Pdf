using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>
    /// Reads the file structure of a PDF (ISO 32000-1 §7.5): header, cross-reference tables
    /// and streams (including hybrid files and the /Prev chain of incremental updates), object
    /// streams, and the trailer. Objects are parsed lazily and cached. A damaged
    /// cross-reference is rebuilt by scanning the file for "n g obj".
    /// </summary>
    internal sealed class PdfParser
    {
        private static readonly byte[] StartXref = Encoding.ASCII.GetBytes("startxref");

        private readonly byte[] _data;
        private readonly Dictionary<int, XrefEntry> _xref = new Dictionary<int, XrefEntry>();
        private readonly Dictionary<int, PdfObject> _cache = new Dictionary<int, PdfObject>();
        private readonly Dictionary<int, ObjectStream> _objectStreams = new Dictionary<int, ObjectStream>();
        private readonly HashSet<int> _resolving = new HashSet<int>();
        private bool _rebuilt;

        public PdfParser(byte[] data, PdfReadOptions options)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            Options = options;
            var lexer = new Lexer(data);
            var header = lexer.IndexOf(Encoding.ASCII.GetBytes("%PDF-"), 0, Math.Min(data.Length, 1024));
            if (header < 0)
            {
                throw new PdfReadException("The data is not a PDF file (no '%PDF-' header in the first 1024 bytes).");
            }

            HeaderOffset = header;
            Version = ReadVersion(header + 5);
            Trailer = new PdfDictionary();
            try
            {
                ReadXrefChain();
            }
            catch (PdfReadException ex) when (IsSyntaxError(ex))
            {
                Rebuild();
            }

            if (Trailer.Get("Encrypt") != null)
            {
                throw new PdfEncryptedException();
            }

            if (!(Resolve(Trailer.Get("Root")) is PdfDictionary) && !_rebuilt)
            {
                Rebuild();
            }

            if (!(Resolve(Trailer.Get("Root")) is PdfDictionary))
            {
                throw new PdfReadException("The PDF has no document catalog (/Root); it is too damaged to open.");
            }
        }

        public PdfReadOptions Options { get; }

        /// <summary>Interned names and keywords shared by all lexers of this file.</summary>
        public NameTable Names { get; } = new NameTable();

        /// <summary>Page tree and names of this file, once loaded.</summary>
        public SourceDocument? Source { get; set; }

        public long HeaderOffset { get; }

        public string Version { get; }

        public PdfDictionary Trailer { get; private set; }

        public PdfDictionary Catalog => (PdfDictionary)Resolve(Trailer.Get("Root"))!;

        public int ObjectCount => _xref.Count;

        // ------------------------------------------------------------------ resolving

        /// <summary>Follows references (with a chain limit) to a direct object; missing objects are null.</summary>
        public PdfObject Resolve(PdfObject? obj)
        {
            var hops = 0;
            while (obj is PdfSourceReference r)
            {
                if (++hops > PdfReadOptions.MaxReferenceChain)
                {
                    throw new PdfLimitException("A chain of more than " + PdfReadOptions.MaxReferenceChain + " references was found; the file is damaged or malicious.");
                }

                obj = GetObject(r.Number);
            }

            return obj ?? PdfNull.Instance;
        }

        public PdfObject GetObject(int number)
        {
            if (_cache.TryGetValue(number, out var cached))
            {
                return cached;
            }

            if (!_resolving.Add(number))
            {
                return PdfNull.Instance; // a reference cycle while parsing (e.g. /Length pointing to itself)
            }

            try
            {
                var value = Load(number);
                _cache[number] = value;
                return value;
            }
            finally
            {
                _resolving.Remove(number);
            }
        }

        /// <summary>Stream /Length given as a reference: resolved without caching surprises.</summary>
        public PdfObject ResolveLength(PdfSourceReference r) => Resolve(r);

        private PdfObject Load(int number)
        {
            if (!_xref.TryGetValue(number, out var entry))
            {
                return PdfNull.Instance;
            }

            if (entry.Type == 2)
            {
                return LoadCompressed(number, (int)entry.Offset, entry.Index);
            }

            if (entry.Type != 1)
            {
                return PdfNull.Instance;
            }

            var value = TryParseAt(entry.Offset, number);
            if (value == null && HeaderOffset > 0)
            {
                value = TryParseAt(entry.Offset + HeaderOffset, number); // offsets counted from the header
            }

            if (value == null && !_rebuilt)
            {
                // Offsets do not match: rebuild the table from the file itself and try once more.
                Rebuild();
                if (!_xref.TryGetValue(number, out var e2))
                {
                    return PdfNull.Instance;
                }

                return e2.Type == 1 ? TryParseAt(e2.Offset, number) ?? PdfNull.Instance
                    : e2.Type == 2 ? LoadCompressed(number, (int)e2.Offset, e2.Index) : PdfNull.Instance;
            }

            return value ?? PdfNull.Instance;
        }

        private PdfObject? TryParseAt(long offset, int number)
        {
            if (offset < 0 || offset >= _data.Length)
            {
                return null;
            }

            var parser = new ObjectParser(new Lexer(_data, offset, Names), this, Options.MaxDepth);
            try
            {
                var value = parser.ParseIndirect(out var n, out _);
                return n == number ? value : null;
            }
            catch (PdfReadException ex) when (IsSyntaxError(ex))
            {
                return null;
            }
        }

        /// <summary>Limit violations stay errors; plain syntax damage only loses the one object.</summary>
        private static bool IsSyntaxError(PdfReadException ex) => !(ex is PdfEncryptedException) && !(ex is PdfLimitException);

        private PdfObject LoadCompressed(int number, int streamNumber, int index)
        {
            if (!_objectStreams.TryGetValue(streamNumber, out var os))
            {
                if (!_xref.TryGetValue(streamNumber, out var se) || se.Type != 1)
                {
                    return PdfNull.Instance; // an object stream must itself be uncompressed (§7.5.7)
                }

                if (!(GetObject(streamNumber) is PdfStream stream))
                {
                    return PdfNull.Instance;
                }

                os = new ObjectStream(this, stream);
                _objectStreams[streamNumber] = os;
            }

            return os.Get(number, index);
        }

        // ------------------------------------------------------------------ cross-reference

        private string ReadVersion(long position)
        {
            var sb = new StringBuilder();
            for (var i = position; i < _data.Length && i < position + 4; i++)
            {
                var c = (char)_data[i];
                if ((c >= '0' && c <= '9') || c == '.')
                {
                    sb.Append(c);
                }
                else
                {
                    break;
                }
            }

            return sb.Length > 0 ? sb.ToString() : "1.4";
        }

        private void ReadXrefChain()
        {
            var lexer = new Lexer(_data, 0, Names);
            var at = lexer.LastIndexOf(StartXref, _data.Length - StartXref.Length, Math.Max(0, _data.Length - 4096));
            if (at < 0)
            {
                throw new PdfReadException("No 'startxref' near the end of the file.");
            }

            lexer.Position = at + StartXref.Length;
            var token = lexer.Next();
            if (token.Kind != TokenKind.Integer)
            {
                throw new PdfReadException("'startxref' is not followed by an offset.");
            }

            var visited = new HashSet<long>();
            var offset = token.Integer;
            var first = true;
            while (offset >= 0)
            {
                if (visited.Count >= PdfReadOptions.MaxXrefSections)
                {
                    throw new PdfLimitException("More than " + PdfReadOptions.MaxXrefSections + " cross-reference sections.");
                }

                if (!visited.Add(offset))
                {
                    break; // /Prev loop: stop following
                }

                var trailer = ReadXrefSection(offset);
                if (first)
                {
                    Trailer = trailer;
                    first = false;
                }
                else
                {
                    // Keys missing from newer trailers are inherited from older ones.
                    foreach (var e in trailer.Entries)
                    {
                        if (Trailer.Get(e.Key) == null && e.Key != "Prev" && e.Key != "XRefStm")
                        {
                            Trailer.Set(e.Key, e.Value);
                        }
                    }
                }

                offset = Resolve(trailer.Get("Prev")) is PdfInteger prev ? prev.Value : -1;
            }

            if (_xref.Count == 0)
            {
                throw new PdfReadException("The cross-reference table is empty.");
            }
        }

        private PdfDictionary ReadXrefSection(long offset)
        {
            var section = TryXrefAt(offset) ?? (HeaderOffset > 0 ? TryXrefAt(offset + HeaderOffset) : null);
            if (section == null)
            {
                throw new PdfReadException("No cross-reference data at offset " + offset + ".");
            }

            return section;
        }

        private PdfDictionary? TryXrefAt(long offset)
        {
            if (offset < 0 || offset >= _data.Length)
            {
                return null;
            }

            var lexer = new Lexer(_data, offset, Names);
            var save = lexer.Position;
            var token = lexer.Next();
            if (token.IsKeyword("xref"))
            {
                return ReadXrefTable(lexer);
            }

            lexer.Position = save;
            if (token.Kind == TokenKind.Integer)
            {
                return ReadXrefStream(offset);
            }

            return null;
        }

        private PdfDictionary ReadXrefTable(Lexer lexer)
        {
            while (true)
            {
                var save = lexer.Position;
                var t = lexer.Next();
                if (t.IsKeyword("trailer"))
                {
                    break;
                }

                if (t.Kind != TokenKind.Integer)
                {
                    throw new PdfReadException("Damaged cross-reference table near offset " + t.Position + ".");
                }

                var countToken = lexer.Next();
                if (countToken.Kind != TokenKind.Integer || t.Integer < 0 || countToken.Integer < 0 || t.Integer + countToken.Integer > Options.MaxObjects + 1L)
                {
                    throw new PdfReadException("Invalid cross-reference subsection header near offset " + save + ".");
                }

                for (long i = 0; i < countToken.Integer; i++)
                {
                    var off = lexer.Next();
                    var gen = lexer.Next();
                    var kind = lexer.Next();
                    if (off.Kind != TokenKind.Integer || gen.Kind != TokenKind.Integer || (kind.Text != "n" && kind.Text != "f"))
                    {
                        throw new PdfReadException("Damaged cross-reference entry near offset " + off.Position + ".");
                    }

                    var number = (int)(t.Integer + i);
                    if (number > 0 && !_xref.ContainsKey(number))
                    {
                        _xref[number] = kind.Text == "n" && off.Integer > 0
                            ? new XrefEntry(1, off.Integer, 0)
                            : new XrefEntry(0, 0, 0);
                    }
                }
            }

            var trailer = new ObjectParser(lexer, this, Options.MaxDepth).ParseObject() as PdfDictionary
                ?? throw new PdfReadException("The trailer is not a dictionary.");
            if (Resolve(trailer.Get("XRefStm")) is PdfInteger xrefStm)
            {
                // Hybrid file (§7.5.8.4): extra entries for objects in object streams.
                try
                {
                    ReadXrefStream(xrefStm.Value);
                }
                catch (PdfReadException ex) when (IsSyntaxError(ex))
                {
                }
            }

            return trailer;
        }

        private PdfDictionary ReadXrefStream(long offset)
        {
            var parser = new ObjectParser(new Lexer(_data, offset, Names), this, Options.MaxDepth);
            if (!(parser.ParseIndirect(out _, out _) is PdfStream stream) || !(stream.Dictionary.Get("Type") is PdfName type) || type.Value != "XRef")
            {
                throw new PdfReadException("Expected a cross-reference stream at offset " + offset + ".");
            }

            var dict = stream.Dictionary;
            if (dict.Get("Encrypt") != null)
            {
                throw new PdfEncryptedException();
            }

            var data = StreamDecoder.Decode(stream, this, Options.MaxStreamSize);
            if (!(dict.Get("W") is PdfArray w) || w.Items.Count < 3)
            {
                throw new PdfReadException("Cross-reference stream without /W.");
            }

            var widths = new int[3];
            for (var i = 0; i < 3; i++)
            {
                widths[i] = w.Items[i] is PdfInteger wi && wi.Value >= 0 && wi.Value <= 8 ? (int)wi.Value : throw new PdfReadException("Invalid /W in cross-reference stream.");
            }

            var size = dict.Get("Size") is PdfInteger s ? s.Value : 0;
            if (size > Options.MaxObjects + 1L)
            {
                throw new PdfLimitException("The file declares " + size + " objects; the limit is " + Options.MaxObjects + " (PdfReadOptions.MaxObjects).");
            }

            var index = new List<long>();
            if (dict.Get("Index") is PdfArray ix)
            {
                foreach (var item in ix.Items)
                {
                    index.Add(item is PdfInteger ii ? ii.Value : 0);
                }
            }
            else
            {
                index.Add(0);
                index.Add(size);
            }

            var entryLength = widths[0] + widths[1] + widths[2];
            var pos = 0;
            for (var k = 0; k + 1 < index.Count; k += 2)
            {
                var start = index[k];
                var count = index[k + 1];
                if (start < 0 || count < 0 || start + count > Options.MaxObjects + 1L)
                {
                    throw new PdfReadException("Invalid /Index in cross-reference stream.");
                }

                for (long i = 0; i < count && entryLength > 0 && pos + entryLength <= data.Length; i++)
                {
                    var t = widths[0] == 0 ? 1 : Field(data, pos, widths[0]);
                    var f2 = Field(data, pos + widths[0], widths[1]);
                    var f3 = Field(data, pos + widths[0] + widths[1], widths[2]);
                    pos += entryLength;
                    var number = (int)(start + i);
                    if (number <= 0 || _xref.ContainsKey(number))
                    {
                        continue;
                    }

                    switch (t)
                    {
                        case 1:
                            _xref[number] = new XrefEntry(1, f2, 0);
                            break;
                        case 2:
                            _xref[number] = new XrefEntry(2, f2, (int)Math.Min(int.MaxValue, f3));
                            break;
                        default:
                            _xref[number] = new XrefEntry(0, 0, 0);
                            break;
                    }
                }
            }

            return dict;
        }

        private static long Field(byte[] data, int pos, int width)
        {
            long v = 0;
            for (var i = 0; i < width; i++)
            {
                v = (v << 8) | data[pos + i];
            }

            return v;
        }

        // ------------------------------------------------------------------ recovery

        /// <summary>
        /// Rebuilds the cross-reference by scanning for "n g obj" at line starts; later definitions
        /// win (like an incremental update). The trailer is the last one with /Root, or a
        /// synthesized one pointing to the last /Type /Catalog object.
        /// </summary>
        private void Rebuild()
        {
            _rebuilt = true;
            _xref.Clear();
            _cache.Clear();
            _objectStreams.Clear();
            var lexer = new Lexer(_data, 0, Names);
            PdfDictionary? trailer = null;
            var objStreams = new List<int>();
            for (long i = 0; i < _data.Length; i++)
            {
                var b = _data[i];
                var lineStart = i == 0 || _data[i - 1] == '\n' || _data[i - 1] == '\r' || Lexer.IsWhitespace(_data[i - 1]);
                if (!lineStart)
                {
                    continue;
                }

                if (b >= '0' && b <= '9')
                {
                    lexer.Position = i;
                    var n = lexer.Next();
                    var g = lexer.Next();
                    var kw = lexer.Next();
                    if (n.Kind == TokenKind.Integer && g.Kind == TokenKind.Integer && kw.IsKeyword("obj") && n.Integer > 0 && n.Integer <= Options.MaxObjects)
                    {
                        _xref[(int)n.Integer] = new XrefEntry(1, i, 0);
                        // Skip the object body so "n g obj" inside stream data is not mistaken for an object.
                        lexer.Position = i;
                        try
                        {
                            new ObjectParser(lexer, this, Options.MaxDepth).ParseIndirect(out _, out _);
                        }
                        catch (PdfReadException ex) when (IsSyntaxError(ex))
                        {
                            lexer.Position = kw.Position + 3;
                        }

                        i = Math.Max(i, lexer.Position - 1);
                    }
                }
                else if (b == 't' && i + 7 <= _data.Length && _data[i + 1] == 'r' && _data[i + 2] == 'a' && _data[i + 3] == 'i' && _data[i + 4] == 'l' && _data[i + 5] == 'e' && _data[i + 6] == 'r')
                {
                    lexer.Position = i + 7;
                    try
                    {
                        if (new ObjectParser(lexer, this, Options.MaxDepth).ParseObject() is PdfDictionary t && t.Get("Root") != null)
                        {
                            trailer = t;
                        }
                    }
                    catch (PdfReadException ex) when (IsSyntaxError(ex))
                    {
                    }
                }
            }

            _cache.Clear(); // values cached while the table was incomplete may be stale

            // Objects inside object streams, and cross-reference streams acting as trailers.
            var numbers = new List<int>(_xref.Keys);
            numbers.Sort();
            PdfDictionary? catalogTrailer = null;
            foreach (var number in numbers)
            {
                PdfObject value;
                try
                {
                    value = GetObject(number);
                }
                catch (PdfReadException ex) when (IsSyntaxError(ex))
                {
                    continue;
                }

                if (value is PdfStream s && s.Dictionary.Get("Type") is PdfName t)
                {
                    if (t.Value == "ObjStm")
                    {
                        objStreams.Add(number);
                    }
                    else if (t.Value == "XRef" && s.Dictionary.Get("Root") != null && trailer == null)
                    {
                        trailer = s.Dictionary;
                    }
                }
                else if (value is PdfDictionary d && d.Get("Type") is PdfName dt && dt.Value == "Catalog")
                {
                    catalogTrailer = new PdfDictionary().Set("Root", new PdfSourceReference(this, number, 0));
                }
            }

            foreach (var os in objStreams)
            {
                try
                {
                    var stream = (PdfStream)GetObject(os);
                    var objectStream = new ObjectStream(this, stream);
                    _objectStreams[os] = objectStream;
                    for (var i = 0; i < objectStream.Numbers.Count; i++)
                    {
                        if (!_xref.ContainsKey(objectStream.Numbers[i]))
                        {
                            _xref[objectStream.Numbers[i]] = new XrefEntry(2, os, i);
                        }
                    }
                }
                catch (PdfReadException ex) when (IsSyntaxError(ex))
                {
                }
            }

            if (trailer != null && !(Resolve(trailer.Get("Root")) is PdfDictionary) && catalogTrailer != null)
            {
                trailer.Set("Root", catalogTrailer.Get("Root")!);
            }

            Trailer = trailer ?? catalogTrailer ?? new PdfDictionary();
            if (Trailer.Get("Encrypt") != null)
            {
                throw new PdfEncryptedException();
            }
        }

        private readonly struct XrefEntry
        {
            public XrefEntry(int type, long offset, int index)
            {
                Type = type;
                Offset = offset;
                Index = index;
            }

            /// <summary>0 free, 1 at a byte offset, 2 inside an object stream.</summary>
            public int Type { get; }

            /// <summary>Byte offset (type 1) or object stream number (type 2).</summary>
            public long Offset { get; }

            public int Index { get; }
        }

        /// <summary>A decoded object stream (§7.5.7): N pairs of (number, offset) then the objects.</summary>
        private sealed class ObjectStream
        {
            private readonly PdfParser _owner;
            private readonly byte[] _data;
            private readonly List<long> _offsets = new List<long>();
            private readonly long _first;

            public ObjectStream(PdfParser owner, PdfStream stream)
            {
                _owner = owner;
                _data = StreamDecoder.Decode(stream, owner, owner.Options.MaxStreamSize);
                var n = owner.Resolve(stream.Dictionary.Get("N")) is PdfInteger ni ? ni.Value : 0;
                _first = owner.Resolve(stream.Dictionary.Get("First")) is PdfInteger fi ? fi.Value : 0;
                if (n < 0 || n > 1000000 || _first < 0 || _first > _data.Length)
                {
                    throw new PdfReadException("Invalid object stream header.");
                }

                var lexer = new Lexer(_data, 0, owner.Names);
                for (var i = 0; i < n; i++)
                {
                    var num = lexer.Next();
                    var off = lexer.Next();
                    if (num.Kind != TokenKind.Integer || off.Kind != TokenKind.Integer)
                    {
                        break;
                    }

                    Numbers.Add((int)Math.Max(0, Math.Min(int.MaxValue, num.Integer)));
                    _offsets.Add(off.Integer);
                }
            }

            public List<int> Numbers { get; } = new List<int>();

            public PdfObject Get(int number, int index)
            {
                if (index < 0 || index >= Numbers.Count || Numbers[index] != number)
                {
                    index = Numbers.IndexOf(number);
                    if (index < 0)
                    {
                        return PdfNull.Instance;
                    }
                }

                var position = _first + _offsets[index];
                if (position < 0 || position >= _data.Length)
                {
                    return PdfNull.Instance;
                }

                var parser = new ObjectParser(new Lexer(_data, position, _owner.Names), _owner, _owner.Options.MaxDepth);
                try
                {
                    return parser.ParseObject(); // objects in object streams are never streams
                }
                catch (PdfReadException ex) when (IsSyntaxError(ex))
                {
                    return PdfNull.Instance;
                }
            }
        }
    }
}
