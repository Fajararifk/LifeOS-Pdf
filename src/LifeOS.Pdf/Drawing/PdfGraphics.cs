using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Drawing
{
    /// <summary>
    /// Draws on one page. Coordinates are in points (1/72 inch) with the origin at the
    /// TOP-LEFT corner and y growing downward — the way most UI and report code thinks.
    /// The conversion to PDF's bottom-left system happens here and nowhere else.
    /// Every operation is wrapped in q/Q so no state leaks between calls.
    /// </summary>
    public sealed class PdfGraphics
    {
        // Control-point distance that makes four cubic Béziers approximate a circle.
        private const double Kappa = 0.5522847498;

        // Annex C allows 28 nested q; every drawing call adds one level of its own.
        private const int MaxUserDepth = 27;

        private readonly PdfPage _page;
        private readonly StringBuilder _main = new StringBuilder();
        private readonly StringBuilder _underlay = new StringBuilder();

        // Where drawing operators go: the main content, or (inside DrawBehind) the underlay.
        private StringBuilder _ops;

        // Height of the coordinate frame for top-left → PDF conversion (the displayed height inside
        // a display-orientation frame, otherwise the page height).
        private double? _frameHeight;

        internal PdfGraphics(PdfPage page)
        {
            _page = page;
            _ops = _main;
        }

        internal string Content => _main.ToString();

        /// <summary>Drawing that goes on top of the page (for new pages: everything after the underlay).</summary>
        internal byte[] ContentBytes() => Bytes(_main);

        /// <summary>Drawing that goes behind the page's existing content (letterheads, backgrounds).</summary>
        internal byte[] UnderlayBytes() => Bytes(_underlay);

        internal int UnderlayLength => _underlay.Length;

        /// <summary>Whole content of a page created here: underlay first, then the rest.</summary>
        internal byte[] AllContentBytes()
        {
            if (_underlay.Length == 0)
            {
                return Bytes(_main);
            }

            var under = Bytes(_underlay);
            var main = Bytes(_main);
            var all = new byte[under.Length + main.Length];
            Buffer.BlockCopy(under, 0, all, 0, under.Length);
            Buffer.BlockCopy(main, 0, all, under.Length, main.Length);
            return all;
        }

        private static byte[] Bytes(StringBuilder sb)
        {
            var bytes = new byte[sb.Length];
            var chunk = new char[Math.Min(8192, Math.Max(1, sb.Length))];
            for (var pos = 0; pos < sb.Length; pos += chunk.Length)
            {
                var n = Math.Min(chunk.Length, sb.Length - pos);
                sb.CopyTo(pos, chunk, 0, n);
                for (var i = 0; i < n; i++)
                {
                    bytes[pos + i] = (byte)chunk[i];
                }
            }

            return bytes;
        }

        /// <summary>Runs <paramref name="draw"/> with its output placed behind everything else on the page.</summary>
        internal void DrawBehind(Action draw)
        {
            if (!ReferenceEquals(_ops, _main))
            {
                draw();
                return;
            }

            _ops = _underlay;
            try
            {
                draw();
            }
            finally
            {
                _ops = _main;
            }
        }

        /// <summary>Width of the page as it is displayed (rotation applied).</summary>
        internal double DisplayWidth => _page.Rotation % 180 == 0 ? _page.Width : _page.Height;

        internal double DisplayHeight => _page.Rotation % 180 == 0 ? _page.Height : _page.Width;

        /// <summary>
        /// Until disposed, coordinates are those of the page as displayed: (0, 0) is the top-left corner
        /// the reader sees, even on a page with /Rotate. Used by stamps and page numbers.
        /// </summary>
        internal IDisposable DisplayFrame()
        {
            var w = _page.Width;
            var h = _page.Height;
            Begin();
            // Display space (after the viewer turns the page clockwise) → page space.
            switch (_page.Rotation)
            {
                case 90: W("0").W("1").W("-1").W("0").W(w).W("0").Operator("cm"); break;
                case 180: W("-1").W("0").W("0").W("-1").W(w).W(h).Operator("cm"); break;
                case 270: W("0").W("-1").W("1").W("0").W("0").W(h).Operator("cm"); break;
            }

            var previous = _frameHeight;
            _frameHeight = DisplayHeight;
            return new FrameScope(this, previous);
        }

        private sealed class FrameScope : IDisposable
        {
            private readonly PdfGraphics _g;
            private readonly double? _previous;
            private bool _done;

            public FrameScope(PdfGraphics g, double? previous)
            {
                _g = g;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_done)
                {
                    return;
                }

                _done = true;
                _g._frameHeight = _previous;
                _g.End();
            }
        }

        internal PdfPage Page => _page;

        /// <summary>Draws a straight line.</summary>
        public void DrawLine(PdfPen pen, double x1, double y1, double x2, double y2)
        {
            RequirePen(pen);
            RequireFinite(x1, y1, x2, y2);
            Begin();
            ApplyPen(pen);
            W(x1).W(Y(y1)).W("m").W(x2).W(Y(y2)).W("l").Operator("S");
            End();
        }

        /// <summary>Strokes the outline of a rectangle.</summary>
        public void DrawRectangle(PdfPen pen, double x, double y, double width, double height)
        {
            RequirePen(pen);
            DrawRectangle(pen, null, x, y, width, height);
        }

        /// <summary>Fills a rectangle with a solid color.</summary>
        public void FillRectangle(PdfColor fill, double x, double y, double width, double height) =>
            DrawRectangle(null, fill, x, y, width, height);

        /// <summary>Draws a rectangle with an optional outline and an optional fill (at least one).</summary>
        public void DrawRectangle(PdfPen? pen, PdfColor? fill, double x, double y, double width, double height)
        {
            RequireBox(x, y, width, height);
            Begin();
            ApplyPaint(pen, fill);
            W(x).W(Y(y) - height).W(width).W(height).W("re").Operator(PaintOperator(pen, fill));
            End();
        }

        /// <summary>Draws a rectangle with rounded corners.</summary>
        public void DrawRoundedRectangle(PdfPen? pen, PdfColor? fill, double x, double y, double width, double height, double radius)
        {
            RequireBox(x, y, width, height);
            PdfFormat.RequireNonNegative(radius, nameof(radius));
            var r = Math.Min(radius, Math.Min(width / 2, height / 2));
            if (r <= 0)
            {
                DrawRectangle(pen, fill, x, y, width, height);
                return;
            }

            var left = x;
            var right = x + width;
            var top = Y(y);
            var bottom = top - height;
            var k = Kappa * r;

            Begin();
            ApplyPaint(pen, fill);
            W(left + r).W(bottom).Operator("m");
            W(right - r).W(bottom).Operator("l");
            W(right - r + k).W(bottom).W(right).W(bottom + r - k).W(right).W(bottom + r).Operator("c");
            W(right).W(top - r).Operator("l");
            W(right).W(top - r + k).W(right - r + k).W(top).W(right - r).W(top).Operator("c");
            W(left + r).W(top).Operator("l");
            W(left + r - k).W(top).W(left).W(top - r + k).W(left).W(top - r).Operator("c");
            W(left).W(bottom + r).Operator("l");
            W(left).W(bottom + r - k).W(left + r - k).W(bottom).W(left + r).W(bottom).Operator("c");
            W("h").Operator(PaintOperator(pen, fill));
            End();
        }

        /// <summary>Draws an ellipse inside the given box (a circle when width == height).</summary>
        public void DrawEllipse(PdfPen? pen, PdfColor? fill, double x, double y, double width, double height)
        {
            RequireBox(x, y, width, height);
            var rx = width / 2;
            var ry = height / 2;
            var cx = x + rx;
            var cy = Y(y) - ry;
            var kx = Kappa * rx;
            var ky = Kappa * ry;

            Begin();
            ApplyPaint(pen, fill);
            W(cx + rx).W(cy).Operator("m");
            W(cx + rx).W(cy + ky).W(cx + kx).W(cy + ry).W(cx).W(cy + ry).Operator("c");
            W(cx - kx).W(cy + ry).W(cx - rx).W(cy + ky).W(cx - rx).W(cy).Operator("c");
            W(cx - rx).W(cy - ky).W(cx - kx).W(cy - ry).W(cx).W(cy - ry).Operator("c");
            W(cx + kx).W(cy - ry).W(cx + rx).W(cy - ky).W(cx + rx).W(cy).Operator("c");
            W("h").Operator(PaintOperator(pen, fill));
            End();
        }

        /// <summary>
        /// Draws a single line of text. <paramref name="y"/> is the TOP of the text line;
        /// line breaks are turned into spaces (use <see cref="DrawTextBox"/> for wrapping).
        /// </summary>
        public void DrawText(string text, PdfFont font, double x, double y, PdfColor? color = null, TextAlign align = TextAlign.Left)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            RequireFont(font);
            RequireFinite(x, y);
            var line = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            WriteTextLine(line, font, x, y, color ?? PdfColor.Black, align);
        }

        /// <summary>
        /// Draws text wrapped to <paramref name="width"/>. Lines break at spaces; words longer
        /// than the box are split. '\n' starts a new paragraph. Returns the height used, in points.
        /// </summary>
        public double DrawTextBox(string text, PdfFont font, double x, double y, double width, PdfColor? color = null,
            TextAlign align = TextAlign.Left, double lineSpacing = 1.2)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            RequireFont(font);
            RequireFinite(x, y);
            PdfFormat.RequireFinite(width, nameof(width));
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Text box width must be greater than zero.");
            }

            PdfFormat.RequireFinite(lineSpacing, nameof(lineSpacing));
            if (lineSpacing <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineSpacing), lineSpacing, "Line spacing must be greater than zero.");
            }

            var lines = WrapText(text, font, width);
            var lineHeight = font.Size * lineSpacing;
            var anchorX = align == TextAlign.Left ? x : align == TextAlign.Center ? x + width / 2 : x + width;
            for (var i = 0; i < lines.Count; i++)
            {
                WriteTextLine(lines[i], font, anchorX, y + i * lineHeight, color ?? PdfColor.Black, align);
            }

            return lines.Count * lineHeight;
        }

        /// <summary>Draws an image stretched to the given box.</summary>
        public void DrawImage(PdfImage image, double x, double y, double width, double height)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            RequireBox(x, y, width, height);
            var name = _page.UseImage(image);
            Begin();
            W(width).W("0").W("0").W(height).W(x).W(Y(y) - height).Operator("cm");
            W("/" + name).Operator("Do");
            End();
        }

        /// <summary>
        /// Draws a page of an opened PDF, scaled into the box (as it is displayed: its crop box and
        /// rotation are applied). Use it for n-up sheets, booklets and letterheads. The page's
        /// annotations (links, notes) are not drawn. Pages that were changed or created in memory
        /// are taken from a saved snapshot of their document.
        /// </summary>
        public void DrawPage(PdfPage page, double x, double y, double width, double height)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            if (!page.IsImported)
            {
                // A page built in memory: save its document and use the copy of the page.
                page = PdfDocument.Pristine(OpenedCopy(page));
            }

            RequireBox(x, y, width, height);
            var source = PdfDocument.Pristine(page);
            var sp = source.Source!;
            var name = _page.UseForm(sp);
            var box = sp.CropBox ?? sp.MediaBox;
            double bx = box[0], by = box[1], bw = box[2] - box[0], bh = box[3] - box[1];
            var rotation = source.Rotation;
            var turned = rotation == 90 || rotation == 270;
            var sx = width / (turned ? bh : bw);
            var sy = height / (turned ? bw : bh);
            var left = x;
            var bottom = Y(y) - height;
            // Map the box to the target rectangle, turning it clockwise by the page rotation.
            double a, b, c, d, e, f;
            switch (rotation)
            {
                case 90:
                    a = 0; b = -sy; c = sx; d = 0; e = left - sx * by; f = bottom + sy * (bw + bx);
                    break;
                case 180:
                    a = -sx; b = 0; c = 0; d = -sy; e = left + sx * (bw + bx); f = bottom + sy * (bh + by);
                    break;
                case 270:
                    a = 0; b = sy; c = -sx; d = 0; e = left + sx * (bh + by); f = bottom - sy * bx;
                    break;
                default:
                    a = sx; b = 0; c = 0; d = sy; e = left - sx * bx; f = bottom - sy * by;
                    break;
            }

            Begin();
            W(a).W(b).W(c).W(d).W(e).W(f).Operator("cm");
            W("/" + name).Operator("Do");
            End();
        }

        private static PdfPage OpenedCopy(PdfPage page)
        {
            var owner = page.Document;
            var index = -1;
            for (var i = 0; i < owner.Pages.Count; i++)
            {
                if (ReferenceEquals(owner.Pages[i], page))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                throw new ArgumentException("The page was removed from its document.", nameof(page));
            }

            return PdfDocument.Open(owner.ToArray()).Pages[index];
        }

        /// <summary>Draws an image with the given width; height follows the image's aspect ratio.</summary>
        public void DrawImage(PdfImage image, double x, double y, double width)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            PdfFormat.RequireNonNegative(width, nameof(width));
            DrawImage(image, x, y, width, width * image.PixelHeight / image.PixelWidth);
        }

        /// <summary>
        /// Draws a QR code as vectors in a square of <paramref name="size"/> points at (x, y),
        /// including the quiet zone. All dark modules form one path filled at once, so viewers
        /// show no hairlines between modules.
        /// </summary>
        /// <param name="qr">The code to draw.</param>
        /// <param name="x">Left edge.</param>
        /// <param name="y">Top edge.</param>
        /// <param name="size">Width and height in points, quiet zone included.</param>
        /// <param name="dark">Module color (default black).</param>
        /// <param name="light">Background color; null leaves the background transparent (default white).</param>
        /// <param name="quietZone">Light border in modules; 4 is the standard minimum for reliable scanning.</param>
        /// <param name="logo">Optional image in the center. Requires error correction Quartile or High.</param>
        /// <param name="logoScale">Logo width as a fraction of the code width (at most 0.25).</param>
        public void DrawQrCode(Barcodes.QrCode qr, double x, double y, double size, PdfColor? dark = null, PdfColor? light = null,
            int quietZone = 4, PdfImage? logo = null, double logoScale = 0.2)
        {
            if (qr == null)
            {
                throw new ArgumentNullException(nameof(qr));
            }

            RequireBox(x, y, size, size);
            if (quietZone < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quietZone), quietZone, "The quiet zone must not be negative.");
            }

            if (logo != null)
            {
                if (qr.ErrorCorrection == Barcodes.QrErrorCorrection.Low || qr.ErrorCorrection == Barcodes.QrErrorCorrection.Medium)
                {
                    throw new ArgumentException("A logo hides part of the code: encode it with QrErrorCorrection.Quartile or High so it stays readable.", nameof(logo));
                }

                PdfFormat.RequireFinite(logoScale, nameof(logoScale));
                if (logoScale <= 0 || logoScale > 0.25)
                {
                    throw new ArgumentOutOfRangeException(nameof(logoScale), logoScale, "Logo scale must be greater than 0 and at most 0.25 of the code width.");
                }
            }

            var modules = qr.Size + 2 * quietZone;
            var unit = size / modules;
            var background = light ?? PdfColor.White;
            Begin();
            SetColor(background, "rg");
            W(x).W(Y(y) - size).W(size).W(size).W("re").Operator("f");
            SetColor(dark ?? PdfColor.Black, "rg");
            var any = false;
            for (var row = 0; row < qr.Size; row++)
            {
                var col = 0;
                while (col < qr.Size)
                {
                    if (!qr.IsDark(col, row))
                    {
                        col++;
                        continue;
                    }

                    var start = col;
                    while (col < qr.Size && qr.IsDark(col, row))
                    {
                        col++;
                    }

                    var top = y + (row + quietZone) * unit;
                    W(x + (start + quietZone) * unit).W(Y(top) - unit).W((col - start) * unit).W(unit).Operator("re");
                    any = true;
                }
            }

            if (any)
            {
                Operator("f");
            }

            End();

            if (logo != null)
            {
                // A light pad around the logo keeps its edge from touching modules.
                var w = size * logoScale;
                var h = w * logo.PixelHeight / logo.PixelWidth;
                var pad = unit;
                var cx = x + size / 2;
                var cy = y + size / 2;
                FillRectangle(background, cx - w / 2 - pad, cy - h / 2 - pad, w + 2 * pad, h + 2 * pad);
                DrawImage(logo, cx - w / 2, cy - h / 2, w, h);
            }
        }

        /// <summary>
        /// Draws a linear barcode as vectors in the box (x, y, width, height). The box includes the
        /// quiet zones; with <paramref name="showText"/> the human-readable text sits under the bars.
        /// </summary>
        /// <param name="barcode">The barcode to draw.</param>
        /// <param name="x">Left edge.</param>
        /// <param name="y">Top edge.</param>
        /// <param name="width">Total width in points, quiet zones included.</param>
        /// <param name="height">Total height in points, text included.</param>
        /// <param name="dark">Bar color (default black).</param>
        /// <param name="light">Background color; default white.</param>
        /// <param name="showText">Print <see cref="Barcodes.Barcode.Text"/> centered below the bars.</param>
        /// <param name="font">Font of the text (default Helvetica, sized to fit).</param>
        public void DrawBarcode(Barcodes.Barcode barcode, double x, double y, double width, double height, PdfColor? dark = null, PdfColor? light = null,
            bool showText = true, PdfFont? font = null)
        {
            if (barcode == null)
            {
                throw new ArgumentNullException(nameof(barcode));
            }

            RequireBox(x, y, width, height);
            var quiet = barcode.QuietZone;
            var unit = width / (barcode.ModuleCount + 2 * quiet);
            var textFont = font;
            double textHeight = 0;
            if (showText)
            {
                // Default: as large as possible up to 10 pt while the text fits under the bars.
                textFont = font ?? new PdfFont(Fonts.StandardFont.Helvetica, 10);
                var available = barcode.ModuleCount * unit;
                var measured = textFont.MeasureWidth(barcode.Text);
                if (font == null && measured > available && measured > 0)
                {
                    textFont = textFont.WithSize(Math.Max(4, 10 * available / measured));
                }

                // Text line plus a gap of a quarter of the font size between bars and text.
                textHeight = textFont.Size * 1.45;
            }

            var barHeight = height - textHeight;
            if (barHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "The height leaves no room for bars under the text; make it larger or set showText: false.");
            }

            Begin();
            SetColor(light ?? PdfColor.White, "rg");
            W(x).W(Y(y) - height).W(width).W(height).W("re").Operator("f");
            SetColor(dark ?? PdfColor.Black, "rg");
            var i = 0;
            while (i < barcode.ModuleCount)
            {
                if (!barcode.IsBar(i))
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < barcode.ModuleCount && barcode.IsBar(i))
                {
                    i++;
                }

                W(x + (start + quiet) * unit).W(Y(y) - barHeight).W((i - start) * unit).W(barHeight).Operator("re");
            }

            Operator("f");
            End();

            if (showText)
            {
                DrawText(barcode.Text, textFont!, x + width / 2, y + barHeight + textFont!.Size * 0.25, dark ?? PdfColor.Black, TextAlign.Center);
            }
        }

        /// <summary>
        /// Saves the graphics state. Use with <c>using</c>; transformations, opacity and
        /// clipping set afterwards are undone when the returned object is disposed.
        /// </summary>
        public PdfGraphicsState Save()
        {
            if (Depth >= MaxUserDepth)
            {
                throw new InvalidOperationException("Graphics states are nested more than " + MaxUserDepth +
                    " levels deep. PDF viewers only guarantee 28 levels (ISO 32000-1 Annex C); restore earlier states first.");
            }

            _ops.Append("q\n");
            Depth++;
            return new PdfGraphicsState(this, Depth);
        }

        /// <summary>Restores the state saved by the matching <see cref="Save"/>.</summary>
        public void Restore()
        {
            if (Depth == 0)
            {
                throw new InvalidOperationException("Restore() was called without a matching Save().");
            }

            _ops.Append("Q\n");
            Depth--;
        }

        /// <summary>Moves the origin by (dx, dy) points; positive dy moves down.</summary>
        public void Translate(double dx, double dy)
        {
            RequireFinite(dx, dy);
            W("1").W("0").W("0").W("1").W(dx).W(-dy).Operator("cm");
        }

        /// <summary>Rotates everything drawn afterwards around (x, y). Positive degrees turn clockwise, as on screen.</summary>
        public void RotateAt(double degrees, double x, double y)
        {
            RequireFinite(degrees, x, y);
            // PDF's y axis points up, so a clockwise turn on screen is a negative angle there.
            var radians = -degrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var px = x;
            var py = Y(y);
            // Row-vector matrix for translate(-p) · rotate · translate(p).
            W(cos).W(sin).W(-sin).W(cos).W(px - px * cos + py * sin).W(py - px * sin - py * cos).Operator("cm");
        }

        /// <summary>Scales everything drawn afterwards around (x, y). Negative factors mirror.</summary>
        public void ScaleAt(double scaleX, double scaleY, double x, double y)
        {
            RequireFinite(scaleX, scaleY, x, y);
            if (scaleX == 0 || scaleY == 0)
            {
                throw new ArgumentOutOfRangeException(scaleX == 0 ? nameof(scaleX) : nameof(scaleY), "Scale factor must not be zero.");
            }

            var px = x;
            var py = Y(y);
            W(scaleX).W("0").W("0").W(scaleY).W(px - scaleX * px).W(py - scaleY * py).Operator("cm");
        }

        /// <summary>Sets the opacity (0 = invisible, 1 = opaque) for fills, strokes, text and images.</summary>
        public void SetOpacity(double opacity) => SetOpacity(opacity, opacity);

        /// <summary>Sets fill opacity (fills, text, images) and stroke opacity separately.</summary>
        public void SetOpacity(double fill, double stroke)
        {
            RequireUnit(fill, nameof(fill));
            RequireUnit(stroke, nameof(stroke));
            var name = _page.UseOpacity(Math.Round(fill, 4), Math.Round(stroke, 4));
            W("/" + name).Operator("gs");
        }

        /// <summary>Limits drawing to a rectangle until the state is restored.</summary>
        public void ClipRectangle(double x, double y, double width, double height)
        {
            RequireBox(x, y, width, height);
            W(x).W(Y(y) - height).W(width).W(height).W("re").W("W").Operator("n");
        }

        internal int Depth { get; private set; }

        internal void RestoreTo(int depth)
        {
            while (Depth > depth && Depth > 0)
            {
                Restore();
            }
        }

        internal static List<string> WrapText(string text, PdfFont font, double width)
        {
            var lines = new List<string>();
            var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var paragraph in paragraphs)
            {
                var current = string.Empty;
                foreach (var word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (font.MeasureWidth(candidate) <= width)
                    {
                        current = candidate;
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        lines.Add(current);
                    }

                    current = word;
                    // A single word wider than the box is split by characters.
                    while (font.MeasureWidth(current) > width && current.Length > 1)
                    {
                        var cut = 1;
                        while (cut < current.Length && font.MeasureWidth(current.Substring(0, cut + 1)) <= width)
                        {
                            cut++;
                        }

                        lines.Add(current.Substring(0, cut));
                        current = current.Substring(cut);
                    }
                }

                lines.Add(current);
            }

            return lines;
        }

        private void WriteTextLine(string line, PdfFont font, double x, double top, PdfColor color, TextAlign align)
        {
            var run = font.Map(line);
            var bytes = run.Codes;
            if (bytes.Length == 0)
            {
                return;
            }

            var textWidth = run.Width * font.Size / 1000.0;
            var left = align == TextAlign.Left ? x : align == TextAlign.Center ? x - textWidth / 2 : x - textWidth;
            var baseline = Y(top + font.Ascent);
            var name = _page.UseFont(font.Face, run);

            Begin();
            SetColor(color, "rg");
            Operator("BT");
            _ops.Append('/').Append(name).Append(' ');
            W(font.Size).Operator("Tf");
            W("1 0 0 1").W(left).W(baseline).Operator("Tm");
            _ops.Append('<');
            PdfFormat.AppendHex(_ops, bytes, 0, bytes.Length);
            _ops.Append("> Tj\nET\n");
            End();
        }

        /// <summary>
        /// Draws an already mapped glyph run with its baseline at <paramref name="baseline"/>
        /// (top-left coordinates). Per-glyph adjustments (kerning) plus optional extra
        /// advance in points (justification) are written with TJ (ISO 32000-1 §9.4.3);
        /// TJ is needed because word spacing (Tw) does not apply to 2-byte Identity-H codes.
        /// </summary>
        internal void DrawGlyphRun(PdfFont font, TextRun run, double[]? extraPoints, double x, double baseline, PdfColor color)
        {
            if (run.Codes.Length == 0)
            {
                return;
            }

            var name = _page.UseFont(font.Face, run);
            Begin();
            SetColor(color, "rg");
            Operator("BT");
            _ops.Append('/').Append(name).Append(' ');
            W(font.Size).Operator("Tf");
            W("1 0 0 1").W(x).W(Y(baseline)).Operator("Tm");

            var bpg = run.BytesPerGlyph;
            var glyphCount = run.Glyphs.Length;
            var any = false;
            for (var i = 0; i < glyphCount - 1 && !any; i++)
            {
                any = Math.Round(run.Adjustment(i) + (extraPoints != null ? extraPoints[i] * 1000.0 / font.Size : 0), 3) != 0;
            }

            if (!any)
            {
                _ops.Append('<');
                PdfFormat.AppendHex(_ops, run.Codes, 0, run.Codes.Length);
                _ops.Append("> Tj\n");
            }
            else
            {
                _ops.Append("[<");
                for (var i = 0; i < glyphCount; i++)
                {
                    PdfFormat.AppendHex(_ops, run.Codes, i * bpg, bpg);
                    var adjust = run.Adjustment(i) + (extraPoints != null ? extraPoints[i] * 1000.0 / font.Size : 0);
                    var rounded = Math.Round(adjust, 3);
                    if (rounded != 0 && i < glyphCount - 1)
                    {
                        // TJ numbers are subtracted from the position: negative = move right.
                        _ops.Append("> ");
                        PdfFormat.AppendNumber(_ops, -rounded);
                        _ops.Append(" <");
                    }
                }

                _ops.Append(">] TJ\n");
            }

            Operator("ET");
            End();
        }

        private double Y(double y) => (_frameHeight ?? _page.Height) - y;

        private static string N(double v) => PdfFormat.Number(v);

        private void Begin() => _ops.Append("q\n");

        private void End() => _ops.Append("Q\n");

        private void Op(params string[] parts)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    _ops.Append(' ');
                }

                _ops.Append(parts[i]);
            }

            _ops.Append('\n');
        }

        // Allocation-free writers for the hot paths: a number or word followed by a space,
        // and an operator ending the line.
        private PdfGraphics W(double value)
        {
            PdfFormat.AppendNumber(_ops, value);
            _ops.Append(' ');
            return this;
        }

        private PdfGraphics W(string word)
        {
            _ops.Append(word).Append(' ');
            return this;
        }

        private void Operator(string op) => _ops.Append(op).Append('\n');

        private void SetColor(PdfColor c, string op)
        {
            W(c.R / 255.0).W(c.G / 255.0).W(c.B / 255.0).Operator(op);
        }

        /// <summary>Length of the content written so far (no string is built).</summary>
        internal int ContentLength => _main.Length + _underlay.Length;

        private void ApplyPen(PdfPen pen)
        {
            SetColor(pen.Color, "RG");
            W(pen.Width).Operator("w");
            if (pen.DashPattern != null && pen.DashPattern.Length > 0)
            {
                var parts = new string[pen.DashPattern.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    parts[i] = N(pen.DashPattern[i]);
                }

                Op("[" + string.Join(" ", parts) + "]", "0", "d");
            }
        }

        private void ApplyPaint(PdfPen? pen, PdfColor? fill)
        {
            if (pen != null)
            {
                ApplyPen(pen);
            }

            if (fill.HasValue)
            {
                SetColor(fill.Value, "rg");
            }
        }

        private static string PaintOperator(PdfPen? pen, PdfColor? fill)
        {
            if (pen != null && fill.HasValue)
            {
                return "B";
            }

            if (pen != null)
            {
                return "S";
            }

            if (fill.HasValue)
            {
                return "f";
            }

            throw new ArgumentException("Provide a pen, a fill color, or both.");
        }

        private static void RequirePen(PdfPen pen)
        {
            if (pen == null)
            {
                throw new ArgumentNullException(nameof(pen));
            }
        }

        private static void RequireFont(PdfFont font)
        {
            if (font == null)
            {
                throw new ArgumentNullException(nameof(font));
            }
        }

        private static void RequireFinite(params double[] values)
        {
            foreach (var v in values)
            {
                PdfFormat.RequireFinite(v, "coordinate");
            }
        }

        private static void RequireUnit(double value, string paramName)
        {
            PdfFormat.RequireFinite(value, paramName);
            if (value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Opacity must be between 0 and 1.");
            }
        }

        private static void RequireBox(double x, double y, double width, double height)
        {
            RequireFinite(x, y);
            PdfFormat.RequireNonNegative(width, nameof(width));
            PdfFormat.RequireNonNegative(height, nameof(height));
        }
    }
}
