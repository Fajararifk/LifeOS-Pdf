# Design Fase 2: Layout Engine

## 1. API publik (namespace `LifeOS.Pdf.Layout`)

```csharp
var noto = FontFamily.FromFiles("NotoSans-Regular.ttf", bold: "NotoSans-Bold.ttf");

var doc = new FlowDocument();
doc.Info.Title = "Laporan Keuangan November 2026";
doc.Styles.Normal = new TextStyle { Family = FontFamily.Helvetica, Size = 10, Fallbacks = { noto } };
doc.Styles.Heading1.Text.Size = 20;

var section = doc.AddSection(new PageSetup { PageSize = PageSize.A4, Margins = new Margins(56) });
section.Header.AddParagraph("Laporan Keuangan").Format.Alignment = ParagraphAlignment.Right;
section.Footer.AddParagraph()
    .AddText("Halaman ").AddPageNumber().AddText(" dari ").AddPageCount()
    .Format.Alignment = ParagraphAlignment.Center;

section.AddHeading("Ringkasan", level: 1);
section.AddParagraph()
    .AddText("Total pengeluaran bulan ini ")
    .AddText("Rp1.924.500", new TextStyle { Bold = true, Color = PdfColor.Red })
    .AddText(", turun 12% dari bulan lalu.")
    .Format.Alignment = ParagraphAlignment.Justify;

var list = section.AddList(ListKind.Bullet);
list.AddItem("Makan");
list.AddItem("Transportasi");

var table = section.AddTable();
table.AddColumn(ColumnWidth.Auto);
table.AddColumn(ColumnWidth.Relative(1));
table.AddColumn(ColumnWidth.Fixed(90), ParagraphAlignment.Right);
table.HeaderRows = 1;
table.AlternateRowBackground = PdfColor.FromHex("#F3F4F6");
table.AddRow("Tanggal", "Catatan", "Nominal");
foreach (var t in transaksi)
    table.AddRow(t.Tanggal, t.Catatan, Rupiah(t.Nominal));

section.AddImage(PdfImage.FromFile("grafik.png"), width: 300, ParagraphAlignment.Center);

doc.Save("laporan.pdf");            // atau PdfDocument pdf = doc.Render();
```

| Tipe | Tanggung jawab |
|---|---|
| `FlowDocument` | `Info`, `Styles`, `Sections`, `AddSection`, `Render()`, `Save()` |
| `PageSetup`, `Margins` | kertas, margin, `Columns`, `ColumnGap`, `HeaderSpacing`, `FooterSpacing`, `DifferentFirstPage` |
| `Section : BlockContainer` | konten + `Header`, `Footer`, `FirstPageHeader`, `FirstPageFooter` |
| `BlockContainer` (abstrak) | `AddParagraph`, `AddHeading`, `AddList`, `AddTable`, `AddImage`, `AddSpacer`, `AddPageBreak`, `AddHorizontalLine` — dipakai `Section`, `HeaderFooter`, `TableCell` |
| `Paragraph` | `AddText`, `AddPageNumber`, `AddPageCount`, `AddLineBreak`, `Style`, `Format` |
| `ParagraphFormat` | `Alignment` (Left/Center/Right/Justify), `LineSpacing`, `SpaceBefore/After`, `LeftIndent`, `RightIndent`, `FirstLineIndent`, `KeepTogether`, `KeepWithNext`, `PageBreakBefore` |
| `TextStyle` | `Family`, `Fallbacks`, `Size`, `Bold`, `Italic`, `Underline`, `Strikethrough`, `Color`, `Kerning` (semua nullable = warisi) |
| `FontFamily` | `Helvetica`, `Times`, `Courier`, `FromFiles(...)`, `FromFaces(...)` |
| `DocumentStyles` | `Normal`, `Heading1..3` (`HeadingStyle`: `Text` + spasi) , `TableHeader` |
| `ListBlock` | `Kind` (Bullet/Numbered), `AddItem`, `AddList` (bertingkat) |
| `Table`, `TableColumn`, `TableRow`, `TableCell`, `ColumnWidth` | lihat FR-15..20 |
| `ImageBlock`, `LayoutException` | |

## 2. Desain internal

### 2.1 Teks: dari gaya ke glyph
```
Paragraph inlines ──resolve style──▶ StyledText (TextStyle lengkap)
  ──font fallback per code point──▶ segmen (PdfFont, string)
  ──tokenisasi break opportunity──▶ atom: Word | Space | Break | Field
  ──Map + kerning──▶ ukuran tiap atom (pt)
  ──greedy line breaking──▶ LineBox (atom per baris, lebar, ascent/descent)
  ──alignment/justify──▶ posisi x + extra space per spasi
  ──▶ DrawItem.Text (font, TextRun, penyesuaian per glyph)
```
- `TextRun` mendapat `Adjustments` (per glyph, 1/1000 em, + = lebih renggang) dan
  `BytesPerGlyph`. Kerning dihitung di `TrueTypeFontProgram.Map(text, kerning)`.
- Justify menambah penyesuaian pada glyph spasi; ditulis dengan operator **`TJ`**
  (`Tw` tidak berlaku untuk Identity-H, ISO 32000-1 §9.3.3).
- `PdfGraphics` mendapat metode internal `DrawGlyphs(font, run, extra, x, baseline, color)`
  yang menulis `[<…> n <…>] TJ`. API publik lama tetap memakai `Tj` tanpa kerning.
- Garis bawah/coret: posisi & tebal dari tabel `post` (TrueType) atau nilai Adobe
  (−100/50 per 1000) untuk font standar.

