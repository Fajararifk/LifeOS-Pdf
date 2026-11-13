using System;

namespace LifeOS.Pdf.Layout
{
    /// <summary>
    /// Content cannot be laid out (for example a table row taller than the page).
    /// The message names the element and how to fix it; nothing is silently cut off.
    /// </summary>
    public sealed class LayoutException : Exception
    {
        public LayoutException(string message)
            : base(message)
        {
        }
    }
}
