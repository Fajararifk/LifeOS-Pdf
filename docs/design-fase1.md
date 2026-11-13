# Design Fase 1: Font, Gambar & Image→PDF

## 1. API publik

### Font
```csharp
// Muat sekali, pakai di banyak ukuran & halaman.
PdfFontFace noto  = PdfFontFace.FromFile("NotoSans-Regular.ttf");
PdfFontFace mincho = PdfFontFace.FromFile(@"C:\Windows\Fonts\msgothic.ttc", collectionIndex: 0);
PdfFontFace helv  = PdfFontFace.Standard(StandardFont.Helvetica);

var judul = new PdfFont(noto, 18);
var body  = new PdfFont(StandardFont.TimesRoman, 11);    // konstruktor Fase 0 tetap ada

g.DrawText("Tiếng Việt · Ελληνικά · Русский · Rp1.350.000", judul, 72, 72);
if (!noto.HasGlyph("中")) { /* pilih font lain */ }
```

```csharp
public sealed class PdfFontFace
{
    public static PdfFontFace Standard(StandardFont font);           // instance di-cache, aman dibandingkan
    public static PdfFontFace FromFile(string path, int collectionIndex = 0);
    public static PdfFontFace FromStream(Stream stream, int collectionIndex = 0);
    public static PdfFontFace FromBytes(byte[] data, int collectionIndex = 0);

    public string PostScriptName { get; }
    public string FamilyName { get; }
    public bool IsBold { get; }
    public bool IsItalic { get; }
    public bool IsEmbedded { get; }        // false untuk 14 font standar
    public int GlyphCount { get; }
    public bool HasGlyph(int codePoint);
    public bool HasGlyph(string text);     // true bila SEMUA karakter punya glyph
}

public sealed class PdfFont
{
    public PdfFont(StandardFont family, double size);   // tetap (Fase 0)
    public PdfFont(PdfFontFace face, double size);      // baru
    public PdfFontFace Face { get; }                    // baru
    // Size, Ascent, Descent, LineHeight, MeasureWidth, WithSize — tetap
}
```

**Breaking change (dicatat di CHANGELOG):** `PdfFont.Family` (tipe `StandardFont`)
dihapus, diganti `PdfFont.Face`. Alasan: font tertanam tidak punya nilai
`StandardFont`; mempertahankan properti yang melempar exception untuk font
tertanam lebih buruk daripada satu perubahan bersih di v0.x.

### Gambar
```csharp
var logo = PdfImage.FromFile("logo.png");      // JPEG atau PNG, dideteksi dari isi
logo.Format;            // PdfImageFormat.Jpeg | Png
logo.HasAlpha;          // true untuk PNG dengan alpha / tRNS
logo.DpiX; logo.DpiY;   // dari JFIF / pHYs; null bila tidak ada
```

### Konversi gambar → PDF
```csharp
PdfDocument doc = ImageToPdf.Convert(new[] { "scan1.jpg", "scan2.png" }, new ImageToPdfOptions
{
    PageSize = PageSize.A4,          // null = halaman seukuran gambar (pakai DPI)
    Margin = 36,                     // point
    Fit = ImageFit.Contain,          // Contain | Cover
    AutoRotatePage = true,           // landscape untuk gambar melebar
    DefaultDpi = 96,                 // bila gambar tidak punya info DPI
});
doc.Save("hasil.pdf");
```
Namespace `LifeOS.Pdf.Conversion`: `ImageToPdf`, `ImageToPdfOptions`, `ImageFit`.

### Transformasi & opacity
```csharp
using (g.Save())                       // state dipulihkan saat Dispose
{
    g.RotateAt(-30, 300, 400);         // derajat, positif = searah jarum jam di layar
    g.SetOpacity(0.15);                // fill & stroke
    g.DrawText("DRAFT", new PdfFont(StandardFont.HelveticaBold, 96), 300, 400, align: TextAlign.Center);
}
g.Translate(dx, dy); g.ScaleAt(sx, sy, x, y); g.SetOpacity(fill: 0.5, stroke: 1);
```
`Save()` mengembalikan `PdfGraphicsState : IDisposable`; `Restore()` juga tersedia.
Nesting > 28 atau `Restore` tanpa `Save` → `InvalidOperationException`.
State yang masih terbuka saat `doc.Save()` → `InvalidOperationException`
dengan pesan "call Restore() / dispose the state returned by Save()".

