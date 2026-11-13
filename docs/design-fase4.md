# Design Fase 4: QR & Barcode

## 1. API publik (namespace `LifeOS.Pdf.Barcodes`)

```csharp
var qr = QrCode.Encode("https://lifeos.app/inv/42");                       // ECC M, versi & mask otomatis
var qrH = QrCode.Encode(payload, QrErrorCorrection.High, minVersion: 5);
qr.Version; qr.ErrorCorrection; qr.Mask; qr.Size; qr.IsDark(x, y);
byte[] png = qr.ToPng(moduleSize: 8, quietZone: 4);
string svg = qr.ToSvg(moduleSize: 4, dark: "#000000");

var code = Barcode.Code128("LIFEOS-0042");    // juga Code39, Ean13, Ean8, UpcA, Itf, Itf14
code.Symbology; code.Text; code.Modules; code.ToPng(...); code.ToSvg(...);

g.DrawQrCode(qr, x, y, size: 120, dark: PdfColor.Black, logo: PdfImage.FromFile("logo.png"));
g.DrawBarcode(code, x, y, width: 220, height: 60, showText: true);

string dynamicQris = Qris.WithAmount(staticQris, 50000);    // tag 01 → 12, tag 54, CRC baru
Qris.IsValid(payload); Qris.ComputeCrc(text);

section.AddQrCode("https://…", size: 90, alignment: ParagraphAlignment.Right);
section.AddBarcode(Barcode.Ean13("899123456789"), width: 160, height: 50);
```
Template: `<qrcode value="{{ Url }}" size="80" ecc="M" align="right"/>`,
`<barcode type="code128" value="{{ Number }}" width="180" height="40" showText="true"/>`.

## 2. Desain internal
- `Barcodes/Qr/QrEncoder.cs`: pemilihan mode (numerik → alfanumerik → byte), bit stream
  (mode indicator, character count 10/12/14 · 9/11/13 · 8/16/16 bit sesuai rentang versi),
  terminator & padding `EC 11`, tabel blok ECC per versi (ISO 18004 Tabel 9), Reed–Solomon
  GF(256) polinom 0x11D, interleaving, pola fungsi (finder, separator, timing, alignment,
  dark module), format info BCH(15,5) mask 0x5412, version info BCH(18,6) polinom 0x1F25,
  penempatan zigzag, 8 mask + penalti N1–N4 (§7.8.3).
- `Barcodes/Linear/*`: tiap simbologi menghasilkan `bool[]` modul (true = bar).
  Code 128 memilih set: C untuk ≥ 4 digit beruntun (genap), A untuk karakter kontrol, selain itu B;
  check = Σ(nilai × posisi) mod 103.
- Gambar vektor: semua modul gelap dalam satu path (`re` per run horizontal) dan satu `f` —
  tanpa garis tipis antar modul di viewer. Barcode: satu `re` per bar.
- `Images/Png/PngWriter.cs`: PNG 8-bit gray / RGB, filter 0, zlib (Fase 1), CRC-32.
- `Qris`: parser TLV top-level (tag 2 digit, panjang 2 digit), CRC-16/CCITT-FALSE
  (poly 0x1021, init 0xFFFF) atas payload termasuk "6304".
- Layout: `BarcodeBlock` (publik, sejenis `ImageBlock`) + `BarcodeLayouter` + DrawItem.
- Template: elemen `qrcode` & `barcode` di posisi blok (section, header/footer, cell).

### Revisi saat implementasi
- **Referensi byte mode memakai `qrcode`, bukan segno.** segno selalu menambah
  `8 − (len mod 8)` bit padding — satu codeword 0x00 ekstra bila bit stream sudah pas di
  batas byte (selalu terjadi di byte mode). ISO 18004 §7.4.10 hanya menambah padding
  "jika tidak berakhir di batas codeword". Encoder kita identik dengan `qrcode` (dan
  keduanya terbaca zxing); segno dipakai untuk mode numerik & alfanumerik.
- **Pilihan mask otomatis tidak dibandingkan dengan library lain**: segno dan `qrcode` hanya
  sepakat di 3 dari 16 kasus (penalti N3 ditafsirkan berbeda). Test memastikan versi sama
  dengan referensi dan mask = penalti terendah menurut §7.8.3; setiap mask valid dan terbaca.
- Template invoice bawaan mendapat QR QRIS opsional (`PaymentQr`).

## 3. Strategi test
- Matriks QR vs **segno** (bit-per-bit): semua versi 1–40 untuk ECC M, plus sampel versi untuk
  L/Q/H, ketiga mode, semua 8 mask (`tools/make_qr_references.py` → `TestAssets/qr/references.json`).
- Pilihan otomatis versi & mask dibandingkan dengan segno.
- Decode ulang dengan **zxing-cpp**: PNG dari `ToPng`, dan halaman PDF yang dirender PDFium
  (`tools/validate_pdf.py --expect-code TEXT`).
- Barcode 1D: pola modul untuk contoh yang diketahui, check digit, decode zxing semua simbologi.
- QRIS: CRC "123456789" = 0x29B1 (nilai cek standar), round-trip statis → dinamis.
