using System;
using System.IO;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Images.Png
{
    /// <summary>
    /// Minimal PNG encoder (W3C PNG Specification): 8-bit RGB, no interlace, filter type 0
    /// on every row, zlib from <see cref="Zlib"/>. Used to export QR codes and barcodes.
    /// </summary>
    internal static class PngWriter
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        /// <param name="rgb">Pixels, row by row, 3 bytes (R, G, B) each.</param>
        /// <param name="dpi">Resolution stored in the pHYs chunk (0 = none).</param>
        public static byte[] WriteRgb(int width, int height, byte[] rgb, double dpi = 0)
        {
            if (width <= 0 || height <= 0 || rgb.Length != (long)width * height * 3)
            {
                throw new ArgumentException("Pixel data does not match the image size.");
            }

            var rowBytes = width * 3;
            var raw = new byte[(rowBytes + 1) * height];
            for (var y = 0; y < height; y++)
            {
                raw[y * (rowBytes + 1)] = 0; // filter: None
                Buffer.BlockCopy(rgb, y * rowBytes, raw, y * (rowBytes + 1) + 1, rowBytes);
            }

            using (var ms = new MemoryStream())
            {
                ms.Write(Signature, 0, Signature.Length);
                var ihdr = new byte[13];
                WriteU32(ihdr, 0, (uint)width);
                WriteU32(ihdr, 4, (uint)height);
                ihdr[8] = 8;  // bit depth
                ihdr[9] = 2;  // color type: RGB
                WriteChunk(ms, "IHDR", ihdr);
                if (dpi > 0)
                {
                    var phys = new byte[9];
                    var ppm = (uint)Math.Round(dpi / 0.0254);
                    WriteU32(phys, 0, ppm);
                    WriteU32(phys, 4, ppm);
                    phys[8] = 1; // unit: metre
                    WriteChunk(ms, "pHYs", phys);
                }

                WriteChunk(ms, "IDAT", Zlib.Compress(raw));
                WriteChunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var buffer = new byte[12 + data.Length];
            WriteU32(buffer, 0, (uint)data.Length);
            for (var i = 0; i < 4; i++)
            {
                buffer[4 + i] = (byte)type[i];
            }

            Buffer.BlockCopy(data, 0, buffer, 8, data.Length);
            WriteU32(buffer, 8 + data.Length, Crc32.Compute(buffer, 4, data.Length + 4));
            s.Write(buffer, 0, buffer.Length);
        }

        private static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }
    }
}
