using System.Collections.Generic;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>Finds (and caches) the analysis of a page; pages changed in memory are read from a snapshot.</summary>
    internal static class Analyzer
    {
        public static PageAnalysis For(PdfPage page)
        {
            if (!page.IsPristine)
            {
                page = PdfDocument.Pristine(page.IsImported ? page : Snapshot(page));
            }

            var cached = page.ExtractionCache;
            if (cached != null && page.ExtractionRotation == page.Rotation)
            {
                return cached;
            }

            var analysis = PageAnalysis.Run(page);
            page.ExtractionCache = analysis;
            page.ExtractionRotation = page.Rotation;
            return analysis;
        }

        /// <summary>The pages of <paramref name="document"/> as they would be saved (one snapshot for all when needed).</summary>
        public static IReadOnlyList<PdfPage> Pages(PdfDocument document)
        {
            foreach (var p in document.Pages)
            {
                if (!p.IsPristine)
                {
                    return PdfDocument.Open(document.ToArray()).Pages;
                }
            }

            return document.Pages;
        }

        private static PdfPage Snapshot(PdfPage page)
        {
            var owner = page.Document;
            var index = 0;
            for (var i = 0; i < owner.Pages.Count; i++)
            {
                if (ReferenceEquals(owner.Pages[i], page))
                {
                    index = i;
                }
            }

            return PdfDocument.Open(owner.ToArray()).Pages[index];
        }
    }
}
