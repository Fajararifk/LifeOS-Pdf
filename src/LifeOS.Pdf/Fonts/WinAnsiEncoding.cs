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
        private static readonly Dictionary<char, byte> Upper = new Dictionary<char, byte>
        {
            ['€'] = 0x80, // €
            ['‚'] = 0x82, // ‚
            ['ƒ'] = 0x83, // ƒ
            ['„'] = 0x84, // „
            ['…'] = 0x85, // …
            ['†'] = 0x86, // †
            ['‡'] = 0x87, // ‡
            ['ˆ'] = 0x88, // ˆ
            ['‰'] = 0x89, // ‰
            ['Š'] = 0x8A, // Š
            ['‹'] = 0x8B, // ‹
            ['Œ'] = 0x8C, // Œ
            ['Ž'] = 0x8E, // Ž
            ['‘'] = 0x91, // ‘
            ['’'] = 0x92, // ’
            ['“'] = 0x93, // “
            ['”'] = 0x94, // ”
            ['•'] = 0x95, // •
            ['–'] = 0x96, // –
            ['—'] = 0x97, // —
            ['˜'] = 0x98, // ˜
            ['™'] = 0x99, // ™
            ['š'] = 0x9A, // š
            ['›'] = 0x9B, // ›
            ['œ'] = 0x9C, // œ
            ['ž'] = 0x9E, // ž
            ['Ÿ'] = 0x9F, // Ÿ
        };

        /// <summary>
        /// Encodes text. Characters outside WinAnsi become '?'. Tabs become a space;
        /// other control characters are dropped.
        /// </summary>
        public static byte[] Encode(string text)
        {
            var bytes = new List<byte>(text.Length);
            foreach (var c in text)
            {
                if (c == '\t')
                {
                    bytes.Add((byte)' ');
                }
                else if (c < 0x20)
                {
                    // Control characters have no glyph; skip.
                }
                else if (c < 0x7F || (c >= 0xA0 && c <= 0xFF))
                {
                    bytes.Add((byte)c);
                }
                else if (Upper.TryGetValue(c, out var b))
                {
                    bytes.Add(b);
                }
                else
                {
                    bytes.Add(Replacement);
                }
            }

            return bytes.ToArray();
        }

        /// <summary>Symbol and ZapfDingbats use their own built-in encoding: pass bytes through.</summary>
        public static byte[] EncodeBuiltIn(string text)
        {
            var bytes = new List<byte>(text.Length);
            foreach (var c in text)
            {
                if (c >= 0x20 && c <= 0xFF)
                {
                    bytes.Add((byte)c);
                }
            }

            return bytes.ToArray();
        }
    }
}
