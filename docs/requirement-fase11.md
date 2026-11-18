# Requirement Fase 11: HTML → PDF

> **Status: disetujui (2026-11-18).** Semua saran di §6 dipakai: 11a Chromium dulu (11b wkhtmltopdf,
> SVG → PDF terpisah), PuppeteerSharp, net8.0+, JS nyala / jaringan & file mati default, pasca-proses
> default nyala, HTML kosong → 1 halaman kosong, Edge 157 untuk tes & benchmark di mesin dev.

## 1. Ringkasan & batas scope

Package terpisah **`LifeOS.Pdf.Html`** mengubah HTML/CSS menjadi PDF lewat engine browser pihak
ketiga. Package inti `LifeOS.Pdf` **tetap tanpa dependency** dan tidak berubah, kecuali bila perlu
API kecil untuk menggabungkan hasil (lihat FR-12).

Menulis engine HTML/CSS dari nol tidak realistis (PHASES.md: use case 1 = "integrasi"), jadi
fase ini membungkus dua backend di balik **satu interface**:

| Backend | Lisensi | Cara jalan | Status |
|---|---|---|---|
| **Chromium headless** (Chrome / Edge / Chromium) | BSD-3 (Chromium); binding MIT/Apache-2.0 | Chrome DevTools Protocol, `Page.printToPDF` | **Direkomendasikan** |
| **wkhtmltopdf** | LGPL-3.0, dipanggil sebagai proses terpisah, binary dipasang user (tidak dibundel) | CLI `wkhtmltopdf` | Legacy (diarsipkan sejak 2023, WebKit lama) |

**Masuk scope:**
- Konversi dari string HTML, file `.html`, dan URL → `byte[]` / `Stream` / file / `PdfDocument`.
- Opsi: ukuran kertas (pakai `PageSize` yang sudah ada), orientasi, margin, skala, latar,
  header/footer HTML dengan nomor halaman, rentang halaman, timeout, tunggu-sampai-siap.
- Keamanan default untuk HTML tak terpercaya (jaringan & file lokal mati kecuali diizinkan).
- Hasil langsung bisa diproses fitur yang sudah ada (merge, stamp, watermark, enkripsi, signing).
- **Benchmark performa** di `benchmarks/LifeOS.Pdf.Benchmarks` + angka di `docs/performance.md`.
- **Tutorial** bab baru di `docs/tutorial.md` + contoh di `samples/`.

**Tidak masuk (dipindah):**
- Engine HTML/CSS dari nol → tidak dijadwalkan.
- **SVG → PDF vektor** (baris "Target fitur lengkap", Fase 11) → diusulkan **Fase 11b**, karena itu
  parser + renderer sendiri ke `PdfGraphics`, bukan integrasi. Lewat Chromium, SVG di dalam HTML
  sudah ikut ter-render (vektor) di Fase 11.
- CSS paged media lanjutan di luar yang didukung Chromium (`@page` margin boxes, `string-set`)
  → batasan tercatat.
- Mengunduh/membundel binary browser atau wkhtmltopdf → tidak; user yang menyediakan.
- PDF/A atau tagged PDF dari HTML → Fase 14.

**Catatan ukuran:** dua backend + manajemen proses + tes yang butuh browser terpasang. Diusulkan
dikerjakan **11a = Chromium** (inti + semua opsi + benchmark + tutorial), **11b = wkhtmltopdf**
(implementasi kedua dari interface yang sama). Lihat §6 Q1.

## 2. Requirement fungsional

### Interface & API publik
- FR-01 `IHtmlToPdfConverter` dengan `ConvertHtml(string html, HtmlToPdfOptions?)`,
  `ConvertFile(string path, …)`, `ConvertUrl(Uri url, …)` → `byte[]`, plus varian `…Async`
  (`Task<byte[]>`, menerima `CancellationToken`).
- FR-02 Implementasi `ChromiumConverter` (Fase 11a) dan `WkHtmlToPdfConverter` (Fase 11b), dan facade
  `HtmlToPdf.Convert(html, options)` yang memakai backend default (Chromium).
- FR-03 Helper ke model yang sudah ada: `HtmlToPdf.ToDocument(html, …)` → `PdfDocument` (lewat
  `PdfDocument.Open(byte[])`), dan `Save(path)` / tulis ke `Stream`.
- FR-04 Lokasi browser: `ChromiumOptions.ExecutablePath`; bila kosong, deteksi otomatis Chrome,
  Edge, lalu Chromium di lokasi standar Windows/Linux/macOS. Tidak ketemu → exception yang
  menyebut path yang dicoba dan cara memasang.
- FR-05 Reuse proses: satu `ChromiumConverter` (IDisposable/IAsyncDisposable) menjaga satu proses
  browser untuk banyak konversi; aman dipakai paralel (satu tab per konversi) dengan batas
  `MaxConcurrency`.

### Opsi (`HtmlToPdfOptions`)
- FR-06 Kertas: `PageSize` (A4/Letter/…/kustom) + `Landscape`; atau "pakai `@page` CSS"
  (`PreferCssPageSize`).
