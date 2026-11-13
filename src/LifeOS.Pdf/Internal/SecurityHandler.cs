using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LifeOS.Pdf.Objects;

namespace LifeOS.Pdf.Internal
{
    /// <summary>
    /// The standard security handler (§7.6.3): computes and verifies file keys and encrypts or
    /// decrypts strings and stream data. RC4 is implemented here (removed from .NET); AES and the
    /// hashes use <see cref="System.Security.Cryptography"/>, which is available on netstandard2.0.
    /// Revisions 2–4 (RC4 and AES-128) are supported. AES-256 (Revision 6, PDF 2.0) is recognized
    /// but not yet implemented.
    /// </summary>
    internal sealed class SecurityHandler
    {
        private static readonly byte[] Padding =
        {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
        };

        private readonly bool _aes;
        private readonly byte[] _fileKey;

        private SecurityHandler(bool aes, byte[] fileKey)
        {
            _aes = aes;
            _fileKey = fileKey;
        }

        // ------------------------------------------------------------------ writer surface

        public int Version { get; private set; }

        public int Revision { get; private set; }

        public int KeyLength { get; private set; }

        public int PermissionsValue { get; private set; }

        public byte[]? O { get; private set; }

        public byte[]? U { get; private set; }

        public bool EncryptMetadata { get; private set; }

        public string Filter => _aes ? "AESV2" : "V2";

        // ------------------------------------------------------------------ write

        public static SecurityHandler Create(PdfEncryptionOptions options, byte[] fileId)
        {
            options.Validate();
            var user = Pad(options.UserPassword ?? string.Empty);
            var owner = Pad(options.OwnerPassword ?? options.UserPassword ?? string.Empty);
            var perms = unchecked((int)(0xFFFFF0C0u | (uint)options.Permissions));
            var length = options.Algorithm == PdfEncryptionAlgorithm.RC4_128 ? 16 : 16;
            var aes = options.Algorithm != PdfEncryptionAlgorithm.RC4_128;
            var revision = aes ? 4 : 3;

            if (options.Algorithm == PdfEncryptionAlgorithm.AES_256)
            {
                throw new NotSupportedException("AES-256 (Revision 6) encryption is not implemented yet; use AES-128 or RC4-128.");
            }

            var ownerKey = Md5(owner);
            for (var i = 0; i < 50; i++)
            {
                ownerKey = Md5(ownerKey);
            }

            ownerKey = Truncate(ownerKey, length);
            var o = Rc4(user, ownerKey);
            for (var i = 1; i <= 19; i++)
            {
                o = Rc4(o, Xor(ownerKey, i));
            }

            var fileKey = FileKey(user, o, perms, fileId, options.EncryptMetadata, revision, length);
            var u = ComputeU(fileId, fileKey, revision);

            return new SecurityHandler(aes, fileKey)
            {
                Version = aes ? 4 : 2,
                Revision = revision,
                KeyLength = length * 8,
                PermissionsValue = perms,
                O = o,
                U = u,
                EncryptMetadata = options.EncryptMetadata,
            };
        }

        // ------------------------------------------------------------------ read

        public static SecurityHandler Open(PdfDictionary encrypt, string? password, byte[]? fileId)
        {
            var v = Number(encrypt.Get("V"), 0);
            var r = Number(encrypt.Get("R"), 0);
            if (r >= 6 || v >= 5)
            {
                throw new PdfEncryptedException("The file uses AES-256 (Revision 6) encryption, which LifeOS.Pdf does not support yet.");
            }

            var length = Number(encrypt.Get("Length"), 40) / 8;
            length = Math.Max(5, Math.Min(16, length));
            var o = Bytes(encrypt.Get("O")) ?? Array.Empty<byte>();
            var u = Bytes(encrypt.Get("U")) ?? Array.Empty<byte>();
            var perms = Number(encrypt.Get("P"), 0);
            var id = fileId ?? new byte[16];
            var pwd = PadBytes(Encoding.GetEncoding("latin1").GetBytes(password ?? string.Empty));
            var aes = v == 4 || (encrypt.Get("CF") is PdfDictionary cf && cf.Get("AESV2") != null);

            var (key, ok) = TryPassword(pwd, o, u, perms, id, r, length);
            if (!ok)
            {
                throw new PdfEncryptedException("The password is wrong, or the file uses an encryption LifeOS.Pdf does not support (remove the protection first, e.g. qpdf --decrypt in.pdf out.pdf).");
            }

            return new SecurityHandler(aes, key)
            {
                Version = v,
                Revision = r,
                KeyLength = length * 8,
                PermissionsValue = perms,
                O = o,
                U = u,
                EncryptMetadata = true,
            };
        }

