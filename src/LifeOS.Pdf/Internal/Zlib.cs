using System;
using System.IO;
using System.IO.Compression;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// zlib (RFC 1950) wrapper around DeflateStream. PDF's FlateDecode expects the
    /// zlib header and Adler-32 trailer, which DeflateStream does not write and
    /// ZLibStream does not exist on netstandard2.0.
    /// </summary>
    internal static class Zlib
    {
        public static byte[] Compress(byte[] data) => Compress(data, CompressionLevel.Optimal);

        public static byte[] Compress(byte[] data, CompressionLevel level)
        {
            using (var ms = new MemoryStream(data.Length / 3 + 64))
            {
                // CMF = deflate, 32K window; FLG = compression level hint with a valid FCHECK
                // (0x9C default, 0x01 fastest — decoders ignore the hint).
                ms.WriteByte(0x78);
                ms.WriteByte(level == CompressionLevel.Optimal ? (byte)0x9C : (byte)0x01);
                using (var deflate = new DeflateStream(ms, level, leaveOpen: true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                var adler = Adler32(data);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        public static byte[] Decompress(byte[] data)
        {
            // Skip the 2-byte zlib header; DeflateStream ignores the trailer.
            using (var input = new MemoryStream(data, 2, data.Length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                deflate.CopyTo(output);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Inflates zlib data that must produce exactly <paramref name="expectedSize"/> bytes.
        /// Stops reading as soon as the output would exceed it, so a decompression bomb
        /// cannot allocate more than the caller already validated.
        /// </summary>
        public static byte[] Decompress(byte[] data, int expectedSize)
        {
            if (data.Length < 2 || (data[0] & 0x0F) != 8 || ((data[0] << 8) | data[1]) % 31 != 0 || (data[1] & 0x20) != 0)
            {
                throw new FormatException("Invalid compressed data: bad zlib header.");
            }

            var output = new byte[expectedSize];
            var total = 0;
            try
            {
                using (var input = new MemoryStream(data, 2, data.Length - 2))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                {
                    while (total < expectedSize)
                    {
                        var read = deflate.Read(output, total, expectedSize - total);
                        if (read == 0)
                        {
                            break;
                        }

                        total += read;
                    }

                    // Extra bytes after the expected size are ignored (never read into memory).
                }
            }
            catch (InvalidDataException ex)
            {
                throw new FormatException("Invalid compressed data: " + ex.Message, ex);
            }

            if (total != expectedSize)
            {
                throw new FormatException("Invalid compressed data: expected " + expectedSize + " bytes, got " + total + " (truncated).");
            }

            return output;
        }

        internal static uint Adler32(byte[] data)
        {
            const uint Mod = 65521;

            // 5552 is the largest block for which the sums cannot overflow 32 bits before the
            // modulo (RFC 1950 / zlib's NMAX), so the expensive '%' runs once per block.
            const int Block = 5552;
            uint a = 1, b = 0;
            var i = 0;
            while (i < data.Length)
            {
                var end = Math.Min(data.Length, i + Block);
                for (; i < end; i++)
                {
                    a += data[i];
                    b += a;
                }

                a %= Mod;
                b %= Mod;
            }

            return (b << 16) | a;
        }
    }
}
