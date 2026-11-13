namespace LifeOS.Pdf
{
    /// <summary>The PDF is encrypted. Reading encrypted files is planned for a later version.</summary>
    public sealed class PdfEncryptedException : PdfReadException
    {
        public PdfEncryptedException()
            : this("The PDF is encrypted (it has an /Encrypt dictionary, possibly with an empty password). " +
                   "Provide the password in PdfReadOptions.Password, or remove the protection first (for example: qpdf --decrypt in.pdf out.pdf).")
        {
        }

        public PdfEncryptedException(string message)
            : base(message)
        {
        }
    }
}
