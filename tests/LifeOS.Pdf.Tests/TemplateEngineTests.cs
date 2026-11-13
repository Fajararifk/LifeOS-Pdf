using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using LifeOS.Pdf.Templates;
using LifeOS.Pdf.Templates.Expressions;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>JSON parser, expressions, filters and formatting.</summary>
    public class TemplateEngineTests
    {
        private static object? Eval(string expression, object? data = null, TemplateOptions? options = null) =>
            Expression.Parse(expression, 1, 1).Evaluate(new Scope(data, options ?? new TemplateOptions()));

        private static string Text(string expression, object? data = null, TemplateOptions? options = null)
        {
            var o = options ?? new TemplateOptions();
            return Values.ToText(Eval(expression, data, o), o);
        }

        // ------------------------------------------------------------------ JSON

        [Fact]
        public void Json_ParsesAllValueKinds()
        {
            var data = (Dictionary<string, object?>)TemplateData.FromJson(
                "{\"name\":\"Fajar \\u00e9\\ud83d\\ude00\",\"amount\":1350000.50,\"big\":1e400,\"ok\":true,\"none\":null,\"list\":[1,2,{\"x\":\"y\"}]}")!;

            Assert.Equal("Fajar é😀", data["NAME"]);           // keys are case-insensitive
            Assert.Equal(1350000.50m, data["amount"]);          // exact decimal
            Assert.IsType<double>(data["big"]);                 // out of decimal range
            Assert.Equal(true, data["ok"]);
            Assert.Null(data["none"]);
            Assert.Equal(3, ((List<object?>)data["list"]!).Count);
        }

        [Theory]
        [InlineData("{\"a\":1,}", "Trailing comma", 1)]
        [InlineData("[1,2,]", "Trailing comma", 1)]
        [InlineData("{\"a\":\n  \"unterminated}", "Unterminated string", 2)]
        [InlineData("{'a':1}", "property name", 1)]
        [InlineData("[1] 2", "after the JSON value", 1)]
        [InlineData("01", "after the JSON value", 1)]
        public void Json_ErrorsAreClearAndLocated(string json, string message, int line)
        {
            var ex = Assert.Throws<TemplateException>(() => TemplateData.FromJson(json));
            Assert.Contains(message, ex.Message);
            Assert.Equal(line, ex.Line);
        }

        [Fact]
        public void Json_DeepNestingIsRejected()
        {
            Assert.Contains("nested", Assert.Throws<TemplateException>(() => TemplateData.FromJson(new string('[', 300) + new string(']', 300))).Message);
        }

        // ------------------------------------------------------------------ expressions

        [Theory]
        [InlineData("1 + 2 * 3", "7")]
        [InlineData("(1 + 2) * 3", "9")]
        [InlineData("10 / 4", "2,5")]
        [InlineData("10 / 0", "")]
        [InlineData("-5 + 2", "-3")]
        [InlineData("'a' + 1", "a1")]
        [InlineData("3 > 2 && !(1 == 2)", "true")]
        [InlineData("null == null", "true")]
        [InlineData("'b' > 'a'", "true")]
        public void Expressions_Operators(string expression, string expected)
        {
            Assert.Equal(expected, Text(expression));
        }

        [Fact]
        public void Filters_BindLoosest()
        {
            var data = new { Qty = 3, Price = 1500, Items = new[] { 1, 2, 3 } };
            Assert.Equal("Rp4.500", Text("Qty * Price | rupiah", data));
            Assert.Equal("true", Text("(Items | count) > 2", data));
        }

        [Fact]
        public void Paths_AreNullSafeCaseInsensitiveAndIndexable()
        {
            var data = new
            {
                Customer = new { Name = "Budi", Address = (object?)null },
                Items = new List<object> { new { Name = "A" }, new { Name = "B" } },
            };

            Assert.Equal("Budi", Text("customer.NAME", data));
            Assert.Equal(string.Empty, Text("Customer.Address.City", data));
            Assert.Equal("A", Text("Items[0].Name", data));
            Assert.Equal("B", Text("Items[-1].Name", data));
            Assert.Equal(string.Empty, Text("Items[5].Name", data));
            Assert.Equal("2", Text("Items.Count", data));
        }

        [Fact]
        public void StrictMode_ReportsMissingData()
        {
            var ex = Assert.Throws<TemplateException>(() => Eval("Customer.Nmae", new { Customer = new { Name = "x" } }, new TemplateOptions { Strict = true }));
            Assert.Contains("Customer.Nmae", ex.Message);
            Assert.Null(Eval("Missing", new { }));
        }

        [Fact]
        public void Methods_CannotBeCalledFromData()
        {
            // GetType / ToString are methods, not properties: invisible to templates.
            Assert.Equal(string.Empty, Text("Name.GetType", new { Name = "x" }));
            Assert.Throws<TemplateException>(() => Eval("Name.ToString()", new { Name = "x" }));
        }

        [Fact]
        public void UnknownFilterAndWrongArity_FailAtParseTime()
        {
            var unknown = Assert.Throws<TemplateException>(() => Expression.Parse("Total | rupia", 4, 9));
            Assert.Contains("rupiah", unknown.Message); // suggests the available names
            Assert.Equal(4, unknown.Line);

            Assert.Contains("takes 2 argument", Assert.Throws<TemplateException>(() => Expression.Parse("x | where:'a'", 1, 1)).Message);
        }

        // ------------------------------------------------------------------ formatting

        [Theory]
        [InlineData(1350000, "Rp1.350.000")]
        [InlineData(1350000.5, "Rp1.350.001")]
        [InlineData(-25000, "-Rp25.000")]
        [InlineData(0, "Rp0")]
        public void Rupiah(double value, string expected) => Assert.Equal(expected, TextFormats.Rupiah((decimal)value));

        [Fact]
        public void Numbers_PercentAndCurrency_InBothLanguages()
        {
            Assert.Equal("1.350.000,50", TextFormats.Number(1350000.5m, 2, TemplateLanguage.Indonesian));
            Assert.Equal("1,350,000.50", TextFormats.Number(1350000.5m, 2, TemplateLanguage.English));
            Assert.Equal("31,5%", TextFormats.Percent(0.315m, 1, TemplateLanguage.Indonesian));
            Assert.Equal("$1,350.00", Text("1350 | currency:'$'", options: new TemplateOptions { Language = TemplateLanguage.English }));
            Assert.Equal("2026", Text("2026")); // plain numbers get no thousands separator
        }

        [Fact]
        public void Formatting_IgnoresTheMachineCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                foreach (var culture in new[] { "ar-SA", "de-DE", "id-ID", "fr-FR" })
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                    Assert.Equal("Rp1.350.000", TextFormats.Rupiah(1350000));
                    Assert.Equal("13 Nov 2026", Text("'2026-11-13' | date"));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Theory]
        [InlineData("d MMM yyyy", "13 Nov 2026")]
        [InlineData("dddd, d MMMM yyyy", "Jumat, 13 November 2026")]
        [InlineData("dd/MM/yy", "13/11/26")]
        [InlineData("MMMM yyyy", "November 2026")]
        [InlineData("'Tanggal' d", "Tanggal 13")]
        public void Dates_Indonesian(string format, string expected) =>
            Assert.Equal(expected, TextFormats.Date(new DateTime(2026, 11, 13), format, TemplateLanguage.Indonesian));

        [Fact]
        public void Dates_AcceptLifeOsStringsAndEnglish()
        {
            Assert.Equal("Friday, November 13, 2026", Text("'2026-11-13' | date:'dddd, MMMM d, yyyy'", options: new TemplateOptions { Language = TemplateLanguage.English }));
            Assert.Equal("Agustus 2026", Text("'2026-08' | date:'MMMM yyyy'"));
            Assert.Equal("bukan tanggal", Text("'bukan tanggal' | date"));
        }

        [Theory]
        [InlineData(0, "nol")]
        [InlineData(1, "satu")]
        [InlineData(10, "sepuluh")]
        [InlineData(11, "sebelas")]
        [InlineData(15, "lima belas")]
        [InlineData(100, "seratus")]
        [InlineData(115, "seratus lima belas")]
        [InlineData(1000, "seribu")]
        [InlineData(1011, "seribu sebelas")]
        [InlineData(2001, "dua ribu satu")]
        [InlineData(100000, "seratus ribu")]
        [InlineData(1350000, "satu juta tiga ratus lima puluh ribu")]
        [InlineData(1000000000, "satu miliar")]
        [InlineData(2500000000000, "dua triliun lima ratus miliar")]
        [InlineData(-75, "minus tujuh puluh lima")]
        public void Terbilang(double value, string expected) => Assert.Equal(expected, TextFormats.Terbilang((decimal)value));

        [Fact]
        public void Words_English()
        {
            Assert.Equal("one million three hundred fifty thousand", TextFormats.Words(1350000));
            Assert.Equal("ninety-nine", TextFormats.Words(99));
            Assert.Equal("zero", TextFormats.Words(0));
        }

        // ------------------------------------------------------------------ list filters

        private static readonly object Shop = TemplateData.FromJson(
            "{\"Items\":[{\"Cat\":\"Makan\",\"Qty\":2,\"Price\":10000,\"Date\":\"2026-11-05\"}," +
            "{\"Cat\":\"BBM\",\"Qty\":1,\"Price\":50000,\"Date\":\"2026-11-01\"}," +
            "{\"Cat\":\"Makan\",\"Qty\":3,\"Price\":5000,\"Date\":\"2026-11-03\"}],\"Tags\":[\"a\",\"b\"]}")!;

        [Fact]
        public void Aggregates()
        {
            Assert.Equal("3", Text("Items | count", Shop));
            Assert.Equal("65000", Text("Items | sum:'Price'", Shop));
            Assert.Equal("85000", Text("Items | sumproduct:'Qty':'Price'", Shop));
            Assert.Equal("5000", Text("Items | min:'Price'", Shop));
            Assert.Equal("50000", Text("Items | max:'Price'", Shop));
            Assert.Equal("2", Text("Items | avg:'Qty'", Shop));
            Assert.Equal(string.Empty, Text("Missing | avg:'Qty'", Shop));
            Assert.Equal("a, b", Text("Tags | join", Shop));
        }

        [Fact]
        public void SortWhereFirstLast()
        {
            Assert.Equal("BBM", Text("(Items | sort:'Date')[0].Cat", Shop));
            Assert.Equal("2026-11-05", Text("(Items | sortdesc:'Date')[0].Date", Shop));
            Assert.Equal("2", Text("Items | where:'Cat':'Makan' | count", Shop));
            Assert.Equal("Makan", Text("(Items | first).Cat", Shop));
            Assert.Equal("3", Text("(Items | last).Qty", Shop));
        }

        [Fact]
        public void GroupBy_KeepsFirstSeenOrderAndTotals()
        {
            var groups = (List<object?>)Eval("Items | groupby:'Cat':'Price'", Shop)!;
            var first = (IDictionary<string, object?>)groups[0]!;

            Assert.Equal(2, groups.Count);
            Assert.Equal("Makan", first["Key"]);
            Assert.Equal(2m, first["Count"]);
            Assert.Equal(15000m, first["Total"]);
            Assert.Equal("BBM", Text("(Items | groupby:'Cat':'Price' | sortdesc:'Total')[0].Key", Shop));
        }

        [Fact]
        public void TextFilters()
        {
            Assert.Equal("FAJAR", Text("'fajar' | upper"));
            Assert.Equal("Dua Juta Lima Ratus Ribu", Text("2500000 | terbilang | title"));
            Assert.Equal("-", Text("'' | default:'-'"));
            Assert.Equal("0", Text("0 | default:'-'")); // zero is a value, not missing
        }
    }
}
