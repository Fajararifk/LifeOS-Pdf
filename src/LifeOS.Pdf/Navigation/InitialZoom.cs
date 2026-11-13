namespace LifeOS.Pdf.Navigation
{
    /// <summary>Zoom when the document opens (written as the /OpenAction destination).</summary>
    public enum InitialZoom
    {
        /// <summary>Viewer default.</summary>
        Default,

        /// <summary>Whole page visible.</summary>
        FitPage,

        /// <summary>Page width fills the window.</summary>
        FitWidth,

        /// <summary><see cref="PdfViewerPreferences.ZoomPercent"/> percent.</summary>
        Percent,
    }
}
