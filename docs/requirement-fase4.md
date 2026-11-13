# Requirement Fase 4: QR & Barcode

## 1. Ringkasan & batas scope
Use case #7: membuat QR code dan barcode dari nol, untuk ditempel ke PDF (vektor, tajam di
semua zoom) atau diekspor sebagai gambar PNG/SVG. Termasuk dukungan **QRIS** (string
pembayaran EMVCo yang dipakai di Indonesia) dan integrasi ke layout engine & template.

**Tidak masuk:** DataMatrix, PDF417, Aztec, Micro QR → backlog (Fase 4b); membaca/scan QR →
Fase 10 (`LifeOS.Pdf.Barcode.Scanning`, ZXing.Net); segmentasi multi-mode QR yang optimal
(satu teks = satu mode); ECI/Kanji mode → backlog.

## 2. Requirement fungsional
- FR-01 Encoder QR Model 2 (ISO/IEC 18004): mode numerik, alfanumerik, byte (UTF-8);
  versi 1–40 (otomatis = terkecil yang muat, atau ditentukan/dibatasi); koreksi error L/M/Q/H;
  mask otomatis (penalti standar) atau ditentukan.
- FR-02 Barcode 1D: **Code 128** (set A/B/C otomatis, hemat untuk angka), **Code 39** (opsional
  check digit mod 43), **EAN-13**, **EAN-8**, **UPC-A** (check digit dihitung atau divalidasi),
  **ITF** / **ITF-14**.
- FR-03 Gambar di PDF sebagai vektor: `PdfGraphics.DrawQrCode`, `DrawBarcode` — warna gelap/terang,
  quiet zone, teks di bawah barcode, **logo di tengah QR** (wajib ECC Q/H supaya tetap terbaca).
- FR-04 Ekspor **PNG** (encoder PNG dari nol) dan **SVG** untuk QR maupun barcode.
- FR-05 **QRIS**: validasi CRC payload, ubah QRIS statis menjadi dinamis dengan nominal
  (tag 54), hitung CRC-16/CCITT-FALSE.
- FR-06 Layout engine: blok QR & barcode di `FlowDocument`; template: elemen `<qrcode>` dan
  `<barcode>` dengan nilai dari data.
- FR-07 Input tidak valid (karakter di luar charset Code 39, digit EAN salah, teks terlalu
  panjang untuk QR versi maks) → exception yang jelas.

## 3. Requirement non-fungsional
- NFR-01 **Benar menurut standar**: matriks QR identik bit-per-bit dengan implementasi
  referensi (segno) untuk versi/ECC/mask yang sama; semua QR & barcode yang digambar dapat
  dibaca kembali oleh decoder independen (zxing-cpp) dari render PDFium dan dari PNG.
- NFR-02 Package inti tetap tanpa dependency; netstandard2.0 + net5–net10.
- NFR-03 Deterministik.
- NFR-04 Performa: QR versi 40 di-encode < 50 ms.
- NFR-05 Vektor ringkas: modul berdekatan digabung (satu operasi fill per QR).

## 4. Referensi
ISO/IEC 18004:2015 (QR), ISO/IEC 15417 (Code 128), ISO/IEC 16388 (Code 39), ISO/IEC 15420
(EAN/UPC), ISO/IEC 16390 (ITF), EMVCo Merchant-Presented QR Specification + standar QRIS (BI),
W3C PNG Specification (writer).

## 5. Edge cases
Teks kosong; teks di batas kapasitas versi; UTF-8 multi-byte (emoji) di byte mode; angka
dengan nol di depan untuk Code 128 set C (jumlah digit ganjil); EAN dengan/ tanpa check digit,
check digit salah; ITF jumlah digit ganjil (ditolak, atau gunakan ITF dengan nol di depan);
logo QR dengan ECC Low/Medium (ditolak); QRIS dengan CRC salah, tanpa tag 63, nominal negatif
atau 0.

## 6. Keputusan analyst (auto-approve)
1. DataMatrix ditunda ke Fase 4b: QR sudah menutup kebutuhan 2D utama (invoice, QRIS, tiket);
   lebih baik QR sempurna daripada dua simbologi setengah jadi.
2. QR byte mode memakai UTF-8 **tanpa** ECI (paling kompatibel dengan kamera ponsel & zxing).
3. Logo hanya diizinkan dengan ECC Q/H dan ukuran ≤ 25% sisi QR.
