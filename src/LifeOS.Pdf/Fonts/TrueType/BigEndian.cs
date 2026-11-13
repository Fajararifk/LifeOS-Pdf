using System;

namespace LifeOS.Pdf.Fonts.TrueType
{
    /// <summary>
    /// Bounds-checked big-endian reads. Font files are untrusted input: every read
    /// goes through here so a corrupt offset becomes a <see cref="FormatException"/>,
    /// never an IndexOutOfRangeException.
    /// </summary>
    internal static class BigEndian
    {
        public static void Require(byte[] data, long offset, long length, string what)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new FormatException("Invalid font file: " + what + " lies outside the file.");
            }
        }

        public static byte U8(byte[] d, long o)
        {
            Require(d, o, 1, "data");
            return d[o];
        }

        public static ushort U16(byte[] d, long o)
        {
            Require(d, o, 2, "data");
            return (ushort)((d[o] << 8) | d[o + 1]);
        }

        public static short I16(byte[] d, long o) => unchecked((short)U16(d, o));

        public static uint U32(byte[] d, long o)
        {
            Require(d, o, 4, "data");
            return ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];
        }

        public static string Tag(byte[] d, long o)
        {
            Require(d, o, 4, "table tag");
            return new string(new[] { (char)d[o], (char)d[o + 1], (char)d[o + 2], (char)d[o + 3] });
        }

        public static void Write16(byte[] d, int o, int value)
        {
            d[o] = (byte)(value >> 8);
            d[o + 1] = (byte)value;
        }

        public static void Write32(byte[] d, int o, uint value)
        {
            d[o] = (byte)(value >> 24);
            d[o + 1] = (byte)(value >> 16);
            d[o + 2] = (byte)(value >> 8);
            d[o + 3] = (byte)value;
        }
    }
}
