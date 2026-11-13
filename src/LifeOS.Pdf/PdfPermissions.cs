using System;

namespace LifeOS.Pdf
{
    /// <summary>Permission flags of an encrypted PDF (§7.6.3.2).</summary>
    [Flags]
    public enum PdfPermissions
    {
        None = 0,

        /// <summary>Print the document.</summary>
        Print = 1 << 2,

        /// <summary>Modify the contents.</summary>
        Modify = 1 << 3,

        /// <summary>Copy text and graphics.</summary>
        Copy = 1 << 4,

        /// <summary>Add or change annotations.</summary>
        Annotate = 1 << 5,

        /// <summary>Fill interactive form fields.</summary>
        FillForms = 1 << 8,

        /// <summary>Extract text for accessibility.</summary>
        ExtractAccessibility = 1 << 9,

        /// <summary>Assemble the document (insert, rotate, delete pages).</summary>
        Assemble = 1 << 10,

        /// <summary>Print at high quality.</summary>
        PrintHighQuality = 1 << 11,

        All = Print | Modify | Copy | Annotate | FillForms | ExtractAccessibility | Assemble | PrintHighQuality,
    }
}
