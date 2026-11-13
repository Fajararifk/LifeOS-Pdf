using System;
using System.IO;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// Byte-oriented, buffered writer that tracks the absolute position, which the
    /// cross-reference table needs. All PDF syntax is written as ASCII.
    /// Call <see cref="Flush"/> before reading the underlying stream.
    /// </summary>
    internal sealed class PdfOutput
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[64 * 1024];
        private readonly char[] _number = new char[32];
        private int _used;

        public PdfOutput(Stream stream)
        {
            _stream = stream;
        }

        public long Position { get; private set; }

        private SecurityHandler? _encryption;
        private int _encryptionObject;
        private bool _encryptionOn;

        /// <summary>Sets the encryption context for the next object written; <paramref name="enabled"/> is false for /Encrypt and the trailer.</summary>
        public void SetEncryption(SecurityHandler? encryption, int objectNumber, bool enabled)
        {
            _encryption = encryption;
            _encryptionObject = objectNumber;
            _encryptionOn = enabled;
        }

        /// <summary>Encrypts a string or stream body when writing an encrypted document.</summary>
        public byte[] Encrypt(byte[] data, bool stream) =>
            _encryption != null && _encryptionOn ? _encryption.EncryptData(data, _encryptionObject, 0, stream) : data;

        public void Write(string ascii)
        {
            for (var i = 0; i < ascii.Length; i++)
            {
                if (_used == _buffer.Length)
                {
                    Flush();
                }

                _buffer[_used++] = (byte)ascii[i];
            }

            Position += ascii.Length;
        }

        public void Write(byte[] bytes) => Write(bytes, 0, bytes.Length);

        public void Write(byte[] bytes, int offset, int count)
        {
            if (count > _buffer.Length - _used)
            {
                Flush();
                if (count > _buffer.Length / 2)
                {
                    _stream.Write(bytes, offset, count); // large data (streams) goes straight through
                    Position += count;
                    return;
                }
            }

            Buffer.BlockCopy(bytes, offset, _buffer, _used, count);
            _used += count;
            Position += count;
        }

        public void WriteByte(byte value)
        {
            if (_used == _buffer.Length)
            {
                Flush();
            }

            _buffer[_used++] = value;
            Position++;
        }

        /// <summary>Writes a number like <see cref="PdfFormat.Number"/> without allocating.</summary>
        public void WriteNumber(double value)
        {
            var length = PdfFormat.FormatNumber(value, _number);
            for (var i = 0; i < length; i++)
            {
                WriteByte((byte)_number[i]);
            }
        }

        public void WriteInteger(long value)
        {
            if (value < 0)
            {
                WriteByte((byte)'-');
                if (value == long.MinValue)
                {
                    Write("9223372036854775808");
                    return;
                }

                value = -value;
            }

            var length = 0;
            do
            {
                _number[length++] = (char)('0' + value % 10);
                value /= 10;
            }
            while (value > 0);

            for (var i = length - 1; i >= 0; i--)
            {
                WriteByte((byte)_number[i]);
            }
        }

        public void Flush()
        {
            if (_used > 0)
            {
                _stream.Write(_buffer, 0, _used);
                _used = 0;
            }
        }
    }
}
