using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Objects;

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

            var catalog = New();
            var pagesRoot = New();

            // Shared resources: one object per font / image for the whole document.
            var fontObjects = new Dictionary<StandardFont, PdfIndirectObject>();
            foreach (var font in document.Resources.Fonts)
            {
                var metrics = StandardFontMetrics.For(font);
                var dict = new PdfDictionary()
                    .Set("Type", "Font")
                    .Set("Subtype", "Type1")
                    .Set("BaseFont", metrics.BaseFont);
                if (!metrics.UsesBuiltInEncoding)
                {
                    dict.Set("Encoding", "WinAnsiEncoding");
                }

                var obj = New();
                obj.Value = dict;
                fontObjects[font] = obj;
            }

            var imageObjects = new Dictionary<Images.PdfImage, PdfIndirectObject>();
            foreach (var image in document.Resources.Images)
            {
                var obj = New();
                obj.Value = image.ToPdfStream();
                imageObjects[image] = obj;
            }

            var kids = new PdfArray();
            foreach (var page in document.Pages)
            {
                var pageObj = New();
                var contentObj = New();

                var content = Encoding.ASCII.GetBytes(page.Graphics.Content);
                var contentDict = new PdfDictionary();
                if (options.CompressContent && content.Length > 0)
                {
                    content = Zlib.Compress(content);
                    contentDict.Set("Filter", "FlateDecode");
                }

                contentObj.Value = new PdfStream(contentDict, content);

                var resources = new PdfDictionary();
                if (page.Fonts.Count > 0)
                {
                    var fonts = new PdfDictionary();
                    foreach (var entry in page.Fonts)
                    {
                        fonts.Set(entry.Key, fontObjects[entry.Value].Reference);
                    }

                    resources.Set("Font", fonts);
                }

                if (page.Images.Count > 0)
                {
                    var xobjects = new PdfDictionary();
                    foreach (var entry in page.Images)
                    {
                        xobjects.Set(entry.Key, imageObjects[entry.Value].Reference);
                    }

                    resources.Set("XObject", xobjects);
                }

                pageObj.Value = new PdfDictionary()
                    .Set("Type", "Page")
                    .Set("Parent", pagesRoot.Reference)
                    .Set("MediaBox", PdfArray.OfNumbers(0, 0, page.Width, page.Height))
                    .Set("Resources", resources)
                    .Set("Contents", contentObj.Reference);
                kids.Items.Add(pageObj.Reference);
            }

            pagesRoot.Value = new PdfDictionary()
                .Set("Type", "Pages")
                .Set("Kids", kids)
                .Set("Count", document.Pages.Count);

            var catalogDict = new PdfDictionary()
                .Set("Type", "Catalog")
                .Set("Pages", pagesRoot.Reference);
            if (!string.IsNullOrEmpty(document.Info.Title))
            {
                // Show the title, not the file name, in the viewer's title bar.
                catalogDict.Set("ViewerPreferences", new PdfDictionary().Set("DisplayDocTitle", PdfBoolean.True));
            }

            catalog.Value = catalogDict;

            var info = New();
            info.Value = BuildInfo(document.Info);

            return Serialize(objects, catalog, info);
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
                    output.Write(obj.Number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
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
                    id = md5.ComputeHash(ms.ToArray());
                }

                var trailer = new PdfDictionary()
                    .Set("Size", objects.Count + 1)
                    .Set("Root", catalog.Reference)
                    .Set("Info", info.Reference)
                    .Set("ID", new PdfArray(PdfString.Hex(id), PdfString.Hex(id)));

                output.Write("trailer\n");
                trailer.WriteTo(output);
                output.Write("\nstartxref\n" + xrefOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
                return ms.ToArray();
            }
        }
    }
}
