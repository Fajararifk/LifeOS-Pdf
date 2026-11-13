namespace LifeOS.Pdf
{
    /// <summary>The PDF is encrypted. Reading encrypted files is planned for a later version.</summary>
    public sealed class PdfEncryptedException : PdfReadException
    {
        public PdfEncryptedException()
            : base("The PDF is encrypted (it has an /Encrypt dictionary, possibly with an empty password). " +
                   "LifeOS.Pdf cannot open encrypted files yet; remove the protection first (for example: qpdf --decrypt in.pdf out.pdf).")
        {
        }
    }
}
