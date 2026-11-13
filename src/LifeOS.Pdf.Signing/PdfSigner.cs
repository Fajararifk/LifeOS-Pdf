using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LifeOS.Pdf;
using LifeOS.Pdf.Forms;

namespace LifeOS.Pdf.Signing
{
    /// <summary>
    /// Adds a CMS/PKCS#7 detached signature (PAdES B-B) to a document that already has a
    /// signature field (§12.8). The signature covers the whole file except the /Contents value.
    /// </summary>
    public sealed class PdfSigner
    {
        private const int PlaceholderBytes = 8192;

        private readonly X509Certificate2 _certificate;

        public PdfSigner(X509Certificate2 certificate)
        {
            _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        }

        /// <summary>Reason for signing, written to /Reason and the CMS signing-time.</summary>
        public string? Reason { get; set; }

        public string? Location { get; set; }

        public string? ContactInfo { get; set; }

        /// <summary>Signs the document in place and returns the signed PDF bytes.</summary>
        public byte[] Sign(PdfDocument document)
        {
            var field = FindSignatureField(document);
            var now = DateTimeOffset.Now;
            field.SignatureContents = new byte[PlaceholderBytes];
            field.SignatureReason = Reason;
            field.SignatureLocation = Location;
            field.SignatureContact = ContactInfo;
            field.SignatureDate = now;

            var bytes = document.ToArray();
            var placeholder = PlaceholderHex();
            var zeroStart = IndexOf(bytes, placeholder);
            if (zeroStart < 0)
            {
                throw new InvalidOperationException("The signature placeholder was not found in the serialized document.");
            }

            // /Contents is written as "<" + hex + ">"; the ByteRange skips the whole value.
            var contentsStart = zeroStart - 1;                              // the '<'
            var contentsEnd = zeroStart + placeholder.Length + 1;           // one past '>'

            // 1. Write the correct /ByteRange first, since it is part of the signed content.
            ReplaceByteRange(bytes, contentsStart, contentsEnd, bytes.Length);

            // 2. Signed content = everything except the /Contents value.
            var signedData = new byte[contentsStart + (bytes.Length - contentsEnd)];
            Buffer.BlockCopy(bytes, 0, signedData, 0, contentsStart);
            Buffer.BlockCopy(bytes, contentsEnd, signedData, contentsStart, bytes.Length - contentsEnd);

            // 3. Sign.
            var der = ComputeSignature(signedData, now);
            var hex = Hex(der);
            if (hex.Length > PlaceholderBytes * 2)
            {
                throw new InvalidOperationException("The signature is too large for the placeholder.");
            }

            // 4. Insert the signature into the /Contents placeholder (padded with zeros).
            var result = (byte[])bytes.Clone();
            var hexBytes = Encoding.ASCII.GetBytes(hex);
            Buffer.BlockCopy(hexBytes, 0, result, zeroStart, hexBytes.Length);
            return result;
        }

        private static PdfSignatureField FindSignatureField(PdfDocument document)
        {
            foreach (var field in document.Form.Fields)
            {
                if (field is PdfSignatureField signature)
                {
                    return signature;
                }
            }

            throw new InvalidOperationException("The document has no signature field; add one with page.AddSignatureField(...) before signing.");
        }

        private byte[] ComputeSignature(byte[] content, DateTimeOffset now)
        {
            var signedCms = new SignedCms(new ContentInfo(content), detached: true);
            var signer = new CmsSigner(_certificate)
            {
                IncludeOption = X509IncludeOption.EndCertOnly,
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            };
            signer.SignedAttributes.Add(new Pkcs9SigningTime(now.UtcDateTime));
            signedCms.ComputeSignature(signer);
            return signedCms.Encode();
        }

        private static string PlaceholderHex() => new string('0', PlaceholderBytes * 2);

        private static string Hex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data)
            {
                sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static int IndexOf(byte[] data, string ascii)
        {
            var pattern = Encoding.ASCII.GetBytes(ascii);
            for (var i = 0; i <= data.Length - pattern.Length; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Overwrites the fixed-width /ByteRange placeholder with the real offsets.</summary>
        private static void ReplaceByteRange(byte[] data, long b, long c, long d)
        {
            const string marker = "/ByteRange [0 ";
            var markerBytes = Encoding.ASCII.GetBytes(marker);
            var idx = IndexOf(data, marker);
            if (idx < 0)
            {
                throw new InvalidOperationException("The /ByteRange placeholder was not found.");
            }

            var start = idx + markerBytes.Length;
            var replacement = b.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " " +
                              c.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " " +
                              d.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
            var replacementBytes = Encoding.ASCII.GetBytes(replacement);
            Buffer.BlockCopy(replacementBytes, 0, data, start, replacementBytes.Length);
        }
    }
}
