using System;
using System.Globalization;

namespace LifeOS.Pdf.Drawing
{
    /// <summary>An RGB color (0–255 per channel).</summary>
    public readonly struct PdfColor : IEquatable<PdfColor>
    {
        public PdfColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public static PdfColor Black => new PdfColor(0, 0, 0);

        public static PdfColor White => new PdfColor(255, 255, 255);

        public static PdfColor Gray => new PdfColor(128, 128, 128);

        public static PdfColor LightGray => new PdfColor(211, 211, 211);

        public static PdfColor Red => new PdfColor(220, 38, 38);

        public static PdfColor Green => new PdfColor(22, 163, 74);

        public static PdfColor Blue => new PdfColor(37, 99, 235);

        /// <summary>Parses "#RRGGBB", "RRGGBB" or "#RGB".</summary>
        public static PdfColor FromHex(string hex)
        {
            if (hex == null)
            {
                throw new ArgumentNullException(nameof(hex));
            }

            var h = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;
            if (h.Length == 3)
            {
                h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            }

            if (h.Length != 6 ||
                !byte.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                throw new FormatException("Color must look like #RRGGBB or #RGB, got '" + hex + "'.");
            }

            return new PdfColor(r, g, b);
        }

        public bool Equals(PdfColor other) => R == other.R && G == other.G && B == other.B;

        public override bool Equals(object? obj) => obj is PdfColor other && Equals(other);

        public override int GetHashCode() => (R << 16) | (G << 8) | B;

        public override string ToString() => "#" + R.ToString("X2", CultureInfo.InvariantCulture) + G.ToString("X2", CultureInfo.InvariantCulture) + B.ToString("X2", CultureInfo.InvariantCulture);

        public static bool operator ==(PdfColor left, PdfColor right) => left.Equals(right);

        public static bool operator !=(PdfColor left, PdfColor right) => !left.Equals(right);
    }
}
