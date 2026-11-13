using System.Collections.Generic;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>
    /// A positioned drawing instruction produced by layout (top-left coordinates in points).
    /// Layouters produce items relative to their own origin; containers translate them.
    /// </summary>
    internal abstract class DrawItem
    {
        public abstract void Translate(double dx, double dy);

        public abstract void Draw(PdfGraphics g);
    }

    internal sealed class TextItem : DrawItem
    {
        public TextItem(PdfFont font, TextRun run, double[]? extra, double x, double baseline, PdfColor color)
        {
            Font = font;
            Run = run;
            Extra = extra;
            X = x;
            Baseline = baseline;
            Color = color;
        }

        public PdfFont Font { get; }

        public TextRun Run { get; }

        public double[]? Extra { get; }

        public double X { get; private set; }

        public double Baseline { get; private set; }

        public PdfColor Color { get; }

        public override void Translate(double dx, double dy)
        {
            X += dx;
            Baseline += dy;
        }

        public override void Draw(PdfGraphics g) => g.DrawGlyphRun(Font, Run, Extra, X, Baseline, Color);
    }

    internal sealed class LineItem : DrawItem
    {
        public LineItem(PdfPen pen, double x1, double y1, double x2, double y2)
        {
            Pen = pen;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public PdfPen Pen { get; }

        public double X1 { get; private set; }

        public double Y1 { get; private set; }

        public double X2 { get; private set; }

        public double Y2 { get; private set; }

        public override void Translate(double dx, double dy)
        {
            X1 += dx;
            X2 += dx;
            Y1 += dy;
            Y2 += dy;
        }

        public override void Draw(PdfGraphics g) => g.DrawLine(Pen, X1, Y1, X2, Y2);
    }

    internal sealed class RectItem : DrawItem
    {
        public RectItem(double x, double y, double width, double height, PdfColor? fill, PdfPen? stroke)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Fill = fill;
            Stroke = stroke;
        }

        public double X { get; private set; }

        public double Y { get; private set; }

        public double Width { get; }

        public double Height { get; }

        public PdfColor? Fill { get; }

        public PdfPen? Stroke { get; }

        public override void Translate(double dx, double dy)
        {
            X += dx;
            Y += dy;
        }

        public override void Draw(PdfGraphics g) => g.DrawRectangle(Stroke, Fill, X, Y, Width, Height);
    }

    internal sealed class ImageItem : DrawItem
    {
        public ImageItem(PdfImage image, double x, double y, double width, double height)
        {
            Image = image;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public PdfImage Image { get; }

        public double X { get; private set; }

        public double Y { get; private set; }

        public double Width { get; }

        public double Height { get; }

        public override void Translate(double dx, double dy)
        {
            X += dx;
            Y += dy;
        }

        public override void Draw(PdfGraphics g) => g.DrawImage(Image, X, Y, Width, Height);
    }

    internal static class DrawItemList
    {
        public static void TranslateAll(List<DrawItem> items, double dx, double dy)
        {
            foreach (var item in items)
            {
                item.Translate(dx, dy);
            }
        }
    }
}
