using System;
using System.Collections.Generic;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>
    /// Copies objects from parsed files into the document being written. Each source object
    /// is copied once (shared fonts and images stay shared). Work is queued instead of
    /// recursing through references, so long reference chains cannot overflow the stack.
    /// References to pages that are saved point to their new page objects; references to
    /// other pages, page-tree nodes or the catalog become null — that is what drops
    /// everything the saved pages do not need.
    /// </summary>
    internal sealed class ObjectCopier
    {
        private readonly Func<PdfIndirectObject> _newObject;
        private readonly Func<PdfParser, int, PdfIndirectObject?> _pageObject;
        private readonly bool _compress;
        private readonly System.IO.Compression.CompressionLevel _level;
        private readonly Dictionary<(PdfParser, int), PdfIndirectObject> _copied = new Dictionary<(PdfParser, int), PdfIndirectObject>();
        private readonly Queue<(PdfIndirectObject Target, PdfParser Parser, PdfObject Source)> _queue = new Queue<(PdfIndirectObject, PdfParser, PdfObject)>();

        public ObjectCopier(Func<PdfIndirectObject> newObject, Func<PdfParser, int, PdfIndirectObject?> pageObject, bool compress,
            System.IO.Compression.CompressionLevel level = System.IO.Compression.CompressionLevel.Optimal)
        {
            _level = level;
            _newObject = newObject;
            _pageObject = pageObject;
            _compress = compress;
        }

        /// <summary>Copies a value (direct objects deeply; references become queued output objects).</summary>
        public PdfObject Copy(PdfParser parser, PdfObject? value) => CopyDirect(parser, value, 0);

        /// <summary>Copies everything queued by previous <see cref="Copy"/> calls.</summary>
        public void Drain()
        {
            while (_queue.Count > 0)
            {
                var (target, parser, source) = _queue.Dequeue();
                target.Value = CopyDirect(parser, source, 0);
            }
        }

        private PdfObject CopyDirect(PdfParser parser, PdfObject? value, int depth)
        {
            if (depth > 512)
            {
                throw new PdfLimitException("Objects are nested too deeply to copy.");
            }

            switch (value)
            {
                case null:
                    return PdfNull.Instance;
                case PdfSourceReference r:
                    return CopyReference(r);
                case PdfArray array:
                    var a = new PdfArray();
                    foreach (var item in array.Items)
                    {
                        a.Items.Add(CopyDirect(parser, item, depth + 1));
                    }

                    return a;
                case PdfStream stream:
                    return CopyStream(parser, stream, depth);
                case PdfDictionary dict:
                    var d = new PdfDictionary();
                    foreach (var e in dict.Entries)
                    {
                        var v = CopyDirect(parser, e.Value, depth + 1);
                        if (!(v is PdfNull))
                        {
                            d.Set(e.Key, v);
                        }
                    }

                    return d;
                default:
                    return value; // names, numbers, strings, booleans and null are immutable
            }
        }

        /// <summary>Copies the object <paramref name="number"/> as <paramref name="replacement"/> (a modified version of it).</summary>
        public PdfReference CopyReplaced(PdfParser parser, int number, PdfDictionary replacement)
        {
            var target = _newObject();
            if (number > 0)
            {
                _copied[(parser, number)] = target;
            }

            target.Value = CopyDirect(parser, replacement, 0);
            return target.Reference;
        }

        private PdfObject CopyReference(PdfSourceReference r)
        {
            var key = (r.Parser, r.Number);
            if (_copied.TryGetValue(key, out var existing))
            {
                return existing.Reference;
            }

            var page = _pageObject(r.Parser, r.Number);
            if (page != null)
            {
                return page.Reference;
            }

            var resolved = r.Parser.Resolve(r);
            if (resolved is PdfNull)
            {
                return PdfNull.Instance;
            }

            if (resolved is PdfDictionary dict && dict.Get("Type") is PdfName type &&
                (type.Value == "Page" || type.Value == "Pages" || type.Value == "Catalog"))
            {
                return PdfNull.Instance; // outside the saved pages
            }

            var target = _newObject();
            _copied[key] = target;
            _queue.Enqueue((target, r.Parser, resolved));
            return target.Reference;
        }

        private PdfStream CopyStream(PdfParser parser, PdfStream stream, int depth)
        {
            var dict = new PdfDictionary();
            foreach (var e in stream.Dictionary.Entries)
            {
                if (e.Key == "Length")
                {
                    continue; // recomputed by the writer
                }

                var v = CopyDirect(parser, e.Value, depth + 1);
                if (!(v is PdfNull))
                {
                    dict.Set(e.Key, v);
                }
            }

            var data = stream.Data;
            var isMetadata = stream.Dictionary.Get("Type") is PdfName t && t.Value == "Metadata";
            if (_compress && stream.Dictionary.Get("Filter") == null && !isMetadata && data.Length > 64)
            {
                data = Zlib.Compress(data, _level);
                dict.Set("Filter", "FlateDecode");
                dict.Remove("DecodeParms");
            }

            return new PdfStream(dict, data);
        }
    }
}
