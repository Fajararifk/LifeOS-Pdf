# QA Report: FASE 11a — HTML → PDF (Chromium)

**Tanggal:** 2026-11-18
**Review terhadap:** requirement-fase11.md & design-fase11.md
**Scope:** `src/LifeOS.Pdf.Html/` (baru), `tests/LifeOS.Pdf.Tests/HtmlTests.cs`, `samples/…/HtmlSample.cs`
+ `Assets/html/`, skenario `F11` di benchmark, `docs/tutorial.md` bab 13, `docs/performance.md`, README,
CHANGELOG `[1.2.0]`, THIRD_PARTY_NOTICES, `LifeOS.Pdf.All`, `tools/release.ps1`, `docs/releasing.md`.
**Browser uji:** Microsoft Edge 157 (Chromium, headless), Windows 11, .NET 8.0.27 / 9 / 10.

## Verdict: ⚠️ PASS DENGAN CATATAN

Semua requirement fungsional 11a terpenuhi dan teruji dengan browser asli. Build bersih, 583/583 tes
hijau di tiga runtime, dan 24/24 PDF sample lolos validator eksternal. Package inti tidak berubah dan tetap
tanpa dependency. **Masih terbuka:** pengecekan manual di Chrome + satu reader lain (CLAUDE.md #1) belum
dilakukan, dan perlu dikonfirmasi user sebelum tag `v1.2.0`. Ada juga 3 warning di bawah, tidak ada yang
blocker.

## Kepatuhan requirement

| ID | Status | Bukti |
|---|---|---|
| FR-01 interface sync/async + cancel | ✅ | `IHtmlToPdfConverter`; tes `Cancellation_IsHonoured`, `Parallel_EightConversions_AllValid` |
| FR-02 Chromium + facade | ✅ | `ChromiumConverter`, `HtmlToPdf`; contoh tutorial dijalankan |
| FR-03 `ToDocument` | ✅ | `HtmlToPdf.ToDocument`; contoh "Combine" di tutorial (8 halaman, terenkripsi, dibuka ulang) |
| FR-04 deteksi browser | ✅ | `BrowserLocator` + tes urutan prioritas & pesan "paths tried" |
| FR-05 reuse proses + konkurensi | ✅ | satu proses, context per konversi, `MaxConcurrency`; tes paralel 8× |
| FR-06/07 kertas, margin, skala, range | ✅ | tes A4/Letter landscape (MediaBox), `PageRanges="2"`, validasi skala/margin |
| FR-08 header/footer placeholder | ✅ | tes `Halaman 2 dari N` diekstrak dari PDF |
| FR-09 wait/timeout | ✅ | tes `while(true)` → `Timeout` dalam 2 s, converter tetap hidup |
| FR-10 BaseUrl / screen media | ✅ | tes file di bawah BaseUrl terbaca; `ConvertFile` memuat CSS tetangga (A5 dari `@page`) |
| FR-11 metadata | ✅ | judul dari `<title>`, `Creator` = `LifeOS.Pdf.Html (…)` |
| FR-12 keamanan default | ✅ | tes: http & file di luar BaseUrl diblokir, traversal `..` diblokir, JS bisa dimatikan |
| FR-13 batas & tanpa proses yatim | ✅ | `MaxHtmlLength`/`MaxOutputBytes`; dicek: 0 proses Edge headless tersisa setelah tes |
| FR-14 integrasi fitur lain | ✅ | tes merge + watermark + AES-128 → buka ulang dengan password |
| FR-15 pasca-proses deterministik | ✅ | tes `SameInputAndDate_SameBytes`, `PostProcessor_SetsMetadataAndIsDeterministic` |
| FR-16/17 wkhtmltopdf | ⏭️ | sesuai keputusan: Fase 11b |
| NFR-03 performa | ⚠️ | lihat W1 |
| NFR-04/05 skip tanpa browser | ✅ | `ChromiumFixture` → tes integrasi return; benchmark menulis "skipped" + alasan |
| NFR-09 tutorial | ✅ | bab 13 (+ daftar isi, setup, error handling, FAQ); semua snippet dikompilasi & dijalankan |

## Temuan

### 🔴 Blocker
Tidak ada. Masalah yang ditemukan selama implementasi sudah diperbaiki dan diverifikasi:
- **Gambar lokal tidak termuat lewat `BaseUrl` file://**: Chromium tidak mengizinkan `about:blank` (tempat
  `setContent` menaruh HTML) memuat `file://`. Diperbaiki dengan menulis HTML ke file sementara lalu membukanya
  sebagai file; `RequestPolicy` tetap membatasi ke folder BaseUrl. Dibuktikan oleh tes `NetworkAndFiles…`.
- **Folder profil sementara tertinggal** (3 folder di `%TEMP%`): proses anak Edge masih mengunci file setelah
  browser ditutup. Diperbaiki: retry hapus ~10 s + pembersihan profil basi (> 1 jam) saat launch. Dicek ulang:
  0 folder baru tersisa setelah test run.

### 🟡 Warning
- **W1 — Target performa "warm < 150 ms" hanya tercapai bila ada jeda antar-request.** Konversi berturut-turut
  187 ms; dengan jeda 500 ms: best 132 / median 158 ms. Penyebab: isolasi per konversi (context baru = renderer
  baru, ~100 ms CPU browser). Mitigasi sudah dibuat (tab cadangan disiapkan lebih dulu). Didokumentasikan di
  `performance.md` dan tutorial. Opsi bila perlu lebih cepat: mode "reuse context" opt-in di fase berikut
  (dengan konsekuensi isolasi yang lebih lemah).