        private static (byte[] Key, bool Ok) TryPassword(byte[] pwd, byte[] o, byte[] u, int perms, byte[] id, int r, int length)
        {
            if (o.Length < length || u.Length < 16)
            {
                return (Array.Empty<byte>(), false);
            }

            var fileKey = FileKey(pwd, o, perms, id, true, r, length);
            if (MatchU(u, ComputeU(id, fileKey, r), r) || r == 2)
            {
                return (fileKey, true);
            }

            // Owner password: recover the padded user password from /O.
            var ownerKey = Md5(pwd);
            for (var i = 0; i < 50; i++)
            {
                ownerKey = Md5(ownerKey);
            }

            ownerKey = Truncate(ownerKey, length);
            var userPad = Rc4(o, ownerKey);
            for (var i = 1; i <= 19; i++)
            {
                userPad = Rc4(userPad, Xor(ownerKey, i));
            }

            var ownerFileKey = FileKey(userPad, o, perms, id, true, r, length);
            if (MatchU(u, ComputeU(id, ownerFileKey, r), r))
            {
                return (ownerFileKey, true);
            }

            return (Array.Empty<byte>(), false);
        }

        // ------------------------------------------------------------------ key derivation

        private static byte[] FileKey(byte[] paddedPassword, byte[] o, int perms, byte[] fileId, bool encryptMetadata, int revision, int length)
        {
            var extra = revision == 4 && !encryptMetadata ? 4 : 0;
            var data = new byte[32 + o.Length + 4 + 16 + extra];
            var pos = 0;
            Buffer.BlockCopy(paddedPassword, 0, data, pos, 32); pos += 32;
            Buffer.BlockCopy(o, 0, data, pos, o.Length); pos += o.Length;
            data[pos++] = (byte)perms;
            data[pos++] = (byte)(perms >> 8);
            data[pos++] = (byte)(perms >> 16);
            data[pos++] = (byte)(perms >> 24);
            Buffer.BlockCopy(fileId, 0, data, pos, Math.Min(16, fileId.Length)); pos += 16;
            if (extra == 4)
            {
                data[pos++] = 0xFF;
                data[pos++] = 0xFF;
                data[pos++] = 0xFF;
                data[pos++] = 0xFF;
            }

            var key = Md5(data);
            if (revision >= 3)
            {
                for (var i = 0; i < 50; i++)
                {
                    key = Md5(key);
                }
            }

            return Truncate(key, length);
        }

        private static byte[] ComputeU(byte[] fileId, byte[] fileKey, int revision)
        {
            if (revision == 2)
            {
                return Rc4(Padding, fileKey);
            }

            // Algorithm 5: MD5 of the padding string + /ID[0], then 20 RC4 rounds; padded to 32 bytes.
            var hashInput = new byte[32 + Math.Min(16, fileId.Length)];
            Buffer.BlockCopy(Padding, 0, hashInput, 0, 32);
            Buffer.BlockCopy(fileId, 0, hashInput, 32, hashInput.Length - 32);
            var result = Rc4(Md5(hashInput), fileKey);
            for (var i = 1; i <= 19; i++)
            {
                result = Rc4(result, Xor(fileKey, i));
            }

            var padded = new byte[32];
            Buffer.BlockCopy(result, 0, padded, 0, result.Length);
            return padded;
        }

