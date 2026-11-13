using System;

namespace LifeOS.Pdf.Forms
{
    /// <summary>
    /// A signature field (§12.7.3.5). Fase 8 only creates the field and its placeholder
    /// appearance; the actual digital signature arrives in Fase 9.
    /// </summary>
    public sealed class PdfSignatureField : PdfFormField
    {
        internal PdfSignatureField(string name) : base(name)
        {
        }

        internal override string FieldType => "Sig";

        /// <summary>Text drawn in the placeholder, or null for an empty box.</summary>
        public string? Caption { get; set; }

        // Signature dictionary contents, filled by LifeOS.Pdf.Signing (Fase 9b).
        internal byte[]? SignatureContents { get; set; }

        internal string? SignatureReason { get; set; }

        internal string? SignatureLocation { get; set; }

        internal string? SignatureName { get; set; }

        internal string? SignatureContact { get; set; }

        internal DateTimeOffset? SignatureDate { get; set; }

        internal bool HasSignature => SignatureContents != null;
    }
}
