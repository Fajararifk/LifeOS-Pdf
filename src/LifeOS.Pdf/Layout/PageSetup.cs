namespace LifeOS.Pdf.Layout
{
    /// <summary>Paper and page geometry of a <see cref="Section"/>.</summary>
    public sealed class PageSetup
    {
        public PageSize PageSize { get; set; } = PageSize.A4;

        /// <summary>Outer margins. Header and footer sit inside them; body text below/above. Default 2 cm.</summary>
        public Margins Margins { get; set; } = Margins.FromMillimeters(20, 20, 20, 20);

        /// <summary>Number of text columns (1 or more). Header and footer always span the full width.</summary>
        public int Columns { get; set; } = 1;

        /// <summary>Space between columns, in points.</summary>
        public double ColumnGap { get; set; } = 18;

        /// <summary>Space between header and body.</summary>
        public double HeaderSpacing { get; set; } = 12;

        /// <summary>Space between body and footer.</summary>
        public double FooterSpacing { get; set; } = 12;

        /// <summary>Use <see cref="Section.FirstPageHeader"/>/<see cref="Section.FirstPageFooter"/> on the first page of the section.</summary>
        public bool DifferentFirstPage { get; set; }

        internal PageSetup Clone() => (PageSetup)MemberwiseClone();
    }
}
