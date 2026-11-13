using System;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Internal
{
    /// <summary>Formatting helpers for PDF syntax. Always culture-invariant.</summary>
    internal static class PdfFormat
    {
        /// <summary>
        /// Formats a real number the way PDF expects: no exponent, '.' as decimal
        /// separator, at most 4 decimals. Non-finite values are rejected.
        /// </summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("NaN or infinite numbers cannot be written to a PDF.");
            }

            var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            if (rounded == 0)
            {
                return "0";
            }

            if (rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15)
            {
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);
            }

            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>PDF date string, e.g. D:20261113101500+07'00'.</summary>
        public static string Date(DateTimeOffset value)
        {
            var sb = new StringBuilder("D:");
            sb.Append(value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            var offset = value.Offset;
            sb.Append(offset < TimeSpan.Zero ? '-' : '+');
            var abs = offset.Duration();
            sb.Append(abs.Hours.ToString("00", CultureInfo.InvariantCulture));
            sb.Append('\'');
            sb.Append(abs.Minutes.ToString("00", CultureInfo.InvariantCulture));
            sb.Append('\'');
            return sb.ToString();
        }

        /// <summary>Uppercase hex of a byte array, used for hex strings.</summary>
        public static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>Rejects NaN/infinity on public API inputs with a clear parameter name.</summary>
        public static void RequireFinite(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Value must be a finite number.", paramName);
            }
        }

        public static void RequireNonNegative(double value, string paramName)
        {
            RequireFinite(value, paramName);
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value must not be negative.");
            }
        }
    }
}
