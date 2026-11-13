using System;
using System.IO;
using LifeOS.Pdf.Extraction.Engine;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction
{
    /// <summary>An image drawn on a page. The data is decoded only when asked for.</summary>
    public sealed class ExtractedImage
    {
        private readonly PdfStream _stream;
        private readonly PdfParser? _parser;
        private byte[]? _bytes;

        internal ExtractedImage(ImageInfo info, PdfRect bounds, PdfParser? parser)
        {
            _stream = ImageDecoder.StreamOf(info);
            _parser = parser;
            Name = info.Name ?? "inline";
            Bounds = bounds;
            IsInline = info.Stream == null;
            PixelWidth = (int)ContentInterpreter.NumOf(R(_stream.Dictionary.Get("Width")));
            PixelHeight = (int)ContentInterpreter.NumOf(R(_stream.Dictionary.Get("Height")));
            Format = ImageDecoder.Format(_stream, parser);
        }

        /// <summary>Resource name (e.g. "Im1"), or "inline".</summary>
        public string Name { get; }

        public bool IsInline { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        /// <summary>Where the image is drawn on the page as displayed (top-left origin).</summary>
        public PdfRect Bounds { get; }

        public ExtractedImageFormat Format { get; }

        /// <summary>".png", ".jpg", ".jp2", ".jb2", ".ccitt" or ".bin".</summary>
        public string FileExtension
        {
            get
            {
                switch (Format)
                {
                    case ExtractedImageFormat.Png: return ".png";
                    case ExtractedImageFormat.Jpeg: return ".jpg";
                    case ExtractedImageFormat.Jpeg2000: return ".jp2";
                    case ExtractedImageFormat.Jbig2: return ".jb2";
                    case ExtractedImageFormat.Ccitt: return ".ccitt";
                    default: return ".bin";
                }
            }
        }

        /// <summary>File bytes in <see cref="Format"/>. Throws <see cref="PdfReadException"/> for unsupported images.</summary>
        public byte[] GetBytes() => (byte[])(_bytes ?? (_bytes = ImageDecoder.Bytes(_stream, Format, _parser, _parser?.Options.MaxStreamSize ?? 256L * 1024 * 1024))).Clone();

        /// <summary>Writes the image; when <paramref name="path"/> has no extension, <see cref="FileExtension"/> is added. Returns the path written.</summary>
        public string Save(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (string.IsNullOrEmpty(Path.GetExtension(path)))
            {
                path += FileExtension;
            }

            File.WriteAllBytes(path, GetBytes());
            return path;
        }

        private PdfObject? R(PdfObject? o) => o is PdfSourceReference && _parser != null ? _parser.Resolve(o) : o;

        public override string ToString() => Name + " " + PixelWidth + "×" + PixelHeight + " " + Format;
    }
}
