using System;

namespace LifeOS.Pdf.Html
{
    /// <summary>An HTML → PDF conversion failed. <see cref="Error"/> says why; the message says how to fix it.</summary>
    public sealed class HtmlConversionException : Exception
    {
        public HtmlConversionException(HtmlConversionError error, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        /// <summary>The kind of failure.</summary>
        public HtmlConversionError Error { get; }
    }
}
