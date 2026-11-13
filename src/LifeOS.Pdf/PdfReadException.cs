using System;

namespace LifeOS.Pdf
{
    /// <summary>A PDF could not be read: it is damaged, not a PDF, or exceeds a <see cref="PdfReadOptions"/> limit.</summary>
    public class PdfReadException : Exception
    {
        public PdfReadException(string message)
            : base(message)
        {
        }

        public PdfReadException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