        private static bool MatchU(byte[] stored, byte[] computed, int revision)
        {
            var n = revision >= 3 ? 16 : 32;
            if (stored.Length < n || computed.Length < n)
            {
                return false;
            }

            for (var i = 0; i < n; i++)
            {
                if (stored[i] != computed[i])
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ object encryption / decryption

        public byte[] EncryptData(byte[] data, int obj, int gen, bool stream)
        {
            var key = ObjectKey(obj, gen, stream);
            return _aes ? AesEncrypt(data, key) : Rc4(data, key);
        }

        public byte[] DecryptData(byte[] data, int obj, int gen, bool stream)
        {
            var key = ObjectKey(obj, gen, stream);
            return _aes ? AesDecrypt(data, key) : Rc4(data, key);
        }

        private byte[] ObjectKey(int obj, int gen, bool stream)
        {
            // "sAlT" is appended for AES for both strings and streams (§7.6.2 Algorithm 1).
            var saltLen = _aes ? 4 : 0;
            var tail = new byte[5 + saltLen];
            tail[0] = (byte)obj;
            tail[1] = (byte)(obj >> 8);
            tail[2] = (byte)(obj >> 16);
            tail[3] = (byte)gen;
            tail[4] = (byte)(gen >> 8);
            if (saltLen == 4)
            {
                tail[5] = (byte)'s';
                tail[6] = (byte)'A';
                tail[7] = (byte)'l';
                tail[8] = (byte)'T';
            }

            var combined = new byte[_fileKey.Length + tail.Length];
            Buffer.BlockCopy(_fileKey, 0, combined, 0, _fileKey.Length);
            Buffer.BlockCopy(tail, 0, combined, _fileKey.Length, tail.Length);
            var hash = Md5(combined);
            var keyLen = Math.Min(16, _fileKey.Length + 5);
            var key = new byte[keyLen];
            Buffer.BlockCopy(hash, 0, key, 0, keyLen);
            return key;
        }

        private static byte[] AesEncrypt(byte[] data, byte[] key)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = key.Length * 8;
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                var cipher = aes.CreateEncryptor().TransformFinalBlock(data, 0, data.Length);
                var result = new byte[aes.IV.Length + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
                return result;
            }
        }

        private static byte[] AesDecrypt(byte[] data, byte[] key)
        {
            if (data.Length < 16)
            {
                throw new PdfReadException("Encrypted string is too short for AES-128.");
            }

            using (var aes = Aes.Create())
            {
                aes.KeySize = key.Length * 8;
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                var iv = new byte[16];
                Buffer.BlockCopy(data, 0, iv, 0, 16);
                aes.IV = iv;
                return aes.CreateDecryptor().TransformFinalBlock(data, 16, data.Length - 16);
            }
        }

        // ------------------------------------------------------------------ primitives

        private static byte[] Pad(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return (byte[])Padding.Clone();
            }

            return PadBytes(Encoding.GetEncoding("latin1").GetBytes(password));
        }

        private static byte[] PadBytes(byte[] raw)
        {
            var result = new byte[32];
            var n = Math.Min(raw.Length, 32);
            Buffer.BlockCopy(raw, 0, result, 0, n);
            Buffer.BlockCopy(Padding, 0, result, n, 32 - n);
            return result;
        }

        private static byte[] Md5(byte[] data)
        {
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(data);
            }
        }

        private static byte[] Truncate(byte[] data, int length)
        {
            if (data.Length <= length)
            {
                return data;
            }

            var result = new byte[length];
            Buffer.BlockCopy(data, 0, result, 0, length);
            return result;
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] Xor(byte[] key, int value)
        {
            var result = new byte[key.Length];
            for (var i = 0; i < key.Length; i++)
            {
                result[i] = (byte)(key[i] ^ value);
            }

            return result;
        }

        private static byte[] Rc4(byte[] data, byte[] key)
        {
            var s = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                s[i] = (byte)i;
            }

            var j = 0;
            for (var i = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                var t = s[i];
                s[i] = s[j];
                s[j] = t;
            }

            var output = new byte[data.Length];
            var x = 0;
            var y = 0;
            for (var k = 0; k < data.Length; k++)
            {
                x = (x + 1) & 0xFF;
                y = (y + s[x]) & 0xFF;
                var t = s[x];
                s[x] = s[y];
                s[y] = t;
                output[k] = (byte)(data[k] ^ s[(s[x] + s[y]) & 0xFF]);
            }

            return output;
        }

        private static int Number(PdfObject? obj, int fallback) =>
            obj is PdfInteger i ? (int)i.Value : fallback;

        private static byte[]? Bytes(PdfObject? obj) =>
            obj is PdfString s ? s.Bytes : null;
    }
}
