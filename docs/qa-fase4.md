# QA Report: FASE 4 — QR & Barcode

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase4.md & design-fase4.md (termasuk "Revisi saat implementasi")
**Mode:** auto-approve

## Verdict: ⚠️ PASS DENGAN CATATAN

FR-01 s.d. FR-07 terpenuhi. Semua QR & barcode yang dihasilkan terbaca oleh decoder
independen (zxing-cpp) dari PNG maupun dari render PDFium, di enam runtime.

## Temuan

### 🔴 Blocker
- Tidak ada.

### Ditemukan selama fase
- **Referensi segno tidak sesuai standar untuk byte mode**: segno menambah satu codeword 0x00
  ekstra saat bit stream sudah pas di batas byte (bertentangan dengan ISO 18004 §7.4.10).
  Dibuktikan dengan library ketiga (`qrcode`) yang identik dengan encoder kita, dan semua
  varian tetap terbaca zxing. Referensi byte mode diganti ke `qrcode` (didokumentasikan).
- **Pilihan mask antar-library tidak konsisten** (segno vs `qrcode` sepakat 3/16), jadi
  test memeriksa mask = penalti terendah §7.8.3 milik kita, bukan pilihan library lain.
- Teks barcode terlalu rapat ke batang → diberi jarak ¼ ukuran font (kosmetik).

### 🟡 Warning
- **CI GitHub masih tidak jalan** (akun terkunci karena billing).
- Belum dicek manual di Acrobat/SumatraPDF dan belum di-scan dengan **kamera ponsel**
  sungguhan (hanya zxing-cpp). Disarankan scan `out/net8.0/barcodes.pdf` dari layar dan dari
  hasil cetak sebelum rilis.
- QRIS: library hanya mengubah nominal; data merchant (NMID, acquirer) harus berasal dari
  penyedia pembayaran. Contoh QRIS di sample adalah merchant fiktif — jangan dipakai untuk
  transaksi.
- Teks EAN/UPC dicetak di tengah bawah (bukan tata letak ritel dengan digit di bawah guard bar).
- DataMatrix/PDF417 belum ada (Fase 4b, backlog).

### 🔵 Saran
- `Barcode.Code128` mendukung ASCII saja; GS1-128 (FNC1, Application Identifier) berguna untuk
  logistik → kandidat Fase 4b.

## Hasil verifikasi otomatis
- build Release bersih: ✅ netstandard2.0, net5.0–net9.0 — 0 warning
- test: ✅ 415/415 di net8.0 dan net9.0 (141 test baru), termasuk:
  - 93 matriks QR identik bit-per-bit dengan referensi (versi 1–40, L/M/Q/H, 3 mode, 8 mask)
  - 24 kasus versi otomatis sama dengan referensi; mask = penalti terendah
  - pola modul EAN-13 standar, check digit EAN/UPC/ITF-14, check Code 39, set Code 128
  - CRC-16/CCITT-FALSE nilai cek 0x29B1; QRIS statis → dinamis (tag 01, 54, CRC)
- decode zxing-cpp: ✅ 10/10 PNG, 11/11 kode di PDF (termasuk QR ber-logo & QRIS dinamis),
  `barcodes.pdf` + `ticket.pdf` + QRIS di `template-invoice.pdf` di 6 runtime
- validator: ✅ 60 PDF lolos qpdf + pypdf + PDFium + fontTools + OTS
- NFR-04: ✅ QR versi 40 di bawah 500 ms di test (encode jauh lebih cepat; batas longgar untuk CI)
- pack: ✅ `LifeOS.Pdf.0.5.0.nupkg`

## Rencana test manual
1. Buka `out/net8.0/barcodes.pdf` di layar, scan setiap kode dengan kamera ponsel (Google Lens /
   aplikasi scanner) — teks harus sama dengan yang tercetak.
2. Cetak halaman yang sama di printer biasa, scan lagi.
3. Scan QR di `out/net8.0/template-invoice.pdf` dengan aplikasi e-wallet: aplikasi akan
   mengenali format QRIS (transaksi akan gagal karena merchant fiktif — itu yang diharapkan).
