# Design Fase 11a: HTML → PDF (Chromium)

**Menyusul requirement-fase11.md.** Keputusan (disetujui user, 2026-11-18 — semua saran §6):
1. Dipecah: **11a = Chromium** + benchmark + tutorial (dokumen ini). **11b = wkhtmltopdf** memakai
   interface yang sama. SVG → PDF vektor = fase terpisah (backlog).
2. Binding = **PuppeteerSharp** (MIT) `25.17.1`, memakai Chrome/Edge/Chromium yang sudah terpasang
   (tidak mengunduh browser).
3. Target = **net8.0; net9.0; net10.0**.
4. JavaScript **nyala** default; jaringan & `file://` **mati** default.
5. Pasca-proses default = **nyala** (buka & simpan ulang lewat writer LifeOS.Pdf → output deterministik).
6. HTML kosong → **1 halaman kosong** yang valid.
7. Browser tes/benchmark di mesin dev = **Microsoft Edge 157** (deteksi otomatis).

## 1. API publik

Namespace `LifeOS.Pdf.Html`, package `LifeOS.Pdf.Html`.

```csharp
public interface IHtmlToPdfConverter : IDisposable, IAsyncDisposable
{
    Task<byte[]> ConvertHtmlAsync(string html, HtmlToPdfOptions? options = null, CancellationToken ct = default);
    Task<byte[]> ConvertFileAsync(string path, HtmlToPdfOptions? options = null, CancellationToken ct = default);
    Task<byte[]> ConvertUrlAsync(Uri url, HtmlToPdfOptions? options = null, CancellationToken ct = default);

    byte[] ConvertHtml(string html, HtmlToPdfOptions? options = null);   // sync = Async().GetAwaiter().GetResult()
    byte[] ConvertFile(string path, HtmlToPdfOptions? options = null);
    byte[] ConvertUrl(Uri url, HtmlToPdfOptions? options = null);
}

public sealed class ChromiumConverter : IHtmlToPdfConverter
{
    public ChromiumConverter(ChromiumOptions? options = null);   // browser diluncurkan malas (lazy) saat konversi pertama
    public string? BrowserVersion { get; }                         // terisi setelah diluncurkan
    public Task WarmUpAsync(CancellationToken ct = default);       // luncurkan sekarang (untuk server/benchmark)
}

public sealed class ChromiumOptions
{
    public string? ExecutablePath { get; set; }          // null → BrowserLocator (env LIFEOS_PDF_CHROMIUM, Chrome, Edge, Chromium)
    public int MaxConcurrency { get; set; } = 4;         // tab paralel maksimum (1–64)
    public TimeSpan LaunchTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public IList<string> ExtraArguments { get; } = new List<string>();
}

public sealed class HtmlToPdfOptions
{
    // Kertas & tata letak (point, konvensi API publik)
    public PageSize PageSize { get; set; } = PageSize.A4;
    public bool Landscape { get; set; }
    public bool PreferCssPageSize { get; set; }          // @page { size } menang atas PageSize
    public HtmlMargins Margins { get; set; } = HtmlMargins.Uniform(36);   // 0,5 inch
    public double Scale { get; set; } = 1.0;             // 0.1–2.0
    public bool PrintBackground { get; set; } = true;
    public PageRange? PageRanges { get; set; }           // tipe inti yang sudah ada

    // Header/footer HTML; placeholder {page} {pages} {date} {title} {url}
    public string? HeaderHtml { get; set; }
    public string? FooterHtml { get; set; }

    // Pemuatan
    public Uri? BaseUrl { get; set; }
    public HtmlWaitUntil WaitUntil { get; set; } = HtmlWaitUntil.Load;
    public string? WaitForSelector { get; set; }
    public TimeSpan ExtraDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EmulateScreenMedia { get; set; }

    // Keamanan
    public bool AllowJavaScript { get; set; } = true;
    public bool AllowNetwork { get; set; }               // http/https/ws
    public bool AllowLocalFiles { get; set; }            // file:// — hanya di bawah folder BaseUrl
    public int MaxHtmlLength { get; set; } = 20 * 1024 * 1024;     // karakter
    public long MaxOutputBytes { get; set; } = 256L * 1024 * 1024;

    // Pasca-proses (FR-11, FR-15)
    public bool PostProcess { get; set; } = true;
    public string? Title { get; set; }                   // null → <title> dari halaman
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset? CreationDate { get; set; }    // null → sama dengan PdfDocument baru
}

public sealed class HtmlMargins
{
    public HtmlMargins(double top, double right, double bottom, double left);
    public static HtmlMargins Uniform(double points);
    public double Top { get; } public double Right { get; } public double Bottom { get; } public double Left { get; }
}

public enum HtmlWaitUntil { Load, DomContentLoaded, NetworkIdle }

public static class HtmlToPdf                       // facade dengan converter bersama (lazy, di-dispose saat ProcessExit)
{
    public static byte[] Convert(string html, HtmlToPdfOptions? options = null);
    public static Task<byte[]> ConvertAsync(string html, HtmlToPdfOptions? options = null, CancellationToken ct = default);
    public static byte[] ConvertFile(string path, HtmlToPdfOptions? options = null);
    public static byte[] ConvertUrl(Uri url, HtmlToPdfOptions? options = null);
    public static PdfDocument ToDocument(string html, HtmlToPdfOptions? options = null);  // PdfDocument.Open(Convert(...))
}

public sealed class HtmlConversionException : Exception   // timeout, browser tidak ketemu/crash, terlalu besar
{
    public HtmlConversionError Error { get; }
}
public enum HtmlConversionError { BrowserNotFound, LaunchFailed, Timeout, NavigationFailed, InputTooLarge, OutputTooLarge, BrowserCrashed }
```