### 2.2 Kerning (`Fonts/TrueType/KerningTable.cs`)
- `kern` versi 0, subtable format 0 horizontal (bukan cross-stream/minimum).
- GPOS: semua lookup dari feature `kern` (tanpa seleksi script — cukup untuk Latin,
  Yunani, Sirilik), lookup type 2 (PairPos format 1 & 2) dan type 9 (Extension → 2).
  Nilai = `XAdvance` dari ValueRecord glyph pertama.
- Parsing malas (saat pertama dipakai), setiap pembacaan dicek batas; error apa pun
  → kerning font itu dinonaktifkan (NFR-05). Batas: ≤ 2.000 subtable, ≤ 1 juta pasangan.

### 2.3 Engine (`Layout/Engine/*`)
- Setiap blok memiliki **layouter** dengan dua operasi:
  - `Measure(width)` → tinggi minimum potongan pertama (untuk keep-with-next) dan
    lebar min/max konten (untuk kolom tabel otomatis).
  - `Layout(context, width, availableHeight)` → item yang muat + sisa (continuation)
    bila tidak muat semua. Paragraf dipotong per baris, tabel per baris/grup rowspan,
    daftar per item, gambar tidak dipotong.
- `FlowLayouter` mengisi kolom → kolom berikutnya → halaman baru, menangani
  `KeepWithNext` (rantai), `KeepTogether`, `PageBreakBefore`, `PageBreak`.
  Penjaga progres: pada halaman kosong, blok selalu dipaksa maju (dipecah atau
  `LayoutException`) sehingga tidak ada loop tak hingga.
- **Field jumlah halaman**: layout dijalankan, jumlah halaman dicatat, lalu layout
  diulang dengan angka sebenarnya bila lebar field berubah (maks. 3 iterasi).
  Nomor halaman di body diketahui saat layout.
- Header/footer di-layout per halaman saat render (field diisi angka asli). Tinggi
  header/footer yang dicadangkan diukur sekali dengan teks field "8888".
  Area body = margin dalam − tinggi header − spasi header − tinggi footer − spasi footer.

### 2.4 Tabel
- Lebar kolom: Fixed → Percent (dari lebar tabel) → sisa untuk Auto dan Relative.
  Auto: lebar preferensi = lebar maks isi (tanpa wrap) + padding, minimum = atom
  terlebar + padding. Bila semua Auto muat, sisa dibagi ke Relative (atau ke Auto
  secara proporsional bila tidak ada Relative). Bila tidak muat, Auto diinterpolasi
  antara minimum dan preferensi; bila minimum pun tidak muat, dibagi proporsional
  (kata dipotong per huruf). Fixed + Percent > lebar tabel → `LayoutException`.
- Tinggi baris = maks tinggi konten sel (+ padding); tinggi sel rowspan yang kurang
  ditambahkan ke baris terakhir yang di-span.
- Paginasi per **grup** (baris + semua baris yang terikat rowspan). Header diulang.
  Grup yang lebih tinggi dari area kosong (dikurangi header) → `LayoutException`.
- Gambar: latar (header, zebra, sel) → isi sel → garis dalam → garis luar, per
  potongan halaman.

## 3. Struktur file
```
src/LifeOS.Pdf/Layout/
  FlowDocument.cs  PageSetup.cs  Margins.cs  Section.cs  HeaderFooter.cs  BlockContainer.cs
  Block.cs  Paragraph.cs  ParagraphFormat.cs  ParagraphAlignment.cs  TextStyle.cs  FontFamily.cs
  DocumentStyles.cs  HeadingStyle.cs  ListBlock.cs  ListKind.cs  ImageBlock.cs  SpacerBlock.cs
  PageBreakBlock.cs  HorizontalLineBlock.cs  Table.cs  TableColumn.cs  TableRow.cs  TableCell.cs
  ColumnWidth.cs  VerticalAlignment.cs  LayoutException.cs
  Engine/ ResolvedStyle.cs  TextShaper.cs  LineBreaker.cs  ParagraphLayouter.cs  TableLayouter.cs
          FlowLayouter.cs  LayoutContext.cs  DrawItems.cs  Renderer.cs
src/LifeOS.Pdf/Fonts/TrueType/KerningTable.cs
```
Diubah: `IFontProgram`/`TextRun` (adjustments), `TrueTypeFontProgram` (kerning,
underline), `FontUsage` (glyph 0 tanpa ToUnicode), `PdfGraphics` (DrawGlyphs internal).

## 4. Kompatibilitas
Hanya API netstandard2.0. Tidak ada `record`/`init`; objek gaya memakai properti
nullable biasa.

## 5. Strategi test
- Kerning: font uji dengan GPOS `kern` dan dengan tabel `kern` (dibuat oleh
  `make_test_assets.py`); nilai dibandingkan dengan fontTools; data rusak → tanpa kerning.
- Line breaker: spasi, hyphen, SHY, ZWSP, NBSP, CJK, kata kepanjangan, `\n`.
- Paragraf: justify (baris terakhir tidak), indent, gaya campuran, fallback.
- Paginasi: jumlah halaman, keep-together/with-next, page break, kolom ganda,
  progres pada konten raksasa (`LayoutException`, bukan hang).
- Tabel: lebar kolom tiap jenis, colspan/rowspan, header berulang, zebra, overflow.
- Header/footer: "Halaman X dari Y" benar di setiap halaman, 9 → 10+ halaman.
- Sample `report.pdf` (laporan multi-halaman) → validator 5 engine + render visual.
- Performa: 1.500 baris tabel < 1 detik.

## 6. Risiko
- Layout engine adalah kode terbesar sejauh ini → mitigasi: layouter kecil per blok,
  test per fitur, validasi visual tiap halaman sample.
- Tanpa shaping, aksara kompleks tetap terbatas (sudah didokumentasikan).