- FR-07 Margin (point, sama dengan konvensi API publik), `Scale` (0.1–2.0), `PrintBackground`,
  `PageRanges` (pakai `PageRange` yang sudah ada).
- FR-08 Header & footer HTML dengan placeholder nomor halaman (`{page}`, `{pages}`, `{date}`,
  `{title}`, `{url}`), dipetakan ke mekanisme backend (`pageNumber`/`totalPages` di Chromium,
  `[page]`/`[topage]` di wkhtmltopdf). Format sama dengan `PdfStamper.AddPageNumbers`.
- FR-09 Kesiapan halaman: `WaitUntil` (load / network idle), `WaitForSelector`, jeda tambahan; dan
  `Timeout` (default 30 s) → `HtmlConversionException` dengan pesan jelas saat lewat.
- FR-10 `BaseUrl` untuk resolve aset relatif (gambar, CSS, font) dari string HTML; media
  `print` vs `screen` (`EmulateScreenMedia`).
- FR-11 Metadata: judul diambil dari `<title>` bila `Title` kosong; `Author`/`Subject` ditulis ke
  `/Info` setelah konversi (lewat parser+writer sendiri, bukan backend).

### Keamanan (input tak terpercaya — CLAUDE.md #7)
- FR-12 Default **aman**: JavaScript boleh dimatikan (`AllowJavaScript`, default `true` untuk
  Chromium karena banyak halaman butuh JS — lihat Q4); akses jaringan eksternal **mati**
  (`AllowNetwork = false`) dan akses `file://` **mati** (`AllowLocalFiles = false`) kecuali
  `BaseUrl`-nya file lokal yang diizinkan eksplisit. Di Chromium lewat request interception; di
  wkhtmltopdf lewat `--disable-local-file-access` / `--disable-javascript` dsb.
- FR-13 Batas: ukuran HTML input, ukuran output, timeout keras yang **membunuh proses** browser bila
  macet; tidak boleh ada proses yatim (orphan) setelah Dispose/exception.

### Integrasi dengan fitur yang sudah ada
- FR-14 Hasil bisa digabung dengan dokumen LifeOS.Pdf: `PdfMerger.Append(doc, htmlDoc)`,
  `PdfImposition.Overlay` (mis. HTML sebagai kop surat/latar), `PdfStamper` (watermark, nomor
  halaman), enkripsi Fase 9, signing Fase 9. Contoh di sample + tes.
- FR-15 Output selalu dibuka ulang & disimpan lewat writer LifeOS.Pdf bila ada opsi pasca-proses
  (metadata, `/ID` deterministik), supaya aturan validitas proyek berlaku. Mode "apa adanya"
  (bytes backend) tersedia untuk kecepatan.

### Backend wkhtmltopdf (Fase 11b)
- FR-16 `WkHtmlToPdfConverter` memanggil `wkhtmltopdf` lewat stdin/stdout (tanpa file sementara
  bila memungkinkan), argumen dibangun aman (tanpa shell; tidak ada injeksi argumen dari
  opsi user).
- FR-17 Opsi yang tidak didukung backend → `NotSupportedException` yang menyebut opsinya, bukan
  diam-diam diabaikan.

## 3. Requirement non-fungsional

