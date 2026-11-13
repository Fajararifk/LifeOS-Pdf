namespace LifeOS.Pdf.Fonts
{
    /// <summary>
    /// The 14 standard Type 1 fonts every PDF viewer must provide (ISO 32000-1 §9.6.2.2).
    /// They need no embedding, so files stay small, but they only cover the
    /// Windows-1252 (WinAnsi) character set. Full Unicode arrives with TrueType
    /// embedding in Fase 1.
    /// </summary>
    public enum StandardFont
    {
        Helvetica,
        HelveticaBold,
        HelveticaOblique,
        HelveticaBoldOblique,
        TimesRoman,
        TimesBold,
        TimesItalic,
        TimesBoldItalic,
        Courier,
        CourierBold,
        CourierOblique,
        CourierBoldOblique,
        Symbol,
        ZapfDingbats,
    }
}
