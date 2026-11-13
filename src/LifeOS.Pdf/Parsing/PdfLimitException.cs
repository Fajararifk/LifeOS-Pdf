namespace LifeOS.Pdf.Parsing
{
    /// <summary>A <see cref="PdfReadOptions"/> limit was exceeded. Never swallowed by error recovery.</summary>
    internal sealed class PdfLimitException : PdfReadException
    {
        public PdfLimitException(string message)
            : base(message)
        {
        }
    }
}
