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
        public static byte[] Compress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                // CMF = deflate, 32K window; FLG = default compression, valid FCHECK.
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
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

        internal static uint Adler32(byte[] data)
        {
            const uint Mod = 65521;
            uint a = 1, b = 0;
            foreach (var d in data)
            {
                a = (a + d) % Mod;
                b = (b + a) % Mod;
            }

            return (b << 16) | a;
        }
    }
}
