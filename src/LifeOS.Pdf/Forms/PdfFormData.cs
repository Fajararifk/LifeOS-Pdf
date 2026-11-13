using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Templates.Json;

namespace LifeOS.Pdf.Forms
{
    /// <summary>JSON import/export of field values. Export writes a flat object; import applies it back.</summary>
    internal static class PdfFormData
    {
        public static string Export(PdfForm form)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var field in form.Fields)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append(Quote(field.Name)).Append(':');
                Value(sb, field);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void Value(StringBuilder sb, PdfFormField field)
        {
            switch (field)
            {
                case PdfTextField t: String(sb, t.Value); break;
                case PdfCheckBox c: sb.Append(c.Checked ? "true" : "false"); break;
                case PdfRadioButton r: String(sb, r.Value); break;
                case PdfChoiceField ch:
                    sb.Append('[');
                    for (var i = 0; i < ch.SelectedValues.Length; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        sb.Append(Quote(ch.SelectedValues[i]));
                    }

                    sb.Append(']');
                    break;
                default: sb.Append("null"); break;
            }
        }

        private static void String(StringBuilder sb, string? value)
        {
            if (value == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append(Quote(value));
            }
        }

        private static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        public static void Import(PdfForm form, string json)
        {
            if (!(JsonParser.Parse(json) is Dictionary<string, object?> map))
            {
                throw new FormatException("Form data must be a JSON object.");
            }

            foreach (var pair in map)
            {
                var field = form.GetField(pair.Key);
                if (field == null)
                {
                    continue;
                }

                Apply(form, pair.Key, field, pair.Value);
            }
        }

        private static void Apply(PdfForm form, string name, PdfFormField field, object? value)
        {
            switch (field)
            {
                case PdfTextField _:
                    form.SetValue(name, value as string);
                    break;
                case PdfCheckBox _:
                    form.SetValue(name, value is bool b && b);
                    break;
                case PdfRadioButton _:
                    form.SetValue(name, value as string);
                    break;
                case PdfChoiceField _:
                    if (value is List<object?> list)
                    {
                        var arr = new string[list.Count];
                        for (var i = 0; i < list.Count; i++)
                        {
                            arr[i] = list[i] as string ?? string.Empty;
                        }

                        form.SetValue(name, arr);
                    }
                    else
                    {
                        form.SetValue(name, value as string);
                    }

                    break;
            }
        }
    }
}
