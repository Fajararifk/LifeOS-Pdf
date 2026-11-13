using System;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>
    /// Converts default user space of a page to the coordinates of the page as displayed:
    /// origin at the top-left of the crop box, the page turned by /Rotate (§8.3.2.3, §14.11.2).
    /// </summary>
    internal sealed class PageGeometry
    {
        private readonly double _x0, _y0, _w, _h;
        private readonly int _rotation;

        public PageGeometry(double[] box, int rotation)
        {
            _x0 = box[0];
            _y0 = box[1];
            _w = box[2] - box[0];
            _h = box[3] - box[1];
            _rotation = rotation;
            Width = rotation % 180 == 0 ? _w : _h;
            Height = rotation % 180 == 0 ? _h : _w;
        }

        /// <summary>Displayed width and height.</summary>
        public double Width { get; }

        public double Height { get; }

        public void ToDisplay(double x, double y, out double dx, out double dy)
        {
            var u = x - _x0;
            var v = y - _y0;
            double du, dv; // display, bottom-left origin
            switch (_rotation)
            {
                case 90: du = v; dv = _w - u; break;
                case 180: du = _w - u; dv = _h - v; break;
                case 270: du = _h - v; dv = u; break;
                default: du = u; dv = v; break;
            }

            dx = du;
            dy = Height - dv;
        }

        /// <summary>Inverse of <see cref="ToDisplay"/>.</summary>
        public void ToUser(double dx, double dy, out double x, out double y)
        {
            var du = dx;
            var dv = Height - dy;
            double u, v;
            switch (_rotation)
            {
                case 90: v = du; u = _w - dv; break;
                case 180: u = _w - du; v = _h - dv; break;
                case 270: v = _h - du; u = dv; break;
                default: u = du; v = dv; break;
            }

            x = u + _x0;
            y = v + _y0;
        }

        public PdfRect Rect(double minX, double minY, double maxX, double maxY)
        {
            ToDisplay(minX, minY, out var ax, out var ay);
            ToDisplay(maxX, maxY, out var bx, out var by);
            return Safe(ax, ay, bx, by);
        }

        /// <summary>Damaged files can produce huge or non-finite coordinates: clamp instead of failing.</summary>
        private static PdfRect Safe(double x1, double y1, double x2, double y2)
        {
            double C(double v) => double.IsNaN(v) ? 0 : Math.Max(-1e7, Math.Min(1e7, v));
            return PdfRect.FromPoints(C(x1), C(y1), C(x2), C(y2));
        }

        public PdfRect Quad(params double[] xy)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var i = 0; i + 1 < xy.Length; i += 2)
            {
                ToDisplay(xy[i], xy[i + 1], out var x, out var y);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            return Safe(minX, minY, maxX, maxY);
        }
    }
}
