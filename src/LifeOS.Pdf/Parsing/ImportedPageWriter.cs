using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>
    /// Writes pages of opened files: their content and resources are copied, annotations are
    /// cleaned (no scripts or extra actions, internal links resolved to explicit destinations),
    /// and anything drawn on them is added on top in its own form XObject.
    /// </summary>
    internal sealed class ImportedPageWriter
    {
        // Annotation actions that are never copied: they run code or open other files.
        private static readonly HashSet<string> UnsafeActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "JavaScript", "Launch", "ImportData", "SubmitForm", "ResetForm", "Rendition", "Movie", "Sound", "GoToE",
        };

        private readonly Func<PdfIndirectObject> _new;
        private readonly ObjectCopier _copier;
        private readonly Dictionary<(PdfParser, int), PdfPage> _sourcePages;
        private readonly Dictionary<PdfPage, PdfIndirectObject> _pageObjects;
        private readonly PdfSaveOptions _options;
        private PdfIndirectObject? _openState;

        public ImportedPageWriter(Func<PdfIndirectObject> newObject, ObjectCopier copier, Dictionary<(PdfParser, int), PdfPage> sourcePages,
            Dictionary<PdfPage, PdfIndirectObject> pageObjects, PdfSaveOptions options)
        {
            _new = newObject;
            _copier = copier;
            _sourcePages = sourcePages;
            _pageObjects = pageObjects;
            _options = options;
        }

        public PdfDictionary Write(PdfPage page, PdfIndirectObject pagesRoot, PageResourceObjects resources)
        {
            var sp = page.Source!;
            var parser = sp.Parser;
            var src = sp.Dictionary;
            var m = sp.MediaBox;
            var dict = new PdfDictionary()
                .Set("Type", "Page")
                .Set("Parent", pagesRoot.Reference)
                .Set("MediaBox", Box(m));
            if (sp.CropBox != null)
            {
                dict.Set("CropBox", Box(sp.CropBox));
            }

            foreach (var key in new[] { "BleedBox", "TrimBox", "ArtBox" })
            {
                if (sp.Document.Rect(src.Get(key)) is double[] box)
                {
                    dict.Set(key, Box(box));
                }
            }

            if (page.Rotation != 0)
            {
                dict.Set("Rotate", page.Rotation);
            }

            foreach (var key in new[] { "Group", "UserUnit", "Tabs" })
            {
                var v = src.Get(key);
                if (v != null)
                {
                    var copied = _copier.Copy(parser, v);
                    if (!(copied is PdfNull))
                    {
                        dict.Set(key, copied);
                    }
                }
            }

            var contents = new List<PdfObject>();
            var replaced = page.ContentOverride;
            if (replaced != null)
            {
                // Redacted content: only the rewritten stream is written; the original is not copied.
                var stream = _new();
                stream.Value = DocumentWriter.ContentStream(replaced.Content, _options);
                contents.Add(stream.Reference);
            }
            else
            {
                foreach (var c in ContentParts(sp))
                {
                    var copied = _copier.Copy(parser, c);
                    if (copied is PdfReference)
                    {
                        contents.Add(copied);
                    }
                }
            }

            PdfObject? copiedResources;
            var drawn = page.HasNewContent || page.Fonts.Count > 0 || page.Images.Count > 0 || page.Forms.Count > 0;
            if (replaced != null && !drawn)
            {
                var merged = MergeResources(parser, sp.Resources, out var xobjects, replaced);
                Replace(parser, xobjects, replaced);
                copiedResources = merged;
            }
            else if (drawn)
            {
                // New drawing goes into form XObjects of its own: their resource names cannot clash with
                // the original ones, and q … Q around the old content keeps its graphics state from leaking.
                var pageResources = resources.For(page);
                var merged = MergeResources(parser, sp.Resources, out var xobjects, replaced);
                if (replaced != null)
                {
                    Replace(parser, xobjects, replaced);
                }

                copiedResources = merged;
                string AddForm(string baseName, byte[] content)
                {
                    var form = _new();
                    var formDict = new PdfDictionary()
                        .Set("Type", "XObject")
                        .Set("Subtype", "Form")
                        .Set("BBox", PdfArray.OfNumbers(0, 0, page.Width, page.Height))
                        .Set("Matrix", PdfArray.OfNumbers(1, 0, 0, 1, page.OriginX, page.OriginY))
                        .Set("Resources", pageResources);
                    var stream = DocumentWriter.ContentStream(content, _options);
                    foreach (var e in stream.Dictionary.Entries)
                    {
                        formDict.Set(e.Key, e.Value);
                    }

                    form.Value = new PdfStream(formDict, stream.Data);
                    var name = baseName;
                    for (var i = 2; xobjects.Get(name) != null; i++)
                    {
                        name = baseName + i;
                    }

                    xobjects.Set(name, form.Reference);
                    return name;
                }

                if (page.Graphics.UnderlayLength > 0)
                {
                    var under = AddForm("LOSUnderlay", page.Graphics.UnderlayBytes());
                    var first = _new();
                    first.Value = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/" + under + " Do\n"));
                    contents.Insert(0, first.Reference);
                }

                var main = page.Graphics.ContentBytes();
                if (main.Length > 0)
                {
                    var over = AddForm("LOSOverlay", main);
                    if (_openState == null)
                    {
                        _openState = _new();
                        _openState.Value = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("q\n"));
                    }

                    var close = _new();
                    close.Value = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("Q\n/" + over + " Do\n"));
                    contents.Insert(page.Graphics.UnderlayLength > 0 ? 1 : 0, _openState.Reference);
                    contents.Add(close.Reference);
                }
            }
            else
            {
                copiedResources = sp.Resources == null ? null : _copier.Copy(parser, sp.Resources);
            }

            if (copiedResources != null && !(copiedResources is PdfNull))
            {
                dict.Set("Resources", copiedResources);
            }
            else
            {
                dict.Set("Resources", new PdfDictionary());
            }

            if (contents.Count == 1)
            {
                dict.Set("Contents", contents[0]);
            }
            else if (contents.Count > 1)
            {
                dict.Set("Contents", new PdfArray(contents.ToArray()));
            }

            _removedAnnotations = page.RemovedAnnotations;
            _formWidgetNumbers = page.FormWidgetNumbers;
            var annots = Annotations(sp);
            if (annots.Items.Count > 0)
            {
                dict.Set("Annots", annots);
            }

            return dict;
        }

        private HashSet<int>? _removedAnnotations;

        private HashSet<int> _formWidgetNumbers = new HashSet<int>();

        /// <summary>Applies redaction replacements to a page's (copied) XObject dictionary.</summary>
        private void Replace(PdfParser parser, PdfDictionary xobjects, Extraction.Engine.ContentOverride replaced)
        {
            foreach (var pair in replaced.XObjects)
            {
                if (pair.Value == null)
                {
                    xobjects.Remove(pair.Key);
                }
                else
                {
                    xobjects.Set(pair.Key, _copier.Copy(parser, pair.Value));
                }
            }
        }

        private static PdfArray Box(double[] b) => PdfArray.OfNumbers(b[0], b[1], b[2], b[3]);

        /// <summary>The page's content streams as found (references or direct streams).</summary>
        internal static List<PdfObject> ContentParts(SourcePage sp)
        {
            var parts = new List<PdfObject>();
            var raw = sp.Dictionary.Get("Contents");
            var resolved = sp.Parser.Resolve(raw);
            if (resolved is PdfStream)
            {
                parts.Add(raw!);
            }
            else if (resolved is PdfArray array)
            {
                foreach (var item in array.Items)
                {
                    if (sp.Parser.Resolve(item) is PdfStream)
                    {
                        parts.Add(item);
                    }
                }
            }

            return parts;
        }

        /// <summary>A direct copy of the page resources whose /XObject dictionary can take one more entry.</summary>
        private PdfDictionary MergeResources(PdfParser parser, PdfObject? source, out PdfDictionary xobjects, Extraction.Engine.ContentOverride? replaced = null)
        {
            var merged = new PdfDictionary();
            xobjects = new PdfDictionary();
            if (parser.Resolve(source) is PdfDictionary res)
            {
                foreach (var e in res.Entries)
                {
                    if (e.Key == "XObject")
                    {
                        if (parser.Resolve(e.Value) is PdfDictionary xo)
                        {
                            foreach (var x in xo.Entries)
                            {
                                if (replaced != null && replaced.XObjects.ContainsKey(x.Key))
                                {
                                    continue; // redacted: the original must not be copied at all
                                }

                                var v = _copier.Copy(parser, x.Value);
                                if (!(v is PdfNull))
                                {
                                    xobjects.Set(x.Key, v);
                                }
                            }
                        }

                        continue;
                    }

                    var copied = _copier.Copy(parser, e.Value);
                    if (!(copied is PdfNull))
                    {
                        merged.Set(e.Key, copied);
                    }
                }
            }

            merged.Set("XObject", xobjects);
            return merged;
        }

        private PdfArray Annotations(SourcePage sp)
        {
            var result = new PdfArray();
            var parser = sp.Parser;
            if (!(parser.Resolve(sp.Dictionary.Get("Annots")) is PdfArray annots))
            {
                return result;
            }

            foreach (var item in annots.Items)
            {
                if (!(parser.Resolve(item) is PdfDictionary annot))
                {
                    continue;
                }

                var number = item is PdfSourceReference r ? r.Number : -1;
                if (number > 0 && _removedAnnotations != null && _removedAnnotations.Contains(number))
                {
                    continue; // removed by redaction
                }

                if (number > 0 && _formWidgetNumbers.Contains(number))
                {
                    continue; // form widget: rewritten by FormWriter with the current value
                }

                var clean = new PdfDictionary();
                foreach (var e in annot.Entries)
                {
                    if (e.Key != "AA" && e.Key != "P" && e.Key != "StructParent" && e.Key != "A" && e.Key != "Dest")
                    {
                        clean.Set(e.Key, e.Value);
                    }
                }

                var action = parser.Resolve(annot.Get("A")) as PdfDictionary;
                var actionType = action?.Get("S") is PdfName s ? s.Value : null;
                if (annot.Get("Dest") != null || actionType == "GoTo")
                {
                    // Internal link: keep it only if its target page is saved too; names become explicit.
                    var dest = sp.Document.DestinationOf(annot);
                    if (dest == null || !(dest.Items[0] is PdfSourceReference target) || !_sourcePages.ContainsKey((parser, target.Number)))
                    {
                        continue;
                    }

                    clean.Set("Dest", dest);
                }
                else if (action != null)
                {
                    if (actionType == null || UnsafeActions.Contains(actionType))
                    {
                        if (annot.Get("Subtype") is PdfName st && st.Value == "Link")
                        {
                            continue; // a link that only runs a script is dropped entirely
                        }
                    }
                    else if (action.Get("Next") != null)
                    {
                        // Chained actions could hide a script: keep only the first one.
                        var single = new PdfDictionary();
                        foreach (var e in action.Entries)
                        {
                            if (e.Key != "Next")
                            {
                                single.Set(e.Key, e.Value);
                            }
                        }

                        clean.Set("A", single);
                    }
                    else
                    {
                        clean.Set("A", annot.Get("A")!);
                    }
                }

                var copied = _copier.CopyReplaced(parser, number, clean);
                result.Items.Add(copied);
            }

            return result;
        }

        /// <summary>An imported page as a form XObject (§8.10): its content streams joined, its resources copied.</summary>
        public static PdfIndirectObject WriteForm(SourcePage sp, Func<PdfIndirectObject> newObject, ObjectCopier copier, PdfSaveOptions options)
        {
            var parser = sp.Parser;
            using (var content = new MemoryStream())
            {
                foreach (var part in ContentParts(sp))
                {
                    var stream = (PdfStream)parser.Resolve(part);
                    if (!StreamDecoder.CanDecode(stream, parser))
                    {
                        throw new PdfReadException("A content stream of the page uses a filter LifeOS.Pdf cannot decode, so the page cannot be drawn as a form.");
                    }

                    var data = StreamDecoder.Decode(stream, parser, parser.Options.MaxStreamSize);
                    if (content.Length + data.Length + 1 > parser.Options.MaxStreamSize)
                    {
                        throw new PdfLimitException("The page content is larger than PdfReadOptions.MaxStreamSize.");
                    }

                    content.Write(data, 0, data.Length);
                    content.WriteByte((byte)'\n'); // streams are split at token boundaries (§7.8.2)
                }

                var bbox = sp.CropBox ?? sp.MediaBox;
                var dict = new PdfDictionary()
                    .Set("Type", "XObject")
                    .Set("Subtype", "Form")
                    .Set("BBox", PdfArray.OfNumbers(bbox[0], bbox[1], bbox[2], bbox[3]));
                var res = sp.Resources == null ? null : copier.Copy(parser, sp.Resources);
                dict.Set("Resources", res == null || res is PdfNull ? new PdfDictionary() : res);
                if (sp.Dictionary.Get("Group") != null)
                {
                    var group = copier.Copy(parser, sp.Dictionary.Get("Group"));
                    if (!(group is PdfNull))
                    {
                        dict.Set("Group", group);
                    }
                }

                var bytes = content.ToArray();
                if (options.CompressContent && bytes.Length > 0)
                {
                    bytes = Zlib.Compress(bytes, options.CompressionLevel);
                    dict.Set("Filter", "FlateDecode");
                }

                var obj = newObject();
                obj.Value = new PdfStream(dict, bytes);
                return obj;
            }
        }
    }
}