## 2. Desain internal

### 2.1 Font: lapisan
```
Fonts/
  PdfFontFace.cs            publik; membungkus salah satu implementasi internal
  IFontProgram.cs           internal: GlyphId(codePoint), AdvanceWidth(gid), Ascent… (1/1000 em)
  Standard/StandardFontProgram.cs     + StandardFontMetrics.Generated.cs (AFM)
  TrueType/TrueTypeReader.cs          pembaca big-endian dengan cek batas
  TrueType/TrueTypeFont.cs            tabel: head hhea maxp hmtx loca glyf cmap OS/2 name post
  TrueType/CmapTable.cs               format 0, 4, 6, 12; prioritas (3,10) > (0,4/6) > (3,1) > (0,3) > (3,0 symbol)
  TrueType/TrueTypeSubsetter.cs
  Embedding/EmbeddedFontWriter.cs     Type0 + CIDFontType2 + FontDescriptor + FontFile2 + W + ToUnicode
  Embedding/ToUnicodeCMap.cs
```

### 2.2 Encoding teks
- Font standar: seperti Fase 0 (WinAnsi, 1 byte per karakter).
- Font tertanam: **Identity-H**, 2 byte per glyph = glyph ID (GID). String .NET
  diiterasi per code point (surrogate pair digabung; surrogate rusak → U+FFFD).
  `CIDToGIDMap /Identity`, jadi CID = GID — tidak perlu tabel pemetaan.
- Setiap `DrawText` mencatat (GID → code point) ke `FontUsage` per dokumen. Jika
  satu GID dipakai untuk beberapa code point, yang pertama menang (deterministik).

### 2.3 Subsetting (TrueType)
- **GID dipertahankan** (glyph tak terpakai dikosongkan: entri `loca` panjang 0),
  sehingga Identity-H tetap valid dan konten tidak perlu ditulis ulang.
  `hmtx` dan `glyf` untuk GID tak terpakai dikosongkan; `numGlyphs` tetap.
- Glyph komposit: komponennya ikut dimasukkan (rekursif, batas kedalaman 16,
  deteksi siklus).
- Tabel yang ditulis: `head` (checkSumAdjustment dihitung ulang), `hhea`, `maxp`,
  `loca` (format dipilih ulang: short bila muat), `glyf`, `hmtx`, `cvt `, `fpgm`,
  `prep`, `OS/2`, `name`, `post` (versi 3, tanpa nama glyph), dan **`cmap` baru**
  berisi hanya karakter yang dipakai (format 4, plus format 12 bila ada karakter
  di atas U+FFFF). `GSUB`, `GPOS`, `kern`, `hdmx`, `VDMX`, `LTSH` dibuang.
  > **Revisi saat implementasi:** desain awal membuang `cmap` (PDF tidak memakainya
  > untuk CIDFontType2). Validasi dengan OTS — sanitizer font di Chrome/Firefox —
  > menolak font tanpa `cmap` karena spesifikasi OpenType mewajibkannya, jadi subset
  > sekarang selalu menyertakan `cmap` kecil. Ukuran tambahan < 100 byte.
- `fsType`: bit 0x0002 (restricted) → tolak; 0x0100 (no subsetting) → tanam
  utuh; 0x0200 (bitmap only) → tolak (Fase 1 tidak menanam bitmap).
- Tag subset 6 huruf A–Z dari hash MD5 daftar GID terpakai + nama PostScript
  → deterministik, berbeda bila glyph set berbeda.

