# Design Fase 6: Parser & Manipulasi

## 1. API publik
```csharp
// Membuka
var doc = PdfDocument.Open("in.pdf");                  // juga Open(Stream), Open(byte[])
var doc = PdfDocument.Open(bytes, new PdfReadOptions { MaxStreamSize = 64 << 20 });
doc.Pages[0].Width / Height / Rotation                 // ukuran MediaBox, putaran 0/90/180/270
doc.Info.Title; doc.Outline                            // dari file sumber

// Operasi halaman
doc.ImportPage(other.Pages[2]);                         // salin halaman dari dokumen lain
doc.ImportPages(other, PageRange.Parse("1-3,7-"));      // banyak sekaligus
doc.InsertPage(0, PageSize.A4);                        // halaman baru di posisi tertentu
doc.InsertImportedPage(0, other.Pages[0]);
doc.RemovePage(3); doc.RemovePage(page);
doc.MovePage(from: 5, to: 0);
page.Rotation = 90;                                    // juga untuk halaman baru

// Menggambar di atas halaman lama — API sama seperti halaman baru
page.Graphics.DrawText("RAHASIA", font, 100, 400);
page.AddLink(...); page.AddStamp(...);

// Halaman sebagai Form XObject
newPage.Graphics.DrawPage(source.Pages[0], x, y, width, height);   // n-up / kop surat

// Alat (namespace LifeOS.Pdf.Manipulation)
PdfMerger.Merge(paths) → PdfDocument;  PdfMerger.Merge(docs, bookmarkPerFile: true)
PdfSplitter.SplitEvery(doc, n) / SplitByRanges(doc, "1-3,4-") / SplitPages(doc) → List<PdfDocument>
PdfStamper.AddTextWatermark(doc, "DRAFT", new WatermarkOptions{...})
PdfStamper.AddImageWatermark(doc, image, options)
PdfStamper.AddPageNumbers(doc, "Halaman {page} dari {pages}", options)
PdfStamper.AddBatesNumbers(doc, "LIFEOS-", start: 1, digits: 6, options)
PdfLayout.NUp(doc, columns: 2, rows: 1, pageSize) → PdfDocument
PdfLayout.Booklet(doc) → PdfDocument (lipat tengah, urutan imposisi)
PdfLayout.Overlay(doc, letterhead.Pages[0], below: true)  // kop surat di belakang konten

new PdfSaveOptions { UseObjectStreams = true }          // xref stream + object stream (lebih kecil)
```
Exception: `PdfReadException` (file rusak/tidak didukung, dengan offset bila ada),
`PdfEncryptedException : PdfReadException`.