Contoh:
```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Html;
using LifeOS.Pdf.Manipulation;

await using var html = new ChromiumConverter();
var options = new HtmlToPdfOptions
{
    PageSize = PageSize.A4,
    Margins = HtmlMargins.Uniform(48),
    FooterHtml = "<div style='font-size:9pt;width:100%;text-align:center'>Halaman {page} dari {pages}</div>",
    Title = "Invoice INV-2026-001",
};
byte[] pdf = await html.ConvertHtmlAsync(File.ReadAllText("invoice.html"), options);

var doc = PdfDocument.Open(pdf);                      // lanjut dengan fitur inti
PdfStamper.AddTextWatermark(doc, "LUNAS");
doc.Save("invoice.pdf");
```

Tidak ada perubahan breaking; package inti tidak berubah.

## 2. Desain internal

### Alur konversi
```
ConvertHtmlAsync(html, o)
 ├─ Validate(o)                         → ArgumentException (margin ≥ kertas, scale, timeout ≤ 0, ...)
 ├─ html.Length > MaxHtmlLength         → HtmlConversionException(InputTooLarge)
 ├─ EnsureBrowserAsync()                → lock; launch bila null / Process sudah mati (relaunch otomatis)
 ├─ semaphore.WaitAsync(ct)             → batasi MaxConcurrency
 ├─ ctx  = browser.CreateBrowserContextAsync()   // terisolasi: cookie/storage tidak bocor antar konversi
 ├─ page = ctx.NewPageAsync()
 │   ├─ SetJavaScriptEnabledAsync(o.AllowJavaScript)
 │   ├─ SetRequestInterceptionAsync(true) + RequestPolicy.Decide(url, o) → Continue / Abort(BlockedByClient)
 │   ├─ EmulateMediaTypeAsync(Screen) bila EmulateScreenMedia
 │   ├─ string HTML: sisipkan <base href=BaseUrl> (bila ada) → SetContentAsync(html, WaitUntil)
 │   │  file      : GoToAsync(file:///…, WaitUntil)  — AllowLocalFiles dianggap true untuk folder file itu
 │   │  url       : GoToAsync(url) — butuh AllowNetwork (kalau false → ArgumentException yang menjelaskan)
 │   ├─ WaitForSelectorAsync / Task.Delay(ExtraDelay)
 │   └─ PdfDataAsync(PdfOptionsMapper.Map(o))
 ├─ finally: page/ctx ditutup; semaphore.Release
 ├─ Timeout (CancellationTokenSource gabungan): tutup tab; bila tab tidak menutup dalam 5 s → Kill proses,
 │   browser = null (relaunch berikutnya)
 └─ PostProcess? → PdfPostProcessor.Apply(bytes, o, pageTitle) : bytes
```

