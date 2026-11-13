# QA Report: FASE 3 — Template Report

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase3.md & design-fase3.md (termasuk bagian "Revisi saat implementasi")
**Mode:** auto-approve

## Verdict: ⚠️ PASS DENGAN CATATAN

FR-01 s.d. FR-10 terpenuhi dan teruji; kelima template bawaan dirender dengan data contoh
di enam runtime dan lolos validator 5 engine. Catatan di bawah tidak memblokir v0.4.

## Temuan

### 🔴 Blocker
- Tidak ada.

### Ditemukan & diperbaiki selama fase
- **Presedensi filter** (`Expressions/Expression.cs`): awalnya filter mengikat lebih kuat dari
  aritmetika sehingga `Quantity * Price | rupiah` memformat `Price` saja → filter dipindah ke
  presedensi terendah (seperti Liquid/Jinja). Tercakup test `Filters_BindLoosest`.
- **Pesan elemen tak dikenal**: `<paragraph>` sempat dilaporkan sebagai "not allowed" → sekarang
  "Unknown element" + daftar elemen yang boleh di posisi itu.
- **Bingkai sertifikat** tidak memenuhi halaman (kosmetik) → `minHeight` disesuaikan.

### 🟡 Warning
- **CI GitHub masih tidak jalan** (akun terkunci karena billing) — sama seperti Fase 1–2.
- Template bawaan berbahasa Indonesia saja; `Language = English` hanya mengubah format angka/
  tanggal, bukan label ("Jatuh tempo", "Terbilang"). Template versi Inggris → backlog.
- Belum ada cek manual di Acrobat/SumatraPDF (hanya PDFium + validator).
- Data gambar bernama (mis. `"Logo": "logo"`) wajib didaftarkan di `TemplateOptions.Images`;
  bila tidak dan template tidak punya folder dasar → `TemplateException`. Perilaku ini sengaja
  (tidak membaca file sembarangan), tapi perlu jelas di dokumentasi → sudah ada di docs/templates.md.

### 🔵 Saran
- `<qrcode>` di template setelah Fase 4 (QR untuk invoice/kwitansi: nomor dokumen, QRIS).
- Kemungkinan helper `TemplateData.FromObject` untuk anonymous type ke dictionary tidak perlu —
  reflection langsung sudah didukung.

## Hasil verifikasi otomatis
- build Release bersih: ✅ netstandard2.0, net5.0–net9.0 — 0 warning, 0 error
- test: ✅ 274/274 di net8.0 dan net9.0 (86 test baru: JSON, ekspresi, filter, format id/en di
  4 culture, terbilang 0…triliun & negatif, markup, kontrol alur, tabel/span, error ber-baris,
  DTD/XXE, path traversal, gambar dari byte/data-URI/nama, font, template bawaan, performa, determinisme)
- sample: ✅ .NET 5–10 → showcase, images, report + 5 template bawaan (48 PDF)
- validator: ✅ qpdf + pypdf + PDFium + fontTools + OTS untuk 48 file; total invoice
  "Rp24.031.500", gaji bersih "Rp20.414.577" + terbilangnya ter-extract di semua runtime
- visual (PDFium): ✅ invoice, kwitansi, slip gaji, laporan bulanan (3 hal., header tabel berulang,
  over-budget merah), sertifikat
- hitungan: ✅ invoice 22.650.000 − 1.000.000 + PPN 11% 2.381.500 = 24.031.500;
  slip 22.390.000 − 1.975.423 = 20.414.577
- NFR-04: ✅ invoice 500 baris → 18 halaman, 52 KB, 209 ms (target < 1 s)

## Rencana test manual
1. Buka `out/net8.0/template-invoice.pdf`, `template-receipt.pdf`, `template-payslip.pdf`,
   `template-monthly-report.pdf`, `template-certificate.pdf` di Chrome dan Adobe Reader.
2. Ubah `samples/LifeOS.Pdf.Samples/Assets/data/invoice.json` (mis. tambah item, hapus
   `Discount`) → jalankan sample lagi → baris diskon hilang, total & terbilang ikut berubah.
3. Salin `BuiltInTemplates.GetSource(BuiltInTemplate.Invoice)` ke file, ganti warna/teks,
   render dengan `PdfTemplate.Load` tanpa compile ulang aplikasi.