## 2. Desain internal
- **Parsing/** (internal): `Lexer` (byte[] + posisi; token angka, nama `#xx`, string literal
  dengan escape & oktal & kurung bersarang, hex, keyword, delimiter), `ObjectParser`
  (array/dict/referensi `n g R` dengan lookahead; kedalaman ≤ `MaxDepth`), `PdfParser`
  (header, `startxref` di 2 KB terakhir, xref tabel & stream, `/Prev` chain dengan set offset
  yang sudah dikunjungi, `/XRefStm`, cache objek, object stream di-cache per nomor, resolve
  dengan penjaga siklus), `XrefRebuilder` (pindai `\d+ \d+ obj`, ambil objek terakhir per nomor,
  cari trailer/Catalog), `StreamDecoder` (filter + batas ukuran; Flate toleran: data terpotong
  tetap dikembalikan sejauh terbaca).
- **Objek sumber**: `PdfSourceReference(num, gen, parser)` di model COS; `PdfStream` dari parser
  menyimpan data mentah (masih ter-encode).
- **Halaman import**: `PdfPage.Source` = parser + dictionary halaman + atribut warisan (Resources,
  MediaBox, CropBox, Rotate). Halaman yang di-import ke dokumen lain berbagi parser yang sama.
  Halaman dari dokumen yang bukan hasil `Open` (atau yang sudah digambari) di-*snapshot*:
  dokumen sumber di-`ToArray()` lalu di-`Open` sekali per panggilan `ImportPages`.
- **Penyalinan saat simpan** (`ObjectCopier`): peta (parser, num, gen) → objek output;
  antrean (bukan rekursi) sehingga rantai referensi panjang aman. Referensi ke halaman yang ikut
  disimpan → objek halaman output; ke halaman lain / node Pages / Catalog → `null`.
  `/Length` dihitung ulang; stream tanpa filter dikompres (kecuali `/Type /Metadata`) bila
  `CompressContent`. Hanya objek yang terjangkau dari halaman yang disimpan yang ikut → objek
  tak terpakai otomatis hilang.
- **Halaman output**: `/MediaBox` (+ CropBox/BleedBox/TrimBox/ArtBox), `/Rotate`, `/Resources`,
  `/Contents`, `/Annots` (lama + baru), `/Group`, `/UserUnit`, `/Tabs`. Tidak disalin:
  `/StructParents`, `/Thumb`, `/B`, `/AA`, `/PieceInfo`, `/Metadata`.
- **Overlay** (konten baru di halaman lama): konten baru dibungkus Form XObject sendiri (resource
  terpisah → tidak ada bentrok nama resource), `/Matrix` menggeser ke origin MediaBox.
  `/Contents` = `[q-stream, konten lama…, Q + "Do" overlay]` sehingga state grafis konten lama
  tidak bocor.
- **DrawPage**: halaman sumber → Form XObject (konten digabung & didekode, `/BBox` = CropBox,
  `/Resources` disalin, `/Group` disalin), diskalakan ke kotak tujuan; hasil di-cache per halaman.
- **Outline sumber**: dibaca (batas jumlah & kedalaman, siklus dijaga), tujuan `/Dest` atau
  `/A /S /GoTo` (eksplisit, named lewat `/Names /Dests` atau `/Dests` lama) diubah ke
  `PdfDestination` milik halaman import. Item yang tujuannya tidak ketemu tetap ada tanpa tujuan
  → ditulis ke halaman pertama.
- **Object stream writer**: objek non-stream (gen 0) dikelompokkan per 100 ke `/ObjStm`
  (Flate), xref stream `/W [1 4 2]` tanpa predictor.

## 3. Batas default `PdfReadOptions`
MaxDepth 100 (nesting langsung), MaxReferenceChain 64, MaxStreamSize 256 MB (setelah decode),
MaxObjects 8 388 607 (batas Annex C), MaxXrefSections 256, MaxPageTreeDepth 64, MaxPages 100 000,
MaxOutlineItems 100 000.

## 4. Struktur file
`Parsing/` Lexer, ObjectParser, PdfParser, XrefRebuilder, StreamDecoder, ObjectCopier,
SourcePage, OutlineReader · `PdfReadOptions`, `PdfReadException`, `PdfEncryptedException`,
`PageRange` · `Manipulation/` PdfMerger, PdfSplitter, PdfStamper, WatermarkOptions,
StampPosition, PdfLayout.

## 5. Strategi test
Unit: lexer (semua bentuk token & error), xref tabel/stream/predictor, object stream,
incremental (objek ditimpa), rebuild, filter (vektor uji LZW/A85/RunLength), batas (bom
dekompresi, nesting 10 000, rantai referensi, `/Prev` melingkar, pohon halaman melingkar) —
semua harus melempar `PdfReadException` cepat. Round-trip: dokumen kita → Open → Save →
PdfStructure + konten sama; file buatan pikepdf/qpdf (object stream, xref stream) sebagai test
asset; fuzz ringan (byte acak dibalik pada file valid, 500 iterasi: tidak boleh hang/crash selain
`PdfReadException`). Validator: hasil merge/split/watermark/n-up lolos 5 engine, teks sumber masih
bisa diekstrak pypdf.

## 6. Revisi saat implementasi
- Nama kelas alat: `PdfImposition` (bukan `PdfLayout`, supaya tidak tertukar dengan namespace
  `LifeOS.Pdf.Layout`). `MergeOptions` (BookmarkPerDocument, KeepBookmarks, ReadOptions),
  `StampOptions`, `WatermarkOptions`, `StampPosition`.
- Konten baru di halaman lama ditulis ke dua form XObject: `LOSUnderlay` (sebelum konten lama,
  dipakai kop surat & watermark `Behind`) dan `LOSOverlay` (sesudahnya, di dalam q … Q).
- Stempel/nomor halaman memakai *display frame* internal: `cm` yang memetakan koordinat halaman
  seperti ditampilkan (setelah /Rotate) ke ruang halaman, jadi posisi "bawah tengah" benar pada
  halaman yang diputar. API publik `PdfGraphics` tetap memakai ruang halaman tak diputar.
- Link internal di halaman sumber: tujuan bernama diubah jadi destinasi eksplisit saat disalin;
  link ke halaman yang tidak ikut disimpan dibuang. Aksi JavaScript/Launch/ImportData/SubmitForm/
  ResetForm/Rendition/Movie/Sound/GoToE dan `/AA` tidak disalin; rantai `/Next` dipotong.
- Named destination sumber tidak diimpor ke `NamedDestinations` (tidak perlu karena link sudah
  eksplisit). Outline sumber diimpor ke `PdfOutline`; `RemovePage` mengarahkan ulang bookmark.
- Batas pelanggaran (`PdfLimitException`, internal, turunan `PdfReadException`) tidak pernah
  "dipulihkan"; kerusakan sintaks hanya membuat satu objek jadi null atau memicu rebuild xref.
- Rebuild xref melewati badan objek (stream) supaya "n g obj" di dalam data stream tidak terbaca.
- `ImportPages`/`Merge`/`Split`/imposisi memakai satu *snapshot* (ToArray → Open) bila halaman
  sumber sudah digambari atau dibuat di memori.
- Object stream ditulis oleh `CompactSerializer` (100 objek/stream, `/W [1 4 2]`).
- Performa (lihat docs/performance.md) dikerjakan di fase ini juga, dengan syarat byte output fase
  0–5 tetap identik.
- Downscale gambar ditunda ke Fase 10 (perlu decoder JPEG); kompresi ulang stream tanpa filter dan
  object stream sudah ada.
