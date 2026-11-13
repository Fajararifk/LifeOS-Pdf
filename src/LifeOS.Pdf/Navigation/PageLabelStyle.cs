namespace LifeOS.Pdf.Navigation
{
    /// <summary>Numbering style of a page-label range (ISO 32000-1 §12.4.2).</summary>
    public enum PageLabelStyle
    {
        /// <summary>No number: the label is only the prefix.</summary>
        None,

        /// <summary>1, 2, 3</summary>
        Decimal,

        /// <summary>I, II, III</summary>
        UpperRoman,

        /// <summary>i, ii, iii</summary>
        LowerRoman,

        /// <summary>A … Z, AA … ZZ</summary>
        UpperLetters,

        /// <summary>a … z, aa … zz</summary>
        LowerLetters,
    }
}
