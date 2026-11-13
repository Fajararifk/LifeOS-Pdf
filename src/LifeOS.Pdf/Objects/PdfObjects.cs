using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Objects
{
    // Minimal COS object model (ISO 32000-1 §7.3). Internal for now; it will be
    // made public once the parser (Fase 6) needs to expose it.

    internal abstract class PdfObject
    {
        internal abstract void WriteTo(PdfOutput output);
    }

    internal sealed class PdfName : PdfObject
    {
        private const string Delimiters = "()<>[]{}/%#";

        public PdfName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Name must not be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        internal override void WriteTo(PdfOutput output)
        {
            var sb = new StringBuilder("/");
            foreach (var b in Encoding.UTF8.GetBytes(Value))
            {
                if (b < 0x21 || b > 0x7E || Delimiters.IndexOf((char)b) >= 0)
                {
                    sb.Append('#').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append((char)b);
                }
            }

            output.Write(sb.ToString());
        }
    }

    internal sealed class PdfInteger : PdfObject
    {
        public PdfInteger(long value) => Value = value;

        public long Value { get; }

        internal override void WriteTo(PdfOutput output) =>
            output.Write(Value.ToString(CultureInfo.InvariantCulture));
    }

    internal sealed class PdfReal : PdfObject
    {
        public PdfReal(double value) => Value = value;

        public double Value { get; }

        internal override void WriteTo(PdfOutput output) => output.Write(PdfFormat.Number(Value));
    }

    internal sealed class PdfNull : PdfObject
    {
        public static readonly PdfNull Instance = new PdfNull();

        private PdfNull()
        {
        }

        internal override void WriteTo(PdfOutput output) => output.Write("null");
    }

    internal sealed class PdfBoolean : PdfObject
    {
        public static readonly PdfBoolean True = new PdfBoolean(true);
        public static readonly PdfBoolean False = new PdfBoolean(false);

        private PdfBoolean(bool value) => Value = value;

        public bool Value { get; }

        internal override void WriteTo(PdfOutput output) => output.Write(Value ? "true" : "false");
    }

    internal sealed class PdfString : PdfObject
    {
        private PdfString(byte[] bytes, bool hex)
        {
            Bytes = bytes;
            IsHex = hex;
        }

        public byte[] Bytes { get; }

        public bool IsHex { get; }

        public static PdfString Hex(byte[] bytes) => new PdfString(bytes, true);

        /// <summary>
        /// A PDF "text string" (§7.9.2.2): plain ASCII stays a literal string,
        /// anything else becomes UTF-16BE with BOM so viewers show it correctly.
        /// </summary>
        public static PdfString Text(string text)
        {
            var ascii = true;
            foreach (var c in text)
            {
                if (c < 0x20 || c > 0x7E)
                {
                    ascii = false;
                    break;
                }
            }

            if (ascii)
            {
                return new PdfString(Encoding.ASCII.GetBytes(text), false);
            }

            var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
            var bytes = new byte[utf16.Length + 2];
            bytes[0] = 0xFE;
            bytes[1] = 0xFF;
            Buffer.BlockCopy(utf16, 0, bytes, 2, utf16.Length);
            return new PdfString(bytes, true);
        }

        internal override void WriteTo(PdfOutput output)
        {
            if (IsHex)
            {
                output.Write("<" + PdfFormat.Hex(Bytes) + ">");
                return;
            }

            var sb = new StringBuilder("(");
            foreach (var b in Bytes)
            {
                switch (b)
                {
                    case (byte)'(':
                    case (byte)')':
                    case (byte)'\\':
                        sb.Append('\\').Append((char)b);
                        break;
                    default:
                        if (b < 0x20 || b > 0x7E)
                        {
                            sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                        }
                        else
                        {
                            sb.Append((char)b);
                        }

                        break;
                }
            }

            sb.Append(')');
            output.Write(sb.ToString());
        }
    }

    internal sealed class PdfArray : PdfObject
    {
        public PdfArray(params PdfObject[] items) => Items = new List<PdfObject>(items);

        public List<PdfObject> Items { get; }

        public static PdfArray OfNumbers(params double[] values)
        {
            var array = new PdfArray();
            foreach (var v in values)
            {
                array.Items.Add(new PdfReal(v));
            }

            return array;
        }

        internal override void WriteTo(PdfOutput output)
        {
            output.Write("[");
            for (var i = 0; i < Items.Count; i++)
            {
                if (i > 0)
                {
                    output.Write(" ");
                }

                Items[i].WriteTo(output);
            }

            output.Write("]");
        }
    }

    internal sealed class PdfDictionary : PdfObject
    {
        // Ordered so output is deterministic and readable.
        private readonly List<KeyValuePair<string, PdfObject>> _entries = new List<KeyValuePair<string, PdfObject>>();

        public int Count => _entries.Count;

        public PdfDictionary Set(string key, PdfObject value)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key)
                {
                    _entries[i] = new KeyValuePair<string, PdfObject>(key, value);
                    return this;
                }
            }

            _entries.Add(new KeyValuePair<string, PdfObject>(key, value));
            return this;
        }

        public PdfDictionary Set(string key, string name) => Set(key, new PdfName(name));

        public PdfDictionary Set(string key, long value) => Set(key, new PdfInteger(value));

        public PdfObject? Get(string key)
        {
            foreach (var e in _entries)
            {
                if (e.Key == key)
                {
                    return e.Value;
                }
            }

            return null;
        }

        internal override void WriteTo(PdfOutput output)
        {
            output.Write("<<");
            foreach (var e in _entries)
            {
                output.Write(" ");
                new PdfName(e.Key).WriteTo(output);
                output.Write(" ");
                e.Value.WriteTo(output);
            }

            output.Write(" >>");
        }
    }

    internal sealed class PdfStream : PdfObject
    {
        public PdfStream(PdfDictionary dictionary, byte[] data)
        {
            Dictionary = dictionary;
            Data = data;
        }

        public PdfDictionary Dictionary { get; }

        public byte[] Data { get; }

        internal override void WriteTo(PdfOutput output)
        {
            Dictionary.Set("Length", Data.Length);
            Dictionary.WriteTo(output);
            output.Write("\nstream\n");
            output.Write(Data);
            output.Write("\nendstream");
        }
    }

    /// <summary>An object stored once in the file and referenced by number.</summary>
    internal sealed class PdfIndirectObject
    {
        public PdfIndirectObject(int number) => Number = number;

        public int Number { get; }

        public PdfObject? Value { get; set; }

        public PdfReference Reference => new PdfReference(this);
    }

    internal sealed class PdfReference : PdfObject
    {
        public PdfReference(PdfIndirectObject target) => Target = target;

        public PdfIndirectObject Target { get; }

        internal override void WriteTo(PdfOutput output) =>
            output.Write(Target.Number.ToString(CultureInfo.InvariantCulture) + " 0 R");
    }
}
