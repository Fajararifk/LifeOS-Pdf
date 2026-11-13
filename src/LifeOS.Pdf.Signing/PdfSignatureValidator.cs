using System;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;

namespace LifeOS.Pdf.Signing
{
    /// <summary>The result of verifying a digital signature.</summary>
    public sealed class PdfSignatureVerificationResult
    {
        internal PdfSignatureVerificationResult(bool valid, string? signerName, DateTimeOffset? signingTime, bool coversWholeDocument, string? error)
        {
            IsValid = valid;
            SignerName = signerName;
            SigningTime = signingTime;
            CoversWholeDocument = coversWholeDocument;
            Error = error;
        }

        public bool IsValid { get; }

        public string? SignerName { get; }

        public DateTimeOffset? SigningTime { get; }

        /// <summary>True when the /ByteRange covers the entire file.</summary>
        public bool CoversWholeDocument { get; }

        public string? Error { get; }
    }

    /// <summary>Verifies a CMS/PKCS#7 signature in a PDF (§12.8.3).</summary>
    public static class PdfSignatureValidator
    {
        /// <summary>Verifies the first signature found in the file.</summary>
        public static PdfSignatureVerificationResult Verify(byte[] pdf)
        {
            if (pdf == null)
            {
                throw new ArgumentNullException(nameof(pdf));
            }

            var text = Latin1(pdf);
            var byteRange = MatchByteRange(text);
            if (byteRange == null)
            {
                return new PdfSignatureVerificationResult(false, null, null, false, "No /ByteRange found.");
            }

            var contents = MatchContents(text);
            if (contents == null)
            {
                return new PdfSignatureVerificationResult(false, null, null, false, "No signature /Contents found.");
            }

            var a = (int)byteRange[0];
            var b = (int)byteRange[1];
            var c = (int)byteRange[2];
            var d = (int)byteRange[3];
            if (a < 0 || b < a || c < b || d > pdf.Length)
            {
                return new PdfSignatureVerificationResult(false, null, null, false, "Invalid /ByteRange.");
            }

            var signedData = new byte[(b - a) + (d - c)];
            Buffer.BlockCopy(pdf, a, signedData, 0, b - a);
            Buffer.BlockCopy(pdf, c, signedData, b - a, d - c);

            try
            {
                var signedCms = new SignedCms(new ContentInfo(signedData), detached: true);
                signedCms.Decode(TrimTrailingZeros(contents));
                signedCms.CheckSignature(verifySignatureOnly: true);

                var signerName = signedCms.Certificates.Count > 0 ? signedCms.Certificates[0].Subject : null;
                var coversWhole = a == 0 && d == pdf.Length;
                return new PdfSignatureVerificationResult(true, signerName, null, coversWhole, null);
            }
            catch (Exception ex)
            {
                return new PdfSignatureVerificationResult(false, null, null, false, ex.Message);
            }
        }

        private static long[]? MatchByteRange(string text)
        {
            var m = Regex.Match(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
            if (!m.Success)
            {
                return null;
            }

            return new[]
            {
                long.Parse(m.Groups[1].Value),
                long.Parse(m.Groups[2].Value),
                long.Parse(m.Groups[3].Value),
                long.Parse(m.Groups[4].Value),
            };
        }

        private static byte[]? MatchContents(string text)
        {
            var m = Regex.Match(text, @"/Contents\s*<([0-9A-Fa-f]+)>");
            if (!m.Success)
            {
                return null;
            }

            var hex = m.Groups[1].Value;
            if (hex.Length % 2 != 0)
            {
                return null;
            }

            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
            }

            return bytes;
        }

        private static byte[] TrimTrailingZeros(byte[] data)
        {
            var length = data.Length;
            while (length > 0 && data[length - 1] == 0)
            {
                length--;
            }

            var result = new byte[length];
            Array.Copy(data, result, length);
            return result;
        }

        private static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            return new string(chars);
        }
    }
}
