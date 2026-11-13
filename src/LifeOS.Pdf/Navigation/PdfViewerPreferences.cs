using System;

namespace LifeOS.Pdf.Navigation
{
    /// <summary>How the viewer presents the document when it is opened (ISO 32000-1 §12.2, §7.7.2).</summary>
    public sealed class PdfViewerPreferences
    {
        private double _zoomPercent = 100;
        private int _initialPage;

        public PageMode PageMode { get; set; }

        public PageLayout PageLayout { get; set; }

        public InitialZoom InitialZoom { get; set; }

        /// <summary>Zoom in percent when <see cref="InitialZoom"/> is <see cref="Navigation.InitialZoom.Percent"/>. Default 100.</summary>
        public double ZoomPercent
        {
            get => _zoomPercent;
            set => _zoomPercent = value > 0 && value <= 6400 ? value : throw new ArgumentOutOfRangeException(nameof(ZoomPercent), value, "Zoom must be between 0 and 6400 percent.");
        }

        /// <summary>Page shown first (0-based). Default 0.</summary>
        public int InitialPage
        {
            get => _initialPage;
            set => _initialPage = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(InitialPage), value, "Page index must not be negative.");
        }

        /// <summary>Show the document title (not the file name) in the window title. Default true.</summary>
        public bool DisplayDocTitle { get; set; } = true;

        public bool HideToolbar { get; set; }

        public bool HideMenubar { get; set; }

        /// <summary>Hide scroll bars and navigation controls.</summary>
        public bool HideWindowUI { get; set; }

        /// <summary>Resize the window to fit the first page.</summary>
        public bool FitWindow { get; set; }

        public bool CenterWindow { get; set; }

        internal void CopyTo(PdfViewerPreferences target)
        {
            target.PageMode = PageMode;
            target.PageLayout = PageLayout;
            target.InitialZoom = InitialZoom;
            target.ZoomPercent = ZoomPercent;
            target.InitialPage = InitialPage;
            target.DisplayDocTitle = DisplayDocTitle;
            target.HideToolbar = HideToolbar;
            target.HideMenubar = HideMenubar;
            target.HideWindowUI = HideWindowUI;
            target.FitWindow = FitWindow;
            target.CenterWindow = CenterWindow;
        }
    }
}
