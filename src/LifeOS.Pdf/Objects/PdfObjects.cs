using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LifeOS.Pdf.Internal;

namespace LifeOS.Pdf.Objects
{
    // COS object model (ISO 32000-1 §7.3), shared by the writer and the parser.
    // Parsed objects keep their original spelling where it matters for round trips
    // (real numbers, name bytes) so copying a page does not change its content.

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

        internal override void WriteTo(PdfOutput output) => Write(output, Value);

        /// <summary>Writes "/name" with #xx escapes (§7.3.5) straight to the output.</summary>
        internal static void Write(PdfOutput output, string value)
        {
            output.WriteByte((byte)'/');
            foreach (var c in value)
            {
                if (c > 0xFF)
                {
                    // Not representable as single bytes: the whole name is UTF-8.
                    foreach (var b in Bytes(value))
                    {
                        WriteByte(output, b);
                    }

                    return;
                }
            }

            foreach (var c in value)
            {
                WriteByte(output, (byte)c);
            }
        }

        private static void WriteByte(PdfOutput output, byte b)
        {
            if (b < 0x21 || b > 0x7E || Delimiters.IndexOf((char)b) >= 0)
            {
                output.WriteByte((byte)'#');
                output.WriteByte((byte)"0123456789ABCDEF"[b >> 4]);
                output.WriteByte((byte)"0123456789ABCDEF"[b & 15]);
            }
            else
            {
                output.WriteByte(b);
            }
        }

        /// <summary>
        /// Characters up to U+00FF are single bytes (so names read from a file, kept as Latin-1,
        /// are written back unchanged); anything above is UTF-8 (§7.3.5 recommends UTF-8).
        /// </summary>
        private static byte[] Bytes(string value)
        {
            var latin = true;
            foreach (var c in value)
            {
                if (c > 0xFF)
                {
                    latin = false;
                    break;
                }
            }

            if (!latin)
            {
                return Encoding.UTF8.GetBytes(value);
            }

            var bytes = new byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                bytes[i] = (byte)value[i];
            }

            return bytes;
        }
    }

    internal sealed class PdfInteger : PdfObject
    {
        public PdfInteger(long value) => Value = value;

        public long Value { get; }

        internal override void WriteTo(PdfOutput output) => output.WriteInteger(Value);
    }

    internal sealed class PdfReal : PdfObject
    {
        public PdfReal(double value) => Value = value;

        /// <summary>A number read from a file: written back exactly as it was spelled.</summary>
        public PdfReal(double value, string spelling)
        {
            Value = value;
            Spelling = spelling;
        }

        public double Value { get; }

        public string? Spelling { get; }

        internal override void WriteTo(PdfOutput output)
        {
            if (Spelling != null)
            {
                output.Write(Spelling);
            }
            else
            {
                output.WriteNumber(Value);
            }
        }
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

        public static PdfString FromBytes(byte[] bytes, bool hex) => new PdfString(bytes, hex);

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
                output.Write("<" + PdfFormat.Hex(output.Encrypt(Bytes, false)) + ">");
                return;
            }

            var data = output.Encrypt(Bytes, false);
            output.WriteByte((byte)'(');
            foreach (var b in data)
            {
                if (b == '(' || b == ')' || b == '\\')
                {
                    output.WriteByte((byte)'\\');
                    output.WriteByte(b);
                }
                else if (b < 0x20 || b > 0x7E)
                {
                    // Three-digit octal escape (7.3.4.2).
                    output.WriteByte((byte)'\\');
                    output.WriteByte((byte)('0' + (b >> 6)));
                    output.WriteByte((byte)('0' + ((b >> 3) & 7)));
                    output.WriteByte((byte)('0' + (b & 7)));
                }
                else
                {
                    output.WriteByte(b);
                }
            }

            output.WriteByte((byte)')');
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

        public IReadOnlyList<KeyValuePair<string, PdfObject>> Entries => _entries;

        public bool Remove(string key)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key)
                {
                    _entries.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

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
                output.WriteByte((byte)' ');
                PdfName.Write(output, e.Key);
                output.WriteByte((byte)' ');
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
            var data = output.Encrypt(Data, true);
            Dictionary.Set("Length", data.Length);
            Dictionary.WriteTo(output);
            output.Write("\nstream\n");
            output.Write(data);
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

    /// <summary>
    /// A reference inside a parsed file ("12 0 R"), resolved lazily through its parser.
    /// It is never written directly: pages are copied into the output first.
    /// </summary>
    internal sealed class PdfSourceReference : PdfObject
    {
        public PdfSourceReference(Parsing.PdfParser parser, int number, int generation)
        {
            Parser = parser;
            Number = number;
            Generation = generation;
        }

        public Parsing.PdfParser Parser { get; }

        public int Number { get; }

        public int Generation { get; }

        internal override void WriteTo(PdfOutput output) =>
            throw new InvalidOperationException("Internal error: a reference into a source file was not copied.");
    }

    internal sealed class PdfReference : PdfObject
    {
        public PdfReference(PdfIndirectObject target) => Target = target;

        public PdfIndirectObject Target { get; }

        internal override void WriteTo(PdfOutput output)
        {
            output.WriteInteger(Target.Number);
            output.Write(" 0 R");
        }
    }
}
