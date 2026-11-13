using System;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Navigation
{
    /// <summary>
    /// A place in the document that links and bookmarks jump to (ISO 32000-1 §12.3.2):
    /// a page with a view, or the name of a named destination.
    /// Coordinates are top-left based, in points, like everywhere in this library.
    /// </summary>
    public sealed class PdfDestination
    {
        private PdfDestination(PdfPage? page, string? name, DestinationView view, double? left, double? top, double? zoom)
        {
            Page = page;
            Name = name;
            View = view;
            Left = left;
            Top = top;
            Zoom = zoom;
        }

        /// <summary>Target page (null for a named destination).</summary>
        public PdfPage? Page { get; }

        /// <summary>Name of a named destination (null for an explicit one).</summary>
        public string? Name { get; }

        internal DestinationView View { get; }

        internal double? Left { get; }

        internal double? Top { get; }

        internal double? Zoom { get; }

        /// <summary>
        /// Shows <paramref name="page"/> with the given point at the top-left of the window.
        /// Null values keep the viewer's current position/zoom. <paramref name="zoom"/> 1 = 100%.
        /// </summary>
        public static PdfDestination ToPage(PdfPage page, double? top = null, double? left = null, double? zoom = null)
        {
            RequirePage(page);
            Check(top, nameof(top));
            Check(left, nameof(left));
            if (zoom.HasValue && (!(zoom.Value > 0) || double.IsInfinity(zoom.Value)))
            {
                throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be greater than 0 (1 = 100%).");
            }

            return new PdfDestination(page, null, DestinationView.Xyz, left, top, zoom);
        }

        /// <summary>Shows the whole page.</summary>
        /// <summary>The same view on another page (used when pages are copied between documents).</summary>
        internal PdfDestination WithPage(PdfPage page) => new PdfDestination(page, null, View, Left, Top, Zoom);

        public static PdfDestination FitPage(PdfPage page)
        {
            RequirePage(page);
            return new PdfDestination(page, null, DestinationView.Fit, null, null, null);
        }

        /// <summary>Fits the page width to the window, optionally scrolled to <paramref name="top"/>.</summary>
        public static PdfDestination FitWidth(PdfPage page, double? top = null)
        {
            RequirePage(page);
            Check(top, nameof(top));
            return new PdfDestination(page, null, DestinationView.FitWidth, null, top, null);
        }

        /// <summary>A named destination (defined in <see cref="PdfDocument.NamedDestinations"/> or by a layout anchor).</summary>
        public static PdfDestination Named(string name)
        {
            ValidateName(name);
            return new PdfDestination(null, name, DestinationView.Xyz, null, null, null);
        }

        /// <summary>Destination names: letters, digits, '-', '_', '.' and ':' (1–127 characters).</summary>
        internal static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 127)
            {
                throw new ArgumentException("A destination name must have 1 to 127 characters.", nameof(name));
            }

            foreach (var c in name)
            {
                if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ':')))
                {
                    throw new ArgumentException("Destination name '" + name + "' may only contain ASCII letters, digits, '-', '_', '.' and ':'.", nameof(name));
                }
            }
        }

        private static void RequirePage(PdfPage page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }
        }

        private static void Check(double? value, string name)
        {
            if (value.HasValue)
            {
                PdfFormat.RequireFinite(value.Value, name);
            }
        }
    }

    internal enum DestinationView
    {
        Xyz,
        Fit,
        FitWidth,
    }
}
