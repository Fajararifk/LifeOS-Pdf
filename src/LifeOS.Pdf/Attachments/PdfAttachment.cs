using System;

namespace LifeOS.Pdf.Attachments
{
    /// <summary>A file embedded in the PDF, listed in the viewer's attachments panel (ISO 32000-1 §7.11.4).</summary>
    public sealed class PdfAttachment
    {
        private readonly byte[] _data;

        internal PdfAttachment(string name, byte[] data, string? mimeType, string? description)
        {
            Name = name;
            _data = (byte[])data.Clone();
            MimeType = mimeType;
            Description = description;
        }

        /// <summary>File name shown to the user (any Unicode text).</summary>
        public string Name { get; }

        /// <summary>MIME type, e.g. application/json; null if unknown.</summary>
        public string? MimeType { get; }

        public string? Description { get; }

        /// <summary>Optional modification date stored with the file. Left empty by default so output stays deterministic.</summary>
        public DateTimeOffset? ModificationDate { get; set; }

        public int Size => _data.Length;

        /// <summary>A copy of the file content.</summary>
        public byte[] GetData() => (byte[])_data.Clone();

        internal byte[] Data => _data;
    }
}
