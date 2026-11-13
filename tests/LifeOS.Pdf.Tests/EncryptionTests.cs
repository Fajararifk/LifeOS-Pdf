using System;
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class EncryptionTests
    {
        private static PdfDocument Build()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Graphics.DrawText("Rahasia", new PdfFont(StandardFont.Helvetica, 12), 72, 72);
            return doc;
        }

        [Theory]
        [InlineData(PdfEncryptionAlgorithm.RC4_128)]
        [InlineData(PdfEncryptionAlgorithm.AES_128)]
        public void Encrypt_ThenOpen_RoundTrips(PdfEncryptionAlgorithm algorithm)
        {
            var bytes = Build().ToArray(new PdfSaveOptions
            {
                Encryption = new PdfEncryptionOptions { Algorithm = algorithm, UserPassword = "pwd", OwnerPassword = "owner" },
            });

            Assert.Throws<PdfEncryptedException>(() => PdfDocument.Open(bytes));

            var opened = PdfDocument.Open(bytes, new PdfReadOptions { Password = "pwd" });
            Assert.True(opened.IsEncrypted);
            Assert.NotNull(opened.Permissions);
            Assert.Contains("Rahasia", opened.ExtractText());

            var owner = PdfDocument.Open(bytes, new PdfReadOptions { Password = "owner" });
            Assert.Contains("Rahasia", owner.ExtractText());
        }

        [Fact]
        public void WrongPassword_Throws()
        {
            var bytes = Build().ToArray(new PdfSaveOptions
            {
                Encryption = new PdfEncryptionOptions { UserPassword = "pwd" },
            });

            Assert.Throws<PdfEncryptedException>(() => PdfDocument.Open(bytes, new PdfReadOptions { Password = "salah" }));
        }

        [Fact]
        public void Encrypt_WritesEncryptDictionary()
        {
            var bytes = Build().ToArray(new PdfSaveOptions
            {
                Encryption = new PdfEncryptionOptions { UserPassword = "pwd" },
            });

            var pdf = PdfStructure.Verify(bytes);
            Assert.Equal(1, pdf.CountObjects("/Filter /Standard"));
            Assert.Equal(1, pdf.CountObjects("/O <"));
        }

        [Fact]
        public void RemovePassword_BySavingWithoutEncryption()
        {
            var bytes = Build().ToArray(new PdfSaveOptions
            {
                Encryption = new PdfEncryptionOptions { UserPassword = "pwd" },
            });

            var opened = PdfDocument.Open(bytes, new PdfReadOptions { Password = "pwd" });
            var reopened = PdfDocument.Open(opened.ToArray());
            Assert.False(reopened.IsEncrypted);
            Assert.Contains("Rahasia", reopened.ExtractText());
        }
    }
}
