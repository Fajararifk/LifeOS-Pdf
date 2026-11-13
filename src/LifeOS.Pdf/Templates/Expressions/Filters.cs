using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LifeOS.Pdf.Templates.Expressions
{
    /// <summary>A named transformation applied with <c>value | name:arg1:arg2</c>.</summary>
    internal sealed class Filter
    {
        public Filter(string name, int minArgs, int maxArgs, string usage, Func<object?, object?[], TemplateOptions, object?> apply)
        {
            Name = name;
            MinArgs = minArgs;
            MaxArgs = maxArgs;
            Usage = usage;
            Apply = apply;
        }

        public string Name { get; }

        public int MinArgs { get; }

        public int MaxArgs { get; }

        public string Usage { get; }

        public Func<object?, object?[], TemplateOptions, object?> Apply { get; }
    }

    internal static class Filters
    {
        private static readonly Dictionary<string, Filter> All = new Dictionary<string, Filter>(StringComparer.OrdinalIgnoreCase);

        static Filters()
        {
            // Formatting
            Add("rupiah", 0, 1, "{{ Total | rupiah }} → Rp1.350.000", (v, a, o) => Num(v, n => TextFormats.Rupiah(n, Int(a, 0, 0))));
            Add("currency", 0, 2, "{{ Price | currency:\"$\":2 }} → $1,350.00", (v, a, o) =>
                Num(v, n => TextFormats.Currency(n, a.Length > 0 ? Str(a[0], o) : "Rp", Int(a, 1, a.Length > 0 && Str(a[0], o) != "Rp" ? 2 : 0), o.Language)));
            Add("number", 0, 1, "{{ Qty | number:2 }} → 1.350,50", (v, a, o) => Num(v, n => TextFormats.Number(n, Int(a, 0, 0), o.Language)));
            Add("percent", 0, 1, "{{ Rate | percent:1 }} → 31,5% (0.315 = 31.5%)", (v, a, o) => Num(v, n => TextFormats.Percent(n, Int(a, 0, 0), o.Language)));
            Add("round", 0, 1, "{{ Value | round:2 }}", (v, a, o) => Num(v, n => Math.Round(n, Int(a, 0, 0), MidpointRounding.AwayFromZero)));
            Add("date", 0, 1, "{{ Date | date:\"d MMMM yyyy\" }} → 13 November 2026", (v, a, o) =>
            {
                var d = Values.ToDate(v);
                return d.HasValue ? TextFormats.Date(d.Value, a.Length > 0 ? Str(a[0], o) : null, o.Language) : v;
            });
            Add("terbilang", 0, 0, "{{ Total | terbilang }} → satu juta tiga ratus lima puluh ribu", (v, a, o) => Num(v, TextFormats.Terbilang));
            Add("words", 0, 0, "{{ Total | words }} → one million three hundred fifty thousand", (v, a, o) => Num(v, TextFormats.Words));

            // Text
            Add("upper", 0, 0, "{{ Name | upper }}", (v, a, o) => v == null ? null : Values.ToText(v, o).ToUpperInvariant());
            Add("lower", 0, 0, "{{ Name | lower }}", (v, a, o) => v == null ? null : Values.ToText(v, o).ToLowerInvariant());
            Add("title", 0, 0, "{{ Name | title }} → Fajar Arif", (v, a, o) => v == null ? null : TitleCase(Values.ToText(v, o)));
            Add("default", 1, 1, "{{ Note | default:\"-\" }}", (v, a, o) => Values.IsTruthy(v) || (Values.IsNumeric(v)) ? v : a[0]);
            Add("join", 0, 1, "{{ Tags | join:\", \" }}", (v, a, o) => string.Join(a.Length > 0 ? Str(a[0], o) : ", ", Items(v).Select(x => Values.ToText(x, o))));

            // Lists
            Add("count", 0, 0, "{{ Items | count }}", (v, a, o) => v == null ? 0m : v is string s ? s.Length : (decimal)Items(v).Count);
            Add("sum", 0, 1, "{{ Items | sum:\"Amount\" }}", (v, a, o) => Numbers(v, a, o).Sum());
            Add("avg", 0, 1, "{{ Items | avg:\"Amount\" }}", (v, a, o) => { var n = Numbers(v, a, o); return n.Count == 0 ? (object?)null : n.Average(); });
            Add("min", 0, 1, "{{ Items | min:\"Amount\" }}", (v, a, o) => { var n = Numbers(v, a, o); return n.Count == 0 ? (object?)null : n.Min(); });
            Add("max", 0, 1, "{{ Items | max:\"Amount\" }}", (v, a, o) => { var n = Numbers(v, a, o); return n.Count == 0 ? (object?)null : n.Max(); });
            Add("sumproduct", 2, 2, "{{ Items | sumproduct:\"Quantity\":\"Price\" }} → Σ quantity × price", (v, a, o) =>
            {
                decimal total = 0;
                foreach (var item in Items(v))
                {
                    var x = Values.ToNumber(Field(item, Str(a[0], o)));
                    var y = Values.ToNumber(Field(item, Str(a[1], o)));
                    if (x.HasValue && y.HasValue)
                    {
                        total += x.Value * y.Value;
                    }
                }

                return total;
            });
            Add("first", 0, 0, "{{ Items | first }}", (v, a, o) => Items(v).FirstOrDefault());
            Add("last", 0, 0, "{{ Items | last }}", (v, a, o) => Items(v).LastOrDefault());
            Add("sort", 0, 1, "<foreach in=\"Items | sort:'Date'\">", (v, a, o) => Sort(v, a, o, descending: false));
            Add("sortdesc", 0, 1, "<foreach in=\"Items | sortdesc:'Amount'\">", (v, a, o) => Sort(v, a, o, descending: true));
            Add("where", 2, 2, "<foreach in=\"Items | where:'Category':'Makan'\">", (v, a, o) =>
                Items(v).Where(x => Values.AreEqual(Field(x, Str(a[0], o)), a[1])).ToList());
            Add("groupby", 1, 2, "<foreach in=\"Items | groupby:'Category':'Amount'\" as=\"g\"> {{ g.Key }} {{ g.Count }} {{ g.Total }}", (v, a, o) =>
            {
                // Each group: Key, Items, Count and — with a second argument — Total (sum of that field).
                var field = Str(a[0], o);
                var sumField = a.Length > 1 ? Str(a[1], o) : null;
                var groups = new List<Dictionary<string, object?>>();
                var index = new Dictionary<string, List<object?>>(StringComparer.Ordinal);
                foreach (var item in Items(v))
                {
                    var key = Field(item, field);
                    var text = Values.ToText(key, o);
                    if (!index.TryGetValue(text, out var members))
                    {
                        // Groups keep the order in which keys first appear.
                        members = new List<object?>();
                        index[text] = members;
                        groups.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Key"] = key, ["Items"] = members });
                    }

                    members.Add(item);
                }

                foreach (var g in groups)
                {
                    var members = (List<object?>)g["Items"]!;
                    g["Count"] = (decimal)members.Count;
                    if (sumField != null)
                    {
                        g["Total"] = Numbers(members, new object?[] { sumField }, o).Sum();
                    }
                }

                return groups.Cast<object?>().ToList();
            });
        }

        public static IEnumerable<string> Names => All.Keys.OrderBy(k => k, StringComparer.Ordinal);

        public static bool TryGet(string name, out Filter filter) => All.TryGetValue(name, out filter!);

        /// <summary>Elements of a list-like value; a single non-list value counts as one element.</summary>
        internal static List<object?> Items(object? v)
        {
            var result = new List<object?>();
            if (v == null)
            {
                return result;
            }

            if (v is string || v is IDictionary || v is IDictionary<string, object?>)
            {
                result.Add(v);
                return result;
            }

            if (v is IEnumerable e)
            {
                foreach (var x in e)
                {
                    result.Add(x);
                }

                return result;
            }

            result.Add(v);
            return result;
        }

        private static void Add(string name, int min, int max, string usage, Func<object?, object?[], TemplateOptions, object?> apply) =>
            All[name] = new Filter(name, min, max, usage, apply);

        private static object? Num(object? v, Func<decimal, object?> f)
        {
            var n = Values.ToNumber(v);
            return n.HasValue ? f(n.Value) : v;
        }

        private static string Str(object? v, TemplateOptions o) => Values.ToText(v, o);

        private static int Int(object?[] args, int index, int fallback)
        {
            if (args.Length <= index)
            {
                return fallback;
            }

            var n = Values.ToNumber(args[index]);
            if (!n.HasValue)
            {
                throw new ArgumentException("argument " + (index + 1) + " must be a number");
            }

            return (int)n.Value;
        }

        private static object? Field(object? item, string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return item;
            }

            object? value = item;
            foreach (var part in field.Split('.'))
            {
                if (!ValueAccess.TryGetMember(value, part, out value))
                {
                    return null;
                }
            }

            return value;
        }

        private static List<decimal> Numbers(object? v, object?[] a, TemplateOptions o)
        {
            var field = a.Length > 0 ? Str(a[0], o) : string.Empty;
            var result = new List<decimal>();
            foreach (var item in Items(v))
            {
                var n = Values.ToNumber(Field(item, field));
                if (n.HasValue)
                {
                    result.Add(n.Value);
                }
            }

            return result;
        }

        private static List<object?> Sort(object? v, object?[] a, TemplateOptions o, bool descending)
        {
            var field = a.Length > 0 ? Str(a[0], o) : string.Empty;
            var items = Items(v);
            // Stable sort: equal keys keep their original order (deterministic output).
            var indexed = items.Select((x, i) => new KeyValuePair<int, object?>(i, x)).ToList();
            indexed.Sort((x, y) =>
            {
                var c = Values.Compare(Field(x.Value, field), Field(y.Value, field)) ?? 0;
                if (descending)
                {
                    c = -c;
                }

                return c != 0 ? c : x.Key.CompareTo(y.Key);
            });
            return indexed.Select(x => x.Value).ToList();
        }

        private static string TitleCase(string s)
        {
            var chars = s.ToLowerInvariant().ToCharArray();
            var start = true;
            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    if (start)
                    {
                        chars[i] = char.ToUpperInvariant(chars[i]);
                    }

                    start = false;
                }
                else
                {
                    // A new word starts after anything but a letter, digit or apostrophe ("Jum'at").
                    start = !char.IsDigit(chars[i]) && chars[i] != '\'';
                }
            }

            return new string(chars);
        }
    }
}
