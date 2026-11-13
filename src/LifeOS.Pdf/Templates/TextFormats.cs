using System;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Templates
{
    /// <summary>
    /// Number, currency, date and number-word formatting used by templates, available for
    /// direct use too. Indonesian rules are built in rather than taken from the OS culture,
    /// so results are identical on every machine (including Linux containers running with
    /// invariant globalization).
    /// </summary>
    public static class TextFormats
    {
        private static readonly string[] MonthsId = { "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };
        private static readonly string[] MonthsShortId = { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des" };
        private static readonly string[] DaysId = { "Minggu", "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu" };
        private static readonly string[] MonthsEn = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        private static readonly string[] DaysEn = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        private static readonly string[] OnesId = { "", "satu", "dua", "tiga", "empat", "lima", "enam", "tujuh", "delapan", "sembilan" };
        private static readonly string[] OnesEn = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        private static readonly string[] TensEn = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        /// <summary>Rp1.350.000 (whole rupiah, rounded half away from zero). Negative: -Rp1.350.000.</summary>
        public static string Rupiah(decimal amount, int decimals = 0) => Currency(amount, "Rp", decimals, TemplateLanguage.Indonesian);

        /// <summary>Symbol directly before the number: Rp1.350.000 or $1,350,000.00.</summary>
        public static string Currency(decimal amount, string symbol, int decimals, TemplateLanguage language)
        {
            var text = Number(Math.Abs(amount), decimals, language);
            return (amount < 0 && text.Trim('0', '.', ',').Length > 0 ? "-" : string.Empty) + symbol + text;
        }

        /// <summary>1.350.000,50 (Indonesian) or 1,350,000.50 (English).</summary>
        public static string Number(decimal value, int decimals, TemplateLanguage language)
        {
            if (decimals < 0 || decimals > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "Use 0 to 10 decimals.");
            }

            var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            var text = rounded.ToString("#,0" + (decimals > 0 ? "." + new string('0', decimals) : string.Empty), CultureInfo.InvariantCulture);
            if (language == TemplateLanguage.Indonesian)
            {
                // Swap separators: 1,350,000.50 → 1.350.000,50
                var sb = new StringBuilder(text.Length);
                foreach (var c in text)
                {
                    sb.Append(c == ',' ? '.' : c == '.' ? ',' : c);
                }

                text = sb.ToString();
            }

            return text;
        }

        /// <summary>A fraction as percentage: 0.315 → "31,5%" (decimals = 1).</summary>
        public static string Percent(decimal fraction, int decimals, TemplateLanguage language) => Number(fraction * 100, decimals, language) + "%";

        /// <summary>
        /// Formats a date. Tokens: d dd ddd dddd, M MM MMM MMMM, yy yyyy, H HH, m mm, s ss;
        /// text in single quotes is literal. Default "d MMM yyyy" → 13 Nov 2026.
        /// </summary>
        public static string Date(DateTime date, string? format, TemplateLanguage language)
        {
            var f = string.IsNullOrEmpty(format) ? "d MMM yyyy" : format!;
            var sb = new StringBuilder();
            var id = language == TemplateLanguage.Indonesian;
            var i = 0;
            while (i < f.Length)
            {
                var c = f[i];
                if (c == '\'')
                {
                    var end = f.IndexOf('\'', i + 1);
                    if (end < 0)
                    {
                        end = f.Length;
                    }

                    sb.Append(f, i + 1, end - i - 1);
                    i = end + 1;
                    continue;
                }

                var run = 1;
                while (i + run < f.Length && f[i + run] == c)
                {
                    run++;
                }

                switch (c)
                {
                    case 'd':
                        sb.Append(run == 1 ? date.Day.ToString(CultureInfo.InvariantCulture)
                            : run == 2 ? date.Day.ToString("00", CultureInfo.InvariantCulture)
                            : run == 3 ? (id ? DaysId : DaysEn)[(int)date.DayOfWeek].Substring(0, 3)
                            : (id ? DaysId : DaysEn)[(int)date.DayOfWeek]);
                        break;
                    case 'M':
                        sb.Append(run == 1 ? date.Month.ToString(CultureInfo.InvariantCulture)
                            : run == 2 ? date.Month.ToString("00", CultureInfo.InvariantCulture)
                            : run == 3 ? (id ? MonthsShortId[date.Month - 1] : MonthsEn[date.Month - 1].Substring(0, 3))
                            : (id ? MonthsId : MonthsEn)[date.Month - 1]);
                        break;
                    case 'y':
                        sb.Append(run <= 2 ? (date.Year % 100).ToString("00", CultureInfo.InvariantCulture) : date.Year.ToString("0000", CultureInfo.InvariantCulture));
                        break;
                    case 'H':
                        sb.Append(date.Hour.ToString(run == 1 ? "0" : "00", CultureInfo.InvariantCulture));
                        break;
                    case 'm':
                        sb.Append(date.Minute.ToString(run == 1 ? "0" : "00", CultureInfo.InvariantCulture));
                        break;
                    case 's':
                        sb.Append(date.Second.ToString(run == 1 ? "0" : "00", CultureInfo.InvariantCulture));
                        break;
                    default:
                        sb.Append(c, run);
                        break;
                }

                i += run;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Indonesian number words ("terbilang"), as used on receipts:
        /// 1350000 → "satu juta tiga ratus lima puluh ribu". Rounded to a whole number.
        /// </summary>
        public static string Terbilang(decimal value)
        {
            var n = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (n == 0)
            {
                return "nol";
            }

            var negative = n < 0;
            n = Math.Abs(n);
            string[] scales = { "", "ribu", "juta", "miliar", "triliun", "kuadriliun", "kuintiliun" };
            var parts = new System.Collections.Generic.List<string>();
            var scale = 0;
            while (n > 0)
            {
                var group = (int)(n % 1000);
                n = Math.Floor(n / 1000);
                if (group > 0)
                {
                    if (scale >= scales.Length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value, "Number is too large to write in words.");
                    }

                    // 1000 is "seribu", not "satu ribu".
                    var words = scale == 1 && group == 1 ? "seribu" : Hundreds(group) + (scale > 0 ? " " + scales[scale] : string.Empty);
                    parts.Insert(0, words);
                }

                scale++;
            }

            return (negative ? "minus " : string.Empty) + string.Join(" ", parts);
        }

        /// <summary>English number words: 1350000 → "one million three hundred fifty thousand".</summary>
        public static string Words(decimal value)
        {
            var n = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (n == 0)
            {
                return "zero";
            }

            var negative = n < 0;
            n = Math.Abs(n);
            string[] scales = { "", "thousand", "million", "billion", "trillion", "quadrillion", "quintillion" };
            var parts = new System.Collections.Generic.List<string>();
            var scale = 0;
            while (n > 0)
            {
                var group = (int)(n % 1000);
                n = Math.Floor(n / 1000);
                if (group > 0)
                {
                    if (scale >= scales.Length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value, "Number is too large to write in words.");
                    }

                    parts.Insert(0, HundredsEn(group) + (scale > 0 ? " " + scales[scale] : string.Empty));
                }

                scale++;
            }

            return (negative ? "minus " : string.Empty) + string.Join(" ", parts);
        }

        private static string Hundreds(int n)
        {
            var sb = new StringBuilder();
            var h = n / 100;
            var rest = n % 100;
            if (h == 1)
            {
                sb.Append("seratus");
            }
            else if (h > 1)
            {
                sb.Append(OnesId[h]).Append(" ratus");
            }

            if (rest > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                if (rest < 10)
                {
                    sb.Append(OnesId[rest]);
                }
                else if (rest == 10)
                {
                    sb.Append("sepuluh");
                }
                else if (rest == 11)
                {
                    sb.Append("sebelas");
                }
                else if (rest < 20)
                {
                    sb.Append(OnesId[rest - 10]).Append(" belas");
                }
                else
                {
                    sb.Append(OnesId[rest / 10]).Append(" puluh");
                    if (rest % 10 > 0)
                    {
                        sb.Append(' ').Append(OnesId[rest % 10]);
                    }
                }
            }

            return sb.ToString();
        }

        private static string HundredsEn(int n)
        {
            var sb = new StringBuilder();
            var h = n / 100;
            var rest = n % 100;
            if (h > 0)
            {
                sb.Append(OnesEn[h]).Append(" hundred");
            }

            if (rest > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                if (rest < 20)
                {
                    sb.Append(OnesEn[rest]);
                }
                else
                {
                    sb.Append(TensEn[rest / 10]);
                    if (rest % 10 > 0)
                    {
                        sb.Append('-').Append(OnesEn[rest % 10]);
                    }
                }
            }

            return sb.ToString();
        }
    }
}
