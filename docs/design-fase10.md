# Design Fase 10: Render, Scan & OCR

**Menyusul requirement-fase10.md.** Keputusan (disetujui user):
1. Urutan Rendering → Barcode.Scanning → Ocr; gabungan `DocumentScanner`.
2. Binding PDFium = **PdfiumCore** (MIT, P/Invoke).
3. Tessdata = **path** (file `eng.traineddata` + `ind.traineddata` disediakan/diunduh AI).
4. Target framework = **semua yang didukung binding** (netstandard2.0 + net5–net10 bila memungkinkan).
5. Format barcode minimal = QR + Code 128/39 + EAN/UPC + ITF.
6. Pipeline = class **`DocumentScanner`** gabungan.

## 1. API publik

### `LifeOS.Pdf.Rendering` (PdfiumCore)
```csharp
public sealed class PdfRenderer
{
    public PdfRenderer();                       // memuat native PDFium (throws kalau binary hilang)
    public RenderedImage Render(byte[] pdf, int pageIndex, RenderOptions options);
    public IReadOnlyList<RenderedImage> RenderAll(byte[] pdf, RenderOptions options);
    public RenderedImage Render(PdfDocument document, int pageIndex, RenderOptions options); // via ToArray()
}
public sealed class RenderOptions
{
    public int Dpi { get; set; } = 150;          // 72/150/300
    public ImageFormat Format { get; set; } = ImageFormat.Png;
}
public enum ImageFormat { Png, Jpeg }
public sealed class RenderedImage { public int PageIndex; public int Width, Height; public byte[] Data; }
```

### `LifeOS.Pdf.Barcode.Scanning` (ZXing.Net)
```csharp
public sealed class BarcodeReader
{
    public BarcodeReadResult? Read(byte[] image);                     // PNG/JPEG
    public IReadOnlyList<BarcodeReadResult> ReadAll(byte[] image);
    public IReadOnlyList<PageBarcodeResult> ReadPdf(PdfDocument document);   // dari gambar Fase 7 + render
    public IReadOnlyList<DocumentSplit> SplitOnSeparator(PdfDocument document, string marker); // batch
}
public sealed class BarcodeReadResult { public string Text; public BarcodeFormat Format; }
public enum BarcodeFormat { QrCode, Code128, Code39, Ean13, Ean8, UpcA, Itf, Other }
```

### `LifeOS.Pdf.Ocr` (Tesseract)
```csharp
public sealed class OcrEngine : IDisposable
{
    public OcrEngine(string tessDataPath, string language = "ind+eng");
    public OcrPageResult Recognize(byte[] image);
    public string RecognizeText(byte[] image);
    public byte[] MakeSearchable(PdfDocument document);   // PDF asli + lapisan teks tak terlihat
}
public sealed class OcrPageResult { public string Text; public IReadOnlyList<OcrWord> Words; }
public sealed class OcrWord { public string Text; public double X, Y, W, H; }
```

### `LifeOS.Pdf.Scanning` (gabungan)
```csharp
public sealed class DocumentScanner : IDisposable
{
    public DocumentScanner(string tessDataPath, string language = "ind+eng");
    public ScannedPage ScanPage(PdfDocument document, int pageIndex, ScanOptions options);
}
public sealed class ScanOptions { public int Dpi = 200; public bool Barcode = true; public bool Ocr = true; }
public sealed class ScannedPage { public string Text; public IReadOnlyList<BarcodeReadResult> Barcodes; }
```

## 2. Desain internal

- Setiap package = `src/LifeOS.Pdf.<Modul>/`, reference `LifeOS.Pdf` (hanya yang butuh; Rendering bisa berdiri
  sendiri). Dependency dicatat di `THIRD_PARTY_NOTICES.md`.
- **Rendering**: PdfiumCore `PdfDocument.Load(byte[])` → `Render(pageIndex, width, height, dpi)` → encode PNG
  (System.Drawing? tidak — pakai writer PNG internal Fase 1? atau SixLabors? **pakai PNG writer internal** yang
  sudah ada `PdfImage`/PngWriter) / JPEG (PDFium bisa output JPEG? tidak — render ke bitmap lalu encode PNG;
  untuk JPEG, pakai encoder bila ada, else PNG). Simpel: render ke RGBA buffer → PNG via `PngWriter` internal.
- **Barcode.Scanning**: ZXing.Net `BarcodeReader` + `RGBLuminanceSource`; `ReadPdf` = `GetImages()` (Fase 7)
  + render halaman (Rendering) sebagai fallback.
- **Ocr**: Tesseract `TesseractEngine(tessDataPath, lang)` → `Pix.LoadFromMemory` → `Process` → `GetText` +
  `GetWords`. `MakeSearchable` = tulis ulang PDF asli (via `PdfDocument.Open` → simpan) lalu tambah lapisan
  teks mode `Tr` (invisible) per halaman menggunakan koordinat kata hasil OCR.
- **DocumentScanner**: komposisi — render (Rendering) → barcode (Barcode.Scanning) → OCR (Ocr); hasil digabung.

## 3. Struktur file

```
src/LifeOS.Pdf.Rendering/           PdfRenderer.cs, RenderOptions.cs, RenderedImage.cs, ImageFormat.cs
src/LifeOS.Pdf.Barcode.Scanning/    BarcodeReader.cs, BarcodeReadResult.cs, BarcodeFormat.cs, ...
src/LifeOS.Pdf.Ocr/                 OcrEngine.cs, OcrPageResult.cs, OcrWord.cs
src/LifeOS.Pdf.Scanning/            DocumentScanner.cs, ScannedPage.cs, ScanOptions.cs
```
Dependency NuGet: `PdfiumCore` (MIT), `ZXing.Net` (Apache-2.0), `Tesseract` (Apache-2.0). Tessdata: file
`eng.traineddata` + `ind.traineddata` di folder `tessdata/` (diunduh, tidak ikut repo bila besar).

## 4. Kompatibilitas

- Target mengikuti dukungan binding: netstandard2.0 + net6.0+ (net5/net7 di-drop bila binding tidak mendukung).
- Native binary PDFium/Tesseract = platform-specific (win-x64 utama; linux-x64 bila disediakan binding).

## 5. Strategi test

- **Rendering**: render PDF buatan sendiri → PNG valid (magic number), ukuran piksel = halaman × dpi/72.
- **Barcode.Scanning**: gambar QR/Code128 hasil encoder Fase 4 → dibaca kembali (round-trip encode→scan).
- **Ocr**: gambar dengan teks sintetis (font jelas) → OCR mendeteksi teks (best-effort; teks sederhana).
- **DocumentScanner**: PDF 1 halaman → `ScanPage` → teks + barcode.
- **Searchable**: PDF hasil `MakeSearchable` → teks bisa di-`ExtractText` (core), tampilan sama.
- Test yang butuh native binary **di-skip bila native tidak tersedia** (attribute `[SkippableFact]` atau try/catch).

## 6. Risiko

- **Native binary gagal dimuat** di environment/CI → mitigasi: lazy-load + exception jelas + test di-skip.
- **Tessdata** harus ada di path → mitigasi: dokumentasi + error jelas.
- **PNG encode** internal perlu ukuran besar (render 300 DPI A4 = ~2480×3508) → perhatikan alokasi.
- **PDF searchable** posisi teks OCR vs halaman → pakai koordinat kata OCR (left/top) → konversi ke PDF.
