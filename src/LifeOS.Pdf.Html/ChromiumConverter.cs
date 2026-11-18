using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LifeOS.Pdf.Html.Internal;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace LifeOS.Pdf.Html
{
    /// <summary>
    /// HTML → PDF with an installed Chrome, Edge or Chromium running headless. One browser process serves
    /// every conversion; each conversion gets its own isolated browser context (no shared cookies or storage)
    /// and tab, and at most <see cref="ChromiumOptions.MaxConcurrency"/> run at once. Create one converter
    /// and reuse it: starting the browser is the slow part. Thread-safe.
    /// </summary>
    public sealed class ChromiumConverter : IHtmlToPdfConverter
    {
        private const string EmptyDocument = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body></body></html>";
        private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);
        private static readonly Regex HeadTag = new Regex(@"<head(\s[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly ChromiumOptions _options;
        private readonly SemaphoreSlim _slots;
        private readonly SemaphoreSlim _launchLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<Task<Tab>> _spareTabs = new ConcurrentQueue<Task<Tab>>();
        private IBrowser? _browser;
        private string? _userDataDir;
        private int _disposed;

        public ChromiumConverter(ChromiumOptions? options = null)
        {
            var source = options ?? new ChromiumOptions();
            OptionsValidator.Validate(source);

            // Copy, so changing the options object afterwards does not affect a running converter.
            _options = new ChromiumOptions
            {
                ExecutablePath = source.ExecutablePath,
                MaxConcurrency = source.MaxConcurrency,
                LaunchTimeout = source.LaunchTimeout,
            };
            foreach (var argument in source.ExtraArguments)
            {
                _options.ExtraArguments.Add(argument);
            }

            _slots = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        }

        /// <summary>The browser's version string (e.g. <c>HeadlessChrome/157.0…</c>), once it has started.</summary>
        public string? BrowserVersion { get; private set; }

        /// <summary>Starts the browser now instead of on the first conversion (useful at server start-up).</summary>
        public async Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            var browser = await EnsureBrowserAsync(cancellationToken).ConfigureAwait(false);
            RefillSpareTabs(browser);
        }

        public Task<byte[]> ConvertHtmlAsync(string html, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (html is null)
            {
                throw new ArgumentNullException(nameof(html));
            }

            var o = options ?? new HtmlToPdfOptions();
            OptionsValidator.Validate(o);
            if (html.Length > o.MaxHtmlLength)
            {
                throw new HtmlConversionException(
                    HtmlConversionError.InputTooLarge,
                    $"The HTML has {html.Length} characters, more than MaxHtmlLength ({o.MaxHtmlLength}). " +
                    "Raise HtmlToPdfOptions.MaxHtmlLength if this input is expected.");
            }

            // Empty input still produces a valid one-page PDF (requirement-fase11 §6, Q6).
            var content = string.IsNullOrWhiteSpace(html) ? EmptyDocument : html;
            if (o.BaseUrl != null)
            {
                content = InjectBase(content, o.BaseUrl);
            }

            var localRoot = RequestPolicy.LocalRoot(o, null);
            return localRoot is null
                ? RunAsync(o, null, "the HTML string", (page, nav) => SetContentAsync(page, content, nav), cancellationToken)
                : ConvertViaTempFileAsync(content, o, localRoot, cancellationToken);
        }

        /// <summary>
        /// Chromium never lets an about:blank page (where setContent puts the HTML) load file:// resources, so
        /// with a local BaseUrl the HTML is written to a temporary file and opened from there. RequestPolicy still
        /// confines reads to the BaseUrl folder; the temporary file is the only extra path allowed.
        /// </summary>
        private async Task<byte[]> ConvertViaTempFileAsync(string content, HtmlToPdfOptions o, string localRoot, CancellationToken cancellationToken)
        {
            var temp = Path.Combine(Path.GetTempPath(), "lifeos-pdf-page-" + Guid.NewGuid().ToString("N") + ".html");
            // A BOM makes Chromium read the file as UTF-8 even when the HTML has no <meta charset>.
            File.WriteAllText(temp, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            try
            {
                var url = new Uri(temp).AbsoluteUri;
                return await RunAsync(o, localRoot, "the HTML string", (page, nav) => NavigateAsync(page, url, nav), cancellationToken, url)
                    .ConfigureAwait(false);
            }
            finally
            {
                TryDeleteFile(temp);
            }
        }

        private static async Task SetContentAsync(IPage page, string html, NavigationOptions navigation)
        {
            // setContent only knows load / DOMContentLoaded; "network idle" is waited for separately afterwards.
            var networkIdle = navigation.WaitUntil?.Contains(WaitUntilNavigation.Networkidle0) == true;
            await page.SetContentAsync(html, new SetContentOptions
            {
                Timeout = navigation.Timeout,
                WaitUntil = networkIdle ? new[] { WaitUntilNavigation.Load } : navigation.WaitUntil,
            }).ConfigureAwait(false);

            if (networkIdle)
            {
                await page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions()).ConfigureAwait(false);
            }
        }

        public Task<byte[]> ConvertFileAsync(string path, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"The HTML file '{full}' does not exist.", full);
            }

            var o = options ?? new HtmlToPdfOptions();
            OptionsValidator.Validate(o);
            var url = new Uri(full).AbsoluteUri;
            var localRoot = RequestPolicy.LocalRoot(o, full);
            return RunAsync(o, localRoot, full, (page, nav) => NavigateAsync(page, url, nav), cancellationToken);
        }

        public Task<byte[]> ConvertUrlAsync(Uri url, HtmlToPdfOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (!url.IsAbsoluteUri)
            {
                throw new ArgumentException("The URL must be absolute, e.g. https://example.com/report.", nameof(url));
            }

            if (url.IsFile)
            {
                return ConvertFileAsync(url.LocalPath, options, cancellationToken);
            }

            var o = options ?? new HtmlToPdfOptions();
            OptionsValidator.Validate(o);
            if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"Only http, https and file URLs can be converted, not '{url.Scheme}'.", nameof(url));
            }

            if (!o.AllowNetwork)
            {
                throw new ArgumentException(
                    "Converting a web page needs network access. Set HtmlToPdfOptions.AllowNetwork = true " +
                    "(only for pages you trust).", nameof(options));
            }

            return RunAsync(o, null, url.AbsoluteUri, (page, nav) => NavigateAsync(page, url.AbsoluteUri, nav), cancellationToken);
        }

        public byte[] ConvertHtml(string html, HtmlToPdfOptions? options = null) =>
            RunSync(() => ConvertHtmlAsync(html, options));

        public byte[] ConvertFile(string path, HtmlToPdfOptions? options = null) =>
            RunSync(() => ConvertFileAsync(path, options));

        public byte[] ConvertUrl(Uri url, HtmlToPdfOptions? options = null) =>
            RunSync(() => ConvertUrlAsync(url, options));

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await _launchLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ShutDownBrowserAsync().ConfigureAwait(false);
            }
            finally
            {
                _launchLock.Release();
            }
        }

        // ------------------------------------------------------------------ conversion

        private async Task<byte[]> RunAsync(
            HtmlToPdfOptions o,
            string? localRoot,
            string source,
            Func<IPage, NavigationOptions, Task> load,
            CancellationToken cancellationToken,
            string? extraAllowedUrl = null)
        {
            ThrowIfDisposed();
            var browser = await EnsureBrowserAsync(cancellationToken).ConfigureAwait(false);
            var pdfOptions = PdfOptionsMapper.Map(o);

            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            // The timeout starts once a tab is free: waiting in the queue is not the page's fault.
            using var timeout = new CancellationTokenSource(o.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var blocked = new List<string>();
            IBrowserContext? context = null;
            IPage? page = null;
            try
            {
                var tab = await TakeTabAsync(browser).ConfigureAwait(false);
                context = tab.Context;
                page = tab.Page;
                var work = ConvertPageAsync(page, o, localRoot, extraAllowedUrl, pdfOptions, load, blocked, linked.Token);

                // A page stuck in a script loop never answers; racing against the token lets us give up on it.
                var cancelled = Task.Delay(Timeout.Infinite, linked.Token);
                if (await Task.WhenAny(work, cancelled).ConfigureAwait(false) != work)
                {
                    _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // observe the abandoned task
                    cancellationToken.ThrowIfCancellationRequested();
                    throw TimeoutError(o, source, null);
                }

                var (bytes, title) = await work.ConfigureAwait(false);
                if (bytes.LongLength > o.MaxOutputBytes)
                {
                    throw new HtmlConversionException(
                        HtmlConversionError.OutputTooLarge,
                        $"The PDF is {bytes.LongLength} bytes, more than MaxOutputBytes ({o.MaxOutputBytes}). " +
                        "Raise HtmlToPdfOptions.MaxOutputBytes if this output is expected.");
                }

                return o.PostProcess
                    ? PdfPostProcessor.Apply(bytes, o, title, "LifeOS.Pdf.Html (" + (BrowserVersion ?? "Chromium") + ")")
                    : bytes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (timeout.IsCancellationRequested && !(ex is HtmlConversionException))
            {
                throw TimeoutError(o, source, ex);
            }
            catch (PuppeteerException ex) when (!browser.IsConnected)
            {
                throw new HtmlConversionException(
                    HtmlConversionError.BrowserCrashed,
                    "The browser stopped during the conversion. The next conversion starts a new browser; " +
                    "if this keeps happening, check memory limits or add --no-sandbox in containers.", ex);
            }
            catch (PuppeteerException ex)
            {
                throw new HtmlConversionException(
                    HtmlConversionError.NavigationFailed,
                    $"Could not load {source}: {ex.Message}" + BlockedHint(blocked), ex);
            }
            finally
            {
                // Closing a tab takes ~15–20 ms and the result does not depend on it, so it runs in the background.
                // CloseAsync never throws and kills the browser itself if the tab hangs.
                _ = CloseAsync(browser, page, context);
                _slots.Release();
            }
        }

        private async Task<(byte[] Pdf, string? Title)> ConvertPageAsync(
            IPage page,
            HtmlToPdfOptions o,
            string? localRoot,
            string? extraAllowedUrl,
            PdfOptions pdfOptions,
            Func<IPage, NavigationOptions, Task> load,
            List<string> blocked,
            CancellationToken token)
        {
            var milliseconds = (int)Math.Min(int.MaxValue, o.Timeout.TotalMilliseconds);
            page.DefaultTimeout = milliseconds;
            page.DefaultNavigationTimeout = milliseconds;

            await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);
            page.Request += (_, e) => _ = DecideAsync(e.Request, o, localRoot, extraAllowedUrl, blocked);
            await page.SetJavaScriptEnabledAsync(o.AllowJavaScript).ConfigureAwait(false);
            if (o.EmulateScreenMedia)
            {
                await page.EmulateMediaTypeAsync(MediaType.Screen).ConfigureAwait(false);
            }

            var navigation = new NavigationOptions
            {
                Timeout = milliseconds,
                WaitUntil = new[] { WaitUntil(o.WaitUntil) },
            };
            await load(page, navigation).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(o.WaitForSelector))
            {
                await page.WaitForSelectorAsync(o.WaitForSelector, new WaitForSelectorOptions { Timeout = milliseconds }).ConfigureAwait(false);
            }

            if (o.ExtraDelay > TimeSpan.Zero)
            {
                await Task.Delay(o.ExtraDelay, token).ConfigureAwait(false);
            }

            string? title = null;
            if (o.PostProcess && o.Title is null)
            {
                try
                {
                    title = await page.GetTitleAsync().ConfigureAwait(false);
                }
                catch (PuppeteerException)
                {
                    // No title is not worth failing the conversion for.
                }
            }

            var pdf = await page.PdfDataAsync(pdfOptions).ConfigureAwait(false);
            return (pdf, title);
        }

        private static async Task NavigateAsync(IPage page, string url, NavigationOptions navigation)
        {
            var response = await page.GoToAsync(url, navigation).ConfigureAwait(false);
            if (response != null && (int)response.Status >= 400)
            {
                throw new HtmlConversionException(
                    HtmlConversionError.NavigationFailed,
                    $"{url} answered HTTP {(int)response.Status} ({response.StatusText}).");
            }
        }

        private static async Task DecideAsync(IRequest request, HtmlToPdfOptions o, string? localRoot, string? extraAllowedUrl, List<string> blocked)
        {
            try
            {
                if (string.Equals(request.Url, extraAllowedUrl, StringComparison.Ordinal) || RequestPolicy.IsAllowed(request.Url, o, localRoot))
                {
                    await request.ContinueAsync().ConfigureAwait(false);
                    return;
                }

                lock (blocked)
                {
                    if (blocked.Count < 100)
                    {
                        blocked.Add(request.Url);
                    }
                }

                await request.AbortAsync(RequestAbortErrorCode.BlockedByClient).ConfigureAwait(false);
            }
            catch (PuppeteerException)
            {
                // The tab was closed while the request was pending; nothing left to decide.
            }
        }

        /// <summary>A fresh, never-used browser context with one tab.</summary>
        private sealed class Tab
        {
            public Tab(IBrowser browser, IBrowserContext context, IPage page)
            {
                Browser = browser;
                Context = context;
                Page = page;
            }

            public IBrowser Browser { get; }

            public IBrowserContext Context { get; }

            public IPage Page { get; }
        }

        private static async Task<Tab> NewTabAsync(IBrowser browser)
        {
            var context = await browser.CreateBrowserContextAsync().ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            return new Tab(browser, context, page);
        }

        /// <summary>
        /// Opening a context starts a renderer process (~100 ms), the largest fixed cost of a conversion. Spare
        /// tabs are opened ahead of time so that cost overlaps with other work. Each tab is still used for exactly
        /// one conversion, so conversions never share cookies, storage or scripts.
        /// </summary>
        private async Task<Tab> TakeTabAsync(IBrowser browser)
        {
            while (_spareTabs.TryDequeue(out var spare))
            {
                Tab? tab = null;
                try
                {
                    tab = await spare.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The spare failed to open (e.g. the browser restarted); open a new one below.
                }

                if (tab != null && ReferenceEquals(tab.Browser, browser) && !tab.Page.IsClosed)
                {
                    RefillSpareTabs(browser);
                    return tab;
                }
            }

            RefillSpareTabs(browser);
            return await NewTabAsync(browser).ConfigureAwait(false);
        }

        private void RefillSpareTabs(IBrowser browser)
        {
            var target = Math.Min(_options.MaxConcurrency, 4);
            while (Volatile.Read(ref _disposed) == 0 && browser.IsConnected && _spareTabs.Count < target)
            {
                var spare = NewTabAsync(browser);
                // Observe failures so a spare that never gets used cannot raise UnobservedTaskException.
                _ = spare.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                _spareTabs.Enqueue(spare);
            }
        }

        private async Task CloseAsync(IBrowser browser, IPage? page, IBrowserContext? context)
        {
            if (context is null)
            {
                return;
            }

            var close = Task.Run(async () =>
            {
                try
                {
                    if (page != null)
                    {
                        await page.CloseAsync().ConfigureAwait(false);
                    }

                    await context.CloseAsync().ConfigureAwait(false);
                }
                catch (PuppeteerException)
                {
                    // Already gone (browser restarted or crashed).
                }
            });

            if (await Task.WhenAny(close, Task.Delay(CloseGrace)).ConfigureAwait(false) != close)
            {
                // A renderer that does not even close is hung for good: restart the whole browser.
                KillProcess(browser);
            }
        }

        // ------------------------------------------------------------------ browser lifetime

        private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var current = _browser;
            if (current != null && current.IsConnected && !current.IsClosed)
            {
                return current;
            }

            await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                current = _browser;
                if (current != null && current.IsConnected && !current.IsClosed)
                {
                    return current;
                }

                // A browser that crashed or was killed after a timeout is replaced transparently.
                await ShutDownBrowserAsync().ConfigureAwait(false);

                var executable = new BrowserLocator().Locate(_options.ExecutablePath);
                DeleteStaleProfiles();
                var userDataDir = Path.Combine(Path.GetTempPath(), ProfilePrefix + Guid.NewGuid().ToString("N"));
                var arguments = new List<string>
                {
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-extensions",
                    "--disable-gpu",
                    "--disable-sync",
                    "--disable-background-networking",
                    "--disable-component-update",
                    "--mute-audio",
                };
                arguments.AddRange(_options.ExtraArguments);

                var launch = new LaunchOptions
                {
                    ExecutablePath = executable,
                    Headless = true,
                    UserDataDir = userDataDir,
                    Args = arguments.ToArray(),
                    Timeout = (int)Math.Min(int.MaxValue, _options.LaunchTimeout.TotalMilliseconds),
                };

                IBrowser browser;
                try
                {
                    browser = await Puppeteer.LaunchAsync(launch).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is HtmlConversionException))
                {
                    TryDeleteDirectory(userDataDir);
                    throw new HtmlConversionException(
                        HtmlConversionError.LaunchFailed,
                        $"The browser '{executable}' did not start: {ex.Message} " +
                        "Check that it runs on this machine; inside a container running as root add " +
                        "\"--no-sandbox\" to ChromiumOptions.ExtraArguments.", ex);
                }

                _browser = browser;
                _userDataDir = userDataDir;
                BrowserVersion = await browser.GetVersionAsync().ConfigureAwait(false);
                return browser;
            }
            finally
            {
                _launchLock.Release();
            }
        }

        /// <summary>Closes the current browser (killing it if it does not answer) and removes its profile folder.</summary>
        private async Task ShutDownBrowserAsync()
        {
            var browser = _browser;
            var userDataDir = _userDataDir;
            _browser = null;
            _userDataDir = null;

            // Spare tabs belong to this browser; closing the browser closes them too.
            while (_spareTabs.TryDequeue(out _))
            {
            }

            if (browser != null)
            {
                var close = Task.Run(async () =>
                {
                    try
                    {
                        await browser.CloseAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Closing a dead browser may throw; killing below covers it.
                    }
                });

                if (await Task.WhenAny(close, Task.Delay(CloseGrace)).ConfigureAwait(false) != close)
                {
                    KillProcess(browser);
                }

                KillProcess(browser);
                try
                {
                    await browser.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }

            if (userDataDir != null)
            {
                await DeleteDirectoryAsync(userDataDir).ConfigureAwait(false);
            }
        }

        private static void KillProcess(IBrowser browser)
        {
            try
            {
                var process = browser.Process;
                if (process != null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                // The process ended on its own in the meantime.
            }
        }

        private const string ProfilePrefix = "lifeos-pdf-html-";

        private static async Task DeleteDirectoryAsync(string path)
        {
            // After the main process exits, its helper processes (network service, crashpad) keep a few files
            // in the profile locked for up to several seconds on Windows. Retry for ~10 s in total.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (TryDeleteDirectory(path))
                {
                    return;
                }

                await Task.Delay(Math.Min(2000, 250 * (attempt + 1))).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes profile folders left behind by earlier runs (e.g. an application that was killed). Only folders
        /// untouched for an hour are considered; one still in use by a running browser is locked and simply stays.
        /// </summary>
        private static void DeleteStaleProfiles()
        {
            try
            {
                var limit = DateTime.UtcNow.AddHours(-1);
                foreach (var directory in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories(ProfilePrefix + "*"))
                {
                    if (directory.LastWriteTimeUtc < limit)
                    {
                        TryDeleteDirectory(directory.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // Housekeeping only; never a reason to fail a conversion.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Left in the temp folder; the OS cleans it up.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static WaitUntilNavigation WaitUntil(HtmlWaitUntil value) => value switch
        {
            HtmlWaitUntil.DomContentLoaded => WaitUntilNavigation.DOMContentLoaded,
            HtmlWaitUntil.NetworkIdle => WaitUntilNavigation.Networkidle0,
            _ => WaitUntilNavigation.Load,
        };

        /// <summary>Adds &lt;base href&gt; so relative links in an HTML string resolve against BaseUrl.</summary>
        internal static string InjectBase(string html, Uri baseUrl)
        {
            var href = baseUrl.AbsoluteUri.Replace("&", "&amp;").Replace("\"", "&quot;");
            var tag = "<base href=\"" + href + "\">";
            var head = HeadTag.Match(html);
            return head.Success ? html.Insert(head.Index + head.Length, tag) : tag + html;
        }

        private static string BlockedHint(List<string> blocked)
        {
            lock (blocked)
            {
                if (blocked.Count == 0)
                {
                    return string.Empty;
                }

                return " Blocked requests (allow them with AllowNetwork / AllowLocalFiles if you trust the page): " +
                       string.Join(", ", blocked.Take(5)) + (blocked.Count > 5 ? ", …" : string.Empty);
            }
        }

        private static HtmlConversionException TimeoutError(HtmlToPdfOptions o, string source, Exception? inner) =>
            new HtmlConversionException(
                HtmlConversionError.Timeout,
                $"Converting {source} took longer than {o.Timeout.TotalSeconds:0.#} s and was stopped. " +
                "Raise HtmlToPdfOptions.Timeout, or check the page for scripts that never finish.", inner);

        private static byte[] RunSync(Func<Task<byte[]>> action) =>
            // Task.Run keeps a UI/ASP.NET synchronization context from deadlocking the blocking wait.
            Task.Run(action).GetAwaiter().GetResult();

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(ChromiumConverter));
            }
        }
    }
}
