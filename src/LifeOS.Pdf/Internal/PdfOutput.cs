using System.IO;
using System.Text;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Byte-oriented writer that tracks the absolute position, which the
    /// cross-reference table needs. All PDF syntax is written as ASCII.
    /// </summary>
    internal sealed class PdfOutput
    {
        private readonly Stream _stream;

        public PdfOutput(Stream stream)
        {
            _stream = stream;
        }

        public long Position { get; private set; }

        public void Write(string ascii)
        {
            var bytes = Encoding.ASCII.GetBytes(ascii);
            Write(bytes);
        }

        public void Write(byte[] bytes)
        {
            _stream.Write(bytes, 0, bytes.Length);
            Position += bytes.Length;
        }

        public void WriteByte(byte value)
        {
            _stream.WriteByte(value);
            Position++;
        }
    }
}
