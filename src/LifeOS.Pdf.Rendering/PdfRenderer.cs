using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LifeOS.Pdf;
using LifeOS.Pdf.Images.Png;
using PDFiumCore;
using static PDFiumCore.fpdfview;

namespace LifeOS.Pdf.Rendering
{
    /// <summary>Renders PDF pages to PNG using PDFium (Apache-2.0).</summary>
    public sealed class PdfRenderer
    {
        private const int FPDFBitmap_BGRA = 4;

        private static readonly object InitLock = new object();

        static PdfRenderer()
        {
            lock (InitLock)
            {
                FPDF_InitLibrary();
            }
        }

        /// <summary>Renders one page; throws when PDFium's native library cannot be loaded.</summary>
        public RenderedImage Render(byte[] pdf, int pageIndex, RenderOptions? options = null)
        {
            options ??= new RenderOptions();
            var handle = GCHandle.Alloc(pdf, GCHandleType.Pinned);
            try
            {
                var document = FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), pdf.Length, null);
                if (document == null || document.__Instance == IntPtr.Zero)
                {
                    throw new InvalidOperationException("PDFium failed to load the document.");
                }

                try
                {
                    var page = FPDF_LoadPage(document, pageIndex);
                    if (page == null || page.__Instance == IntPtr.Zero)
                    {
                        throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "The document has no page at that index.");
                    }

                    try
                    {
                        return RenderPage(page, pageIndex, options.Dpi);
                    }
                    finally
                    {
                        FPDF_ClosePage(page);
                    }
                }
                finally
                {
                    FPDF_CloseDocument(document);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Renders one page of a <see cref="PdfDocument"/>.</summary>
        public RenderedImage Render(PdfDocument document, int pageIndex, RenderOptions? options = null) =>
            Render(document.ToArray(), pageIndex, options);

        /// <summary>Renders every page, in order.</summary>
        public IReadOnlyList<RenderedImage> RenderAll(byte[] pdf, RenderOptions? options = null)
        {
            options ??= new RenderOptions();
            var handle = GCHandle.Alloc(pdf, GCHandleType.Pinned);
            try
            {
                var document = FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), pdf.Length, null);
                if (document == null || document.__Instance == IntPtr.Zero)
                {
                    throw new InvalidOperationException("PDFium failed to load the document.");
                }

                try
                {
                    var count = FPDF_GetPageCount(document);
                    var result = new List<RenderedImage>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var page = FPDF_LoadPage(document, i);
                        if (page == null || page.__Instance == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            result.Add(RenderPage(page, i, options.Dpi));
                        }
                        finally
                        {
                            FPDF_ClosePage(page);
                        }
                    }

                    return result;
                }
                finally
                {
                    FPDF_CloseDocument(document);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        private static RenderedImage RenderPage(FpdfPageT page, int index, int dpi)
        {
            var scale = dpi / 72.0;
            var width = Math.Max(1, (int)Math.Round(FPDF_GetPageWidth(page) * scale));
            var height = Math.Max(1, (int)Math.Round(FPDF_GetPageHeight(page) * scale));
            var bitmap = FPDFBitmapCreateEx(width, height, FPDFBitmap_BGRA, IntPtr.Zero, 0);
            try
            {
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 0);
                var stride = FPDFBitmapGetStride(bitmap);
                var buffer = FPDFBitmapGetBuffer(bitmap);
                var bgra = new byte[stride * height];
                Marshal.Copy(buffer, bgra, 0, bgra.Length);

                var rgb = new byte[width * height * 3];
                for (var y = 0; y < height; y++)
                {
                    var srcRow = y * stride;
                    var dstRow = y * width * 3;
                    for (var x = 0; x < width; x++)
                    {
                        var s = srcRow + x * 4;
                        var d = dstRow + x * 3;
                        // PDFium renders BGRA with a transparent background: composite over white.
                        var alpha = bgra[s + 3];
                        var a = alpha / 255.0;
                        rgb[d] = (byte)(bgra[s + 2] * a + 255 * (1 - a));
                        rgb[d + 1] = (byte)(bgra[s + 1] * a + 255 * (1 - a));
                        rgb[d + 2] = (byte)(bgra[s] * a + 255 * (1 - a));
                    }
                }

                return new RenderedImage(index, width, height, PngWriter.WriteRgb(width, height, rgb, dpi));
            }
            finally
            {
                FPDFBitmapDestroy(bitmap);
            }
        }
    }
}
