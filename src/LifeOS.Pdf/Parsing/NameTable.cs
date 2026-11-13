using System.Collections.Generic;

namespace LifeOS.Pdf.Parsing
{
    /// <summary>
    /// Interns names and keywords read from a file: the same few dozen names (/Type, /Font,
    /// /Length …) appear thousands of times, so each distinct spelling becomes one string.
    /// Bounded, and every hit is verified byte-for-byte, so a hash collision only costs a miss.
    /// </summary>
    internal sealed class NameTable
    {
        private const int MaxEntries = 4096;
        private const int MaxLength = 64;
        private readonly Dictionary<ulong, string> _map = new Dictionary<ulong, string>();

        public string Get(byte[] data, long start, int length)
        {
            if (length > MaxLength)
            {
                return Latin1(data, start, length);
            }

            // FNV-1a over the bytes.
            var hash = 14695981039346656037UL;
            for (var i = 0; i < length; i++)
            {
                hash = (hash ^ data[start + i]) * 1099511628211UL;
            }

            hash ^= (ulong)length << 56;
            if (_map.TryGetValue(hash, out var existing) && Matches(existing, data, start, length))
            {
                return existing;
            }

            var text = Latin1(data, start, length);
            if (_map.Count < MaxEntries)
            {
                _map[hash] = text;
            }

            return text;
        }

        private static bool Matches(string s, byte[] data, long start, int length)
        {
            if (s.Length != length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (s[i] != data[start + i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string Latin1(byte[] data, long start, int length)
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)data[start + i];
            }

            return new string(chars);
        }
    }
}
