using System;
using System.Collections.Generic;
using System.Text;

namespace LifeOS.Pdf.Barcodes.Linear
{
    /// <summary>
    /// Encoders for 1D symbologies. Each returns the module sequence (true = bar) without quiet zones.
    /// Specifications: ISO/IEC 15417 (Code 128), 16388 (Code 39), 15420 (EAN/UPC), 16390 (ITF).
    /// </summary>
    internal static class LinearEncoders
    {
        // ------------------------------------------------------------------ Code 128

        // Bar/space widths of symbol values 0–106 (106 = stop, 7 elements).
        private static readonly string[] Code128Patterns =
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
        };

        private const int StartA = 103, StartB = 104, StartC = 105, Stop = 106;
        private const int CodeA = 101, CodeB = 100, CodeC = 99;

        public static bool[] Code128(string text)
        {
            if (text.Length == 0)
            {
                throw new ArgumentException("Code 128 needs at least one character.", nameof(text));
            }

            foreach (var c in text)
            {
                if (c > 127)
                {
                    throw new ArgumentException("Code 128 encodes ASCII only; '" + c + "' is not ASCII. Use a QR code for other characters.", nameof(text));
                }
            }

            var values = new List<int>();
            char set;
            if (DigitRun(text, 0) >= 4 || (text.Length == 2 && DigitRun(text, 0) == 2))
            {
                set = 'C';
                values.Add(StartC);
            }
            else if (text[0] < 32)
            {
                set = 'A';
                values.Add(StartA);
            }
            else
            {
                set = 'B';
                values.Add(StartB);
            }

            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (set == 'C')
                {
                    if (DigitRun(text, i) >= 2)
                    {
                        values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
                        i += 2;
                        continue;
                    }

                    set = c < 32 ? 'A' : 'B';
                    values.Add(set == 'A' ? CodeA : CodeB);
                    continue;
                }

                // Switch to set C for a run of 4+ digits (or 2+ that ends the text), aligned to an even length.
                var run = DigitRun(text, i);
                if (run >= 4 || (run >= 2 && run == text.Length - i && run % 2 == 0))
                {
                    if (run % 2 == 1)
                    {
                        values.Add(ValueIn(set, c));
                        i++;
                    }

                    set = 'C';
                    values.Add(CodeC);
                    continue;
                }

                if (set == 'B' && c < 32)
                {
                    set = 'A';
                    values.Add(CodeA);
                }
                else if (set == 'A' && c >= 96)
                {
                    set = 'B';
                    values.Add(CodeB);
                }

                values.Add(ValueIn(set, c));
                i++;
            }

            var checksum = values[0];
            for (var k = 1; k < values.Count; k++)
            {
                checksum += values[k] * k;
            }

            values.Add(checksum % 103);
            values.Add(Stop);

            var modules = new List<bool>();
            foreach (var v in values)
            {
                AppendWidths(modules, Code128Patterns[v]);
            }

