using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace LifeOS.Pdf.Templates.Expressions
{
    /// <summary>
    /// Read-only access to template data: dictionaries, lists and public instance
    /// properties/fields. Never calls methods, so a template cannot run code.
    /// </summary>
    internal static class ValueAccess
    {
        private static readonly Dictionary<Type, Dictionary<string, Func<object, object?>>> Accessors =
            new Dictionary<Type, Dictionary<string, Func<object, object?>>>();

        public static bool TryGetMember(object? target, string name, out object? value)
        {
            value = null;
            switch (target)
            {
                case null:
                    return false;
                case IDictionary<string, object?> generic:
                    if (generic.TryGetValue(name, out value))
                    {
                        return true;
                    }

                    foreach (var pair in generic)
                    {
                        if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                        {
                            value = pair.Value;
                            return true;
                        }
                    }

                    return false;
                case IDictionary dictionary:
                    if (dictionary.Contains(name))
                    {
                        value = dictionary[name];
                        return true;
                    }

                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Key is string key && string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                        {
                            value = entry.Value;
                            return true;
                        }
                    }

                    return false;
            }

            if (target is ICollection collection && string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase))
            {
                value = (decimal)collection.Count;
                return true;
            }

            var accessors = AccessorsFor(target.GetType());
            if (accessors.TryGetValue(name, out var getter))
            {
                value = getter(target);
                return true;
            }

            return false;
        }

        public static bool TryGetIndex(object? target, object? index, out object? value)
        {
            value = null;
            if (target is IList list)
            {
                var n = Values.ToNumber(index);
                if (!n.HasValue || n.Value != Math.Floor(n.Value))
                {
                    return false;
                }

                var i = (long)n.Value;
                if (i < 0)
                {
                    i += list.Count; // items[-1] = last item
                }

                if (i < 0 || i >= list.Count)
                {
                    return true; // out of range: empty, not an error
                }

                value = list[(int)i];
                return true;
            }

            return index is string key && TryGetMember(target, key, out value);
        }

        private static Dictionary<string, Func<object, object?>> AccessorsFor(Type type)
        {
            lock (Accessors)
            {
                if (Accessors.TryGetValue(type, out var cached))
                {
                    return cached;
                }

                var map = new Dictionary<string, Func<object, object?>>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.CanRead && p.GetIndexParameters().Length == 0 && p.GetMethod != null && p.GetMethod.IsPublic && !map.ContainsKey(p.Name))
                    {
                        var property = p;
                        map[p.Name] = o => property.GetValue(o);
                    }
                }

                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!map.ContainsKey(f.Name))
                    {
                        var field = f;
                        map[f.Name] = o => field.GetValue(o);
                    }
                }

                Accessors[type] = map;
                return map;
            }
        }
    }

    /// <summary>Conversions, comparisons and truthiness shared by expressions and filters.</summary>
    internal static class Values
    {
        public static bool IsTruthy(object? v)
        {
            switch (v)
            {
                case null: return false;
                case bool b: return b;
                case string s: return s.Length > 0;
                case ICollection c: return c.Count > 0;
                case IEnumerable e:
                    foreach (var _ in e)
                    {
                        return true;
                    }

                    return false;
            }

            var n = ToNumber(v);
            return !n.HasValue || n.Value != 0;
        }

        public static decimal? ToNumber(object? v)
        {
            try
            {
                switch (v)
                {
                    case decimal d: return d;
                    case int i: return i;
                    case long l: return l;
                    case short s: return s;
                    case byte b: return b;
                    case uint ui: return ui;
                    case ulong ul: return ul;
                    case ushort us: return us;
                    case sbyte sb: return sb;
                    case double db: return double.IsNaN(db) || double.IsInfinity(db) ? (decimal?)null : (decimal)db;
                    case float f: return float.IsNaN(f) || float.IsInfinity(f) ? (decimal?)null : (decimal)f;
                    case string str:
                        return decimal.TryParse(str.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
                    default: return null;
                }
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        public static bool IsNumeric(object? v) => v != null && !(v is string) && !(v is bool) && ToNumber(v).HasValue;

        public static DateTime? ToDate(object? v)
        {
            switch (v)
            {
                case DateTime dt: return dt;
                case DateTimeOffset dto: return dto.DateTime;
                case string s:
                    // LifeOS stores dates as "YYYY-MM-DD"; accept ISO 8601 date-times too.
                    string[] formats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss", "yyyy-MM" };
                    return DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
                default: return null;
            }
        }

        public static bool AreEqual(object? a, object? b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }

            if (IsNumeric(a) && IsNumeric(b))
            {
                return ToNumber(a) == ToNumber(b);
            }

            if (a is string || b is string)
            {
                return string.Equals(ToPlainString(a), ToPlainString(b), StringComparison.Ordinal);
            }

            return a.Equals(b);
        }

        /// <summary>Orders two values (numbers, dates, strings); null when they are not comparable.</summary>
        public static int? Compare(object? a, object? b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            var na = ToNumber(a);
            var nb = ToNumber(b);
            if (na.HasValue && nb.HasValue && (IsNumeric(a) || IsNumeric(b)))
            {
                return na.Value.CompareTo(nb.Value);
            }

            if (a is DateTime || a is DateTimeOffset || b is DateTime || b is DateTimeOffset)
            {
                var da = ToDate(a);
                var db = ToDate(b);
                return da.HasValue && db.HasValue ? da.Value.CompareTo(db.Value) : (int?)null;
            }

            return string.CompareOrdinal(ToPlainString(a), ToPlainString(b));
        }

        /// <summary>Text for output: numbers in the template language, dates with the default format.</summary>
        public static string ToText(object? v, TemplateOptions options)
        {
            switch (v)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case DateTime _:
                case DateTimeOffset _:
                    return TextFormats.Date(ToDate(v)!.Value, null, options.Language);
            }

            if (IsNumeric(v))
            {
                // No thousands separators by default (IDs and years must not become "2.026").
                var n = ToNumber(v)!.Value;
                var text = n.ToString("0.############################", CultureInfo.InvariantCulture);
                return options.Language == TemplateLanguage.Indonesian ? text.Replace('.', ',') : text;
            }

            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string ToPlainString(object v) =>
            v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v.ToString() ?? string.Empty;
    }
}