- **W2 — Satu halaman yang hang bisa menggagalkan konversi lain yang berjalan bersamaan.**
  `ChromiumConverter.CloseAsync` mematikan seluruh proses browser bila sebuah tab tidak bisa ditutup dalam 5 s.
  Konversi lain di browser yang sama akan gagal dengan `BrowserCrashed` (browser lalu diluncurkan ulang otomatis).
  Ini jarang (`Target.closeTarget` biasanya berhasil, termasuk untuk `while(true)` di tes), tapi perlu dicatat
  untuk server dengan HTML tak terpercaya. Saran 11b: retry otomatis sekali untuk konversi yang kena `BrowserCrashed`.
- **W3 — `ConvertFileAsync` tidak menerapkan `MaxHtmlLength`.** Batas hanya berlaku untuk string HTML. File besar
  tetap dibatasi `Timeout` dan `MaxOutputBytes`, tapi konsistensinya kurang. Saran: cek ukuran file sebelum navigasi.

### 🔵 Saran
- `MaxOutputBytes` diperiksa setelah PDF diterima utuh dari browser (bukan streaming). Cukup untuk mencegah
  hasil raksasa diproses lebih lanjut, tapi tidak mencegah memori sesaat.
- Facade `HtmlToPdf` menutup browser lewat `ProcessExit`; pembersihan profil di sana bisa sampai ~10 s. Untuk
  aplikasi yang butuh shutdown cepat, pakai `ChromiumConverter` sendiri dengan `await using`.
- Kolom alokasi benchmark F11 tidak bermakna (kerja di proses browser & thread async); sudah dihilangkan dari
  tabel `performance.md`.
- Output belum tagged (`Tagged = false`); PDF/UA ada di Fase 14.

## Checklist

**Validitas PDF**
- [x] qpdf + pypdf + PDFium + fontTools + OTS: 24/24 PDF sample OK (termasuk `html-invoice.pdf` 1 hlm,
  `html-report.pdf` 5 hlm). `encrypted.pdf` dikecualikan karena butuh password (fitur Fase 9, bukan regresi).
- [x] `PdfStructure.Verify` pada output konversi di tes
- [x] Teks bisa diekstrak (`ExtractText`): "Rp1.350.000", nomor halaman footer, "Barang 300"
- [x] Render PDFium dicek visual (`out/png/net8.0-html-*.png`): layout invoice (grid, tabel, total), header tabel
  berulang di laporan, watermark DRAFT di atas konten HTML
- [ ] **Chrome + satu reader lain — belum, perlu user** (lihat rencana test manual)

**Kompatibilitas**
- [x] Build 0 warning / 0 error, semua target (`TreatWarningsAsErrors`)
- [x] Package inti tidak berubah; `LifeOS.Pdf.Html` net8/9/10 sesuai design
- [x] Sample tetap build di net5.0 (bagian HTML di balik `#if NET8_0_OR_GREATER` + reference kondisional)
- [x] Culture-invariant: tes mapper di bawah `id-ID` → `"8.2677in"`, `"0.5in"`
- [x] Deterministik (input + `CreationDate` sama → byte sama, versi browser sama)

**Keamanan**
- [x] Default: jaringan & `file://` diblokir, `..` traversal ditolak, skema lain (`chrome:` dll.) ditolak
- [x] Timeout keras + kill proses; tidak ada proses yatim; profil sementara dibersihkan
- [x] Argumen browser tetap (tidak dari input HTML); tidak ada shell
- [x] Tidak ada kode yang disalin; dependency PuppeteerSharp (MIT) dicatat di THIRD_PARTY_NOTICES

**API & kode**
- [x] Satu tipe publik per file, internal default, XML doc pada API publik
- [x] Pesan exception menyebut cara memperbaiki (path yang dicoba, opsi yang perlu dinyalakan)
- [x] Tidak ada tes yang di-skip diam-diam: tes integrasi hanya return bila browser tidak ditemukan

## Rencana test manual
1. Buka `out/net8.0/html-invoice.pdf` di **Chrome**: warna teal (band, badge, total), font, tabel rata kanan,
   footer "Halaman 1 dari 1".
2. Buka file yang sama di reader kedua (**Acrobat / SumatraPDF / Firefox**): tampilan sama, teks bisa diseleksi
   dan dicari ("Rp10.711.500").
3. Buka `out/net8.0/html-report.pdf` di kedua viewer: sampul LifeOS.Pdf + 4 halaman HTML, header tabel berulang,
   watermark DRAFT, bookmark "Isi laporan".
4. (Opsional, Linux/Docker) `apt install chromium`, jalankan tes dengan `--no-sandbox` di `ExtraArguments`.

## Hasil verifikasi otomatis
- **build:** `dotnet build -c Release` → 0 warning, 0 error
- **test:** `dotnet test -c Release` → 583/583 pass di net8.0, net9.0, net10.0 (34 tes HTML: 16 tanpa browser,
  18 dengan Edge 157)
- **validator:** `tools/validate_pdf.py out/net8.0/*.pdf` (tanpa `encrypted.pdf`) → 24/24 OK
- **pack:** `tools/release.ps1 -PackOnly` → 8 package versi 1.2.0, termasuk `LifeOS.Pdf.Html.1.2.0.nupkg`
- **benchmark:** `F11` dijalankan; angka ada di `docs/performance.md` § Phase 11
- **tutorial:** snippet bab 13 dikompilasi & dijalankan (first conversion, paper options, combine + encrypt,
  end-to-end invoice, opsi untrusted)
- **viewer:** belum (manual, lihat di atas)
