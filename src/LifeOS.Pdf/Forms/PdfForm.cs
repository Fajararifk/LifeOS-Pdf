using System;
using System.Collections.Generic;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Forms.Internal;

namespace LifeOS.Pdf.Forms
{
    /// <summary>
    /// The interactive form of a document (§12.7). Create fields with the Add* methods on
    /// <see cref="PdfPage"/>; fill an opened form with <see cref="SetValue(string, string?)"/>;
    /// bake the result into static pages with <see cref="Flatten"/>.
    /// </summary>
    public sealed class PdfForm
    {
        private static readonly PdfFont Helvetica = new PdfFont(StandardFont.Helvetica, 10);

        private readonly List<PdfFormField> _fields = new List<PdfFormField>();
        private readonly Dictionary<string, PdfFormField> _byName = new Dictionary<string, PdfFormField>(StringComparer.Ordinal);
        private readonly List<FlattenItem> _flattenItems = new List<FlattenItem>();

        internal PdfForm(PdfDocument document)
        {
            Document = document;
        }

        internal PdfDocument Document { get; }

        /// <summary>The fields, in creation (or document) order.</summary>
        public IReadOnlyList<PdfFormField> Fields => _fields;

        /// <summary>The default appearance of new text/choice fields: "/Helv 10 Tf 0 g".</summary>
        public string? DefaultAppearance { get; set; } = "/Helv 10 Tf 0 g";

        internal bool IsFlattened { get; private set; }

        internal IReadOnlyList<FlattenItem> FlattenItems => _flattenItems;

        /// <summary>Finds a field by its fully qualified partial name (dot-separated hierarchy).</summary>
        public PdfFormField? GetField(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return _byName.TryGetValue(name, out var field) ? field : null;
        }

        /// <summary>Sets the value of a text, radio or single-select choice field.</summary>
        public void SetValue(string name, string? value)
        {
            var field = Require(name);
            switch (field)
            {
                case PdfTextField t: t.Value = value; break;
                case PdfRadioButton r: r.Value = value; break;
                case PdfChoiceField c: c.SelectedValues = value == null ? Array.Empty<string>() : new[] { value }; break;
                default: throw new InvalidOperationException("Field '" + name + "' is not a text, radio or choice field.");
            }

            field.MarkDirty();
        }

        /// <summary>Checks or unchecks a checkbox.</summary>
        public void SetValue(string name, bool value)
        {
            var field = Require(name);
            if (!(field is PdfCheckBox checkBox))
            {
                throw new InvalidOperationException("Field '" + name + "' is not a checkbox.");
            }

            checkBox.Checked = value;
            field.MarkDirty();
        }

        /// <summary>Selects one or more options of a choice field.</summary>
        public void SetValue(string name, string[] values)
        {
            var field = Require(name);
            if (!(field is PdfChoiceField choice))
            {
                throw new InvalidOperationException("Field '" + name + "' is not a choice field.");
            }

            choice.SelectedValues = values ?? throw new ArgumentNullException(nameof(values));
            field.MarkDirty();
        }

        /// <summary>Bakes every field's appearance into the page content and removes the form.</summary>
        public void Flatten()
        {
            foreach (var field in _fields)
            {
                foreach (var widget in field.Widgets)
                {
                    _flattenItems.Add(new FlattenItem(widget.Page, widget.X, widget.Y, widget.Width, widget.Height,
                        FieldAppearance.Build(Document, field, widget, IsOn(field, widget))));
                }
            }

            _fields.Clear();
            _byName.Clear();
            IsFlattened = true;
        }

        /// <summary>All field values as a JSON object ("Name" → string | bool | string[]).</summary>
        public string ExportJson() => PdfFormData.Export(this);

        /// <summary>Applies values from <see cref="ExportJson"/> (unknown names are ignored).</summary>
        public void ImportJson(string json) => PdfFormData.Import(this, json);

        internal void Add(PdfFormField field)
        {
            if (_byName.ContainsKey(field.Name))
            {
                throw new ArgumentException("A field named '" + field.Name + "' already exists.", nameof(field));
            }

            _fields.Add(field);
            _byName[field.Name] = field;
        }

        /// <summary>Registers every font a field draws with, before the writer allocates font objects.</summary>
        internal void RegisterFonts()
        {
            if (_fields.Count == 0)
            {
                return;
            }

            Document.Resources.FontName(Helvetica.Face, Helvetica.Map(" "));
            foreach (var field in _fields)
            {
                switch (field)
                {
                    case PdfTextField t:
                        var text = t.Value ?? string.Empty;
                        if (t.Password)
                        {
                            text = new string('*', text.Length);
                        }

                        Register(t.Font, text.Length > 0 ? text : " ");
                        break;
                    case PdfChoiceField ch: Register(ch.Font, FieldAppearance.DisplayText(ch)); break;
                    case PdfButton b: Register(b.Font, b.Caption); break;
                    case PdfSignatureField s when !string.IsNullOrEmpty(s.Caption):
                        Register(Helvetica, s.Caption!);
                        break;
                }
            }
        }

        private void Register(PdfFont font, string text) => Document.Resources.FontName(font.Face, font.Map(text));

        private static bool IsOn(PdfFormField field, FormWidget widget)
        {
            switch (field)
            {
                case PdfCheckBox c: return c.Checked;
                case PdfRadioButton r: return widget.OnState != null && string.Equals(r.Value, widget.OnState, StringComparison.Ordinal);
                default: return true;
            }
        }

        private PdfFormField Require(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (!_byName.TryGetValue(name, out var field))
            {
                throw new ArgumentException("There is no field named '" + name + "'.", nameof(name));
            }

            if (field.ReadOnly)
            {
                throw new InvalidOperationException("Field '" + name + "' is read-only.");
            }

            return field;
        }
    }
}
