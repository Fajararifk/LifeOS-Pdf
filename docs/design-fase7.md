# Design Fase 7: Ekstraksi Konten

## 1. API publik (namespace `LifeOS.Pdf.Extraction`)
```csharp
var page = PdfDocument.Open("mutasi.pdf").Pages[0];
string text = page.ExtractText();                                  // urutan baca
string layout = page.ExtractText(new TextExtractionOptions { Layout = true, Area = new PdfRect(0, 100, 600, 300) });
IReadOnlyList<TextLine> lines = page.GetTextLines();               // Text, Bounds, Words
IReadOnlyList<TextWord> words = page.GetWords();                   // Text, Bounds, FontName, FontSize, Bold, Italic
IReadOnlyList<TextGlyph> glyphs = page.GetGlyphs();
IReadOnlyList<SearchResult> hits = doc.Search("Rp1.924.500", ignoreCase: true); // PageIndex, Bounds (per baris)
IReadOnlyList<ExtractedImage> images = page.GetImages();           // Width, Height, Bounds, Format, GetBytes(), Save(path)
IReadOnlyList<ExtractedTable> tables = page.ExtractTables();       // Rows, Bounds, ToCsv(), ToJson()
string all = PdfTextConverter.ToText(doc);  string html = PdfTextConverter.ToHtml(doc);
PdfRedactor.Redact(page, new PdfRect(72, 100, 200, 14));           // + RedactText(doc, "NIK 3174…")
```
`PdfRect` (struct, publik di namespace `LifeOS.Pdf`): X, Y, Width, Height (kiri-atas), Right, Bottom,
Intersects, Contains, Union.

## 2. Desain internal (`Extraction/Engine/`)
- `ContentReader`: token → operasi (operand + operator), inline image `BI … ID <data> EI`
  (panjang dari /L atau pencarian `EI` yang diikuti whitespace/delimiter).
- `ContentInterpreter`: state grafis (CTM, warna isi tidak dilacak kecuali untuk stroke tabel),
  state teks (§9.3), stack q/Q (maks 64), form XObject rekursif (maks kedalaman 12, deteksi siklus,
  batas total 2 juta operasi), memancarkan `GlyphEvent` (kode, unicode, matriks, lebar), `ImageEvent`
  (objek, CTM), `PathSegment` (garis horizontal/vertikal & persegi tipis untuk tabel).
- `FontReader` (cache per dictionary font): `Decode(bytes) → (code, length)`, `Unicode(code)`,
  `Width(code)` (satuan glyph/1000). Sumber unicode: ToUnicode → encoding (+Differences → AGL /
  uniXXXX / uXXXX / gNN) → kode itu sendiri. Tabel AGL disimpan sebagai embedded resource terkompres.
- `PageGeometry`: device space → koordinat tampilan kiri-atas (CropBox, /Rotate).
- `TextAssembler`: glyph → kata (jarak > 0.15 em atau spasi) → baris (baseline ± 0.3 em, arah sama)
  → blok/kolom (celah horizontal besar & konsisten). Dobel (posisi + glyph sama) dibuang.
- `TableDetector`: *lattice* — garis H/V digabung (toleransi 2 pt), grid dari perpotongan, sel →
  kata; *stream* — blok baris berturut-turut, kolom dari celah vertikal yang kosong di ≥ 60% baris.
- `ImageDecoder`: bytes mentah → RGB/Gray/RGBA 8-bit → PNG (pakai `PngWriter`), JPEG/JPX apa adanya.
- `Redactor`: interpreter mode tulis — operasi teks dengan glyph di area ditulis ulang sebagai TJ
  (glyph dibuang diganti angka penyesuaian sehingga sisa teks tidak bergeser), `Do` gambar yang
  bersinggungan dihapus, form XObject yang terkena disalin & diredaksi rekursif (dengan nama
  resource baru), lalu kotak penutup digambar di overlay. Halaman menyimpan `ContentOverride`
  (bytes + resource tambahan) yang dipakai `ImportedPageWriter`.

## 3. Struktur file
`PdfRect.cs` · `Extraction/` TextExtractionOptions, TextGlyph, TextWord, TextLine, SearchResult,
ExtractedImage, ExtractedImageFormat, ExtractedTable, TableExtractionOptions, PdfTextConverter,
PdfRedactor, PageExtensions (method di PdfPage/PdfDocument) · `Extraction/Engine/` ContentReader,
ContentInterpreter, FontReader, CMapParser, GlyphList (+ `Resources/glyphlist.txt.gz`),
Encodings, PageGeometry, TextAssembler, TableDetector, ImageDecoder, Redactor.

## 4. Strategi test
Round-trip dokumen kita (teks/posisi diketahui persis), file qpdf, PDF buatan pikepdf dengan font
encoding custom (Differences, ToUnicode, CID), dibanding pypdf/pdfium di skrip validator
(`--expect-extract`), tabel dari sample laporan & mutasi → CSV, gambar PNG/JPEG round-trip
(piksel sama), redaksi: teks hilang dari hasil ekstraksi kita **dan** pypdf/pdfium.

## 5. Revisi saat implementasi
- Redaksi juga menghapus **bentuk vektor**: interpreter mencatat subpath yang dicat (re, m…l…h,
  kurva) beserta kotaknya; subpath yang menyentuh area dibuang, operator cat ikut dibuang bila
  path-nya habis. Path kliping tidak disentuh. Garis yang melintasi area hilang seluruhnya
  (redaksi berlebih lebih aman daripada bocor). Ditemukan karena QR (vektor) masih terbaca zxing.
- XObject yang diganti/dihapus redaksi **tidak disalin sama sekali** (sebelumnya objek asli ikut
  tertulis sebagai objek yatim — teks rahasia masih ada di file). Form diganti dengan nama yang
  sama, gambar yang tidak dipakai lagi dihapus dari resource halaman.
- `RedactText` juga membersihkan metadata (Title/Author/Subject/Keywords/Creator) dan judul bookmark
  (`RedactionOptions.ScrubMetadata`, default true) — ditemukan karena /Subject invoice memuat nama.
- `ObjectCopier`: stream yang bukan nilai utuh objek indirect (form hasil redaksi) otomatis diberi
  objek indirect sendiri.
- Halaman yang sudah digambari diredaksi dari snapshot (`PdfPage.AdoptSource`), jadi gambar/teks
  yang ditambahkan di memori ikut teredaksi.
- Tabel *stream* yang kolom pertamanya hanya penanda list (•, 1., a)) diabaikan (bukan tabel).
- Deduplikasi teks tebal palsu dibatasi 0.06 em (sebelumnya "ll" ikut terhapus).
- `PngWriter.Write` mendukung gray, gray+alpha, RGB, RGBA (keluaran RGB tetap identik).
- Koordinat ekstrem/NaN dari file rusak di-clamp (tidak melempar ArgumentException).