### 2.4 Objek PDF per font tertanam (sekali per dokumen)
```
Type0 font:   /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+NotoSans-Regular
              /Encoding /Identity-H /DescendantFonts [CIDFont] /ToUnicode CMap
CIDFont:      /Subtype /CIDFontType2 /CIDSystemInfo <</Registry (Adobe) /Ordering (Identity) /Supplement 0>>
              /FontDescriptor … /DW 1000 /W [gid [w] …] /CIDToGIDMap /Identity
Descriptor:   /FontName /Flags (4 Symbolic + Italic/FixedPitch bila perlu) /FontBBox /ItalicAngle
              /Ascent /Descent /CapHeight /StemV /FontFile2 (FlateDecode, /Length1)
ToUnicode:    CMap dengan beginbfchar per GID → UTF-16BE (pasangan surrogate untuk > U+FFFF)
```
Lebar `W` hanya untuk GID terpakai, dikelompokkan berurutan supaya ringkas.

### 2.5 PNG
```
Images/Png/PngDecoder.cs   chunk + CRC-32, IHDR/PLTE/tRNS/IDAT/pHYs/IEND, filter 0–4, Adam7
Images/Png/Crc32.cs
Images/PdfImage.cs         deteksi format; menghasilkan 1 atau 2 stream (gambar + SMask)
```
- **Jalur cepat** (tanpa alpha, tanpa tRNS, tanpa interlace): data IDAT
  digabung apa adanya → `/Filter /FlateDecode /DecodeParms << /Predictor 15
  /Colors n /BitsPerComponent b /Columns w >>`. Palet → `/ColorSpace [/Indexed /DeviceRGB n <hex>]`.
- **Jalur decode** (alpha, tRNS, interlace): decode ke baris piksel, pisahkan
  warna & alpha, kompres ulang keduanya dengan Flate; alpha → stream `/SMask`
  (`/ColorSpace /DeviceGray`). Bit depth 16 dipertahankan untuk warna, alpha
  diturunkan ke 8 bit.
- Batas: ≤ 30.000 px per sisi, ≤ 200 juta piksel, ukuran data hasil inflate
  dihitung dari IHDR dan dicek sebelum alokasi (bom dekompresi).
- `Zlib.Decompress` diberi batas ukuran output.

### 2.6 Grafis
- Stack state di `PdfGraphics` (kedalaman, opacity aktif). Semua transformasi
  ditulis sebagai `cm` di ruang PDF:
  - `Translate(dx, dy)` → `1 0 0 1 dx -dy cm`
  - `RotateAt(θ, x, y)` → translate(px, py) · rotate(−θ) · translate(−px, −py),
    dengan (px, py) = (x, H − y). Rotasi negatif di ruang PDF = searah jarum jam di layar.
  - `ScaleAt(sx, sy, x, y)` → analog, skala tidak terpengaruh flip sumbu y.
- Opacity → ExtGState `<< /Type /ExtGState /CA s /ca f >>`, satu objek per
  pasangan nilai (dibulatkan 4 desimal) per dokumen, nama `GS1…` di registry.
- `ResourceRegistry` diperluas: font berdasarkan `PdfFontFace` (identitas
  objek; face standar di-cache sehingga satu per `StandardFont`), ExtGState,
  gambar (gambar PNG beralpha → 2 objek).

### 2.7 Metrik AFM
`tools/generate_afm_metrics.py` (hanya dijalankan developer): membaca 14 file AFM
Adobe Core14, memetakan kode WinAnsi → nama glyph (Adobe Glyph List via
fontTools) dan menulis `StandardFontMetrics.Generated.cs` (lebar 32–255, ascent,
descent, cap height, bbox). File hasil generate di-commit; AFM mentah tidak.

