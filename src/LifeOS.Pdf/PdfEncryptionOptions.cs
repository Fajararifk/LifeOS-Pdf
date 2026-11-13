using System;

namespace LifeOS.Pdf
{
    /// <summary>How a document is encrypted on save (§7.6).</summary>
    public sealed class PdfEncryptionOptions
    {
        /// <summary>Password required to open the file; null or empty = no password.</summary>
        public string? UserPassword { get; set; }

        /// <summary>Password for full access; defaults to <see cref="UserPassword"/> when null.</summary>
        public string? OwnerPassword { get; set; }

        /// <summary>The encryption algorithm; AES-128 by default.</summary>
        public PdfEncryptionAlgorithm Algorithm { get; set; } = PdfEncryptionAlgorithm.AES_128;

        /// <summary>Operations allowed when opened with the user password.</summary>
        public PdfPermissions Permissions { get; set; } = PdfPermissions.All;

        /// <summary>True to encrypt document metadata too; true by default.</summary>
        public bool EncryptMetadata { get; set; } = true;

        internal void Validate()
        {
            if (UserPassword != null && UserPassword.Length > 127)
            {
                throw new ArgumentException("The user password may be at most 127 characters.", nameof(UserPassword));
            }

            if (OwnerPassword != null && OwnerPassword.Length > 127)
            {
                throw new ArgumentException("The owner password may be at most 127 characters.", nameof(OwnerPassword));
            }
        }
    }
}
