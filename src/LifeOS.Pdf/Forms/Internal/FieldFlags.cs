namespace LifeOS.Pdf.Forms.Internal
{
    /// <summary>Field flag bits (§12.7.3, Tables 226–228).</summary>
    internal static class FieldFlags
    {
        internal const long ReadOnly = 1L << 0;
        internal const long Required = 1L << 1;
        internal const long NoExport = 1L << 2;

        internal const long Multiline = 1L << 12;
        internal const long Password = 1L << 13;
        internal const long NoToggleToOff = 1L << 14;
        internal const long Radio = 1L << 15;
        internal const long Pushbutton = 1L << 16;
        internal const long Combo = 1L << 17;
        internal const long Edit = 1L << 18;
        internal const long MultiSelect = 1L << 21;
        internal const long Comb = 1L << 24;
    }
}
