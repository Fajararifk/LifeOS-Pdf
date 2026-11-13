using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Internal;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Forms.Internal
{
    /// <summary>
    /// Writes the /AcroForm catalog entry, the field dictionaries and their widget annotations
    /// (§12.7). Simple fields are written as a merged field+widget object; radio groups (and any
    /// field with several widgets) use a field dictionary with /Kids. Unmodified fields read from
    /// a file are copied verbatim; modified ones get a regenerated appearance.
    /// </summary>
    internal sealed class FormWriter
    {
        private readonly PdfDocument _document;
        private readonly Func<PdfIndirectObject> _new;
        private readonly Dictionary<PdfFontFace, PdfIndirectObject> _fonts;
        private readonly ObjectCopier _copier;
        private readonly Dictionary<string, PdfFontFace> _drFonts = new Dictionary<string, PdfFontFace>(StringComparer.Ordinal);

        public FormWriter(PdfDocument document, Func<PdfIndirectObject> newObject,
            Dictionary<PdfFontFace, PdfIndirectObject> fonts, ObjectCopier copier)
        {
            _document = document;
            _new = newObject;
            _fonts = fonts;
            _copier = copier;
        }

        /// <summary>
        /// Writes every field and its widgets, appends widget references to each page's /Annots,
        /// and sets the catalog /AcroForm.
        /// </summary>
        public void Write(PdfDictionary catalog, Dictionary<PdfPage, PdfIndirectObject> pageObjects)
        {
            var form = _document.Form;
            if (form.IsFlattened)
            {
                FlattenInto(form, pageObjects);
                return;
            }

            if (form.Fields.Count == 0)
            {
                return;
            }

            var fields = new PdfArray();
            foreach (var field in form.Fields)
            {
                if (field.Widgets.Count == 1 && !(field is PdfRadioButton))
                {
                    fields.Items.Add(WriteMerged(field, field.Widgets[0], pageObjects));
                }
                else
                {
                    fields.Items.Add(WriteFieldWithKids(field, pageObjects));
                }
            }

            var acro = new PdfDictionary()
                .Set("Fields", fields)
                .Set("DA", PdfString.Text(form.DefaultAppearance ?? "/Helv 10 Tf 0 g"))
                .Set("NeedAppearances", PdfBoolean.False);
            _drFonts["Helv"] = PdfFontFace.Standard(StandardFont.Helvetica);
            var dr = new PdfDictionary();
            var drFonts = new PdfDictionary();
            foreach (var pair in _drFonts)
            {
                if (_fonts.TryGetValue(pair.Value, out var fontObj))
                {
                    drFonts.Set(pair.Key, fontObj.Reference);
                }
            }

            if (drFonts.Count > 0)
            {
                dr.Set("Font", drFonts);
                acro.Set("DR", dr);
            }

            catalog.Set("AcroForm", acro);
        }

        // ------------------------------------------------------------------ simple field

        private PdfReference WriteMerged(PdfFormField field, FormWidget widget, Dictionary<PdfPage, PdfIndirectObject> pageObjects)
        {
            var obj = _new();
            var dict = FieldDictionary(field, null);
            WidgetDictionary(dict, field, widget, pageObjects);
            obj.Value = dict;
            AddToAnnots(pageObjects, widget.Page, obj.Reference);
            return obj.Reference;
        }

        private PdfReference WriteFieldWithKids(PdfFormField field, Dictionary<PdfPage, PdfIndirectObject> pageObjects)
        {
            var parent = _new();
            var dict = FieldDictionary(field, null);
            var kids = new PdfArray();
            foreach (var widget in field.Widgets)
            {
                var kid = _new();
                var kidDict = new PdfDictionary()
                    .Set("Type", "Annot")
                    .Set("Subtype", "Widget")
                    .Set("Parent", parent.Reference);
                WidgetDictionary(kidDict, field, widget, pageObjects);
                kid.Value = kidDict;
                kids.Items.Add(kid.Reference);
                AddToAnnots(pageObjects, widget.Page, kid.Reference);
            }

            dict.Set("Kids", kids);
            parent.Value = dict;
            return parent.Reference;
        }

        // ------------------------------------------------------------------ dictionaries

        private PdfDictionary FieldDictionary(PdfFormField field, PdfReference? parent)
        {
            var dict = new PdfDictionary();
            if (parent != null)
            {
                dict.Set("Parent", parent);
            }

            dict.Set("FT", field.FieldType).Set("T", PdfString.Text(field.Name));
            if (!string.IsNullOrEmpty(field.Tooltip))
            {
                dict.Set("TU", PdfString.Text(field.Tooltip!));
            }

            var flags = Flags(field);
            if (flags != 0)
            {
                dict.Set("Ff", flags);
            }

            switch (field)
            {
                case PdfTextField t: TextField(dict, t); break;
                case PdfCheckBox c: dict.Set("V", new PdfName(c.Checked ? c.OnValue : "Off")); break;
                case PdfRadioButton r: dict.Set("V", new PdfName(r.Value ?? "Off")); break;
                case PdfChoiceField ch: ChoiceField(dict, ch); break;
                case PdfButton b:
                    if (b.Action == ButtonAction.Reset)
                    {
                        dict.Set("A", new PdfDictionary().Set("S", "ResetForm"));
                    }

                    break;
                case PdfSignatureField s:
                    if (s.HasSignature)
                    {
                        dict.Set("V", SignatureDictionary(s));
                    }

                    break;
            }

            return dict;
        }

        private void TextField(PdfDictionary dict, PdfTextField t)
        {
            if (t.Value != null)
            {
                dict.Set("V", PdfString.Text(t.Value));
            }

            if (t.DefaultValue != null)
            {
                dict.Set("DV", PdfString.Text(t.DefaultValue));
            }

            if (t.MaxLength is int max)
            {
                dict.Set("MaxLen", max);
            }

            dict.Set("DA", PdfString.Text(DaFor(t.Font)));
            var q = t.Align == TextAlign.Center ? 1 : t.Align == TextAlign.Right ? 2 : 0;
            if (q != 0)
            {
                dict.Set("Q", q);
            }
        }

        private void ChoiceField(PdfDictionary dict, PdfChoiceField ch)
        {
            if (ch.Options.Count > 0)
            {
                var opt = new PdfArray();
                foreach (var o in ch.Options)
                {
                    var pair = new PdfArray(PdfString.Text(o.Value), PdfString.Text(o.Display));
                    opt.Items.Add(pair);
                }

                dict.Set("Opt", opt);
            }

            if (ch.SelectedValues.Length == 1)
            {
                dict.Set("V", PdfString.Text(ch.SelectedValues[0]));
            }
            else if (ch.SelectedValues.Length > 1)
            {
                var values = new PdfArray();
                foreach (var v in ch.SelectedValues)
                {
                    values.Items.Add(PdfString.Text(v));
                }

                dict.Set("V", values);
            }

            dict.Set("DA", PdfString.Text(DaFor(ch.Font)));
        }

        private string DaFor(PdfFont font)
        {
            var name = DaFont(font);
            return "/" + name + " " + PdfFormat.Number(font.Size) + " Tf 0 g";
        }

        private string DaFont(PdfFont font)
        {
            if (!font.Face.IsEmbedded)
            {
                var shorthand = Shorthand(font.Face.StandardFont ?? StandardFont.Helvetica);
                _drFonts[shorthand] = font.Face;
                return shorthand;
            }

            var name = _document.Resources.FontName(font.Face, font.Map(string.Empty));
            _drFonts[name] = font.Face;
            return name;
        }

        private static string Shorthand(StandardFont font)
        {
            switch (font)
            {
                case StandardFont.Helvetica: return "Helv";
                case StandardFont.HelveticaBold: return "HeBo";
                case StandardFont.HelveticaOblique: return "HeOb";
                case StandardFont.HelveticaBoldOblique: return "HeBO";
                case StandardFont.TimesRoman: return "TiRo";
                case StandardFont.TimesBold: return "TiBo";
                case StandardFont.TimesItalic: return "TiIt";
                case StandardFont.TimesBoldItalic: return "TiBI";
                case StandardFont.Courier: return "Cour";
                case StandardFont.CourierBold: return "CoBo";
                case StandardFont.CourierOblique: return "CoOb";
                case StandardFont.CourierBoldOblique: return "CoBO";
                case StandardFont.Symbol: return "Symb";
                case StandardFont.ZapfDingbats: return "ZaDb";
                default: return "Helv";
            }
        }

        private long Flags(PdfFormField field)
        {
            long flags = 0;
            if (field.ReadOnly)
            {
                flags |= FieldFlags.ReadOnly;
            }

            if (field.Required)
            {
                flags |= FieldFlags.Required;
            }

            switch (field)
            {
                case PdfTextField t:
                    if (t.Multiline) flags |= FieldFlags.Multiline;
                    if (t.Password) flags |= FieldFlags.Password;
                    if (t.Comb) flags |= FieldFlags.Comb;
                    break;
                case PdfRadioButton _:
                    flags |= FieldFlags.Radio | FieldFlags.NoToggleToOff;
                    break;
                case PdfButton _:
                    flags |= FieldFlags.Pushbutton;
                    break;
                case PdfChoiceField ch:
                    if (ch.Combo) flags |= FieldFlags.Combo;
                    if (ch.Editable) flags |= FieldFlags.Edit;
                    if (ch.MultiSelect) flags |= FieldFlags.MultiSelect;
                    break;
            }

            return flags;
        }

        // ------------------------------------------------------------------ widget

        private void WidgetDictionary(PdfDictionary dict, PdfFormField field, FormWidget widget, Dictionary<PdfPage, PdfIndirectObject> pageObjects)
        {
            var page = widget.Page;
            var left = page.OriginX + widget.X;
            var bottom = page.OriginY + page.Height - widget.Y - widget.Height;
            dict.Set("Type", "Annot")
                .Set("Subtype", "Widget")
                .Set("Rect", PdfArray.OfNumbers(left, bottom, left + widget.Width, bottom + widget.Height))
                .Set("P", pageObjects[page].Reference);

            switch (field)
            {
                case PdfCheckBox c:
                    dict.Set("AS", new PdfName(c.Checked ? c.OnValue : "Off"));
                    break;
                case PdfRadioButton r:
                    var on = widget.OnState != null && string.Equals(r.Value, widget.OnState, StringComparison.Ordinal);
                    dict.Set("AS", new PdfName(on ? widget.OnState! : "Off"));
                    break;
            }

            dict.Set("AP", Appearance(field, widget));
        }

        private PdfDictionary Appearance(PdfFormField field, FormWidget widget)
        {
            var w = widget.Width;
            var h = widget.Height;
            if (field is PdfCheckBox checkBox)
            {
                // The on-state name is the export value; /AS must match an /AP /N key (§12.7.4.2.2).
                return new PdfDictionary().Set("N", new PdfDictionary()
                    .Set(checkBox.OnValue, Stream(FieldAppearance.Build(_document, field, widget, true), w, h))
                    .Set("Off", Stream(FieldAppearance.Build(_document, field, widget, false), w, h)));
            }

            if (field is PdfRadioButton)
            {
                var on = widget.OnState ?? "Off";
                return new PdfDictionary().Set("N", new PdfDictionary()
                    .Set(on, Stream(FieldAppearance.Build(_document, field, widget, true), w, h))
                    .Set("Off", Stream(FieldAppearance.Build(_document, field, widget, false), w, h)));
            }

            return new PdfDictionary().Set("N", Stream(FieldAppearance.Build(_document, field, widget, true), w, h));
        }

        private PdfReference Stream(FieldAppearance appearance, double width, double height)
        {
            var resources = new PdfDictionary();
            if (appearance.Fonts.Count > 0)
            {
                var fonts = new PdfDictionary();
                foreach (var pair in appearance.Fonts)
                {
                    fonts.Set(pair.Key, _fonts[pair.Value].Reference);
                }

                resources.Set("Font", fonts);
            }

            var dict = new PdfDictionary()
                .Set("Type", "XObject")
                .Set("Subtype", "Form")
                .Set("BBox", PdfArray.OfNumbers(0, 0, width, height))
                .Set("Resources", resources)
                .Set("Filter", "FlateDecode");
            var obj = _new();
            obj.Value = new PdfStream(dict, Zlib.Compress(Encoding.ASCII.GetBytes(appearance.Content)));
            return obj.Reference;
        }

        private static PdfDictionary SignatureDictionary(PdfSignatureField s)
        {
            // Fixed-width ByteRange placeholders so the signer can overwrite them in place.
            var dict = new PdfDictionary()
                .Set("Type", "Sig")
                .Set("Filter", "Adobe.PPKLite")
                .Set("SubFilter", "adbe.pkcs7.detached")
                .Set("Contents", PdfString.Hex(s.SignatureContents!))
                .Set("ByteRange", new PdfArray(
                    new PdfReal(0, "0"),
                    new PdfReal(0, "0000000000"),
                    new PdfReal(0, "0000000000"),
                    new PdfReal(0, "0000000000")));
            if (s.SignatureDate.HasValue)
            {
                dict.Set("M", PdfString.Text(PdfFormat.Date(s.SignatureDate.Value)));
            }

            if (!string.IsNullOrEmpty(s.SignatureReason))
            {
                dict.Set("Reason", PdfString.Text(s.SignatureReason!));
            }

            if (!string.IsNullOrEmpty(s.SignatureLocation))
            {
                dict.Set("Location", PdfString.Text(s.SignatureLocation!));
            }

            if (!string.IsNullOrEmpty(s.SignatureName))
            {
                dict.Set("Name", PdfString.Text(s.SignatureName!));
            }

            if (!string.IsNullOrEmpty(s.SignatureContact))
            {
                dict.Set("ContactInfo", PdfString.Text(s.SignatureContact!));
            }

            return dict;
        }

        private static void AddToAnnots(Dictionary<PdfPage, PdfIndirectObject> pageObjects, PdfPage page, PdfObject widget)
        {
            var pageDict = (PdfDictionary)pageObjects[page].Value!;
            if (pageDict.Get("Annots") is PdfArray existing)
            {
                existing.Items.Add(widget);
            }
            else
            {
                pageDict.Set("Annots", new PdfArray(widget));
            }
        }

        // ------------------------------------------------------------------ flatten

        /// <summary>Bakes each widget's appearance into its page content as a form XObject (§8.10).</summary>
        private void FlattenInto(PdfForm form, Dictionary<PdfPage, PdfIndirectObject> pageObjects)
        {
            var byPage = new Dictionary<PdfPage, List<FlattenItem>>();
            foreach (var item in form.FlattenItems)
            {
                if (!byPage.TryGetValue(item.Page, out var list))
                {
                    list = new List<FlattenItem>();
                    byPage[item.Page] = list;
                }

                list.Add(item);
            }

            foreach (var pair in byPage)
            {
                var page = pair.Key;
                var pageDict = (PdfDictionary)pageObjects[page].Value!;
                var xobjects = new PdfDictionary();
                var content = new StringBuilder();
                var n = 0;
                foreach (var item in pair.Value)
                {
                    var left = page.OriginX + item.X;
                    var bottom = page.OriginY + page.Height - item.Y - item.Height;
                    var name = "Fm" + (++n).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    xobjects.Set(name, Stream(item.Appearance, item.Width, item.Height));
                    content.Append("q 1 0 0 1 ");
                    PdfFormat.AppendNumber(content, left);
                    content.Append(' ');
                    PdfFormat.AppendNumber(content, bottom);
                    content.Append(" cm /").Append(name).Append(" Do Q\n");
                }

                var contentObj = _new();
                contentObj.Value = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content.ToString()));
                AppendContents(pageDict, contentObj.Reference);

                var resources = pageDict.Get("Resources") as PdfDictionary;
                if (resources == null)
                {
                    resources = new PdfDictionary();
                    pageDict.Set("Resources", resources);
                }

                var xo = resources.Get("XObject") as PdfDictionary;
                if (xo == null)
                {
                    xo = new PdfDictionary();
                    resources.Set("XObject", xo);
                }

                foreach (var e in xobjects.Entries)
                {
                    xo.Set(e.Key, e.Value);
                }
            }
        }

        private static void AppendContents(PdfDictionary pageDict, PdfObject content)
        {
            var existing = pageDict.Get("Contents");
            if (existing == null)
            {
                pageDict.Set("Contents", content);
            }
            else if (existing is PdfArray array)
            {
                array.Items.Add(content);
            }
            else
            {
                pageDict.Set("Contents", new PdfArray(existing, content));
            }
        }
    }
}
