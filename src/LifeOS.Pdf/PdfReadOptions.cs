using System;

namespace LifeOS.Pdf
{
    /// <summary>
    /// Limits applied while reading a PDF. Files from outside are untrusted: these limits keep
    /// a damaged or malicious file from using unbounded memory or time. The defaults suit
    /// ordinary business documents; raise them only for files you trust.
    /// </summary>
    public sealed class PdfReadOptions
    {
        private int _maxDepth = 100;
        private long _maxStreamSize = 256L * 1024 * 1024;
        private int _maxObjects = 8388607;
        private int _maxPages = 100000;

        /// <summary>Deepest nesting of arrays and dictionaries inside one object.</summary>
        public int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value >= 8 ? value : throw new ArgumentOutOfRangeException(nameof(MaxDepth), value, "MaxDepth must be at least 8.");
        }

        /// <summary>Largest size of a stream after decompression, in bytes (default 256 MB).</summary>
        public long MaxStreamSize
        {
            get => _maxStreamSize;
            set => _maxStreamSize = value >= 1024 && value <= int.MaxValue ? value : throw new ArgumentOutOfRangeException(nameof(MaxStreamSize), value, "MaxStreamSize must be between 1 KB and 2 GB.");
        }

        /// <summary>Highest object number accepted (default 8 388 607, the ISO 32000 Annex C limit).</summary>
        public int MaxObjects
        {
            get => _maxObjects;
            set => _maxObjects = value >= 16 ? value : throw new ArgumentOutOfRangeException(nameof(MaxObjects), value, "MaxObjects must be at least 16.");
        }

        /// <summary>Most pages a document may have (default 100 000).</summary>
        public int MaxPages
        {
            get => _maxPages;
            set => _maxPages = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(MaxPages), value, "MaxPages must be at least 1.");
        }

        /// <summary>Password used to open an encrypted PDF (§7.6).</summary>
        public string? Password { get; set; }

        internal const int MaxReferenceChain = 64;
        internal const int MaxXrefSections = 256;
        internal const int MaxTreeDepth = 64;
        internal const int MaxOutlineItems = 100000;
    }
}
