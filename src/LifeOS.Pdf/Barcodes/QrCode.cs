using System;
using LifeOS.Pdf.Barcodes.Qr;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Barcodes
{
    /// <summary>
    /// A QR code (Model 2, ISO/IEC 18004), encoded from scratch. Draw it on a page with
    /// <see cref="PdfGraphics.DrawQrCode"/> (vector, sharp at any zoom) or export it as PNG/SVG.
    /// <code>
    /// var qr = QrCode.Encode("https://lifeos.app/inv/42");
    /// page.Graphics.DrawQrCode(qr, 400, 60, 100);
    /// File.WriteAllBytes("qr.png", qr.ToPng(moduleSize: 8));
    /// </code>
    /// </summary>
    public sealed class QrCode
    {
        private readonly QrMatrix _matrix;

        private QrCode(QrMatrix matrix, string text)
        {
            _matrix = matrix;
            Text = text;
        }

        internal QrMatrix Matrix => _matrix;

        /// <summary>The encoded text.</summary>
        public string Text { get; }

        /// <summary>Version 1–40 (size grows by 4 modules per version).</summary>
        public int Version => _matrix.Version;

        public QrErrorCorrection ErrorCorrection => _matrix.ErrorCorrection;

        /// <summary>Mask pattern 0–7 that was applied.</summary>
        public int Mask => _matrix.Mask;

        /// <summary>Width and height in modules, without the quiet zone (21 for version 1).</summary>
        public int Size => _matrix.Size;

        /// <summary>
        /// Encodes text: digits only → numeric mode, upper-case letters/digits/" $%*+-./:" →
        /// alphanumeric mode, anything else → bytes (UTF-8). The smallest version that fits is used.
        /// </summary>
        /// <param name="text">Text to encode.</param>
        /// <param name="errorCorrection">Error correction level. Use High when drawing a logo over the code.</param>
        /// <param name="minVersion">Smallest allowed version (1–40).</param>
        /// <param name="maxVersion">Largest allowed version (1–40).</param>
        /// <param name="mask">Mask 0–7, or -1 to choose the best one automatically.</param>
        public static QrCode Encode(string text, QrErrorCorrection errorCorrection = QrErrorCorrection.Medium, int minVersion = 1, int maxVersion = 40, int mask = -1)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return new QrCode(QrEncoder.Encode(text, errorCorrection, minVersion, maxVersion, mask), text);
        }

        /// <summary>Encodes raw bytes in byte mode.</summary>
        public static QrCode EncodeBytes(byte[] data, QrErrorCorrection errorCorrection = QrErrorCorrection.Medium, int minVersion = 1, int maxVersion = 40, int mask = -1)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return new QrCode(QrEncoder.EncodeBytes(data, errorCorrection, minVersion, maxVersion, mask), string.Empty);
        }

        /// <summary>True for a dark module. Coordinates are 0 … Size−1, origin top-left.</summary>
        public bool IsDark(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size)
            {
                throw new ArgumentOutOfRangeException(x < 0 || x >= Size ? nameof(x) : nameof(y), "Module coordinates must be between 0 and " + (Size - 1) + ".");
            }

            return _matrix.IsDark(x, y);
        }

        /// <summary>PNG image; each module is <paramref name="moduleSize"/> pixels, with a light quiet zone (4 modules is the standard).</summary>
        public byte[] ToPng(int moduleSize = 8, int quietZone = 4, PdfColor? dark = null, PdfColor? light = null) =>
            ModuleRaster.ToPng(Size, Size, _matrix.IsDark, moduleSize, moduleSize, quietZone, dark ?? PdfColor.Black, light ?? PdfColor.White);

        /// <summary>SVG document (one path); <paramref name="light"/> null = transparent background.</summary>
        public string ToSvg(double moduleSize = 4, int quietZone = 4, PdfColor? dark = null, PdfColor? light = null) =>
            ModuleRaster.ToSvg(Size, Size, _matrix.IsDark, moduleSize, moduleSize, quietZone, dark ?? PdfColor.Black, light ?? PdfColor.White);

        public override string ToString() => "QR version " + Version + "-" + ErrorCorrection + " (" + Size + "×" + Size + ")";
    }
}
