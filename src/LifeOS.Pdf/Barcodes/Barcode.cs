using System;
using LifeOS.Pdf.Barcodes.Linear;
using LifeOS.Pdf.Drawing;

namespace LifeOS.Pdf.Barcodes
{
    /// <summary>
    /// A linear (1D) barcode, encoded from scratch. Draw it with
    /// <see cref="PdfGraphics.DrawBarcode"/> or export it as PNG/SVG.
    /// <code>
    /// var code = Barcode.Code128("INV-2026-0042");
    /// page.Graphics.DrawBarcode(code, 72, 700, 200, 50);
    /// var ean = Barcode.Ean13("899123456789");   // check digit added: 8991234567895
    /// </code>
    /// </summary>
    public sealed class Barcode
    {
        private readonly bool[] _modules;

        private Barcode(BarcodeSymbology symbology, string text, bool[] modules)
        {
            Symbology = symbology;
            Text = text;
            _modules = modules;
        }

        public BarcodeSymbology Symbology { get; }

        /// <summary>Human-readable text printed under the bars (including any check digit).</summary>
        public string Text { get; }

        /// <summary>Number of modules (narrowest bar widths), without quiet zones.</summary>
        public int ModuleCount => _modules.Length;

        /// <summary>Recommended quiet zone on each side, in modules (ISO/IEC specification minimums).</summary>
        public int QuietZone => Symbology == BarcodeSymbology.Ean8 ? 7 : Symbology == BarcodeSymbology.Ean13 || Symbology == BarcodeSymbology.UpcA ? 11 : 10;

        /// <summary>True when module <paramref name="index"/> is a bar.</summary>
        public bool IsBar(int index)
        {
            if (index < 0 || index >= _modules.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Module index must be between 0 and " + (_modules.Length - 1) + ".");
            }

            return _modules[index];
        }

        /// <summary>Code 128 for any ASCII text; sets A/B/C are chosen automatically (digit runs use the compact set C).</summary>
        public static Barcode Code128(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return new Barcode(BarcodeSymbology.Code128, text, LinearEncoders.Code128(text));
        }

        /// <summary>Code 39 (upper-case letters, digits, space and - . $ / + %), optionally with a mod-43 check character.</summary>
        public static Barcode Code39(string text, bool checkDigit = false)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (text.Length == 0)
            {
                throw new ArgumentException("Code 39 needs at least one character.", nameof(text));
            }

            var modules = LinearEncoders.Code39(text, checkDigit, out var encoded);
            return new Barcode(BarcodeSymbology.Code39, encoded, modules);
        }

        /// <summary>EAN-13 from 12 digits (check digit calculated) or 13 digits (check digit verified).</summary>
        public static Barcode Ean13(string digits)
        {
            var full = LinearEncoders.WithCheckDigit(digits, 13, "EAN-13");
            return new Barcode(BarcodeSymbology.Ean13, full, LinearEncoders.Ean13(full));
        }

        /// <summary>EAN-8 from 7 digits (check digit calculated) or 8 digits (verified).</summary>
        public static Barcode Ean8(string digits)
        {
            var full = LinearEncoders.WithCheckDigit(digits, 8, "EAN-8");
            return new Barcode(BarcodeSymbology.Ean8, full, LinearEncoders.Ean8(full));
        }

        /// <summary>UPC-A from 11 digits (check digit calculated) or 12 digits (verified). Encoded as EAN-13 with a leading 0.</summary>
        public static Barcode UpcA(string digits)
        {
            var full = LinearEncoders.WithCheckDigit(digits, 12, "UPC-A");
            return new Barcode(BarcodeSymbology.UpcA, full, LinearEncoders.Ean13("0" + full));
        }

        /// <summary>Interleaved 2 of 5 for an even number of digits (no check digit).</summary>
        public static Barcode Itf(string digits)
        {
            if (digits == null)
            {
                throw new ArgumentNullException(nameof(digits));
            }

            return new Barcode(BarcodeSymbology.Itf, digits, LinearEncoders.Itf(digits));
        }

        /// <summary>ITF-14 (GTIN-14) from 13 digits (check digit calculated) or 14 digits (verified).</summary>
        public static Barcode Itf14(string digits)
        {
            var full = LinearEncoders.WithCheckDigit(digits, 14, "ITF-14");
            return new Barcode(BarcodeSymbology.Itf14, full, LinearEncoders.Itf(full));
        }

        /// <summary>
        /// Creates a barcode by symbology name: code128, code39, ean13, ean8, upca, itf, itf14
        /// (case-insensitive; used by templates).
        /// </summary>
        public static Barcode Create(string symbology, string value)
        {
            switch ((symbology ?? string.Empty).Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty))
            {
                case "code128": return Code128(value);
                case "code39": return Code39(value);
                case "ean13": return Ean13(value);
                case "ean8": return Ean8(value);
                case "upca": return UpcA(value);
                case "itf": return Itf(value);
                case "itf14": return Itf14(value);
                default: throw new ArgumentException("Unknown barcode type '" + symbology + "'. Use code128, code39, ean13, ean8, upca, itf or itf14.", nameof(symbology));
            }
        }

        /// <summary>PNG of the bars only (no text): <paramref name="moduleWidth"/> pixels per module, <paramref name="height"/> pixels tall.</summary>
        public byte[] ToPng(int moduleWidth = 2, int height = 60, int? quietZone = null, PdfColor? dark = null, PdfColor? light = null) =>
            ModuleRaster.ToPng(_modules.Length, 1, (x, y) => _modules[x], moduleWidth, height, quietZone ?? QuietZone, dark ?? PdfColor.Black, light ?? PdfColor.White);

        /// <summary>SVG of the bars only (no text).</summary>
        public string ToSvg(double moduleWidth = 2, double height = 60, int? quietZone = null, PdfColor? dark = null, PdfColor? light = null) =>
            ModuleRaster.ToSvg(_modules.Length, 1, (x, y) => _modules[x], moduleWidth, height, quietZone ?? QuietZone, dark ?? PdfColor.Black, light ?? PdfColor.White);

        public override string ToString() => Symbology + " " + Text;
    }
}
