using System;

namespace LifeOS.Pdf.Templates
{
    /// <summary>A template or its data is invalid. <see cref="Line"/>/<see cref="Column"/> point at the problem.</summary>
    public sealed class TemplateException : Exception
    {
        public TemplateException(string message, int line = 0, int column = 0)
            : base(line > 0 ? message + " (line " + line + ", column " + column + ")" : message)
        {
            Line = line;
            Column = column;
        }

        /// <summary>1-based line in the template or JSON text; 0 when unknown.</summary>
        public int Line { get; }

        public int Column { get; }
    }
}
