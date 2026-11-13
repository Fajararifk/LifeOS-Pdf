using System.Collections.Generic;

namespace LifeOS.Pdf.Fonts
{
    /// <summary>
    /// Unicode → WinAnsiEncoding (Windows-1252) conversion, implemented by hand
    /// because code page 1252 is not available on .NET Core without an extra package.
    /// </summary>
    internal static class WinAnsiEncoding
    {
        public const byte Replacement = (byte)'?';

        // 0x80–0x9F is where Windows-1252 differs from Latin-1.
        private static readonly Dictionary<int, byte> Upper = new Dictionary<int, byte>
        {
            [0x20AC] = 0x80, // €
            [0x201A] = 0x82, // ‚
            [0x0192] = 0x83, // ƒ
            [0x201E] = 0x84, // „
            [0x2026] = 0x85, // …
            [0x2020] = 0x86, // †
            [0x2021] = 0x87, // ‡
            [0x02C6] = 0x88, // ˆ
            [0x2030] = 0x89, // ‰
            [0x0160] = 0x8A, // Š
            [0x2039] = 0x8B, // ‹
            [0x0152] = 0x8C, // Œ
            [0x017D] = 0x8E, // Ž
            [0x2018] = 0x91, // ‘
            [0x2019] = 0x92, // ’
            [0x201C] = 0x93, // “
            [0x201D] = 0x94, // ”
            [0x2022] = 0x95, // •
            [0x2013] = 0x96, // –
            [0x2014] = 0x97, // —
            [0x02DC] = 0x98, // ˜
            [0x2122] = 0x99, // ™
            [0x0161] = 0x9A, // š
            [0x203A] = 0x9B, // ›
            [0x0153] = 0x9C, // œ
            [0x017E] = 0x9E, // ž
            [0x0178] = 0x9F, // Ÿ
        };

        public static bool TryEncode(int codePoint, out byte code)
        {
            if ((codePoint >= 0x20 && codePoint < 0x7F) || (codePoint >= 0xA0 && codePoint <= 0xFF))
            {
                code = (byte)codePoint;
                return true;
            }

            return Upper.TryGetValue(codePoint, out code);
        }

        /// <summary>
        /// Encodes text. Characters outside WinAnsi become '?'. Tabs become a space;
        /// other control characters are dropped.
        /// </summary>
        public static byte[] Encode(string text)
        {
            var codePoints = CodePointReader.Read(text);
            var bytes = new byte[codePoints.Count];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = TryEncode(codePoints[i], out var b) ? b : Replacement;
            }

            return bytes;
        }
    }
}