            return modules.ToArray();
        }

        private static int ValueIn(char set, char c) => set == 'A' ? (c < 32 ? c + 64 : c - 32) : c - 32;

        private static int DigitRun(string s, int from)
        {
            var n = 0;
            while (from + n < s.Length && s[from + n] >= '0' && s[from + n] <= '9')
            {
                n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ Code 39

        private const string Code39Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

        // 9-bit wide/narrow pattern (bar, space, bar, …; 1 = wide) per character; last entry is '*'.
        private static readonly int[] Code39Patterns =
        {
            0x034, 0x121, 0x061, 0x160, 0x031, 0x130, 0x070, 0x025, 0x124, 0x064,
            0x109, 0x049, 0x148, 0x019, 0x118, 0x058, 0x00D, 0x10C, 0x04C, 0x01C,
            0x103, 0x043, 0x142, 0x013, 0x112, 0x052, 0x007, 0x106, 0x046, 0x016,
            0x181, 0x0C1, 0x1C0, 0x091, 0x190, 0x0D0, 0x085, 0x184, 0x0C4, 0x0A8,
            0x0A2, 0x08A, 0x02A, 0x094,
        };

        public static bool[] Code39(string text, bool checkDigit, out string encoded)
        {
            var sb = new StringBuilder();
            var sum = 0;
            foreach (var c in text)
            {
                var index = Code39Chars.IndexOf(c);
                if (index < 0)
                {
                    throw new ArgumentException("Code 39 cannot encode '" + c + "'. Allowed: 0–9, A–Z (upper case), space and - . $ / + %.", nameof(text));
                }

                sum += index;
                sb.Append(c);
            }

            if (checkDigit)
            {
                sb.Append(Code39Chars[sum % 43]);
            }

            encoded = sb.ToString();
            var modules = new List<bool>();
            var full = "*" + encoded + "*";
            for (var k = 0; k < full.Length; k++)
            {
                var pattern = full[k] == '*' ? Code39Patterns[43] : Code39Patterns[Code39Chars.IndexOf(full[k])];
                for (var e = 0; e < 9; e++)
                {
                    var wide = ((pattern >> (8 - e)) & 1) == 1;
                    Append(modules, e % 2 == 0, wide ? 3 : 1);
                }

                if (k < full.Length - 1)
                {
                    Append(modules, false, 1); // inter-character gap
                }
            }

            return modules.ToArray();
        }

        // ------------------------------------------------------------------ EAN / UPC

        private static readonly string[] EanL =
            { "0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011" };

        // Parity of the six left digits of EAN-13, selected by the first digit (L = odd, G = even).
        private static readonly string[] EanParity =
            { "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL" };

        public static bool[] Ean13(string digits13)
        {
            var modules = new StringBuilder("101");
            var parity = EanParity[digits13[0] - '0'];
            for (var i = 1; i <= 6; i++)
            {
                var d = digits13[i] - '0';
                modules.Append(parity[i - 1] == 'L' ? EanL[d] : G(d));
            }

            modules.Append("01010");
            for (var i = 7; i <= 12; i++)
            {
                modules.Append(R(digits13[i] - '0'));
            }

            modules.Append("101");
            return Bits(modules.ToString());
        }

        public static bool[] Ean8(string digits8)
        {
            var modules = new StringBuilder("101");
            for (var i = 0; i < 4; i++)
            {
                modules.Append(EanL[digits8[i] - '0']);
            }

            modules.Append("01010");
            for (var i = 4; i < 8; i++)
            {
                modules.Append(R(digits8[i] - '0'));
            }

            modules.Append("101");
            return Bits(modules.ToString());
        }

        /// <summary>R pattern = L with every module inverted.</summary>
        private static string R(int d)
        {
            var l = EanL[d];
            var sb = new StringBuilder(7);
            foreach (var c in l)
            {
                sb.Append(c == '1' ? '0' : '1');
            }

            return sb.ToString();
        }

        /// <summary>G pattern = R reversed.</summary>
        private static string G(int d)
        {
            var r = R(d).ToCharArray();
            Array.Reverse(r);
            return new string(r);
        }

        /// <summary>
        /// GTIN check digit (EAN-8/13, UPC-A, ITF-14): weights 3 and 1 alternating from the right.
        /// </summary>
        public static int GtinCheckDigit(string digitsWithoutCheck)
        {
            var sum = 0;
            for (var i = 0; i < digitsWithoutCheck.Length; i++)
            {
                var weight = (digitsWithoutCheck.Length - i) % 2 == 1 ? 3 : 1;
                sum += (digitsWithoutCheck[i] - '0') * weight;
            }

            return (10 - sum % 10) % 10;
        }

        /// <summary>Validates digits and appends or checks the GTIN check digit.</summary>
        public static string WithCheckDigit(string digits, int length, string symbology)
        {
            if (digits == null)
            {
                throw new ArgumentNullException(nameof(digits));
            }

            foreach (var c in digits)
            {
                if (c < '0' || c > '9')
                {
                    throw new ArgumentException(symbology + " encodes digits only; got '" + digits + "'.", nameof(digits));
                }
            }

            if (digits.Length == length - 1)
            {
                return digits + GtinCheckDigit(digits);
            }

            if (digits.Length == length)
            {
                var expected = GtinCheckDigit(digits.Substring(0, length - 1));
                if (digits[length - 1] - '0' != expected)
                {
                    throw new ArgumentException("Wrong check digit in " + symbology + " '" + digits + "': the last digit should be " + expected +
                        ". Pass the first " + (length - 1) + " digits to have it calculated.", nameof(digits));
                }

                return digits;
            }

            throw new ArgumentException(symbology + " needs " + (length - 1) + " digits (check digit calculated) or " + length + " digits; got " + digits.Length + ".", nameof(digits));
        }

        // ------------------------------------------------------------------ ITF

        // Narrow/wide pattern of each digit (5 elements, 1 = wide).
        private static readonly string[] ItfPatterns =
            { "00110", "10001", "01001", "11000", "00101", "10100", "01100", "00011", "10010", "01010" };

        public static bool[] Itf(string digits)
        {
            foreach (var c in digits)
            {
                if (c < '0' || c > '9')
                {
                    throw new ArgumentException("ITF encodes digits only; got '" + digits + "'.", nameof(digits));
                }
            }

            if (digits.Length == 0 || digits.Length % 2 != 0)
            {
                throw new ArgumentException("ITF needs an even number of digits; got " + digits.Length + ". Add a leading 0.", nameof(digits));
            }

            var modules = new List<bool>();
            // Start: narrow bar, narrow space, narrow bar, narrow space.
            Append(modules, true, 1);
            Append(modules, false, 1);
            Append(modules, true, 1);
            Append(modules, false, 1);
            for (var i = 0; i < digits.Length; i += 2)
            {
                var bars = ItfPatterns[digits[i] - '0'];
                var spaces = ItfPatterns[digits[i + 1] - '0'];
                for (var e = 0; e < 5; e++)
                {
                    Append(modules, true, bars[e] == '1' ? 3 : 1);
                    Append(modules, false, spaces[e] == '1' ? 3 : 1);
                }
            }

            // Stop: wide bar, narrow space, narrow bar.
            Append(modules, true, 3);
            Append(modules, false, 1);
            Append(modules, true, 1);
            return modules.ToArray();
        }

        // ------------------------------------------------------------------ helpers

        private static void AppendWidths(List<bool> modules, string widths)
        {
            for (var e = 0; e < widths.Length; e++)
            {
                Append(modules, e % 2 == 0, widths[e] - '0');
            }
        }

        private static void Append(List<bool> modules, bool bar, int count)
        {
            for (var k = 0; k < count; k++)
            {
                modules.Add(bar);
            }
        }

        private static bool[] Bits(string s)
        {
            var result = new bool[s.Length];
            for (var i = 0; i < s.Length; i++)
            {
                result[i] = s[i] == '1';
            }

            return result;
        }
    }
}
