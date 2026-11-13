using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LifeOS.Pdf.Attachments
{
    /// <summary>Files embedded in a document. Names must be unique.</summary>
    public sealed class PdfAttachmentCollection : IReadOnlyList<PdfAttachment>
    {
        private readonly List<PdfAttachment> _items = new List<PdfAttachment>();

        public int Count => _items.Count;

        public PdfAttachment this[int index] => _items[index];

        /// <summary>Embeds <paramref name="data"/> under <paramref name="name"/>.</summary>
        /// <param name="name">File name shown in the viewer, e.g. "transaksi.json".</param>
        /// <param name="data">File content.</param>
        /// <param name="mimeType">MIME type such as application/json (optional).</param>
        /// <param name="description">Short description shown by some viewers (optional).</param>
        public PdfAttachment Add(string name, byte[] data, string? mimeType = null, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An attachment needs a file name.", nameof(name));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (mimeType != null && (mimeType.IndexOf('/') <= 0 || mimeType.IndexOf(' ') >= 0))
            {
                throw new ArgumentException("'" + mimeType + "' is not a MIME type (expected type/subtype, e.g. application/json).", nameof(mimeType));
            }

            foreach (var existing in _items)
            {
                if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    throw new ArgumentException("An attachment named '" + name + "' already exists. Attachment names must be unique.", nameof(name));
                }
            }

            var attachment = new PdfAttachment(name, data, mimeType, description);
            _items.Add(attachment);
            return attachment;
        }

        /// <summary>Embeds a file from disk under its own file name.</summary>
        public PdfAttachment AddFile(string path, string? mimeType = null, string? description = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return Add(Path.GetFileName(path), File.ReadAllBytes(path), mimeType, description);
        }

        internal void CopyTo(PdfAttachmentCollection target)
        {
            foreach (var item in _items)
            {
                if (target._items.Exists(t => string.Equals(t.Name, item.Name, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("An attachment named '" + item.Name + "' already exists.");
                }

                target._items.Add(item);
            }
        }

        public IEnumerator<PdfAttachment> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
