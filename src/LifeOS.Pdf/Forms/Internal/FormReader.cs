using System;
using System.Collections.Generic;
using System.Globalization;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Forms.Internal
{
    /// <summary>
    /// Builds the field model of an opened PDF from its /AcroForm (§12.7.2). Traversal is
    /// iterative and bounded so a damaged file cannot loop or use unbounded memory.
    /// </summary>
    internal sealed class FormReader
    {
        private readonly PdfForm _form;
        private readonly SourceDocument _source;
        private readonly PdfParser _parser;

        private FormReader(PdfForm form, SourceDocument source)
        {
            _form = form;
            _source = source;
            _parser = source.Parser;
        }

        public static void Read(PdfForm form, SourceDocument source) => new FormReader(form, source).Read();

        private void Read()
        {
            if (!(_parser.Resolve(_parser.Catalog.Get("AcroForm")) is PdfDictionary acro))
            {
                return;
            }

            if (!(_parser.Resolve(acro.Get("Fields")) is PdfArray fields))
            {
                return;
            }

            var visited = new HashSet<int>();
            foreach (var entry in fields.Items)
            {
                if (!(_parser.Resolve(entry) is PdfDictionary dict))
                {
                    continue;
                }

                if (entry is PdfSourceReference r && !visited.Add(r.Number))
                {
                    continue;
                }

                ReadField(entry, dict);
            }
        }

        private void ReadField(PdfObject entry, PdfDictionary dict)
        {
            var name = FullName(dict);
            if (name == null)
            {
                return;
            }

            var flags = Flags(dict);
            PdfFormField field;
            switch (Name(dict.Get("FT")))
            {
                case "Tx": field = ReadText(dict, name, flags); break;
                case "Ch": field = ReadChoice(dict, name, flags); break;
                case "Sig": field = new PdfSignatureField(name); break;
                case "Btn":
                    if ((flags & FieldFlags.Radio) != 0)
                    {
                        field = ReadRadio(dict, name, flags);
                    }
                    else if ((flags & FieldFlags.Pushbutton) != 0)
                    {
                        field = ReadButton(dict, name, flags);
                    }
                    else
                    {
                        field = ReadCheckBox(dict, name, flags);
                    }

                    break;
                default:
                    return;
            }

            field.ReadOnly = (flags & FieldFlags.ReadOnly) != 0;
            field.Required = (flags & FieldFlags.Required) != 0;
            field.Tooltip = Text(dict.Get("TU"));

            if (_form.GetField(name) != null)
            {
                return; // duplicate in a damaged file: first one wins
            }

            _form.Add(field);
            ReadWidgets(dict, field, entry is PdfSourceReference r ? r.Number : -1);
        }

        private void ReadWidgets(PdfDictionary fieldDict, PdfFormField field, int fieldNumber)
        {
            if (_parser.Resolve(fieldDict.Get("Kids")) is PdfArray kids)
            {
                foreach (var kid in kids.Items)
                {
                    if (_parser.Resolve(kid) is PdfDictionary kidDict)
                    {
                        AddWidget(kid is PdfSourceReference r ? r.Number : -1, kidDict, field);
                    }
                }
            }
            else
            {
                AddWidget(fieldNumber, fieldDict, field);
            }
        }

        private void AddWidget(int number, PdfDictionary widgetDict, PdfFormField field)
        {
            var page = PageOf(widgetDict.Get("P"));
            var rect = _source.Rect(widgetDict.Get("Rect"));
            if (page == null || rect == null)
            {
                return;
            }

            var widget = field.AddWidget(page, rect[0] - page.OriginX, page.OriginY + page.Height - rect[3], rect[2] - rect[0], rect[3] - rect[1]);
            var onState = Name(widgetDict.Get("AS"));
            if (onState != null && onState != "Off")
            {
                widget.OnState = onState;
            }

            if (number > 0)
            {
                widget.SourceNumber = number;
                page.FormWidgetNumbers.Add(number);
            }
        }

        // ------------------------------------------------------------------ field types

        private PdfTextField ReadText(PdfDictionary dict, string name, long flags)
        {
            var t = new PdfTextField(name)
            {
                Value = Text(dict.Get("V")),
                DefaultValue = Text(dict.Get("DV")),
                Multiline = (flags & FieldFlags.Multiline) != 0,
                Password = (flags & FieldFlags.Password) != 0,
                Comb = (flags & FieldFlags.Comb) != 0,
            };
            if (_source.Number(dict.Get("MaxLen")) is double ml)
            {
                t.MaxLength = (int)ml;
            }

            if (_source.Number(dict.Get("Q")) is double q)
            {
                t.Align = q == 1 ? TextAlign.Center : q == 2 ? TextAlign.Right : TextAlign.Left;
            }

            t.Font = ParseDa(Text(dict.Get("DA")));
            return t;
        }

        private PdfCheckBox ReadCheckBox(PdfDictionary dict, string name, long flags)
        {
            var c = new PdfCheckBox(name);
            var v = Name(dict.Get("V"));
            if (v != null && v != "Off")
            {
                c.Checked = true;
                c.OnValue = v;
            }

            return c;
        }

        private PdfRadioButton ReadRadio(PdfDictionary dict, string name, long flags)
        {
            var r = new PdfRadioButton(name);
            var v = Name(dict.Get("V"));
            if (v != null && v != "Off")
            {
                r.Value = v;
            }

            return r;
        }

        private PdfChoiceField ReadChoice(PdfDictionary dict, string name, long flags)
        {
            var c = new PdfChoiceField(name, (flags & FieldFlags.Combo) != 0)
            {
                Editable = (flags & FieldFlags.Edit) != 0,
                MultiSelect = (flags & FieldFlags.MultiSelect) != 0,
                Font = ParseDa(Text(dict.Get("DA"))),
            };
            if (_parser.Resolve(dict.Get("Opt")) is PdfArray opt)
            {
                foreach (var o in opt.Items)
                {
                    if (_parser.Resolve(o) is PdfArray pair && pair.Items.Count >= 2)
                    {
                        var value = Text(pair.Items[0]) ?? string.Empty;
                        var display = Text(pair.Items[1]) ?? value;
                        c.AddOption(new PdfChoiceOption(display, value));
                    }
                    else if (Text(o) is string s)
                    {
                        c.AddOption(new PdfChoiceOption(s));
                    }
                }
            }

            if (_parser.Resolve(dict.Get("V")) is PdfArray va)
            {
                var values = new List<string>();
                foreach (var v in va.Items)
                {
                    if (Text(v) is string s)
                    {
                        values.Add(s);
                    }
                }

                c.SelectedValues = values.ToArray();
            }
            else
            {
                var sv = Text(dict.Get("V"));
                c.SelectedValues = sv == null ? Array.Empty<string>() : new[] { sv };
            }

            return c;
        }

        private PdfButton ReadButton(PdfDictionary dict, string name, long flags)
        {
            var b = new PdfButton(name);
            if (_parser.Resolve(dict.Get("MK")) is PdfDictionary mk)
            {
                b.Caption = Text(mk.Get("CA")) ?? string.Empty;
            }

            return b;
        }

        // ------------------------------------------------------------------ helpers

        private string? FullName(PdfDictionary field)
        {
            var parts = new List<string>();
            var current = field;
            var hops = 0;
            while (current != null && hops++ < 64)
            {
                if (Text(current.Get("T")) is string t)
                {
                    parts.Add(t);
                }

                if (_parser.Resolve(current.Get("Parent")) is PdfDictionary parent && parent.Get("FT") != null)
                {
                    current = parent;
                }
                else
                {
                    break;
                }
            }

            if (parts.Count == 0)
            {
                return null;
            }

            parts.Reverse();
            return string.Join(".", parts);
        }

        private string? Text(PdfObject? obj) => _parser.Resolve(obj) is PdfString s ? SourceDocument.TextString(s.Bytes) : null;

        private string? Name(PdfObject? obj) => _parser.Resolve(obj) is PdfName n ? n.Value : null;

        private long Flags(PdfDictionary dict) => _source.Number(dict.Get("Ff")) is double f ? (long)f : 0;

        private PdfPage? PageOf(PdfObject? p)
        {
            if (p is PdfSourceReference r)
            {
                return _source.PageIndex.TryGetValue(r.Number, out var index) ? _form.Document.Pages[index] : null;
            }

            return null;
        }

        private PdfFont ParseDa(string? da)
        {
            var size = 10.0;
            var face = PdfFontFace.Standard(StandardFont.Helvetica);
            if (da != null)
            {
                var slash = da.IndexOf('/');
                if (slash >= 0)
                {
                    var end = slash + 1;
                    while (end < da.Length && !char.IsWhiteSpace(da[end]))
                    {
                        end++;
                    }

                    face = FaceFromShorthand(da.Substring(slash + 1, end - slash - 1)) ?? face;
                    var s = end;
                    while (s < da.Length && char.IsWhiteSpace(da[s]))
                    {
                        s++;
                    }

                    var e = s;
                    while (e < da.Length && (char.IsDigit(da[e]) || da[e] == '.'))
                    {
                        e++;
                    }

                    if (e > s && double.TryParse(da.Substring(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    {
                        size = parsed;
                    }
                }
            }

            return new PdfFont(face, size);
        }

        private static PdfFontFace? FaceFromShorthand(string name)
        {
            switch (name)
            {
                case "Helv": return PdfFontFace.Standard(StandardFont.Helvetica);
                case "HeBo": return PdfFontFace.Standard(StandardFont.HelveticaBold);
                case "HeOb": return PdfFontFace.Standard(StandardFont.HelveticaOblique);
                case "HeBO": return PdfFontFace.Standard(StandardFont.HelveticaBoldOblique);
                case "TiRo": return PdfFontFace.Standard(StandardFont.TimesRoman);
                case "TiBo": return PdfFontFace.Standard(StandardFont.TimesBold);
                case "TiIt": return PdfFontFace.Standard(StandardFont.TimesItalic);
                case "TiBI": return PdfFontFace.Standard(StandardFont.TimesBoldItalic);
                case "Cour": return PdfFontFace.Standard(StandardFont.Courier);
                case "CoBo": return PdfFontFace.Standard(StandardFont.CourierBold);
                case "CoOb": return PdfFontFace.Standard(StandardFont.CourierOblique);
                case "CoBO": return PdfFontFace.Standard(StandardFont.CourierBoldOblique);
                case "Symb": return PdfFontFace.Standard(StandardFont.Symbol);
                case "ZaDb": return PdfFontFace.Standard(StandardFont.ZapfDingbats);
                default: return null;
            }
        }
    }
}