## 3. Struktur file
Baru:
```
src/LifeOS.Pdf/Fonts/PdfFontFace.cs, IFontProgram.cs, FontUsage.cs
src/LifeOS.Pdf/Fonts/Standard/StandardFontProgram.cs, StandardFontMetrics.Generated.cs
src/LifeOS.Pdf/Fonts/TrueType/TrueTypeReader.cs, TrueTypeFont.cs, CmapTable.cs, TrueTypeSubsetter.cs
src/LifeOS.Pdf/Fonts/Embedding/EmbeddedFontWriter.cs, ToUnicodeCMap.cs
src/LifeOS.Pdf/Images/PdfImageFormat.cs, Png/PngDecoder.cs, Png/Crc32.cs, Jpeg/JpegInfo.cs
src/LifeOS.Pdf/Conversion/ImageToPdf.cs, ImageToPdfOptions.cs, ImageFit.cs
src/LifeOS.Pdf/Drawing/PdfGraphicsState.cs
tools/generate_afm_metrics.py, tools/make_test_fonts.py
tests/…/FontTests.cs, TrueTypeTests.cs, PngTests.cs, ConversionTests.cs, TransformTests.cs
tests/…/TestAssets/fonts/NotoSans-subset.ttf, NotoSansMath-astral.ttf, test.ttc, OFL.txt
tests/…/TestAssets/png/*.png (suite dihasilkan Pillow: semua color type × bit depth, interlace, tRNS)
samples/…/Assets/NotoSans-Regular.ttf + OFL.txt
CHANGELOG.md
```
Diubah: `PdfFont.cs`, `StandardFontMetrics.cs` (diganti generated), `PdfImage.cs`,
`PdfGraphics.cs`, `PdfPage.cs`, `ResourceRegistry.cs`, `DocumentWriter.cs`,
`Zlib.cs` (batas output), `THIRD_PARTY_NOTICES.md` (Noto/OFL, AFM), sample (halaman 3: Unicode, PNG, transform).

## 4. Kompatibilitas target framework
Tidak ada API baru dari .NET modern. `BinaryPrimitives` tidak tersedia di
netstandard2.0 → pembaca big-endian ditulis manual. `MD5` dan `DeflateStream`
tersedia di semua target. Tidak ada `Span` di API publik.

## 5. Strategi test
- **TrueType**: parse Noto subset (jumlah glyph, unitsPerEm, nama), cmap 4 & 12
  (`NotoSansMath-astral` berisi glyph U+1D400+), lebar sesuai nilai fontTools,
  TTC index 0/1, `fsType` restricted (byte di-patch di test) → ditolak, font
  rusak (dipotong di 20 titik acak berbiji tetap) → hanya `FormatException`.
- **Subset**: hasil subset di-parse ulang oleh `TrueTypeFont` kita **dan** oleh
  fontTools (via `validate_pdf.py --check-fonts`), glyph komposit ikut, checksum benar.
- **PDF**: `PdfStructure.Verify` + struktur Type0/CIDFont/ToUnicode; satu
  objek font untuk 100 halaman; deterministik; teks campuran standar+tertanam.
- **Ekstraksi**: pypdf mengembalikan teks Unicode persis (Indonesia, Vietnam,
  Yunani, Sirilik, U+1D400) — ditambahkan ke `validate_pdf.py --expect-text`.
- **PNG**: suite gambar dari Pillow; hasil decode kita dibandingkan piksel per
  piksel dengan Pillow (via PNG referensi mentah `.rgba` di TestAssets); CRC salah
  & bom dekompresi → ditolak; render PDFium halaman beralpha dibandingkan
  dengan komposit Pillow di atas putih (toleransi ±2 per kanal).
- **Transform/opacity**: operator `cm`, ExtGState dedup, error nesting.
- **Konversi**: jumlah halaman, orientasi, ukuran halaman dari DPI.
- **Manual**: Chrome + Adobe Reader/SumatraPDF: copy-paste teks Vietnam &
  Jepang (MS Gothic, lokal), PNG transparan di atas latar berwarna, watermark miring.

## 6. Risiko
- Subsetting yang salah membuat font tidak tampil di beberapa viewer meski lolos
  qpdf → mitigasi: validasi fontTools + render PDFium + cek manual 2 viewer.
- Font Windows tidak boleh di-commit → uji CJK hanya lokal; CI memakai Noto.
- Tanpa shaping, aksara Arab/India tampil tidak tersambung → didokumentasikan sebagai batasan.
- Scope Fase 1 besar; bila perlu dipecah: **1a** (font + AFM) dan **1b** (PNG, konversi, transform).
