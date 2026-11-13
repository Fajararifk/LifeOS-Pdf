# Design Fase 0: Core Writer

## 1. API publik

```csharp
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;

var doc = new PdfDocument();
doc.Info.Title = "Laporan Bulanan";

var page = doc.AddPage(PageSize.A4);           // atau PageSize.Letter.Landscape
var g = page.Graphics;                          // origin kiri-atas, satuan point

g.DrawText("Halo dunia", new PdfFont(StandardFont.HelveticaBold, 24), 72, 72);
g.DrawTextBox(longText, new PdfFont(StandardFont.TimesRoman, 11), 72, 120, 450);
g.DrawRectangle(new PdfPen(PdfColor.Black, 1), PdfColor.FromHex("#E0F2F1"), 72, 300, 200, 80);
g.DrawImage(PdfImage.FromFile("foto.jpg"), 72, 400, 300);   // tinggi ikut rasio

doc.Save("laporan.pdf");                        // atau Save(stream) / ToArray()
```

| Tipe | Tanggung jawab |
|---|---|
| `PdfDocument` | daftar halaman, `Info`, `Save`/`ToArray` |
| `PdfPage` | ukuran + `Graphics` |
| `PageSize` | preset & custom (pt/mm/inch), `Landscape`/`Portrait` |
| `PdfDocumentInfo` | metadata |
| `PdfSaveOptions` | `CompressContent` |
| `PdfGraphics` | DrawLine, DrawRectangle, FillRectangle, DrawRoundedRectangle, DrawEllipse, DrawText, DrawTextBox, DrawImage |
| `PdfPen`, `PdfColor`, `TextAlign` | gaya gambar |
| `PdfFont`, `StandardFont` | font + `MeasureWidth`, `Ascent`, `LineHeight` |
| `PdfImage` | JPEG dari file/stream/bytes |

## 2. Desain internal
- **Model objek** (`Objects/PdfObjects.cs`, internal): Name, Integer, Real,
  Boolean, String (literal/hex), Array, Dictionary (berurutan → deterministik),
  Stream, IndirectObject, Reference. Akan dipublikasikan saat parser (Fase 6).
- **Content stream**: `PdfGraphics` menulis operator ke `StringBuilder`; setiap
  operasi dibungkus `q … Q`. Teks ditulis sebagai hex string `<…> Tj` agar tidak
  perlu escaping dan aman untuk byte 0x80–0xFF.
- **Koordinat**: y PDF = tinggi halaman − y. Untuk teks, y = atas baris;
  baseline = y + ascent.
- **Resource**: `ResourceRegistry` per dokumen memberi nama stabil (F1, Im1) dan
  memastikan satu objek per font/gambar. Tiap halaman hanya mencantumkan resource
  yang dipakainya.
- **Writer** (`DocumentWriter`): urutan objek = Catalog, Pages, font, image,
  (Page, Contents)×n, Info. Dibangun di `MemoryStream` → xref 20-byte per entri
  → /ID = MD5 isi file → trailer. Save ke file baru dilakukan setelah semua byte
  siap (tidak ada file setengah jadi).
- **Font**: 14 standard Type 1, WinAnsiEncoding dibuat manual (code page 1252
  tidak ada di .NET Core tanpa package). Lebar glyph dari AFM Adobe.
  Batasan: Times-Italic/BoldItalic memakai lebar tegak, Symbol/ZapfDingbats
  lebar rata-rata, kode 128–255 memakai lebar huruf dasarnya → hanya
  memengaruhi pengukuran/perataan, bukan tampilan. Diperbaiki di Fase 1.
- **JPEG**: parse marker sampai SOF untuk ukuran & jumlah komponen, deteksi
  APP14 "Adobe" untuk CMYK terbalik → `/Decode [1 0 1 0 1 0 1 0]`. Data
  disisipkan apa adanya (DCTDecode), tanpa re-encode.
- **Flate**: `DeflateStream` + header zlib manual + Adler-32 (ZLibStream tidak
  ada di netstandard2.0).

Alternatif yang ditolak: origin kiri-bawah ala PDF mentah (membingungkan untuk
laporan/UI); System.Drawing untuk gambar (tidak cross-platform); xref stream
(belum perlu, dan xref klasik paling kompatibel).

## 3. Struktur file
```
src/LifeOS.Pdf/
  PdfDocument.cs  PdfPage.cs  PageSize.cs  PdfDocumentInfo.cs  PdfSaveOptions.cs
  Drawing/  PdfGraphics.cs PdfPen.cs PdfColor.cs TextAlign.cs
  Fonts/    PdfFont.cs StandardFont.cs StandardFontMetrics.cs WinAnsiEncoding.cs
  Images/   PdfImage.cs
  Objects/  PdfObjects.cs
  Internal/ DocumentWriter.cs PdfOutput.cs PdfFormat.cs ResourceRegistry.cs Zlib.cs
tests/LifeOS.Pdf.Tests/   PdfStructure.cs + *Tests.cs + TestAssets/
samples/LifeOS.Pdf.Samples/ Program.cs + Assets/
tools/validate_pdf.py
```

## 4. Kompatibilitas target framework
`Directory.Build.props` mendefinisikan daftar target sekali untuk semua project.
net10.0 ditambahkan otomatis bila SDK ≥ 10 (dibaca dari nama folder SDK karena
`NETCoreSdkVersion` belum tersedia saat props dievaluasi). Tidak memakai API
modern; semua kode identik di setiap target.

## 5. Strategi test
- `PdfStructure.Verify`: validator independen (tidak memakai kode writer) untuk
  header, startxref, setiap offset xref, /Size, /ID, /Length stream, referensi.
- Test per fitur: koordinat, alignment, metrik, wrap, dedup resource,
  deterministik, culture id-ID, JPEG gray/RGB/CMYK, input invalid.
- Eksternal: sample dijalankan di net5–net10 → `validate_pdf.py` (qpdf, pypdf,
  PDFium render tidak blank) → cek visual PNG + viewer.

## 6. Risiko
- Metrik perkiraan untuk sebagian font (lihat di atas).
- WinAnsi tidak mencakup aksara non-Latin → menunggu Fase 1.
