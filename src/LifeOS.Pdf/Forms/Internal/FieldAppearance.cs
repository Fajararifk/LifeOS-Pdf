using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Forms.Internal
{
    /// <summary>
    /// Builds a widget appearance stream (§12.7.4) as a compact content program. All numbers go
    /// through <see cref="PdfFormat.AppendNumber"/> so nothing is allocated per number.
    /// </summary>
    internal sealed class FieldAppearance
    {
        private const double K = 0.5522847498307936; // circle Bézier constant

        private readonly StringBuilder _sb = new StringBuilder(160);
        private readonly Dictionary<string, PdfFontFace> _fonts = new Dictionary<string, PdfFontFace>(StringComparer.Ordinal);

        private FieldAppearance()
        {
        }

        public string Content => _sb.ToString();

        public IReadOnlyDictionary<string, PdfFontFace> Fonts => _fonts;

        public static FieldAppearance Build(PdfDocument document, PdfFormField field, FormWidget widget, bool on)
        {
            var a = new FieldAppearance();
            switch (field)
            {
                case PdfTextField t: a.Text(document, t, widget); break;
                case PdfCheckBox c: a.CheckBox(c, widget, on); break;
                case PdfRadioButton r: a.Radio(r, widget, on); break;
                case PdfChoiceField ch: a.Choice(document, ch, widget); break;
                case PdfButton b: a.Push(document, b, widget); break;
                case PdfSignatureField s: a.Signature(document, s, widget); break;
            }

            return a;
        }

        // ------------------------------------------------------------------ helpers

        private string UseFont(PdfDocument document, PdfFont font, string text)
        {
            var name = document.Resources.FontName(font.Face, font.Map(text));
            _fonts[name] = font.Face;
            return name;
        }

        private static void Rgb(StringBuilder sb, PdfColor c)
        {
            PdfFormat.AppendNumber(sb, c.R / 255.0);
            sb.Append(' ');
            PdfFormat.AppendNumber(sb, c.G / 255.0);
            sb.Append(' ');
            PdfFormat.AppendNumber(sb, c.B / 255.0);
        }

        private void Background(double w, double h, PdfColor? color)
        {
            if (color == null)
            {
                return;
            }

            Rgb(_sb, color.Value);
            _sb.Append(" rg 0 0 ");
            PdfFormat.AppendNumber(_sb, w);
            _sb.Append(' ');
            PdfFormat.AppendNumber(_sb, h);
            _sb.Append(" re f\n");
        }

        private void Border(double w, double h, PdfColor? color)
        {
            if (color == null)
            {
                return;
            }

            Rgb(_sb, color.Value);
            _sb.Append(" RG 1 w 0.5 0.5 ");
            PdfFormat.AppendNumber(_sb, w - 1);
            _sb.Append(' ');
            PdfFormat.AppendNumber(_sb, h - 1);
            _sb.Append(" re S\n");
        }

        private void TextLine(string fontName, double size, PdfColor color, double x, double y, byte[] codes)
        {
            _sb.Append("BT /").Append(fontName).Append(' ');
            PdfFormat.AppendNumber(_sb, size);
            _sb.Append(" Tf ");
            Rgb(_sb, color);
            _sb.Append(" rg ");
            PdfFormat.AppendNumber(_sb, x);
            _sb.Append(' ');
            PdfFormat.AppendNumber(_sb, y);
            _sb.Append(" Td <");
            PdfFormat.AppendHex(_sb, codes, 0, codes.Length);
            _sb.Append("> Tj ET\n");
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        // ------------------------------------------------------------------ text field

        private void Text(PdfDocument document, PdfTextField t, FormWidget widget)
        {
            var w = widget.Width;
            var h = widget.Height;
            Background(w, h, t.Background);
            Border(w, h, t.BorderColor);

            var value = t.Value ?? string.Empty;
            if (t.Password)
            {
                value = new string('*', value.Length);
            }

            var font = t.Font;
            if (t.Comb && t.MaxLength is int max && max > 0)
            {
                CombText(document, font, t.TextColor, value, max, w, h);
                return;
            }

            var lines = t.Multiline ? SplitLines(value) : new[] { value };
            if (lines.Length == 0)
            {
                lines = new[] { string.Empty };
            }

            var name = UseFont(document, font, value.Length > 0 ? value : " ");
            var lineHeight = font.Size * 1.2;
            var top = (h - lineHeight * lines.Length) / 2.0;
            var y = h - top - font.Ascent;

            foreach (var line in lines)
            {
                var run = font.Map(line);
                var textWidth = run.Width * font.Size / 1000.0;
                double x = 2;
                switch (t.Align)
                {
                    case TextAlign.Center: x = (w - textWidth) / 2.0; break;
                    case TextAlign.Right: x = w - textWidth - 2; break;
                }

                TextLine(name, font.Size, t.TextColor, x, y, run.Codes);
                y -= lineHeight;
            }
        }

        private void CombText(PdfDocument document, PdfFont font, PdfColor color, string value, int max, double w, double h)
        {
            var name = UseFont(document, font, value.Length > 0 ? value : " ");
            var count = Math.Min(value.Length, max);
            var cell = w / max;
            var y = (h - font.Ascent - font.Descent) / 2.0 + font.Descent;
            for (var i = 0; i < count; i++)
            {
                var ch = value[i].ToString();
                var run = font.Map(ch);
                var cw = run.Width * font.Size / 1000.0;
                var x = i * cell + (cell - cw) / 2.0;
                TextLine(name, font.Size, color, x, y, run.Codes);
            }
        }

        // ------------------------------------------------------------------ checkbox

        private void CheckBox(PdfCheckBox c, FormWidget widget, bool on)
        {
            var w = widget.Width;
            var h = widget.Height;
            Background(w, h, c.Background);
            Border(w, h, c.BorderColor);
            if (on)
            {
                Mark(c.Style, w, h, c.BorderColor ?? PdfColor.Black);
            }
        }

        // ------------------------------------------------------------------ radio

        private void Radio(PdfRadioButton r, FormWidget widget, bool on)
        {
            var w = widget.Width;
            var h = widget.Height;
            Background(w, h, r.Background);
            var color = r.BorderColor ?? PdfColor.Black;
            Rgb(_sb, color);
            _sb.Append(" RG 1 w ");
            AppendCircle(_sb, w / 2.0, h / 2.0, Math.Min(w, h) / 2.0 - 1);
            _sb.Append(" S\n");

            if (on)
            {
                Rgb(_sb, color);
                _sb.Append(" rg ");
                AppendCircle(_sb, w / 2.0, h / 2.0, Math.Min(w, h) / 2.0 - 2);
                _sb.Append(" f\n");
            }
        }

        // ------------------------------------------------------------------ choice

        private void Choice(PdfDocument document, PdfChoiceField ch, FormWidget widget)
        {
            var w = widget.Width;
            var h = widget.Height;
            Background(w, h, ch.Background);
            Border(w, h, ch.BorderColor);

            var display = DisplayText(ch);
            var font = ch.Font;
            var name = UseFont(document, font, display.Length > 0 ? display : " ");
            var run = font.Map(display);
            var y = (h - font.Ascent - font.Descent) / 2.0 + font.Descent;
            TextLine(name, font.Size, ch.TextColor, 2, y, run.Codes);
        }

        internal static string DisplayText(PdfChoiceField ch)
        {
            if (ch.SelectedValues.Length == 0)
            {
                return string.Empty;
            }

            var picked = new string[ch.SelectedValues.Length];
            for (var i = 0; i < ch.SelectedValues.Length; i++)
            {
                var v = ch.SelectedValues[i];
                string display = v;
                foreach (var o in ch.Options)
                {
                    if (string.Equals(o.Value, v, StringComparison.Ordinal))
                    {
                        display = o.Display;
                        break;
                    }
                }

                picked[i] = display;
            }

            return string.Join(", ", picked);
        }

        // ------------------------------------------------------------------ push button

        private void Push(PdfDocument document, PdfButton b, FormWidget widget)
        {
            var w = widget.Width;
            var h = widget.Height;
            Background(w, h, b.Background);
            Border(w, h, b.BorderColor);

            var caption = b.Caption;
            var font = b.Font;
            var name = UseFont(document, font, caption.Length > 0 ? caption : " ");
            var run = font.Map(caption);
            var textWidth = run.Width * font.Size / 1000.0;
            var x = (w - textWidth) / 2.0;
            var y = (h - font.Ascent - font.Descent) / 2.0 + font.Descent;
            TextLine(name, font.Size, b.TextColor, x, y, run.Codes);
        }

        // ------------------------------------------------------------------ signature

        private void Signature(PdfDocument document, PdfSignatureField s, FormWidget widget)
        {
            var w = widget.Width;
            var h = widget.Height;
            Background(w, h, PdfColor.White);
            Border(w, h, PdfColor.Gray);

            if (string.IsNullOrEmpty(s.Caption))
            {
                return;
            }

            var font = new PdfFont(StandardFont.Helvetica, 10);
            var name = UseFont(document, font, s.Caption!);
            var run = font.Map(s.Caption!);
            var textWidth = run.Width * font.Size / 1000.0;
            var x = (w - textWidth) / 2.0;
            var y = (h - font.Ascent - font.Descent) / 2.0 + font.Descent;
            TextLine(name, font.Size, PdfColor.Gray, x, y, run.Codes);
        }

        // ------------------------------------------------------------------ marks

        private void Mark(CheckStyle style, double w, double h, PdfColor color)
        {
            Rgb(_sb, color);
            var sb = _sb;
            switch (style)
            {
                case CheckStyle.Check:
                    sb.Append(" RG ");
                    N(sb, Stroke(w, h));
                    sb.Append(" w ");
                    N(sb, 0.22 * w); sb.Append(' '); N(sb, 0.5 * h); sb.Append(" m ");
                    N(sb, 0.4 * w); sb.Append(' '); N(sb, 0.32 * h); sb.Append(" l ");
                    N(sb, 0.78 * w); sb.Append(' '); N(sb, 0.68 * h); sb.Append(" l S\n");
                    break;
                case CheckStyle.Cross:
                    sb.Append(" RG ");
                    N(sb, Stroke(w, h));
                    sb.Append(" w ");
                    N(sb, 0.25 * w); sb.Append(' '); N(sb, 0.25 * h); sb.Append(" m ");
                    N(sb, 0.75 * w); sb.Append(' '); N(sb, 0.75 * h); sb.Append(" l S ");
                    N(sb, 0.75 * w); sb.Append(' '); N(sb, 0.25 * h); sb.Append(" m ");
                    N(sb, 0.25 * w); sb.Append(' '); N(sb, 0.75 * h); sb.Append(" l S\n");
                    break;
                case CheckStyle.Square:
                    sb.Append(" rg ");
                    N(sb, 0.2 * w); sb.Append(' '); N(sb, 0.2 * h); sb.Append(' ');
                    N(sb, 0.6 * w); sb.Append(' '); N(sb, 0.6 * h); sb.Append(" re f\n");
                    break;
                case CheckStyle.Circle:
                    sb.Append(" rg ");
                    AppendCircle(sb, w / 2.0, h / 2.0, Math.Min(w, h) * 0.3);
                    sb.Append(" f\n");
                    break;
                case CheckStyle.Star:
                    sb.Append(" rg ");
                    Star(sb, w / 2.0, h / 2.0, Math.Min(w, h) * 0.35);
                    sb.Append(" f\n");
                    break;
            }
        }

        private static double Stroke(double w, double h) => Math.Max(1, Math.Min(w, h) * 0.12);

        private static void N(StringBuilder sb, double v) => PdfFormat.AppendNumber(sb, v);

        private static void AppendCircle(StringBuilder sb, double cx, double cy, double r)
        {
            var rk = r * K;
            N(sb, cx + r); sb.Append(' '); N(sb, cy); sb.Append(" m ");
            N(sb, cx + r); sb.Append(' '); N(sb, cy + rk); sb.Append(' ');
            N(sb, cx + rk); sb.Append(' '); N(sb, cy + r); sb.Append(' ');
            N(sb, cx); sb.Append(' '); N(sb, cy + r); sb.Append(" c ");
            N(sb, cx - rk); sb.Append(' '); N(sb, cy + r); sb.Append(' ');
            N(sb, cx - r); sb.Append(' '); N(sb, cy + rk); sb.Append(' ');
            N(sb, cx - r); sb.Append(' '); N(sb, cy); sb.Append(" c ");
            N(sb, cx - r); sb.Append(' '); N(sb, cy - rk); sb.Append(' ');
            N(sb, cx - rk); sb.Append(' '); N(sb, cy - r); sb.Append(' ');
            N(sb, cx); sb.Append(' '); N(sb, cy - r); sb.Append(" c ");
            N(sb, cx + rk); sb.Append(' '); N(sb, cy - r); sb.Append(' ');
            N(sb, cx + r); sb.Append(' '); N(sb, cy - rk); sb.Append(' ');
            N(sb, cx + r); sb.Append(' '); N(sb, cy); sb.Append(" c\n");
        }

        private static void Star(StringBuilder sb, double cx, double cy, double r)
        {
            for (var i = 0; i < 10; i++)
            {
                var angle = Math.PI / 2.0 + i * Math.PI / 5.0;
                var radius = i % 2 == 0 ? r : r * 0.4;
                if (i > 0)
                {
                    sb.Append(' ');
                }

                N(sb, cx + radius * Math.Cos(angle));
                sb.Append(' ');
                N(sb, cy + radius * Math.Sin(angle));
                sb.Append(i == 0 ? " m" : " l");
            }

            sb.Append(" h\n");
        }
    }
}
