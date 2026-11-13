using System;
using System.Collections.Generic;
using System.Linq;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// Removes content inside areas from a page's content stream (and from form XObjects it uses):
    /// glyphs are taken out of text operators and replaced by an equal TJ displacement so the rest
    /// of the line does not move; images and inline images that touch an area are removed; forms
    /// that touch an area are rewritten recursively under the same resource name.
    /// </summary>
    internal sealed class Redactor
    {
        private const int MaxDepth = 12;

        private readonly PdfParser _parser;
        private readonly PageGeometry _geometry;
        private readonly List<PdfRect> _areas;

        public Redactor(PdfParser parser, PageGeometry geometry, List<PdfRect> areas)
        {
            _parser = parser;
            _geometry = geometry;
            _areas = areas;
        }

        /// <summary>Number of glyphs, images and forms changed.</summary>
        public int Removed { get; private set; }

        /// <summary>Rewrites one content level; returns null when nothing in it touches an area.</summary>
        public List<ContentOperation>? Rewrite(List<ContentOperation> ops, PdfDictionary? resources, double[] ctm,
            Dictionary<string, PdfStream?> xobjectChanges, int depth)
        {
            var interpreter = new ContentInterpreter(_parser, _parser.Options.MaxStreamSize) { FollowForms = false, TrackPaths = true };
            interpreter.Run(ops, resources, ctm);

            // Glyphs to drop, per (operation, array element): byte offsets.
            var drop = new Dictionary<(int Op, int Element), Dictionary<int, GlyphInfo>>();
            foreach (var g in interpreter.Glyphs)
            {
                var box = _geometry.Quad(g.X0, g.Y0, g.X1, g.Y1, g.TopX, g.TopY, g.X1 + g.TopX - g.X0, g.Y1 + g.TopY - g.Y0);
                if (Hits(box))
                {
                    if (!drop.TryGetValue((g.Op, g.Element), out var set))
                    {
                        drop[(g.Op, g.Element)] = set = new Dictionary<int, GlyphInfo>();
                    }

                    set[g.Offset] = g;
                }
            }

            var removedOps = new HashSet<int>();
            var imageNames = new HashSet<string>();
            foreach (var image in interpreter.Images)
            {
                var c = image.Ctm;
                var box = _geometry.Quad(c[4], c[5], c[0] + c[4], c[1] + c[5], c[2] + c[4], c[3] + c[5], c[0] + c[2] + c[4], c[1] + c[3] + c[5]);
                if (Hits(box))
                {
                    removedOps.Add(image.Op);
                    if (image.Name != null)
                    {
                        imageNames.Add(image.Name);
                    }

                    Removed++;
                }
            }

            // Painted vector shapes (e.g. a QR code drawn as rectangles): subpaths touching an area go;
            // the painting operator goes too when nothing of its path is left.
            foreach (var path in interpreter.Paths)
            {
                var left = path.Subpaths.Count;
                foreach (var sub in path.Subpaths)
                {
                    if (Hits(_geometry.Rect(sub.X0, sub.Y0, sub.X1, sub.Y1)))
                    {
                        for (var o = sub.Start; o <= sub.End; o++)
                        {
                            removedOps.Add(o);
                        }

                        left--;
                        Removed++;
                    }
                }

                if (left == 0)
                {
                    removedOps.Add(path.Paint);
                }
            }

            var changed = drop.Count > 0 || removedOps.Count > 0;
            foreach (var form in interpreter.Forms)
            {
                if (depth >= MaxDepth || !Hits(_geometry.Rect(form.X0, form.Y0, form.X1, form.Y1)))
                {
                    continue;
                }

                var formChanges = new Dictionary<string, PdfStream?>();
                var formOps = ContentReader.Read(StreamDecoder.Decode(form.Stream, _parser, _parser.Options.MaxStreamSize));
                var rewritten = Rewrite(formOps, form.Resources, form.Matrix, formChanges, depth + 1);
                if (rewritten != null)
                {
                    xobjectChanges[form.Name] = NewForm(form, rewritten, formChanges);
                    changed = true;
                    Removed++;
                }
            }

            if (!changed)
            {
                return null;
            }

            var result = new List<ContentOperation>(ops.Count);
            for (var i = 0; i < ops.Count; i++)
            {
                if (removedOps.Contains(i))
                {
                    continue;
                }

                var op = ops[i];
                if (!drop.Keys.Any(k => k.Op == i))
                {
                    result.Add(op);
                    continue;
                }

                RewriteText(op, i, drop, result);
            }

            // Images no longer drawn anywhere on this level leave the resources too.
            var stillUsed = new HashSet<string>(result.Where(o => o.Op == "Do" && o.Operands.Length > 0 && o.Operands[0] is PdfName).Select(o => ((PdfName)o.Operands[0]).Value));
            foreach (var name in imageNames)
            {
                if (!stillUsed.Contains(name))
                {
                    xobjectChanges[name] = null;
                }
            }

            return result;
        }

        private bool Hits(PdfRect box)
        {
            foreach (var a in _areas)
            {
                if (a.Intersects(box))
                {
                    return true;
                }
            }

            return false;
        }

        private void RewriteText(ContentOperation op, int index, Dictionary<(int Op, int Element), Dictionary<int, GlyphInfo>> drop, List<ContentOperation> output)
        {
            // ' and " move to the next line (and set spacing) before showing: keep that part.
            var stringIndex = 0;
            switch (op.Op)
            {
                case "'":
                    output.Add(new ContentOperation("T*", Array.Empty<PdfObject>()));
                    break;
                case "\"":
                    output.Add(new ContentOperation("Tw", new[] { op.Operands[0] }));
                    output.Add(new ContentOperation("Tc", new[] { op.Operands[1] }));
                    output.Add(new ContentOperation("T*", Array.Empty<PdfObject>()));
                    stringIndex = 2;
                    break;
            }

            var elements = new PdfArray();
            if (op.Op == "TJ" && op.Operands.Length > 0 && op.Operands[0] is PdfArray source)
            {
                for (var e = 0; e < source.Items.Count; e++)
                {
                    if (source.Items[e] is PdfString s && drop.TryGetValue((index, e), out var gone))
                    {
                        Strip(s, gone, elements);
                    }
                    else
                    {
                        elements.Items.Add(source.Items[e]);
                    }
                }
            }
            else if (op.Operands.Length > stringIndex && op.Operands[stringIndex] is PdfString s)
            {
                if (drop.TryGetValue((index, stringIndex == 2 ? 2 : 0), out var gone))
                {
                    Strip(s, gone, elements);
                }
                else
                {
                    elements.Items.Add(s);
                }
            }

            output.Add(new ContentOperation("TJ", new PdfObject[] { elements }));
        }

        /// <summary>Keeps the codes not in <paramref name="gone"/>; each removed code becomes its displacement.</summary>
        private void Strip(PdfString text, Dictionary<int, GlyphInfo> gone, PdfArray output)
        {
            var bytes = text.Bytes;
            var kept = new List<byte>();
            void FlushKept()
            {
                if (kept.Count > 0)
                {
                    output.Items.Add(PdfString.FromBytes(kept.ToArray(), text.IsHex));
                    kept.Clear();
                }
            }

            var pos = 0;
            while (pos < bytes.Length)
            {
                if (gone.TryGetValue(pos, out var glyph))
                {
                    FlushKept();
                    var scale = glyph.Tfs * glyph.Th;
                    if (Math.Abs(scale) > 1e-9)
                    {
                        // TJ numbers move by −n/1000 · Tfs · Th (§9.4.3), so this keeps the advance.
                        output.Items.Add(new PdfReal(-glyph.Advance * 1000 / scale));
                    }

                    pos += Math.Max(1, glyph.Length);
                    Removed++;
                    continue;
                }

                kept.Add(bytes[pos]);
                pos++;
            }

            FlushKept();
        }

        private PdfStream NewForm(FormInfo form, List<ContentOperation> content, Dictionary<string, PdfStream?> nested)
        {
            var dict = new PdfDictionary();
            foreach (var e in form.Stream.Dictionary.Entries)
            {
                if (e.Key != "Length" && e.Key != "Filter" && e.Key != "DecodeParms")
                {
                    dict.Set(e.Key, e.Value);
                }
            }

            if (nested.Count > 0)
            {
                // The form's own resources, with its XObjects replaced or removed.
                var resources = new PdfDictionary();
                foreach (var e in form.Resources.Entries)
                {
                    resources.Set(e.Key, e.Value);
                }

                var xobjects = new PdfDictionary();
                if (_parser.Resolve(form.Resources.Get("XObject")) is PdfDictionary existing)
                {
                    foreach (var e in existing.Entries)
                    {
                        xobjects.Set(e.Key, e.Value);
                    }
                }

                foreach (var pair in nested)
                {
                    if (pair.Value == null)
                    {
                        xobjects.Remove(pair.Key);
                    }
                    else
                    {
                        xobjects.Set(pair.Key, pair.Value);
                    }
                }

                resources.Set("XObject", xobjects);
                dict.Set("Resources", resources);
            }

            dict.Set("Filter", "FlateDecode");
            return new PdfStream(dict, Zlib.Compress(ContentReader.Write(content)));
        }
    }
}
