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
        private readonly StringBuilder _ops = new StringBuilder();

        internal PdfGraphics(PdfPage page)
        {
            _page = page;
        }

        internal string Content => _ops.ToString();

        /// <summary>Draws a straight line.</summary>
        public void DrawLine(PdfPen pen, double x1, double y1, double x2, double y2)
        {
            RequirePen(pen);
            RequireFinite(x1, y1, x2, y2);
            Begin();
            ApplyPen(pen);
            Op(N(x1), N(Y(y1)), "m", N(x2), N(Y(y2)), "l", "S");
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
            Op(N(x), N(Y(y) - height), N(width), N(height), "re", PaintOperator(pen, fill));
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
            Op(N(left + r), N(bottom), "m");
            Op(N(right - r), N(bottom), "l");
            Op(N(right - r + k), N(bottom), N(right), N(bottom + r - k), N(right), N(bottom + r), "c");
            Op(N(right), N(top - r), "l");
            Op(N(right), N(top - r + k), N(right - r + k), N(top), N(right - r), N(top), "c");
            Op(N(left + r), N(top), "l");
            Op(N(left + r - k), N(top), N(left), N(top - r + k), N(left), N(top - r), "c");
            Op(N(left), N(bottom + r), "l");
            Op(N(left), N(bottom + r - k), N(left + r - k), N(bottom), N(left + r), N(bottom), "c");
            Op("h", PaintOperator(pen, fill));
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
            Op(N(cx + rx), N(cy), "m");
            Op(N(cx + rx), N(cy + ky), N(cx + kx), N(cy + ry), N(cx), N(cy + ry), "c");
            Op(N(cx - kx), N(cy + ry), N(cx - rx), N(cy + ky), N(cx - rx), N(cy), "c");
            Op(N(cx - rx), N(cy - ky), N(cx - kx), N(cy - ry), N(cx), N(cy - ry), "c");
            Op(N(cx + kx), N(cy - ry), N(cx + rx), N(cy - ky), N(cx + rx), N(cy), "c");
            Op("h", PaintOperator(pen, fill));
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
            Op(N(width), "0", "0", N(height), N(x), N(Y(y) - height), "cm");
            Op("/" + name, "Do");
            End();
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
            Op(ColorOperands(background), "rg");
            Op(N(x), N(Y(y) - size), N(size), N(size), "re", "f");
            Op(ColorOperands(dark ?? PdfColor.Black), "rg");
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
                    Op(N(x + (start + quietZone) * unit), N(Y(top) - unit), N((col - start) * unit), N(unit), "re");
                    any = true;
                }
            }

            if (any)
            {
                Op("f");
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
            Op(ColorOperands(light ?? PdfColor.White), "rg");
            Op(N(x), N(Y(y) - height), N(width), N(height), "re", "f");
            Op(ColorOperands(dark ?? PdfColor.Black), "rg");
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

                Op(N(x + (start + quiet) * unit), N(Y(y) - barHeight), N((i - start) * unit), N(barHeight), "re");
            }

            Op("f");
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
            Op("1", "0", "0", "1", N(dx), N(-dy), "cm");
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
            Op(N(cos), N(sin), N(-sin), N(cos), N(px - px * cos + py * sin), N(py - px * sin - py * cos), "cm");
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
            Op(N(scaleX), "0", "0", N(scaleY), N(px - scaleX * px), N(py - scaleY * py), "cm");
        }

        /// <summary>Sets the opacity (0 = invisible, 1 = opaque) for fills, strokes, text and images.</summary>
        public void SetOpacity(double opacity) => SetOpacity(opacity, opacity);

        /// <summary>Sets fill opacity (fills, text, images) and stroke opacity separately.</summary>
        public void SetOpacity(double fill, double stroke)
        {
            RequireUnit(fill, nameof(fill));
            RequireUnit(stroke, nameof(stroke));
            var name = _page.UseOpacity(Math.Round(fill, 4), Math.Round(stroke, 4));
            Op("/" + name, "gs");
        }

        /// <summary>Limits drawing to a rectangle until the state is restored.</summary>
        public void ClipRectangle(double x, double y, double width, double height)
        {
            RequireBox(x, y, width, height);
            Op(N(x), N(Y(y) - height), N(width), N(height), "re", "W", "n");
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
            Op(ColorOperands(color), "rg");
            Op("BT");
            Op("/" + name, N(font.Size), "Tf");
            Op("1", "0", "0", "1", N(left), N(baseline), "Tm");
            Op("<" + PdfFormat.Hex(bytes) + ">", "Tj");
            Op("ET");
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
            Op(ColorOperands(color), "rg");
            Op("BT");
            Op("/" + name, N(font.Size), "Tf");
            Op("1", "0", "0", "1", N(x), N(Y(baseline)), "Tm");

            var bpg = run.BytesPerGlyph;
            var glyphCount = run.Glyphs.Length;
            var tj = new StringBuilder("[<");
            var any = false;
            for (var i = 0; i < glyphCount; i++)
            {
                for (var b = 0; b < bpg; b++)
                {
                    tj.Append(run.Codes[i * bpg + b].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }

                var adjust = run.Adjustments[i] + (extraPoints != null ? extraPoints[i] * 1000.0 / font.Size : 0);
                var rounded = Math.Round(adjust, 3);
                if (rounded != 0 && i < glyphCount - 1)
                {
                    // TJ numbers are subtracted from the position: negative = move right.
                    tj.Append("> ").Append(N(-rounded)).Append(" <");
                    any = true;
                }
            }

            tj.Append(">]");
            if (any)
            {
                Op(tj.ToString(), "TJ");
            }
            else
            {
                Op("<" + PdfFormat.Hex(run.Codes) + ">", "Tj");
            }

            Op("ET");
            End();
        }

        private double Y(double y) => _page.Height - y;

        private static string N(double v) => PdfFormat.Number(v);

        private static string ColorOperands(PdfColor c) => N(c.R / 255.0) + " " + N(c.G / 255.0) + " " + N(c.B / 255.0);

        private void Begin() => _ops.Append("q\n");

        private void End() => _ops.Append("Q\n");

        private void Op(params string[] parts)
        {
            _ops.Append(string.Join(" ", parts)).Append('\n');
        }

        private void ApplyPen(PdfPen pen)
        {
            Op(ColorOperands(pen.Color), "RG");
            Op(N(pen.Width), "w");
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
                Op(ColorOperands(fill.Value), "rg");
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
