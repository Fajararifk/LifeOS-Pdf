using System;
using System.Collections.Generic;
using System.Text;

namespace LifeOS.Pdf.Barcodes.Qr
{
    /// <summary>
    /// QR Code Model 2 encoder written from ISO/IEC 18004:2015.
    /// Produces the module matrix (true = dark) for a byte payload or text.
    /// </summary>
    internal static class QrEncoder
    {
        private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

        // Error correction codewords per block, by level (L, M, Q, H) and version (index 0 unused). ISO 18004 Table 9.
        private static readonly sbyte[,] EccCodewordsPerBlock =
        {
            { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
            { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        };

        // Number of error correction blocks, by level and version. ISO 18004 Table 9.
        private static readonly sbyte[,] ErrorCorrectionBlocks =
        {
            { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
            { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
            { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 },
        };

        // Penalty weights, ISO 18004 §7.8.3.
        private const int N1 = 3, N2 = 3, N3 = 40, N4 = 10;

        internal enum Mode
        {
            Numeric = 1,
            Alphanumeric = 2,
            Byte = 4,
        }

        public static QrMatrix Encode(string text, QrErrorCorrection ecl, int minVersion, int maxVersion, int mask)
        {
            var mode = ChooseMode(text);
            var data = mode == Mode.Byte ? Encoding.UTF8.GetBytes(text) : Encoding.ASCII.GetBytes(text);
            return Encode(data, mode, mode == Mode.Byte ? data.Length : text.Length, ecl, minVersion, maxVersion, mask);
        }

        public static QrMatrix EncodeBytes(byte[] data, QrErrorCorrection ecl, int minVersion, int maxVersion, int mask) =>
            Encode(data, Mode.Byte, data.Length, ecl, minVersion, maxVersion, mask);

        internal static Mode ChooseMode(string text)
        {
            var numeric = true;
            var alnum = true;
            foreach (var c in text)
            {
                numeric &= c >= '0' && c <= '9';
                alnum &= Alphanumeric.IndexOf(c) >= 0;
            }

            return text.Length > 0 && numeric ? Mode.Numeric : text.Length > 0 && alnum ? Mode.Alphanumeric : Mode.Byte;
        }

        private static QrMatrix Encode(byte[] data, Mode mode, int charCount, QrErrorCorrection ecl, int minVersion, int maxVersion, int mask)
        {
            if (minVersion < 1 || maxVersion > 40 || minVersion > maxVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(minVersion), "Versions must satisfy 1 ≤ minVersion ≤ maxVersion ≤ 40.");
            }

            if (mask < -1 || mask > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(mask), mask, "Mask must be -1 (automatic) or 0–7.");
            }

            // Smallest version that holds the data.
            int version;
            int usedBits = 0;
            for (version = minVersion; ; version++)
            {
                var capacityBits = DataCodewords(version, ecl) * 8;
                usedBits = 4 + CountBits(mode, version) + DataBits(mode, charCount);
                if (usedBits <= capacityBits)
                {
                    break;
                }

                if (version >= maxVersion)
                {
                    throw new ArgumentException("The data (" + charCount + " " + (mode == Mode.Byte ? "bytes" : "characters") +
                        ") does not fit in a QR code up to version " + maxVersion + " with error correction " + ecl +
                        ". Shorten the data, lower the error correction or allow a larger version.");
                }
            }

            var bits = new BitBuffer();
            bits.Append((int)mode, 4);
            bits.Append(charCount, CountBits(mode, version));
            AppendData(bits, mode, data);

            var capacity = DataCodewords(version, ecl) * 8;
            bits.Append(0, Math.Min(4, capacity - bits.Length));      // terminator
            bits.Append(0, (8 - bits.Length % 8) % 8);                  // byte align
            for (var pad = 0xEC; bits.Length < capacity; pad ^= 0xEC ^ 0x11)
            {
                bits.Append(pad, 8);
            }

            var codewords = AddEccAndInterleave(bits.ToBytes(), version, ecl);
            return Build(version, ecl, codewords, mask);
        }

        internal static int CountBits(Mode mode, int version)
        {
            var range = version <= 9 ? 0 : version <= 26 ? 1 : 2;
            switch (mode)
            {
                case Mode.Numeric: return new[] { 10, 12, 14 }[range];
                case Mode.Alphanumeric: return new[] { 9, 11, 13 }[range];
                default: return new[] { 8, 16, 16 }[range];
            }
        }

        private static int DataBits(Mode mode, int count)
        {
            switch (mode)
            {
                case Mode.Numeric: return count / 3 * 10 + (count % 3 == 0 ? 0 : count % 3 == 1 ? 4 : 7);
                case Mode.Alphanumeric: return count / 2 * 11 + (count % 2) * 6;
                default: return count * 8;
            }
        }

        private static void AppendData(BitBuffer bits, Mode mode, byte[] data)
        {
            switch (mode)
            {
                case Mode.Numeric:
                    for (var i = 0; i < data.Length; i += 3)
                    {
                        var n = Math.Min(3, data.Length - i);
                        var value = 0;
                        for (var k = 0; k < n; k++)
                        {
                            value = value * 10 + (data[i + k] - '0');
                        }

                        bits.Append(value, n * 3 + 1); // 3 digits → 10 bits, 2 → 7, 1 → 4
                    }

                    break;
                case Mode.Alphanumeric:
                    for (var i = 0; i < data.Length; i += 2)
                    {
                        if (i + 1 < data.Length)
                        {
                            bits.Append(Alphanumeric.IndexOf((char)data[i]) * 45 + Alphanumeric.IndexOf((char)data[i + 1]), 11);
                        }
                        else
                        {
                            bits.Append(Alphanumeric.IndexOf((char)data[i]), 6);
                        }
                    }

                    break;
                default:
                    foreach (var b in data)
                    {
                        bits.Append(b, 8);
                    }

                    break;
            }
        }

        /// <summary>Number of modules available for data and ECC (everything except function patterns).</summary>
        internal static int RawDataModules(int version)
        {
            var result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                var align = version / 7 + 2;
                result -= (25 * align - 10) * align - 55;
                if (version >= 7)
                {
                    result -= 36;
                }
            }

            return result;
        }

        internal static int DataCodewords(int version, QrErrorCorrection ecl) =>
            RawDataModules(version) / 8 - EccCodewordsPerBlock[(int)ecl, version] * ErrorCorrectionBlocks[(int)ecl, version];

        /// <summary>Splits data into blocks, appends Reed–Solomon ECC to each and interleaves (ISO 18004 §7.6).</summary>
        private static byte[] AddEccAndInterleave(byte[] data, int version, QrErrorCorrection ecl)
        {
            var numBlocks = ErrorCorrectionBlocks[(int)ecl, version];
            var eccLen = EccCodewordsPerBlock[(int)ecl, version];
            var rawCodewords = RawDataModules(version) / 8;
            var numShortBlocks = numBlocks - rawCodewords % numBlocks;
            var shortBlockLen = rawCodewords / numBlocks;

            var divisor = ReedSolomonDivisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                var dataLen = shortBlockLen - eccLen + (i < numShortBlocks ? 0 : 1);
                var dat = new byte[dataLen];
                Array.Copy(data, k, dat, 0, dataLen);
                k += dataLen;
                var ecc = ReedSolomonRemainder(dat, divisor);
                // Short blocks get a placeholder byte so all blocks have equal length for interleaving.
                var block = new byte[shortBlockLen + 1];
                Array.Copy(dat, block, dataLen);
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks.Add(block);
            }

            var result = new byte[rawCodewords];
            var pos = 0;
            for (var i = 0; i < blocks[0].Length; i++)
            {
                for (var j = 0; j < blocks.Count; j++)
                {
                    // Skip the placeholder of short blocks.
                    if (i != shortBlockLen - eccLen || j >= numShortBlocks)
                    {
                        result[pos++] = blocks[j][i];
                    }
                }
            }

            return result;
        }

