using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LifeOS.Pdf;
using LifeOS.Pdf.Html;
using LifeOS.Pdf.Html.Internal;
using LifeOS.Pdf.Manipulation;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Phase 11a: pure option/policy logic. These never need a browser.</summary>
    public class HtmlOptionsTests
    {
        [Fact]
        public void Mapper_A4_InInchesWithInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                // id-ID writes "8,2639" — Chromium would reject it. The mapper must ignore the culture.
                CultureInfo.CurrentCulture = new CultureInfo("id-ID");
                var pdf = PdfOptionsMapper.Map(new HtmlToPdfOptions { Margins = HtmlMargins.Uniform(36) });

                Assert.Equal("8.2677in", pdf.Width);
                Assert.Equal("11.6929in", pdf.Height);
                Assert.Equal("0.5in", pdf.MarginOptions.Top);
                Assert.Equal("0.5in", pdf.MarginOptions.Left);
                Assert.False(pdf.Landscape);
                Assert.False(pdf.DisplayHeaderFooter);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Mapper_Landscape_SwapsSidesItself()
        {
            var pdf = PdfOptionsMapper.Map(new HtmlToPdfOptions { PageSize = PageSize.Letter, Landscape = true });
            Assert.Equal("11in", pdf.Width);
            Assert.Equal("8.5in", pdf.Height);
            Assert.False(pdf.Landscape);
        }

        [Fact]
        public void Mapper_PageRanges()
        {
            Assert.Null(PdfOptionsMapper.PageRangesText(null));
            Assert.Null(PdfOptionsMapper.PageRangesText(PageRange.All));
            Assert.Equal("1-3,5", PdfOptionsMapper.PageRangesText(PageRange.Parse("1-3,5")));
            var pdf = PdfOptionsMapper.Map(new HtmlToPdfOptions { PageRanges = PageRange.Parse("2") });
            Assert.Equal("2", pdf.PageRanges);
        }

        [Fact]
        public void HeaderFooter_PlaceholdersBecomeChromiumClasses()
        {
            var html = HeaderFooterTemplate.Build("Hal {page}/{pages} {date} {title} {url}", 36, 36);
            Assert.Contains("<span class=\"pageNumber\"></span>", html);
            Assert.Contains("<span class=\"totalPages\"></span>", html);
            Assert.Contains("<span class=\"date\"></span>", html);
            Assert.Contains("<span class=\"title\"></span>", html);
            Assert.Contains("<span class=\"url\"></span>", html);
            Assert.Contains("font-size:9pt", html);
            Assert.Contains("padding:0 36pt 0 36pt", html);
        }

        [Fact]
        public void HeaderFooter_OnlyFooter_SendsEmptyHeader()
        {
            var pdf = PdfOptionsMapper.Map(new HtmlToPdfOptions { FooterHtml = "{page}" });
            Assert.True(pdf.DisplayHeaderFooter);
            Assert.Equal("<span></span>", pdf.HeaderTemplate);
            Assert.Contains("pageNumber", pdf.FooterTemplate);
        }

        [Theory]
        [InlineData(0.05)]
        [InlineData(2.5)]
        [InlineData(double.NaN)]
        public void Validate_RejectsScale(double scale) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionsValidator.Validate(new HtmlToPdfOptions { Scale = scale }));

        [Fact]
        public void Validate_RejectsMarginsWiderThanPaper() =>
            Assert.Throws<ArgumentException>(() => OptionsValidator.Validate(new HtmlToPdfOptions { Margins = new HtmlMargins(10, 300, 10, 300) }));

        [Fact]
        public void Validate_RejectsZeroTimeoutAndConcurrency()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionsValidator.Validate(new HtmlToPdfOptions { Timeout = TimeSpan.Zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChromiumConverter(new ChromiumOptions { MaxConcurrency = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => HtmlMargins.Uniform(-1));
        }

        [Fact]
        public void Policy_DefaultAllowsOnlyInlineContent()
        {
            var o = new HtmlToPdfOptions();
            Assert.True(RequestPolicy.IsAllowed("data:image/png;base64,AAAA", o, null));
            Assert.True(RequestPolicy.IsAllowed("about:blank", o, null));
            Assert.False(RequestPolicy.IsAllowed("https://example.com/a.png", o, null));
            Assert.False(RequestPolicy.IsAllowed("http://169.254.169.254/latest/meta-data", o, null));
            Assert.False(RequestPolicy.IsAllowed("file:///C:/Windows/win.ini", o, null));
            Assert.False(RequestPolicy.IsAllowed("chrome://settings", o, null));
            Assert.False(RequestPolicy.IsAllowed("not a url", o, null));
        }

        [Fact]
        public void Policy_NetworkOptIn()
        {
            var o = new HtmlToPdfOptions { AllowNetwork = true };
            Assert.True(RequestPolicy.IsAllowed("https://example.com/a.png", o, null));
            Assert.True(RequestPolicy.IsAllowed("wss://example.com/socket", o, null));
        }

        [Fact]
        public void Policy_LocalFilesConfinedToBaseFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "lifeos-html-policy", "site") + Path.DirectorySeparatorChar;
            var o = new HtmlToPdfOptions { AllowLocalFiles = true, BaseUrl = new Uri(root) };
            var localRoot = RequestPolicy.LocalRoot(o, null);
            Assert.NotNull(localRoot);

            var inside = new Uri(Path.Combine(root, "img", "logo.png")).AbsoluteUri;
            var traversal = new Uri(root).AbsoluteUri + "../secret.txt";
            var sibling = new Uri(Path.Combine(Path.GetTempPath(), "lifeos-html-policy", "site-other", "x.png")).AbsoluteUri;

            Assert.True(RequestPolicy.IsAllowed(inside, o, localRoot));
            Assert.False(RequestPolicy.IsAllowed(traversal, o, localRoot));
            Assert.False(RequestPolicy.IsAllowed(sibling, o, localRoot));

            // Without the opt-in a file base URL gives no access at all.
            Assert.Null(RequestPolicy.LocalRoot(new HtmlToPdfOptions { BaseUrl = new Uri(root) }, null));
        }

        [Fact]
        public void Locator_PrefersExplicitThenEnvironmentThenChrome()
        {
            var env = new System.Collections.Generic.Dictionary<string, string?>
            {
                ["ProgramFiles"] = @"C:\PF",
                ["ProgramFiles(x86)"] = @"C:\PF86",
                ["LOCALAPPDATA"] = @"C:\Local",
            };
            var existing = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\PF86\Microsoft\Edge\Application\msedge.exe",
                @"C:\Local\Google\Chrome\Application\chrome.exe",
            };
            var locator = new BrowserLocator(existing.Contains, k => env.TryGetValue(k, out var v) ? v : null, BrowserLocator.OSPlatformKind.Windows);

            Assert.Equal(@"C:\Local\Google\Chrome\Application\chrome.exe", locator.Locate(null));

            existing.Add(@"D:\custom\chrome.exe");
            Assert.Equal(@"D:\custom\chrome.exe", locator.Locate(@"D:\custom\chrome.exe"));

            env[BrowserLocator.EnvironmentVariable] = @"C:\PF86\Microsoft\Edge\Application\msedge.exe";
            Assert.Equal(@"C:\PF86\Microsoft\Edge\Application\msedge.exe", locator.Locate(null));
        }

        [Fact]
        public void Locator_NotFound_ListsPathsTried()
        {
            var locator = new BrowserLocator(_ => false, _ => null, BrowserLocator.OSPlatformKind.Linux);
            var ex = Assert.Throws<HtmlConversionException>(() => locator.Locate(null));
            Assert.Equal(HtmlConversionError.BrowserNotFound, ex.Error);
            Assert.Contains("/usr/bin/google-chrome", ex.Message);
            Assert.Contains(BrowserLocator.EnvironmentVariable, ex.Message);

            var bad = Assert.Throws<HtmlConversionException>(() => locator.Locate("/nope/chrome"));
            Assert.Contains("/nope/chrome", bad.Message);
        }

        [Fact]
        public void InjectBase_GoesIntoHead()
        {
            var html = ChromiumConverter.InjectBase("<html><HEAD lang=\"id\"><title>x</title></head></html>", new Uri("https://a.test/r/?a=1&b=2"));
            Assert.Contains("<HEAD lang=\"id\"><base href=\"https://a.test/r/?a=1&amp;b=2\"><title>", html);
            Assert.StartsWith("<base href=", ChromiumConverter.InjectBase("<p>hi</p>", new Uri("https://a.test/")));
        }

        [Fact]
        public void PostProcessor_SetsMetadataAndIsDeterministic()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            var source = doc.ToArray();
            var options = new HtmlToPdfOptions { Author = "Tim LifeOS", CreationDate = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(7)) };

            var first = PdfPostProcessor.Apply(source, options, "Judul Halaman", "LifeOS.Pdf.Html (test)");
            var second = PdfPostProcessor.Apply(source, options, "Judul Halaman", "LifeOS.Pdf.Html (test)");

            Assert.Equal(first, second);
            PdfStructure.Verify(first);
            var reopened = PdfDocument.Open(first);
            Assert.Equal("Judul Halaman", reopened.Info.Title);
            Assert.Equal("Tim LifeOS", reopened.Info.Author);
            Assert.Equal("LifeOS.Pdf.Html (test)", reopened.Info.Creator);
        }
    }

    /// <summary>One browser for the whole integration class; null when no Chrome/Edge/Chromium is installed.</summary>
    public sealed class ChromiumFixture : IDisposable
    {
        public ChromiumFixture()
        {
            try
            {
                var converter = new ChromiumConverter();
                converter.WarmUpAsync().GetAwaiter().GetResult();
                Converter = converter;
            }
            catch (HtmlConversionException ex) when (ex.Error == HtmlConversionError.BrowserNotFound || ex.Error == HtmlConversionError.LaunchFailed)
            {
                Converter = null;   // Integration tests skip themselves (same approach as the PDFium/Tesseract tests).
            }
        }

        public ChromiumConverter? Converter { get; }

        public void Dispose() => Converter?.Dispose();
    }

    /// <summary>Phase 11a: real conversions. Each test returns early when no browser is available.</summary>
    public class HtmlConversionTests : IClassFixture<ChromiumFixture>
    {
        private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        private readonly ChromiumConverter? _html;

        public HtmlConversionTests(ChromiumFixture fixture) => _html = fixture.Converter;

        private static string Rows(int count)
        {
            var sb = new StringBuilder("<table border=1 style='border-collapse:collapse;width:100%'><thead><tr><th>No</th><th>Item</th><th>Jumlah</th></tr></thead><tbody>");
            for (var i = 1; i <= count; i++)
            {
                sb.Append("<tr><td>").Append(i).Append("</td><td>Barang ").Append(i).Append("</td><td>Rp").Append((i * 1500).ToString("N0", CultureInfo.InvariantCulture)).Append("</td></tr>");
            }

            return sb.Append("</tbody></table>").ToString();
        }

        [Fact]
        public void SimpleHtml_OneValidA4PageWithText()
        {
            if (_html is null)
            {
                return;
            }

            var pdf = _html.ConvertHtml("<html><head><title>Halo Dunia</title></head><body><h1>Halo LifeOS.Pdf</h1><p>Rp1.350.000</p></body></html>");

            PdfStructure.Verify(pdf);
            var doc = PdfDocument.Open(pdf);
            Assert.Single(doc.Pages);
            Assert.InRange(doc.Pages[0].Width, 594, 596);
            Assert.InRange(doc.Pages[0].Height, 841, 843);
            var text = doc.ExtractText();
            Assert.Contains("Halo LifeOS.Pdf", text);
            Assert.Contains("Rp1.350.000", text);
            Assert.Equal("Halo Dunia", doc.Info.Title);
            Assert.StartsWith("LifeOS.Pdf.Html", doc.Info.Creator);
        }

        [Fact]
        public void EmptyHtml_OneBlankPage()
        {
            if (_html is null)
            {
                return;
            }

            var pdf = _html.ConvertHtml("   ");
            PdfStructure.Verify(pdf);
            Assert.Single(PdfDocument.Open(pdf).Pages);
        }

        [Fact]
        public void LongTable_ManyPagesWithFooterPageNumbers()
        {
            if (_html is null)
            {
                return;
            }

            var pdf = _html.ConvertHtml("<h1>Laporan</h1>" + Rows(300), new HtmlToPdfOptions
            {
                Margins = new HtmlMargins(54, 36, 54, 36),
                FooterHtml = "Halaman {page} dari {pages}",
            });

            PdfStructure.Verify(pdf);
            var doc = PdfDocument.Open(pdf);
            Assert.True(doc.Pages.Count > 1, "300 rows should not fit on one page");
            var text = doc.ExtractText();
            Assert.Contains($"Halaman 2 dari {doc.Pages.Count}", text);
            Assert.Contains("Barang 300", text);
        }

        [Fact]
        public void LandscapeLetterAndPageRanges()
        {
            if (_html is null)
            {
                return;
            }

            var landscape = PdfDocument.Open(_html.ConvertHtml("<p>x</p>", new HtmlToPdfOptions { PageSize = PageSize.Letter, Landscape = true }));
            Assert.InRange(landscape.Pages[0].Width, 791, 793);
            Assert.InRange(landscape.Pages[0].Height, 611, 613);

            var many = "<div style='page-break-after:always'>1</div><div style='page-break-after:always'>2</div><div>3</div>";
            Assert.Equal(3, PdfDocument.Open(_html.ConvertHtml(many)).Pages.Count);
            var only2 = PdfDocument.Open(_html.ConvertHtml(many, new HtmlToPdfOptions { PageRanges = PageRange.Parse("2") }));
            Assert.Single(only2.Pages);
            Assert.Contains("2", only2.ExtractText());
        }

        [Fact]
        public void NetworkAndFilesBlockedByDefault_StillProducesPdf()
        {
            if (_html is null)
            {
                return;
            }

            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lifeos-html-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var image = Path.Combine(dir, "logo.png");
                File.WriteAllBytes(image, Convert.FromBase64String(Png1x1));
                var html = $"<p>Gambar</p><img src='{new Uri(image).AbsoluteUri}' style='width:100px;height:100px'>" +
                           "<img src='http://127.0.0.1:9/never.png' style='width:100px;height:100px'>";

                var blocked = PdfDocument.Open(_html.ConvertHtml(html));
                // Chromium draws its 14×16 "broken image" icon for blocked images; the real 1×1 logo must be absent.
                Assert.DoesNotContain(blocked.Pages[0].GetImages(), i => i.PixelWidth == 1 && i.PixelHeight == 1);

                // Opt in to files under the base folder: the logo appears; still no network.
                var allowed = PdfDocument.Open(_html.ConvertHtml(
                    "<p>Gambar</p><img src='logo.png' style='width:100px;height:100px'>",
                    new HtmlToPdfOptions { BaseUrl = new Uri(dir + Path.DirectorySeparatorChar), AllowLocalFiles = true }));
                Assert.Contains(allowed.Pages[0].GetImages(), i => i.PixelWidth == 1 && i.PixelHeight == 1);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void DataUriImages_AlwaysWork()
        {
            if (_html is null)
            {
                return;
            }

            var doc = PdfDocument.Open(_html.ConvertHtml($"<img src='data:image/png;base64,{Png1x1}' style='width:120px;height:120px'>"));
            Assert.Contains(doc.Pages[0].GetImages(), i => i.PixelWidth == 1 && i.PixelHeight == 1);
        }

        [Fact]
        public void ConvertFile_ReadsSiblingCss()
        {
            if (_html is null)
            {
                return;
            }

            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lifeos-html-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "style.css"), "@page { size: A5; } body { font-family: sans-serif; }");
                var file = Path.Combine(dir, "invoice.html");
                File.WriteAllText(file, "<html><head><link rel='stylesheet' href='style.css'></head><body><p>Dari file</p></body></html>");

                var doc = PdfDocument.Open(_html.ConvertFile(file, new HtmlToPdfOptions { PreferCssPageSize = true }));
                Assert.Contains("Dari file", doc.ExtractText());
                Assert.InRange(doc.Pages[0].Width, 418, 421);   // A5 from the sibling CSS: proves it was loaded
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ConvertUrl_RequiresNetworkOptIn()
        {
            if (_html is null)
            {
                return;
            }

            Assert.Throws<ArgumentException>(() => _html.ConvertUrl(new Uri("https://example.com/")));
        }

        [Fact]
        public void InfiniteScript_TimesOut_ConverterStillWorks()
        {
            if (_html is null)
            {
                return;
            }

            var ex = Assert.Throws<HtmlConversionException>(() =>
                _html.ConvertHtml("<script>while(true){}</script>", new HtmlToPdfOptions { Timeout = TimeSpan.FromSeconds(2) }));
            Assert.Equal(HtmlConversionError.Timeout, ex.Error);

            var after = _html.ConvertHtml("<p>Masih hidup</p>");
            Assert.Contains("Masih hidup", PdfDocument.Open(after).ExtractText());
        }

        [Fact]
        public void JavaScriptCanBeDisabled()
        {
            if (_html is null)
            {
                return;
            }

            const string html = "<p id='p'>statis</p><script>document.getElementById('p').textContent='dinamis'</script>";
            Assert.Contains("dinamis", PdfDocument.Open(_html.ConvertHtml(html)).ExtractText());
            Assert.Contains("statis", PdfDocument.Open(_html.ConvertHtml(html, new HtmlToPdfOptions { AllowJavaScript = false })).ExtractText());
        }

        [Fact]
        public async Task Parallel_EightConversions_AllValid()
        {
            if (_html is null)
            {
                return;
            }

            var tasks = Enumerable.Range(1, 8).Select(i => _html.ConvertHtmlAsync($"<h1>Dokumen {i}</h1>" + Rows(20))).ToArray();
            var results = await Task.WhenAll(tasks);
            for (var i = 0; i < results.Length; i++)
            {
                PdfStructure.Verify(results[i]);
                Assert.Contains($"Dokumen {i + 1}", PdfDocument.Open(results[i]).ExtractText());
            }
        }

        [Fact]
        public void SameInputAndDate_SameBytes()
        {
            if (_html is null)
            {
                return;
            }

            var options = new HtmlToPdfOptions { CreationDate = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero) };
            var html = "<h1>Deterministik</h1>" + Rows(30);
            Assert.Equal(_html.ConvertHtml(html, options), _html.ConvertHtml(html, options));
        }

        [Fact]
        public void Cancellation_IsHonoured()
        {
            if (_html is null)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            Assert.ThrowsAny<OperationCanceledException>(() =>
                _html.ConvertHtmlAsync("<script>while(true){}</script>", null, cts.Token).GetAwaiter().GetResult());
        }

        [Fact]
        public void Result_WorksWithMergeStampAndEncryption()
        {
            if (_html is null)
            {
                return;
            }

            var cover = new PdfDocument();
            cover.AddPage();
            var invoice = PdfDocument.Open(_html.ConvertHtml("<h1>Invoice</h1>" + Rows(10)));

            PdfMerger.Append(cover, invoice);
            PdfStamper.AddTextWatermark(cover, "LUNAS");
            var encrypted = cover.ToArray(new PdfSaveOptions { Encryption = new PdfEncryptionOptions { UserPassword = "rahasia" } });

            var reopened = PdfDocument.Open(encrypted, new PdfReadOptions { Password = "rahasia" });
            Assert.Equal(1 + invoice.Pages.Count, reopened.Pages.Count);
            Assert.Contains("Invoice", reopened.ExtractText());
        }

        [Fact]
        public void PostProcessOff_ReturnsBrowserBytes()
        {
            if (_html is null)
            {
                return;
            }

            var raw = _html.ConvertHtml("<p>mentah</p>", new HtmlToPdfOptions { PostProcess = false });
            Assert.Equal((byte)'%', raw[0]);
            Assert.Contains("mentah", PdfDocument.Open(raw).ExtractText());
        }
    }
}
