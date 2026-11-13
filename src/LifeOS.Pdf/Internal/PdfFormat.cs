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
            var buffer = new char[32];
            var length = FormatNumber(value, buffer);
            return new string(buffer, 0, length);
        }

        /// <summary>Appends <see cref="Number"/>'s text without allocating (hot path of content streams).</summary>
        public static void AppendNumber(StringBuilder sb, double value)
        {
            var buffer = _scratch ?? (_scratch = new char[32]);
            var length = FormatNumber(value, buffer);
            sb.Append(buffer, 0, length);
        }

        [ThreadStatic]
        private static char[]? _scratch;

        /// <summary>
        /// Writes the number into <paramref name="buffer"/> and returns its length. Same result as
        /// rounding to 4 decimals (away from zero) and printing with "0.####" in the invariant
        /// culture, but done with integer arithmetic: no culture lookups, no allocations.
        /// </summary>
        internal static int FormatNumber(double value, char[] buffer)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("NaN or infinite numbers cannot be written to a PDF.");
            }

            // Round to 4 decimals, halves away from zero: the same arithmetic as
            // Math.Round(value, 4, MidpointRounding.AwayFromZero) (scale, add just under ½, truncate),
            // done once and kept as an integer so no second rounding or framework formatting is needed.
            var x = value * 10000.0;
            if (Math.Abs(x) >= 1e14)
            {
                // Very large values (rare in PDF): framework formatting, integer scaling would lose precision.
                var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
                var text = rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15
                    ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
                    : rounded.ToString("0.####", CultureInfo.InvariantCulture);
                text.CopyTo(0, buffer, 0, text.Length);
                return text.Length;
            }

            var r = Math.Truncate(x + (x >= 0 ? 0.49999999999999994 : -0.49999999999999994));
            if (r == 0)
            {
                buffer[0] = '0';
                return 1;
            }

            var scaled = (long)Math.Abs(r);
            var integer = scaled / 10000;
            var fraction = (int)(scaled % 10000);
            var pos = 0;
            if (r < 0)
            {
                buffer[pos++] = '-';
            }

            pos = AppendDigits(buffer, pos, integer);
            if (fraction != 0)
            {
                buffer[pos++] = '.';
                var digits = 4;
                while (fraction % 10 == 0)
                {
                    fraction /= 10;
                    digits--;
                }

                for (var i = digits - 1; i >= 0; i--)
                {
                    buffer[pos + i] = (char)('0' + fraction % 10);
                    fraction /= 10;
                }

                pos += digits;
            }

            return pos;
        }

        private static int AppendDigits(char[] buffer, int pos, long value)
        {
            var digits = 1;
            for (var v = value; v >= 10; v /= 10)
            {
                digits++;
            }

            for (var i = pos + digits - 1; i >= pos; i--)
            {
                buffer[i] = (char)('0' + value % 10);
                value /= 10;
            }

            return pos + digits;
        }

        private static readonly char[] HexDigits = "0123456789ABCDEF".ToCharArray();

        /// <summary>Appends uppercase hex of <paramref name="count"/> bytes from <paramref name="start"/>.</summary>
        public static void AppendHex(StringBuilder sb, byte[] bytes, int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var b = bytes[i];
                sb.Append(HexDigits[b >> 4]).Append(HexDigits[b & 15]);
            }
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
            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = HexDigits[bytes[i] >> 4];
                chars[i * 2 + 1] = HexDigits[bytes[i] & 15];
            }

            return new string(chars);
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
