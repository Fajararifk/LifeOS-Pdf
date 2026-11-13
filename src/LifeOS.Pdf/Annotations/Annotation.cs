using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf.Annotations
{
    /// <summary>
    /// An annotation on a page (ISO 32000-1 §12.5). Rectangles are stored in top-left
    /// coordinates; the writer converts them. Every visible annotation carries its own
    /// appearance stream (§12.5.5) so it looks the same in every viewer.
    /// </summary>
    internal abstract class Annotation
    {
        protected Annotation(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public abstract string Subtype { get; }

        /// <summary>Annotation flags (§12.5.3): 4 = Print.</summary>
        public virtual int Flags => 4;

        public string? Contents { get; set; }

        public string? Author { get; set; }

        public PdfColor? Color { get; set; }

        /// <summary>Appearance content in the annotation's own space (origin bottom-left of the rectangle), or null.</summary>
        public virtual Appearance? CreateAppearance() => null;
    }

    /// <summary>A form XObject used as the normal appearance of an annotation.</summary>
    internal sealed class Appearance
    {
        public StringBuilder Content { get; } = new StringBuilder();

        /// <summary>Resource name → font face used by the content.</summary>
        public Dictionary<string, PdfFontFace> Fonts { get; } = new Dictionary<string, PdfFontFace>(StringComparer.Ordinal);

        /// <summary>Blend mode written to an inline graphics state named /GS0, or null.</summary>
        public string? BlendMode { get; set; }

        public Appearance Op(params string[] parts)
        {
            Content.Append(string.Join(" ", parts)).Append('\n');
            return this;
        }

        public static string N(double v) => PdfFormat.Number(v);

        public static string Rgb(PdfColor c) => N(c.R / 255.0) + " " + N(c.G / 255.0) + " " + N(c.B / 255.0);
    }

    internal sealed class LinkAnnotation : Annotation
    {
        private static readonly string[] AllowedSchemes = { "http", "https", "mailto", "tel" };

        public LinkAnnotation(double x, double y, double w, double h, string? uri, PdfDestination? destination)
            : base(x, y, w, h)
        {
            Uri = uri;
            Destination = destination;
        }

        public override string Subtype => "Link";

        public override int Flags => 0;

        /// <summary>Escaped ASCII URI, or null for an internal link.</summary>
        public string? Uri { get; }

        public PdfDestination? Destination { get; }

        /// <summary>Validates and escapes a URI (§12.6.4.7: 7-bit ASCII). Only safe schemes are allowed.</summary>
        public static string NormalizeUri(string uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (!System.Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed) || Array.IndexOf(AllowedSchemes, parsed.Scheme.ToLowerInvariant()) < 0)
            {
                throw new ArgumentException("'" + uri + "' is not an allowed link. Use an absolute http, https, mailto or tel URI (other schemes such as javascript: are refused).", nameof(uri));
            }

            var text = parsed.AbsoluteUri;
            foreach (var c in text)
            {
                if (c > 127)
                {
                    throw new ArgumentException("The link '" + uri + "' contains characters that cannot be escaped to ASCII.", nameof(uri));
                }
            }

            return text;
        }
    }

    internal sealed class NoteAnnotation : Annotation
    {
        public NoteAnnotation(double x, double y, NoteIcon icon, bool open)
            : base(x, y, 20, 20)
        {
            Icon = icon;
            Open = open;
        }

        public override string Subtype => "Text";

        /// <summary>NoZoom (8) + NoRotate (16): the icon keeps its size; notes are not printed.</summary>
        public override int Flags => 8 | 16;

        public NoteIcon Icon { get; }

        public bool Open { get; }

        public override Appearance CreateAppearance()
        {
            // A small speech-bubble icon: filled rounded box with three text lines.
            var fill = Color ?? PdfColor.FromHex("#FACC15");
            var a = new Appearance();
            a.Op(Appearance.Rgb(fill), "rg", "0.35 0.3 0.1 RG", "0.8 w");
            a.Op("1.5 6 m 18.5 6 l 18.5 19 l 1.5 19 l h B");
            a.Op("5 6 m 4 1.5 l 10 6 l h B");
            a.Op("0.35 0.3 0.1 RG", "1 w", "5 15.5 m 15 15.5 l S", "5 12.5 m 15 12.5 l S", "5 9.5 m 12 9.5 l S");
            return a;
        }
    }

    internal sealed class HighlightAnnotation : Annotation
    {
        public HighlightAnnotation(double x, double y, double w, double h)
            : base(x, y, w, h)
        {
        }

        public override string Subtype => "Highlight";

        public override Appearance CreateAppearance()
        {
            // Multiply keeps the text under the highlight readable.
            var a = new Appearance { BlendMode = "Multiply" };
            a.Op("/GS0", "gs", Appearance.Rgb(Color ?? PdfColor.FromHex("#FDE047")), "rg");
            a.Op("0", "0", Appearance.N(Width), Appearance.N(Height), "re", "f");
            return a;
        }
    }

    internal sealed class StampAnnotation : Annotation
    {
        private readonly PdfFont _font;
        private readonly string _fontName;

        public StampAnnotation(double x, double y, double w, double h, string text, PdfFont font, string fontName)
            : base(x, y, w, h)
        {
            Text = text;
            _font = font;
            _fontName = fontName;
        }

        public override string Subtype => "Stamp";

        public string Text { get; }

        public override Appearance CreateAppearance()
        {
            var color = Color ?? PdfColor.FromHex("#B91C1C");
            var border = Math.Max(1.5, Math.Min(Width, Height) * 0.05);
            var a = new Appearance();
            a.Fonts[_fontName] = _font.Face;
            var r = Math.Min(Height / 4, 8);
            var inset = border / 2;
            double l = inset, b = inset, rt = Width - inset, t = Height - inset;
            const double K = 0.5522847498;
            a.Op(Appearance.Rgb(color), "RG", Appearance.N(border), "w");
            a.Op(Appearance.N(l + r), Appearance.N(b), "m", Appearance.N(rt - r), Appearance.N(b), "l");
            a.Op(Appearance.N(rt - r + K * r), Appearance.N(b), Appearance.N(rt), Appearance.N(b + r - K * r), Appearance.N(rt), Appearance.N(b + r), "c");
            a.Op(Appearance.N(rt), Appearance.N(t - r), "l");
            a.Op(Appearance.N(rt), Appearance.N(t - r + K * r), Appearance.N(rt - r + K * r), Appearance.N(t), Appearance.N(rt - r), Appearance.N(t), "c");
            a.Op(Appearance.N(l + r), Appearance.N(t), "l");
            a.Op(Appearance.N(l + r - K * r), Appearance.N(t), Appearance.N(l), Appearance.N(t - r + K * r), Appearance.N(l), Appearance.N(t - r), "c");
            a.Op(Appearance.N(l), Appearance.N(b + r), "l");
            a.Op(Appearance.N(l), Appearance.N(b + r - K * r), Appearance.N(l + r - K * r), Appearance.N(b), Appearance.N(l + r), Appearance.N(b), "c");
            a.Op("h", "S");

            var run = _font.Map(Text);
            var textWidth = run.Width * _font.Size / 1000.0;
            var x = (Width - textWidth) / 2;
            var y = (Height - _font.Size * 0.7) / 2;
            var hex = new StringBuilder();
            foreach (var c in run.Codes)
            {
                hex.Append(c.ToString("X2", CultureInfo.InvariantCulture));
            }

            a.Op(Appearance.Rgb(color), "rg", "BT", "/" + _fontName, Appearance.N(_font.Size), "Tf", Appearance.N(x), Appearance.N(y), "Td", "<" + hex + ">", "Tj", "ET");
            return a;
        }
    }
}
