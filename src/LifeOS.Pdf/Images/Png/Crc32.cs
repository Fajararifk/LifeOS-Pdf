namespace LifeOS.Pdf.Images.Png
{
    /// <summary>CRC-32 (ISO 3309 / ITU-T V.42, polynomial 0xEDB88320) as used by PNG chunks.</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(byte[] data, int offset, int length)
        {
            var crc = 0xFFFFFFFFu;
            for (var i = offset; i < offset + length; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
