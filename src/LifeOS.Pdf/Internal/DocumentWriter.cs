using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Fonts.Embedding;
using LifeOS.Pdf.Fonts.Standard;
using LifeOS.Pdf.Fonts.TrueType;
using LifeOS.Pdf.Forms.Internal;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Serializes a <see cref="PdfDocument"/> into a complete PDF 1.7 file:
    /// header, indirect objects, classic cross-reference table and trailer
    /// (ISO 32000-1 §7.5). Output is deterministic for the same input.
    /// </summary>
    internal static class DocumentWriter
    {
        // '%' + four bytes above 127 tells transfer tools the file is binary (§7.5.2).
        private static readonly byte[] Header = { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7', (byte)'\n', (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' };

        public static byte[] Write(PdfDocument document, PdfSaveOptions options)
        {
            var objects = new List<PdfIndirectObject>();
            PdfIndirectObject New()
            {
                var o = new PdfIndirectObject(objects.Count + 1);
                objects.Add(o);
                return o;
            }

            for (var i = 0; i < document.Pages.Count; i++)
            {
                if (document.Pages[i].Graphics.Depth != 0)
                {
                    throw new InvalidOperationException("Page " + (i + 1) + " has " + document.Pages[i].Graphics.Depth +
                        " graphics state(s) still open. Call Restore() or dispose the object returned by Save() before saving.");
                }
            }

            var catalog = New();
            var pagesRoot = New();

            // Shared resources: one object per font / image / state for the whole document.
            // Form field fonts are registered first so embedded fonts subset the right glyphs.
            document.Form.RegisterFonts();
            var fontObjects = new Dictionary<PdfFontFace, PdfIndirectObject>();
            foreach (var face in document.Resources.Fonts)
            {
                var obj = New();
                fontObjects[face] = obj;
                if (face.Program is StandardFontProgram standard)
                {
                    var dict = new PdfDictionary()
                        .Set("Type", "Font")
                        .Set("Subtype", "Type1")
                        .Set("BaseFont", standard.Data.BaseFont);
                    if (!standard.Data.UsesBuiltInEncoding)
                    {
                        dict.Set("Encoding", "WinAnsiEncoding");
                    }

                    obj.Value = dict;
                }
                else
                {
                    EmbeddedFontWriter.Write(obj, (TrueTypeFontProgram)face.Program, document.Resources.FontUsages[face], New);
                }
            }

            var imageObjects = new Dictionary<Images.PdfImage, PdfIndirectObject>();
            foreach (var image in document.Resources.Images)
            {
                var obj = New();
                var softMask = image.ToSoftMaskStream();
                PdfReference? softMaskRef = null;
                if (softMask != null)
                {
                    var maskObj = New();
                    maskObj.Value = softMask;
                    softMaskRef = maskObj.Reference;
                }

                obj.Value = image.ToPdfStream(softMaskRef);
                imageObjects[image] = obj;
            }

            var stateObjects = new Dictionary<string, PdfIndirectObject>(StringComparer.Ordinal);
            foreach (var state in document.Resources.GraphicsStates)
            {
                var obj = New();
                obj.Value = new PdfDictionary()
                    .Set("Type", "ExtGState")
                    .Set("CA", new PdfReal(state.Value.Stroke))
                    .Set("ca", new PdfReal(state.Value.Fill));
                stateObjects[state.Key] = obj;
            }

            var kids = new PdfArray();
            var pageObjects = new Dictionary<PdfPage, PdfIndirectObject>();
            PdfIndirectObject PageObject(PdfPage p)
            {
                // Allocated on first use: links and copied annotations may point to later pages.
                if (!pageObjects.TryGetValue(p, out var o))
                {
                    o = New();
                    pageObjects[p] = o;
                }

                return o;
            }

            // Source page (file, object number) → the page that represents it in this document.
            var sourcePages = new Dictionary<(PdfParser, int), PdfPage>();
            foreach (var page in document.Pages)
            {
                if (page.Source != null && page.Source.Number > 0 && !sourcePages.ContainsKey((page.Source.Parser, page.Source.Number)))
                {
                    sourcePages[(page.Source.Parser, page.Source.Number)] = page;
                }
            }

            var copier = new ObjectCopier(New, (parser, number) => sourcePages.TryGetValue((parser, number), out var p) ? PageObject(p) : null, options.CompressContent, options.CompressionLevel);
            var formObjects = new Dictionary<SourcePage, PdfIndirectObject>();
            foreach (var form in document.Resources.Forms)
            {
                formObjects[form] = ImportedPageWriter.WriteForm(form, New, copier, options);
            }

            copier.Drain();
            var resourceObjects = new PageResourceObjects(fontObjects, imageObjects, stateObjects, formObjects);
            var imported = new ImportedPageWriter(New, copier, sourcePages, pageObjects, options);
            foreach (var page in document.Pages)
            {
                var pageObj = PageObject(page);
                kids.Items.Add(pageObj.Reference);
                if (page.Source != null)
                {
                    pageObj.Value = imported.Write(page, pagesRoot, resourceObjects);
                    copier.Drain();
                    continue;
                }

                var contentObj = New();
                contentObj.Value = ContentStream(page.Graphics.AllContentBytes(), options);
                var dict = new PdfDictionary()
                    .Set("Type", "Page")
                    .Set("Parent", pagesRoot.Reference)
                    .Set("MediaBox", PdfArray.OfNumbers(0, 0, page.Width, page.Height))
                    .Set("Resources", resourceObjects.For(page))
                    .Set("Contents", contentObj.Reference);
                if (page.Rotation != 0)
                {
                    dict.Set("Rotate", page.Rotation);
                }

                pageObj.Value = dict;
            }

            // Interactive features come after the pages so destinations can point to any page.
            var interactive = new InteractiveWriter(document, pageObjects, New, fontObjects);
            foreach (var page in document.Pages)
            {
                var annots = interactive.Annotations(page);
                if (annots != null)
                {
                    var pageDict = (PdfDictionary)pageObjects[page].Value!;
                    if (pageDict.Get("Annots") is PdfArray existing)
                    {
                        existing.Items.AddRange(annots.Items);
                    }
                    else
                    {
                        pageDict.Set("Annots", annots);
                    }
                }
            }

            pagesRoot.Value = new PdfDictionary()
                .Set("Type", "Pages")
                .Set("Kids", kids)
                .Set("Count", document.Pages.Count);

            var catalogDict = new PdfDictionary()
                .Set("Type", "Catalog")
                .Set("Pages", pagesRoot.Reference);
            interactive.Complete(catalogDict);

            var formWriter = new FormWriter(document, New, fontObjects, copier);
            formWriter.Write(catalogDict, pageObjects);

            catalog.Value = catalogDict;

            var info = New();
            info.Value = BuildInfo(document.Info);

            if (options.Encryption != null)
            {
                return Encrypt(options, objects, catalog, info, New);
            }

            return options.UseObjectStreams ? CompactSerializer.Serialize(objects, catalog, info, Header) : Serialize(objects, catalog, info, null, null, null);
        }

        internal static PdfStream ContentStream(byte[] content, PdfSaveOptions options)
        {
            var dict = new PdfDictionary();
            if (options.CompressContent && content.Length > 0)
            {
                content = Zlib.Compress(content, options.CompressionLevel);
                dict.Set("Filter", "FlateDecode");
            }

            return new PdfStream(dict, content);
        }

        private static PdfDictionary BuildInfo(PdfDocumentInfo info)
        {
            var dict = new PdfDictionary();
            void Text(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    dict.Set(key, PdfString.Text(value!));
                }
            }

            Text("Title", info.Title);
            Text("Author", info.Author);
            Text("Subject", info.Subject);
            Text("Keywords", info.Keywords);
            Text("Creator", info.Creator);
            Text("Producer", info.Producer);
            dict.Set("CreationDate", PdfString.Text(PdfFormat.Date(info.CreationDate ?? DateTimeOffset.Now)));
            if (info.ModificationDate.HasValue)
            {
                dict.Set("ModDate", PdfString.Text(PdfFormat.Date(info.ModificationDate.Value)));
            }

            return dict;
        }

        private static byte[] Serialize(List<PdfIndirectObject> objects, PdfIndirectObject catalog, PdfIndirectObject info)
        {
            using (var ms = new MemoryStream())
            {
                var output = new PdfOutput(ms);
                output.Write(Header);

                var offsets = new long[objects.Count];
                for (var i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    if (obj.Value == null)
                    {
                        throw new InvalidOperationException("Internal error: object " + obj.Number + " has no value.");
                    }

                    offsets[i] = output.Position;
                    output.WriteInteger(obj.Number);
                    output.Write(" 0 obj\n");
                    obj.Value.WriteTo(output);
                    output.Write("\nendobj\n");
                }

                var xrefOffset = output.Position;
                var xref = new StringBuilder();
                xref.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
                // Each entry is exactly 20 bytes: 10-digit offset, space, 5-digit generation, space, type, space, LF.
                xref.Append("0000000000 65535 f \n");
                foreach (var offset in offsets)
                {
                    xref.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                }

                output.Write(xref.ToString());

                // File identifier derived from the content: same input → same ID.
                byte[] id;
                using (var md5 = MD5.Create())
                {
                    output.Flush();
                    id = md5.ComputeHash(ms.GetBuffer(), 0, (int)ms.Length);
                }

                var trailer = new PdfDictionary()
                    .Set("Size", objects.Count + 1)
                    .Set("Root", catalog.Reference)
                    .Set("Info", info.Reference)
                    .Set("ID", new PdfArray(PdfString.Hex(id), PdfString.Hex(id)));

                output.Write("trailer\n");
                trailer.WriteTo(output);
                output.Write("\nstartxref\n" + xrefOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
                output.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] Encrypt(PdfSaveOptions options, List<PdfIndirectObject> objects, PdfIndirectObject catalog, PdfIndirectObject info, Func<PdfIndirectObject> newObject)
        {
            // The file key is derived from /ID[0], which is itself derived from the content; compute it
            // on the unencrypted document first, then serialize again with encryption.
            var id = MD5.Create().ComputeHash(Serialize(objects, catalog, info));
            var handler = SecurityHandler.Create(options.Encryption!, id);

            var encryptObject = newObject();
            encryptObject.Value = BuildEncryptDict(handler);

            return Serialize(objects, catalog, info, encryptObject, handler, id);
        }

        private static byte[] Serialize(List<PdfIndirectObject> objects, PdfIndirectObject catalog, PdfIndirectObject info, PdfIndirectObject? encryptObject, SecurityHandler? encryption, byte[]? fixedId)
        {
            using (var ms = new MemoryStream())
            {
                var output = new PdfOutput(ms);
                output.Write(Header);

                var offsets = new long[objects.Count];
                for (var i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    if (obj.Value == null)
                    {
                        throw new InvalidOperationException("Internal error: object " + obj.Number + " has no value.");
                    }

                    output.SetEncryption(encryption, obj.Number, encryption != null && !ReferenceEquals(obj, encryptObject));
                    offsets[i] = output.Position;
                    output.WriteInteger(obj.Number);
                    output.Write(" 0 obj\n");
                    obj.Value.WriteTo(output);
                    output.Write("\nendobj\n");
                }

                output.SetEncryption(null, 0, false);

                var xrefOffset = output.Position;
                var xref = new StringBuilder();
                xref.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
                xref.Append("0000000000 65535 f \n");
                foreach (var offset in offsets)
                {
                    xref.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                }

                output.Write(xref.ToString());

                byte[] id = fixedId ?? ComputeId(ms, output);

                var trailer = new PdfDictionary()
                    .Set("Size", objects.Count + 1)
                    .Set("Root", catalog.Reference)
                    .Set("Info", info.Reference);
                if (encryptObject != null)
                {
                    trailer.Set("Encrypt", encryptObject.Reference);
                }

                trailer.Set("ID", new PdfArray(PdfString.Hex(id), PdfString.Hex(id)));

                output.Write("trailer\n");
                trailer.WriteTo(output);
                output.Write("\nstartxref\n" + xrefOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
                output.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] ComputeId(MemoryStream ms, PdfOutput output)
        {
            using (var md5 = MD5.Create())
            {
                output.Flush();
                return md5.ComputeHash(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        private static PdfDictionary BuildEncryptDict(SecurityHandler handler)
        {
            var dict = new PdfDictionary()
                .Set("Filter", "Standard")
                .Set("V", handler.Version)
                .Set("R", handler.Revision)
                .Set("Length", handler.KeyLength)
                .Set("P", (long)handler.PermissionsValue)
                .Set("O", PdfString.Hex(handler.O!))
                .Set("U", PdfString.Hex(handler.U!));
            if (handler.Version >= 4)
            {
                var name = handler.Filter;
                dict.Set("StmF", name).Set("StrF", name);
                dict.Set("CF", new PdfDictionary().Set(name, new PdfDictionary()
                    .Set("CFM", name)
                    .Set("Length", handler.KeyLength / 8)));
            }

            if (!handler.EncryptMetadata)
            {
                dict.Set("EncryptMetadata", PdfBoolean.False);
            }

            return dict;
        }
    }
}