        private static byte[] ReedSolomonDivisor(int degree)
        {
            // Generator polynomial (x − α^0)(x − α^1)…(x − α^(degree−1)), leading 1 omitted.
            var result = new byte[degree];
            result[degree - 1] = 1;
            var root = 1;
            for (var i = 0; i < degree; i++)
            {
                for (var j = 0; j < result.Length; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < result.Length)
                    {
                        result[j] ^= result[j + 1];
                    }
                }

                root = Multiply(root, 0x02);
            }

            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (var b in data)
            {
                var factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] ^= Multiply(divisor[i], factor);
                }
            }

            return result;
        }

        /// <summary>Multiplication in GF(2^8) with the QR reducing polynomial x^8+x^4+x^3+x^2+1 (0x11D).</summary>
        private static byte Multiply(int x, int y)
        {
            var z = 0;
            for (var i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }

            return (byte)z;
        }

        private static QrMatrix Build(int version, QrErrorCorrection ecl, byte[] codewords, int requestedMask)
        {
            var m = new QrMatrix(version, ecl);
            DrawFunctionPatterns(m);
            DrawCodewords(m, codewords);

            var mask = requestedMask;
            if (mask == -1)
            {
                var best = int.MaxValue;
                for (var candidate = 0; candidate < 8; candidate++)
                {
                    ApplyMask(m, candidate);
                    DrawFormatBits(m, candidate);
                    var penalty = Penalty(m);
                    if (penalty < best)
                    {
                        best = penalty;
                        mask = candidate;
                    }

                    ApplyMask(m, candidate); // XOR again = undo
                }
            }

            ApplyMask(m, mask);
            DrawFormatBits(m, mask);
            m.Mask = mask;
            return m;
        }

        private static void DrawFunctionPatterns(QrMatrix m)
        {
            var size = m.Size;
            // Timing patterns.
            for (var i = 0; i < size; i++)
            {
                m.SetFunction(6, i, i % 2 == 0);
                m.SetFunction(i, 6, i % 2 == 0);
            }

            // Finder patterns with separators.
            DrawFinder(m, 3, 3);
            DrawFinder(m, size - 4, 3);
            DrawFinder(m, 3, size - 4);

            // Alignment patterns, except where they would overlap a finder.
            var positions = AlignmentPositions(m.Version);
            var n = positions.Length;
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    if (!((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)))
                    {
                        for (var dy = -2; dy <= 2; dy++)
                        {
                            for (var dx = -2; dx <= 2; dx++)
                            {
                                m.SetFunction(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                            }
                        }
                    }
                }
            }

            // Reserve format areas (real bits drawn after masking) and draw version info.
            DrawFormatBits(m, 0);
            DrawVersion(m);
        }

        private static void DrawFinder(QrMatrix m, int cx, int cy)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var dist = Math.Max(Math.Abs(dx), Math.Abs(dy)); // Chebyshev distance
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && x < m.Size && y >= 0 && y < m.Size)
                    {
                        m.SetFunction(x, y, dist != 2 && dist != 4);
                    }
                }
            }
        }

        internal static int[] AlignmentPositions(int version)
        {
            if (version == 1)
            {
                return new int[0];
            }

            var count = version / 7 + 2;
            var step = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = version * 4 + 17 - 7; i >= 1; i--, pos -= step)
            {
                result[i] = pos;
            }

            return result;
        }

        private static void DrawFormatBits(QrMatrix m, int mask)
        {
            // Level indicator bits: L=01, M=00, Q=11, H=10.
            var levelBits = m.ErrorCorrection == QrErrorCorrection.Low ? 1 : m.ErrorCorrection == QrErrorCorrection.Medium ? 0 : m.ErrorCorrection == QrErrorCorrection.Quartile ? 3 : 2;
            var data = (levelBits << 3) | mask;
            var rem = data;
            for (var i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            }

            var bits = ((data << 10) | rem) ^ 0x5412;
            var size = m.Size;

            // First copy, around the top-left finder.
            for (var i = 0; i <= 5; i++)
            {
                m.SetFunction(8, i, Bit(bits, i));
            }

            m.SetFunction(8, 7, Bit(bits, 6));
            m.SetFunction(8, 8, Bit(bits, 7));
            m.SetFunction(7, 8, Bit(bits, 8));
            for (var i = 9; i < 15; i++)
            {
                m.SetFunction(14 - i, 8, Bit(bits, i));
            }

            // Second copy, split between the other two finders.
            for (var i = 0; i < 8; i++)
            {
                m.SetFunction(size - 1 - i, 8, Bit(bits, i));
            }

            for (var i = 8; i < 15; i++)
            {
                m.SetFunction(8, size - 15 + i, Bit(bits, i));
            }

            m.SetFunction(8, size - 8, true); // the dark module
        }

        private static void DrawVersion(QrMatrix m)
        {
            if (m.Version < 7)
            {
                return;
            }

            var rem = m.Version;
            for (var i = 0; i < 12; i++)
            {
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            }

            var bits = (m.Version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = Bit(bits, i);
                var a = m.Size - 11 + i % 3;
                var b = i / 3;
                m.SetFunction(a, b, bit);
                m.SetFunction(b, a, bit);
            }
        }

        /// <summary>Places codewords in the two-module-wide zigzag columns, right to left (§7.7.3).</summary>
        private static void DrawCodewords(QrMatrix m, byte[] data)
        {
            var size = m.Size;
            var i = 0;
            for (var right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5; // skip the vertical timing pattern
                }

                for (var vert = 0; vert < size; vert++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? size - 1 - vert : vert;
                        if (!m.IsFunction(x, y) && i < data.Length * 8)
                        {
                            m.Set(x, y, Bit(data[i >> 3], 7 - (i & 7)));
                            i++;
                        }

                        // Remainder bits (0–7) stay light, as initialised.
                    }
                }
            }
        }

        // Mask patterns per version (index [version * 8 + mask]), built on first use. The array
        // holds immutable data, so a race only means a pattern is computed twice.
        private static readonly bool[]?[] MaskPatterns = new bool[]?[41 * 8];

        private static bool[] MaskPattern(int version, int mask)
        {
            var pattern = MaskPatterns[version * 8 + mask];
            if (pattern != null)
            {
                return pattern;
            }

            var size = version * 4 + 17;
            pattern = new bool[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }

                    pattern[y * size + x] = invert;
                }
            }

            MaskPatterns[version * 8 + mask] = pattern;
            return pattern;
        }

        private static void ApplyMask(QrMatrix m, int mask)
        {
            // XOR the mask pattern into every non-function module (§7.8.2).
            var pattern = MaskPattern(m.Version, mask);
            var dark = m.Modules;
            var function = m.Functions;
            for (var i = 0; i < dark.Length; i++)
            {
                if (pattern[i] && !function[i])
                {
                    dark[i] = !dark[i];
                }
            }
        }

        /// <summary>Penalty score of a masked symbol (ISO 18004 §7.8.3); the lowest wins.</summary>
        internal static int Penalty(QrMatrix m)
        {
            var size = m.Size;
            var g = m.Modules;
            var result = 0;
            var history = new int[7];

            // N1 (runs of 5+ same color) and N3 (finder-like 1:1:3:1:1 patterns) in rows and columns.
            for (var pass = 0; pass < 2; pass++)
            {
                for (var a = 0; a < size; a++)
                {
                    var runColor = false;
                    var runLength = 0;
                    Array.Clear(history, 0, 7);
                    var index = pass == 0 ? a * size : a;
                    var step = pass == 0 ? 1 : size;
                    for (var b = 0; b < size; b++, index += step)
                    {
                        var dark = g[index];
                        if (dark == runColor)
                        {
                            runLength++;
                            if (runLength == 5)
                            {
                                result += N1;
                            }
                            else if (runLength > 5)
                            {
                                result++;
                            }
                        }
                        else
                        {
                            AddHistory(runLength, history, size);
                            if (!runColor)
                            {
                                result += CountFinderPatterns(history) * N3;
                            }

                            runColor = dark;
                            runLength = 1;
                        }
                    }

                    result += TerminateAndCount(runColor, runLength, history, size) * N3;
                }
            }

            // N2: 2×2 blocks of the same color. N4: proportion of dark modules.
            var darkCount = 0;
            for (var y = 0; y < size; y++)
            {
                var row = y * size;
                for (var x = 0; x < size; x++)
                {
                    var c = g[row + x];
                    if (c)
                    {
                        darkCount++;
                    }

                    if (y < size - 1 && x < size - 1 && c == g[row + x + 1] && c == g[row + size + x] && c == g[row + size + x + 1])
                    {
                        result += N2;
                    }
                }
            }

            var total = size * size;
            var k = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
            result += k * N4;
            return result;
        }

        private static int CountFinderPatterns(int[] h)
        {
            var n = h[1];
            var core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
            return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
        }

        private static int TerminateAndCount(bool runColor, int runLength, int[] history, int size)
        {
            if (runColor)
            {
                AddHistory(runLength, history, size);
                runLength = 0;
            }

            runLength += size; // the light border beyond the symbol
            AddHistory(runLength, history, size);
            return CountFinderPatterns(history);
        }

        private static void AddHistory(int runLength, int[] history, int size)
        {
            if (history[0] == 0)
            {
                runLength += size; // light border before the first run
            }

            history[6] = history[5];
            history[5] = history[4];
            history[4] = history[3];
            history[3] = history[2];
            history[2] = history[1];
            history[1] = history[0];
            history[0] = runLength;
        }

        private static bool Bit(int value, int i) => ((value >> i) & 1) != 0;

        private sealed class BitBuffer
        {
            private readonly List<bool> _bits = new List<bool>();

            public int Length => _bits.Count;

            public void Append(int value, int length)
            {
                for (var i = length - 1; i >= 0; i--)
                {
                    _bits.Add(((value >> i) & 1) != 0);
                }
            }

            public byte[] ToBytes()
            {
                var result = new byte[_bits.Count / 8];
                for (var i = 0; i < _bits.Count; i++)
                {
                    if (_bits[i])
                    {
                        result[i >> 3] |= (byte)(0x80 >> (i & 7));
                    }
                }

                return result;
            }
        }
    }

    /// <summary>Module grid of a QR symbol under construction (flat row-major arrays).</summary>
    internal sealed class QrMatrix
    {
        public QrMatrix(int version, QrErrorCorrection ecl)
        {
            Version = version;
            ErrorCorrection = ecl;
            Size = version * 4 + 17;
            Modules = new bool[Size * Size];
            Functions = new bool[Size * Size];
        }

        public int Version { get; }

        public QrErrorCorrection ErrorCorrection { get; }

        public int Size { get; }

        public int Mask { get; set; }

        /// <summary>Dark modules, index y * Size + x.</summary>
        public bool[] Modules { get; }

        /// <summary>Function-pattern modules (never masked), index y * Size + x.</summary>
        public bool[] Functions { get; }

        public bool IsDark(int x, int y) => Modules[y * Size + x];

        public bool IsFunction(int x, int y) => Functions[y * Size + x];

        public void Set(int x, int y, bool dark) => Modules[y * Size + x] = dark;

        public void SetFunction(int x, int y, bool dark)
        {
            Modules[y * Size + x] = dark;
            Functions[y * Size + x] = true;
        }
    }
}