- NFR-01 PDF hasil konversi **lolos** `PdfStructure.Verify`, `tools/validate_pdf.py`
  (qpdf + pypdf + PDFium), dan tampil benar di Chrome + satu reader lain (CLAUDE.md #1).
- NFR-02 Package inti tetap tanpa dependency. `LifeOS.Pdf.Html` menargetkan **net8.0+** (lihat Q3),
  seperti Rendering/Signing. Dependency baru wajib dicatat di `THIRD_PARTY_NOTICES.md` dan plan.
- NFR-03 **Performa** (diukur, dicatat di `docs/performance.md` bagian baru "Phase 11"):
  - Cold start Chromium (luncurkan + konversi pertama) — target < 2 s di laptop dev.
  - Warm: HTML 1 halaman sederhana — target < 150 ms per konversi.
  - Invoice HTML 1 halaman dengan tabel + CSS + gambar inline — target < 250 ms.
  - Laporan HTML 50 halaman (tabel 1500 baris, setara skenario F2) — dicatat sebagai baseline dan
    dibandingkan dengan layout engine Fase 2 (134–176 ms).
  - Throughput paralel: 20 dokumen dengan `MaxConcurrency` 1 vs 4.
  - Pasca-proses (open + set metadata + save) — overhead dicatat terpisah.
  - Kolom: ms, alokasi managed MB, ukuran output KB (format sama dengan tabel Fase 7–10).
  - Benchmark wkhtmltopdf (11b) di skenario yang sama bila binary tersedia; dilewati dengan pesan
    bila tidak.
- NFR-04 Benchmark **tidak boleh gagal** di mesin tanpa browser: skenario dilewati dengan alasan
  jelas, skenario lain tetap jalan.
- NFR-05 **Tes**: unit test yang tidak butuh browser (pemetaan opsi, placeholder header/footer,
  argumen wkhtmltopdf, deteksi path) selalu jalan; tes integrasi yang butuh browser memakai
  skip-otomatis bila browser tidak ada (pola sama dengan tes Rendering/OCR yang butuh native).
- NFR-06 Determinisme: output Chromium memuat tanggal & ID acak; dengan pasca-proses (FR-15),
  `/ID` diturunkan dari isi dan `CreationDate` bisa di-set → output stabil antar run di mesin &
  versi browser yang sama. Perbedaan antar versi browser dicatat sebagai batasan.
- NFR-07 Tidak meninggalkan proses atau file sementara (profil browser sementara dihapus saat
  Dispose).
- NFR-08 Pesan exception jelas dan menyebut cara memperbaikinya (browser tidak ketemu, timeout,
  HTML terlalu besar, opsi tidak didukung).
- NFR-09 **Tutorial**: bab baru "HTML → PDF (Phase 11)" di `docs/tutorial.md` (bahasa Inggris,
  gaya sama dengan bab 12): setup browser, konversi pertama, opsi kertas/margin/header-footer,
  aset & `BaseUrl`, HTML tak terpercaya, gabung dengan dokumen LifeOS.Pdf, performa & reuse
  converter, troubleshooting. Plus entri daftar isi, FAQ, `README.md`, `CHANGELOG.md` [1.2.0].

## 4. Referensi spesifikasi

- ISO 32000-1 §7.5 (struktur file) & §14.3.3 (`/ID`) — pasca-proses dan validitas output.
- ISO 32000-1 §14.3.3 / §10.2 (dokumen `/Info`) — metadata yang ditulis ulang.
- ISO 32000-1 §7.7.3 — page tree saat merge/overlay hasil HTML.
- Chrome DevTools Protocol — `Page.printToPDF`, `Fetch.requestPaused` (interception),
  `Page.navigate`, `Page.setContent`/`lifecycleEvent`.
- CSS Paged Media Level 3 (`@page`, `size`, `margin`) — sebatas yang didukung Chromium.
- wkhtmltopdf usage docs (opsi CLI) — untuk 11b.

## 5. Edge cases

- HTML kosong / hanya whitespace → PDF 1 halaman kosong yang valid (atau exception — lihat Q6).
- HTML sangat besar (> batas) → exception sebelum browser dipanggil.
- Teks Unicode (Indonesia, Arab, CJK, emoji) → tergantung font sistem; dokumentasikan cara
  menyertakan web font lewat `@font-face`.
- Locale mesin `id-ID` → semua angka opsi yang dikirim ke CDP/CLI culture-invariant (CLAUDE.md #3).
- Gambar relatif tanpa `BaseUrl` → gambar hilang; dokumentasikan. `data:` URI selalu jalan.
- Halaman dengan JS tak berujung / `while(true)` → timeout keras membunuh tab/proses.
- Request ke `file:///C:/Windows/...` atau jaringan internal (SSRF) dari HTML tak terpercaya →
  diblokir default.
- URL 404 / DNS gagal / sertifikat TLS tidak valid → exception dengan status.
- Margin lebih besar dari kertas, `Scale` di luar rentang, `PageRanges` di luar jumlah halaman →
  `ArgumentException` jelas.
- Header/footer HTML dengan karakter khusus `<`, `&` di placeholder → di-escape.
- Browser crash di tengah konversi → exception, converter bisa dipakai lagi (relaunch otomatis).
- Pemakaian paralel dari banyak thread; Dispose saat konversi masih jalan.
- Path browser/wkhtmltopdf berisi spasi (mis. `C:\Program Files\...`).
- Mesin CI tanpa browser → tes integrasi & benchmark dilewati, bukan gagal.

## 6. Pertanyaan klarifikasi

1. **Pemecahan** — setujui 11a (Chromium + benchmark + tutorial) dulu, lalu 11b (wkhtmltopdf) dan
   SVG → PDF vektor sebagai fase terpisah?
2. **Binding Chromium** — pilih:
   (a) **PuppeteerSharp** (MIT) — matang, bisa pakai Chrome/Edge yang sudah terpasang;
   (b) Microsoft.Playwright (Apache-2.0) — lebih berat, butuh driver Node bawaan;
   (c) klien **CDP sendiri** lewat `ClientWebSocket` — tanpa dependency, kontrol penuh, kode lebih banyak.
   Saran: (a) untuk 11a; (c) bisa dipertimbangkan nanti kalau dependency jadi masalah.
3. **Target framework** — net8.0+ saja (konsisten dengan Signing/Scanning), atau net6.0+?
4. **Default JavaScript** — nyala (praktis, kebanyakan template butuh) atau mati (lebih aman untuk
   HTML tak terpercaya)? Jaringan & file lokal tetap mati default.
5. **Pasca-proses default** — selalu buka & simpan ulang lewat writer LifeOS.Pdf (output
   deterministik, metadata konsisten, sedikit lebih lambat) atau kembalikan bytes browser apa
   adanya kecuali diminta?
6. **HTML kosong** — hasilkan 1 halaman kosong atau lempar exception?
7. **Browser untuk tes/benchmark di mesin ini** — di mesin dev terdeteksi **Microsoft Edge 157**
   (Chromium). Boleh dipakai sebagai browser default tes & benchmark?