### `PdfOptionsMapper` (internal, murni → bisa dites tanpa browser)
- Ukuran: point → inci (`pt / 72`), string `"8.2639in"` dengan `InvariantCulture` (CLAUDE.md #3).
  `Landscape` ditangani dengan **menukar lebar/tinggi sendiri**, bukan flag CDP `landscape` (flag itu
  berperilaku tak konsisten bila width/height eksplisit).
- Margin → `MarginOptions` (inci, invariant). `Scale` → `decimal`. `PageRanges` → teks `PageRange.Text`
  (`"1-3,5"`; `"1-"` → dihilangkan = semua halaman).
- Header/footer: `HeaderFooterTemplate.Build(html)` mengganti placeholder:
  `{page}`→`<span class="pageNumber"></span>`, `{pages}`→`totalPages`, `{date}`→`date`,
  `{title}`→`title`, `{url}`→`url`. Chromium sendiri meng-escape nilai `title`/`url`. Bila hanya
  salah satu yang diisi, yang lain diisi `<span></span>` (kalau tidak, Chromium memakai template
  default berisi tanggal & URL). Default font Chromium untuk template = 0 → dibungkus
  `<div style="font-size:9pt">` bila user tidak menulis font-size.
- `DisplayHeaderFooter = HeaderHtml != null || FooterHtml != null`.

### `RequestPolicy` (internal, murni)
| Skema | Keputusan |
|---|---|
| `data:`, `blob:`, `about:` | izinkan |
| `http:`, `https:`, `ws:`, `wss:` | izinkan hanya bila `AllowNetwork` |
| `file:` | izinkan hanya bila `AllowLocalFiles` **dan** path kanonik (setelah `Path.GetFullPath`, tanpa `..`) berada di bawah folder `BaseUrl`/file sumber |
| lainnya (`chrome:`, `ftp:`, …) | blokir |

Request yang diblokir dicatat (maks. 100) dan dilampirkan di pesan `HtmlConversionException` bila
navigasi utama gagal, supaya user tahu harus menyalakan `AllowNetwork`/`AllowLocalFiles`.
Navigasi dokumen utama dari `SetContentAsync` (about:blank) selalu diizinkan.

### `BrowserLocator` (internal)
Urutan: `ChromiumOptions.ExecutablePath` → env `LIFEOS_PDF_CHROMIUM` → kandidat per OS:
- Windows: `%ProgramFiles%`/`%ProgramFiles(x86)%`/`%LocalAppData%` × `Google\Chrome\Application\chrome.exe`,
  `Microsoft\Edge\Application\msedge.exe`, `Chromium\Application\chrome.exe`.
- Linux: `google-chrome`, `google-chrome-stable`, `chromium`, `chromium-browser`, `microsoft-edge` di `PATH`
  + `/usr/bin`, `/snap/bin`.
- macOS: `/Applications/Google Chrome.app/…`, `Microsoft Edge.app`, `Chromium.app`.
Tidak ketemu → `HtmlConversionException(BrowserNotFound)` yang mendaftar path yang dicoba + cara set
`ExecutablePath`/`LIFEOS_PDF_CHROMIUM`. Pencarian file diabstraksi (`Func<string,bool> fileExists`)
supaya bisa dites.

### Peluncuran
`Puppeteer.LaunchAsync(new LaunchOptions { ExecutablePath, Headless = true, UserDataDir = <temp unik>,
Args = ["--no-first-run","--no-default-browser-check","--disable-extensions","--disable-gpu",
"--disable-sync","--disable-background-networking","--mute-audio"] + ExtraArguments, Timeout = LaunchTimeout })`.
`--no-sandbox` **tidak** dipakai default (bisa lewat `ExtraArguments` di container root). `UserDataDir`
dihapus saat Dispose (retry 3× karena Windows kadang masih mengunci file).

### `PdfPostProcessor` (internal)
`PdfDocument.Open(bytes)` → `Info.Title = o.Title ?? pageTitle`, `Author`, `Subject`,
`Creator = "LifeOS.Pdf.Html (Chromium <versi>)"`, `CreationDate = o.CreationDate ?? <default dokumen>`,
`ModificationDate = null` → `ToArray()`. Writer LifeOS.Pdf menulis ulang xref dan `/ID` dari isi
(CLAUDE.md #5), jadi tanggal/ID acak dari Skia hilang. Hasilnya diverifikasi oleh tes yang sama
dengan fitur lain (`PdfStructure.Verify`).

### Keputusan teknis & alternatif yang ditolak
- **PuppeteerSharp** dipilih atas Playwright (driver Node bawaan, ~100 MB) dan klien CDP sendiri
  (lebih banyak kode, belum perlu). Bisa diganti nanti tanpa mengubah API publik.
- **Satu proses browser, satu BrowserContext per konversi**: isolasi cookie/localStorage dan biaya
  rendah (~5 ms) dibanding proses baru per konversi (~400–1000 ms).
- Sync API = blocking di atas async (pola sama dengan library lain); didokumentasikan bahwa
  ASP.NET sebaiknya memakai `…Async`.

## 3. Struktur file

```
src/LifeOS.Pdf.Html/                         (baru, net8.0;net9.0;net10.0)
  LifeOS.Pdf.Html.csproj                     PuppeteerSharp 25.17.1 (MIT) + ProjectReference LifeOS.Pdf
  IHtmlToPdfConverter.cs                     interface publik
  ChromiumConverter.cs                       proses browser, antrean, timeout, relaunch
  ChromiumOptions.cs
  HtmlToPdf.cs                               facade statis
  HtmlToPdfOptions.cs, HtmlMargins.cs, HtmlWaitUntil.cs
  HtmlConversionException.cs, HtmlConversionError.cs
  Internal/BrowserLocator.cs                 deteksi Chrome/Edge/Chromium
  Internal/PdfOptionsMapper.cs               opsi → PuppeteerSharp PdfOptions (invariant)
  Internal/HeaderFooterTemplate.cs           placeholder → kelas Chromium
  Internal/RequestPolicy.cs                  allow/block per URL
  Internal/PdfPostProcessor.cs               metadata + /ID deterministik
src/LifeOS.Pdf.All/                          + ProjectReference LifeOS.Pdf.Html
tests/LifeOS.Pdf.Tests/HtmlTests.cs          (baru) + ProjectReference
benchmarks/LifeOS.Pdf.Benchmarks/Program.cs  + skenario F11
samples/LifeOS.Pdf.Samples/                  + HtmlSample (dilewati bila browser tidak ada)
docs/tutorial.md                             + bab "HTML → PDF (Phase 11)"
docs/performance.md                          + bagian "Phase 11"
README.md, CHANGELOG.md [1.2.0], THIRD_PARTY_NOTICES.md (PuppeteerSharp MIT, Chromium BSD-3), LifeOS.Pdf.sln
```

Dependency baru: **PuppeteerSharp 25.17.1 (MIT)** dan dependensi transitifnya (Microsoft.Extensions.Logging,
System.Text.Json — Microsoft, MIT). Browser **tidak** dibundel.

## 4. Kompatibilitas target framework

- `LifeOS.Pdf.Html` hanya net8.0/net9.0/net10.0 (async disposal, PuppeteerSharp modern). netstandard2.0
  tidak didukung — dicatat di README seperti Signing.
- Package inti tidak berubah, tetap netstandard2.0 + net5–net10.
- Sample: proyek sample multi-target; bagian HTML dikompilasi hanya untuk `NET8_0_OR_GREATER`
  (pola yang sama dengan Signing di sample, dicek saat implementasi).

## 5. Strategi test

**Tanpa browser (selalu jalan):**
- `PdfOptionsMapper`: A4 → `8.2639in × 11.6944in`; landscape menukar dimensi; margin 36 pt → `0.5in`;
  dijalankan di bawah `CultureInfo("id-ID")` → tetap titik desimal; `PageRange.Parse("1-3,5")` → `"1-3,5"`.
- Validasi: scale 0/3, margin kiri+kanan ≥ lebar, timeout 0, MaxConcurrency 0 → `ArgumentException`.
- `HeaderFooterTemplate`: semua placeholder; hanya footer → header kosong `<span></span>`; font-size default.
- `RequestPolicy`: data/about izin; http diblokir default; file di luar BaseUrl & `..\` traversal diblokir;
  file di dalam BaseUrl diizinkan dengan `AllowLocalFiles`.
- `BrowserLocator`: dengan `fileExists` palsu → urutan prioritas benar; tidak ketemu → pesan berisi daftar path.
- `PdfPostProcessor`: input PDF buatan LifeOS.Pdf → judul/penulis terisi, dua kali proses = byte identik.

**Dengan browser (skip otomatis bila `BrowserLocator` gagal — `HtmlConversionError.BrowserNotFound`):**
- HTML sederhana → 1 halaman, MediaBox A4 (595×842 ±1), `ExtractText` memuat teks, `PdfStructure.Verify` lolos.
- Tabel 300 baris → > 1 halaman; footer "Halaman {page} dari {pages}" → teks "Halaman 2 dari N" ada.
- Landscape & Letter → MediaBox sesuai. `PageRanges = "2"` → 1 halaman.
- HTML kosong → 1 halaman valid.
- `<img src="http://127.0.0.1:1/x.png">` default → tidak ada request keluar (tercatat diblokir), PDF tetap jadi.
- `<img src="file:///…">` di luar BaseUrl → diblokir; di dalam BaseUrl + AllowLocalFiles → gambar ada (`GetImages`).
- `while(true){}` dengan Timeout 2 s → `HtmlConversionException(Timeout)`; konversi berikutnya berhasil.
- 8 konversi paralel → semua valid. Determinisme: 2× konversi dengan `CreationDate` tetap → byte sama.
- Integrasi: `PdfMerger.Append`, `PdfStamper.AddTextWatermark`, enkripsi AES-128 lalu buka lagi.

**Validasi eksternal & manual:** sample menulis `out/<tfm>/html-invoice.pdf` dan `html-report.pdf` →
`tools/validate_pdf.py` (qpdf + pypdf + PDFium) → buka di Chrome dan Edge.

**Benchmark (F11, `docs/performance.md`):** cold start (luncur + konversi pertama), HTML 1 halaman,
invoice dengan CSS + gambar `data:`, laporan tabel 1500 baris (~50 halaman) vs F2, 20 dokumen dengan
`MaxConcurrency` 1 vs 4, pasca-proses saja. Kolom ms / alloc MB / out KB. Dilewati dengan pesan bila
browser tidak ada.

## 6. Risiko

- **Browser tidak ada di CI/server** → tes & benchmark skip; pesan error jelas; tutorial menjelaskan
  instalasi (termasuk `apt install chromium` dan opsi `--no-sandbox` untuk container root).
- **Versi browser berbeda → byte output berbeda** (Skia) → determinisme hanya dijamin untuk versi
  browser yang sama; dicatat di tutorial & performance.md.
- **Request interception menambah latensi** untuk halaman dengan banyak aset → diukur di benchmark.
- **Proses yatim** bila aplikasi crash → `UserDataDir` sementara + Kill saat timeout/Dispose; facade
  mendaftarkan `AppDomain.ProcessExit`.
- **Keamanan JS default nyala** → jaringan & file tetap mati, jadi JS tidak bisa mengirim data keluar
  (fetch/XHR/WebSocket ikut lewat interception). Didokumentasikan: untuk HTML tak terpercaya set
  `AllowJavaScript = false`.
- PuppeteerSharp memakai `System.Text.Json`/logging → ukuran dependency; tetap terpisah dari inti.

## 7. Rencana implementasi (developer) — menunggu persetujuan

1. Buat proyek `src/LifeOS.Pdf.Html` (csproj, sln, `LifeOS.Pdf.All`), tipe publik §1.
2. Internal murni: `PdfOptionsMapper`, `HeaderFooterTemplate`, `RequestPolicy`, `BrowserLocator`,
   `PdfPostProcessor` + tes tanpa browser.
3. `ChromiumConverter` (launch, context per konversi, interception, timeout/kill/relaunch, dispose) +
   facade `HtmlToPdf` + tes integrasi (skip bila tanpa browser).
4. Sample `HtmlSample` (invoice + laporan multi-halaman + merge/watermark).
5. Skenario benchmark F11 + jalankan + tulis tabel di `docs/performance.md`.
6. Bab tutorial "HTML → PDF (Phase 11)" + daftar isi, FAQ, README, CHANGELOG `[1.2.0]`,
   THIRD_PARTY_NOTICES, PHASES.md (status).
7. Verifikasi: `dotnet build -c Release`, `dotnet test -c Release`, sample + `tools/validate_pdf.py`;
   lalu QA → `docs/qa-fase11.md`.

## 8. Perubahan selama implementasi (2026-11-18)

- **HTML string + `BaseUrl` file://**: Chromium tidak mengizinkan halaman `about:blank` (hasil `setContent`)
  memuat `file://`. Bila `AllowLocalFiles` + `BaseUrl` lokal, HTML ditulis ke file sementara
  (`%TEMP%/lifeos-pdf-page-*.html`, UTF-8 BOM) lalu dibuka dengan `GoToAsync`; hanya file itu yang diizinkan
  di luar folder BaseUrl, dan file dihapus setelah konversi.
- **`setContent` + NetworkIdle**: PuppeteerSharp 25 tidak lagi mendukung networkidle untuk `setContent`;
  dipakai `load` lalu `WaitForNetworkIdleAsync`.
- **Tab cadangan**: membuka context baru (= renderer baru) ~100 ms adalah biaya tetap terbesar. Converter
  menyiapkan sampai `min(MaxConcurrency, 4)` context+tab kosong lebih dulu; tiap tab tetap dipakai sekali
  (isolasi tidak berubah). Penutupan tab berjalan di background.
- **Profil sementara**: hapus dengan retry ~10 s (proses anak Edge masih mengunci file), dan profil basi
  (> 1 jam, prefix `lifeos-pdf-html-`) dibersihkan saat launch.
- **`Tagged = false`, `Outline = false`** saat `printToPDF`: struktur tag belum dibawa oleh pasca-proses
  (PDF/UA = Fase 14).
- Angka performa & batasannya: `docs/performance.md` § Phase 11; temuan QA: `docs/qa-fase11.md`.
