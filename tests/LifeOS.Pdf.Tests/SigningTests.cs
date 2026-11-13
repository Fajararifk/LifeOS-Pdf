using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LifeOS.Pdf;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Signing;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class SigningTests
    {
        private static X509Certificate2 CreateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LifeOS Test Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }

        [Fact]
        public void Sign_ThenVerify_IsValid()
        {
            using var cert = CreateCertificate();
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Graphics.DrawText("Dokumen penting", new PdfFont(StandardFont.Helvetica, 12), 72, 72);
            page.AddSignatureField("TandaTangan", 400, 700, 160, 40);

            var signed = new PdfSigner(cert) { Reason = "Persetujuan", Location = "Jakarta" }.Sign(doc);

            // The signed file is still a structurally valid PDF.
            PdfStructure.Verify(signed);

            var result = PdfSignatureValidator.Verify(signed);
            Assert.True(result.IsValid, result.Error);
            Assert.True(result.CoversWholeDocument);
            Assert.Contains("LifeOS Test Signer", result.SignerName);
        }

        [Fact]
        public void TamperedSignature_IsInvalid()
        {
            using var cert = CreateCertificate();
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Graphics.DrawText("Dokumen penting", new PdfFont(StandardFont.Helvetica, 12), 72, 72);
            page.AddSignatureField("TandaTangan", 400, 700, 160, 40);

            var signed = new PdfSigner(cert).Sign(doc);

            // Tamper with a byte inside the signed range (the content stream).
            var tampered = (byte[])signed.Clone();
            tampered[0] = (byte)'X'; // corrupt the PDF header
            var result = PdfSignatureValidator.Verify(tampered);
            Assert.False(result.IsValid);
        }

        [Fact]
        public void Sign_WithoutSignatureField_Throws()
        {
            using var cert = CreateCertificate();
            var doc = new PdfDocument();
            doc.AddPage();
            Assert.Throws<InvalidOperationException>(() => new PdfSigner(cert).Sign(doc));
        }
    }
}
