using System;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images.Png;

namespace LifeOS.Pdf.Barcodes
{
    /// <summary>Shared PNG/SVG export for module grids (QR) and bar rows (1D barcodes).</summary>
    internal static class ModuleRaster
    {
        /// <summary>Renders modules to an RGB PNG. <c>isDark</c> is queried in symbol coordinates (without quiet zone).</summary>
        public static byte[] ToPng(int columns, int rows, Func<int, int, bool> isDark, int moduleWidth, int moduleHeight, int quietZone, PdfColor dark, PdfColor light)
        {
            if (moduleWidth < 1 || moduleHeight < 1 || quietZone < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moduleWidth), "Module size must be at least 1 pixel and the quiet zone must not be negative.");
            }

            var width = (columns + 2 * quietZone) * moduleWidth;
            var height = (rows + 2 * (rows > 1 ? quietZone : 0)) * moduleHeight;
            if ((long)width * height > 100_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(moduleWidth), "The image would be larger than 100 megapixels; use a smaller module size.");
            }

            var top = rows > 1 ? quietZone : 0;
            var pixels = new byte[width * height * 3];
            for (var py = 0; py < height; py++)
            {
                var my = py / moduleHeight - top;
                for (var px = 0; px < width; px++)
                {
                    var mx = px / moduleWidth - quietZone;
                    var on = mx >= 0 && mx < columns && my >= 0 && my < rows && isDark(mx, my);
                    var c = on ? dark : light;
                    var i = (py * width + px) * 3;
                    pixels[i] = c.R;
                    pixels[i + 1] = c.G;
                    pixels[i + 2] = c.B;
                }
            }

            return PngWriter.WriteRgb(width, height, pixels);
        }

        public static string ToSvg(int columns, int rows, Func<int, int, bool> isDark, double moduleWidth, double moduleHeight, int quietZone, PdfColor dark, PdfColor? light)
        {
            var top = rows > 1 ? quietZone : 0;
            var width = (columns + 2 * quietZone) * moduleWidth;
            var height = (rows + 2 * top) * moduleHeight;
            var path = new StringBuilder();
            for (var y = 0; y < rows; y++)
            {
                var x = 0;
                while (x < columns)
                {
                    if (!isDark(x, y))
                    {
                        x++;
                        continue;
                    }

                    var start = x;
                    while (x < columns && isDark(x, y))
                    {
                        x++;
                    }

                    path.Append('M').Append(N((start + quietZone) * moduleWidth)).Append(' ').Append(N((y + top) * moduleHeight))
                        .Append('h').Append(N((x - start) * moduleWidth)).Append('v').Append(N(moduleHeight))
                        .Append('h').Append(N(-(x - start) * moduleWidth)).Append('z');
                }
            }

            var svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(N(width)).Append("\" height=\"").Append(N(height))
                .Append("\" viewBox=\"0 0 ").Append(N(width)).Append(' ').Append(N(height)).Append("\" shape-rendering=\"crispEdges\">");
            if (light.HasValue)
            {
                svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(light.Value).Append("\"/>");
            }

            svg.Append("<path fill=\"").Append(dark).Append("\" d=\"").Append(path).Append("\"/></svg>");
            return svg.ToString();
        }

        private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
