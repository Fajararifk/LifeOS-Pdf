# LifeOS.Pdf — Project Rules

Library PDF open-source untuk .NET, ditulis dari nol dengan C#. Target: semua
yang biasa dilakukan PDFsharp, iText, dan ByteScout (buat, baca, edit, merge,
form, tanda tangan, barcode/QR) — dibangun bertahap per fase. Repo terpisah dari
LifeOS, tapi memakai alur kerja yang sama (analyst → architect → developer → qa).

## Stack (final, jangan diganti tanpa persetujuan)
- C# `LangVersion latest`, `Nullable enable`, `TreatWarningsAsErrors`
- Target library: `netstandard2.0; net5.0; net6.0; net7.0; net8.0; net9.0; net10.0`
  (net10.0 otomatis ikut saat build pakai SDK 10, lihat `Directory.Build.props`)
- Test: xUnit (net8.0, net9.0, net10.0)
- Validasi eksternal: `tools/validate_pdf.py` (qpdf/pikepdf + pypdf + PDFium)

## Arsitektur
- `src/LifeOS.Pdf/` — package inti, **tanpa dependency apa pun**
  - `Objects/` — model objek PDF (COS): Name, String, Array, Dictionary, Stream, Reference
  - `Internal/` — writer, format angka, zlib, registry resource
  - `Fonts/`, `Images/`, `Drawing/` — API publik
  - `PdfDocument`, `PdfPage`, `PageSize` — titik masuk utama
- Modul yang butuh library pihak ketiga = package terpisah
  `src/LifeOS.Pdf.<Modul>/` (contoh: `LifeOS.Pdf.Barcode.Scanning` pakai ZXing.Net).
  Package inti TIDAK boleh ikut membawa dependency itu.
- `tests/LifeOS.Pdf.Tests/` — unit test; `PdfStructure` = validator struktur independen
- `samples/LifeOS.Pdf.Samples/` — showcase, dijalankan di semua runtime
- Dokumen fase di `docs/` (lihat Workflow)

## Aturan non-negotiable
1. **PDF WAJIB VALID.** Setiap fitur baru harus lolos: unit test (termasuk
   `PdfStructure.Verify`), `tools/validate_pdf.py` (qpdf + pypdf + PDFium), dan
   dicek manual di Chrome + minimal satu reader lain (Acrobat/Edge/Firefox/SumatraPDF)
   sebelum fase ditutup. Lebih baik melempar exception daripada menulis PDF rusak.
2. **SEMUA RUNTIME.** API harus jalan di netstandard2.0 (baseline). API .NET modern
   (Span, ZLibStream, `init`, `record`) hanya boleh di balik `#if NET6_0_OR_GREATER`
   dengan fallback yang hasil outputnya sama.
3. **CULTURE-INVARIANT.** Angka di PDF selalu lewat `PdfFormat.Number`. Dilarang
   `ToString()` tanpa `CultureInfo.InvariantCulture` (id-ID pakai koma → PDF rusak).
4. **KOORDINAT.** API publik: titik asal kiri-atas, satuan point (1/72 inch).
   Konversi ke sistem PDF (kiri-bawah) HANYA di `PdfGraphics`.
5. **DETERMINISTIK.** Input sama → byte output sama (tidak ada GUID/waktu acak
   kecuali `CreationDate` default). /ID diturunkan dari isi file.
6. **LISENSI.** Sumber kebenaran = spesifikasi ISO 32000. Dilarang menyalin kode
   dari iText (AGPL), ByteScout, Aspose, atau library proprietary lain. Kode
   MIT/Apache (PDFsharp, PdfPig, ZXing.Net) boleh dipelajari/dipakai dengan
   atribusi di `THIRD_PARTY_NOTICES.md`.
7. **INPUT TIDAK TERPERCAYA** (mulai Fase 6/parser): batasi kedalaman rekursi,
   ukuran stream, jumlah objek; tidak boleh infinite loop atau OOM pada file rusak.
8. **API PUBLIK** stabil: perubahan breaking wajib dicatat di design fase dan
   CHANGELOG. Pesan exception jelas dan menyebut cara memperbaikinya.

## Gaya kode
- Satu tipe publik per file, namespace mengikuti folder.
- Tipe internal default; publikasikan hanya yang memang API.
- Komentar menjelaskan *kenapa* (sering merujuk pasal ISO 32000, mis. `§7.5.4`).
- Tanpa dependency baru di package inti. Dependency di modul lain wajib disebut di plan.

## Workflow tim (subagents di .claude/agents/)
analyst → architect → developer (plan dulu, tunggu approval) → qa (read-only).
Output tiap tahap: `docs/requirement-faseN.md`, `docs/design-faseN.md`,
`docs/qa-faseN.md`. Fase berikutnya tidak boleh mulai sebelum QA fase sebelumnya
PASS. Playbook dan daftar fase: `docs/PHASES.md`.

## Verifikasi
Setelah perubahan kode:
```
dotnet build -c Release
dotnet test -c Release
```
Sebelum tutup fase (butuh `pip install pikepdf pypdf pypdfium2`):
```
dotnet run --project samples/LifeOS.Pdf.Samples -f net8.0 -- out/net8.0
python tools/validate_pdf.py out/*/showcase.pdf --render-dir out/png
```
lalu buka `out/net8.0/showcase.pdf` di Chrome dan satu PDF reader lain.
