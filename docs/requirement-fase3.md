# Requirement Fase 3: Template Report

## 1. Ringkasan & batas scope
Use case #6: membuat laporan PDF dari **template** + **data**, tanpa menulis kode
layout. Template adalah file yang bisa diedit tanpa compile ulang; data berasal dari
objek C# atau JSON (mis. hasil query LifeOS). Engine template menghasilkan
`FlowDocument` (Fase 2), sehingga semua fitur layout tersedia.

**Tidak masuk:** editor template visual (WYSIWYG) → backlog; grafik/chart dari data →
backlog (Fase 12 punya gradien/shape lanjut); QR/barcode di template → Fase 4 (elemen
`<qrcode>` ditambahkan di sana); bahasa template Turing-complete (sengaja tidak: template
tidak boleh menjalankan kode).

## 2. Requirement fungsional
- FR-01 Template berupa file teks yang memetakan elemen dokumen: section (ukuran, margin,
  kolom), header/footer, heading, paragraf dengan teks bergaya, daftar, tabel (kolom, baris,
  span, header berulang, zebra), gambar, garis, spasi, page break, field halaman.
- FR-02 Placeholder `{{ ... }}` di teks dan atribut: path data (`Customer.Name`,
  `Items[0].Price`), literal, operator perbandingan & logika, dan filter berantai (`|`).
- FR-03 Perulangan `foreach` atas list (baris tabel, item daftar, paragraf, isi paragraf),
  dengan variabel item dan indeks.
- FR-04 Kondisi `if` / `else` pada elemen apa pun.
- FR-05 Agregasi: jumlah, rata-rata, min, max, count; pengelompokan (group by) dan pengurutan.
- FR-06 Format bawaan **id-ID** tanpa bergantung pada data ICU sistem: Rupiah
  (`Rp1.350.000`), angka (`1.350.000,50`), persen, tanggal (`13 Nov 2026`,
  `Jumat, 13 November 2026`), **terbilang** (`satu juta tiga ratus lima puluh ribu`).
  Bahasa Inggris (`en`) juga tersedia. Tanggal bisa berupa `DateTime`, `DateTimeOffset`,
  atau string ISO `YYYY-MM-DD` (format tanggal LifeOS).
- FR-07 Data dari objek C# (properti publik, dictionary, list, tipe anonim) atau JSON.
- FR-08 Gambar dari path file, byte[], base64 (`data:`), atau `PdfImage`; font TrueType dari file.
- FR-09 **Template siap pakai**: invoice, kwitansi, slip gaji, laporan keuangan bulanan,
  sertifikat — masing-masing dengan contoh data JSON yang terdokumentasi.
- FR-10 Kesalahan template (sintaks, filter tidak dikenal, elemen tidak dikenal) →
  `TemplateException` dengan nomor baris & kolom dan saran perbaikan. Mode ketat opsional:
  path data yang tidak ada → error (default: kosong).

## 3. Requirement non-fungsional
- NFR-01 Package inti tetap tanpa dependency: parser JSON ditulis sendiri; XML memakai
  `System.Xml` bawaan BCL.
- NFR-02 **Aman untuk template tidak tepercaya**: tidak ada eksekusi kode atau pemanggilan
  method; hanya membaca properti/field publik. XML: DTD dilarang (cegah XXE / billion laughs).
  Path file relatif tidak boleh keluar dari folder dasar template (`..`).
- NFR-03 Deterministik; hasil sama di semua culture sistem (format tidak memakai `CultureInfo` OS).
- NFR-04 Performa: invoice 500 baris dirender < 1 s.
- NFR-05 Semua output lolos validator 5 engine.

## 4. Edge cases
- Path ke null di tengah (`Customer.Address.City` saat Address null) → kosong, bukan crash.
- Indeks di luar jangkauan, list kosong (tabel hanya header), `foreach` atas non-list.
- Angka besar (triliun) & negatif & desimal untuk terbilang/rupiah; 0 → "nol".
- Pembagian dengan nol di ekspresi (tidak didukung aritmetika → tidak relevan) — agregasi
  rata-rata atas list kosong → kosong.
- Escape `{{` literal (`{{'{{'}}`), spasi di dalam placeholder, entitas XML (`&amp;`).
- JSON: unicode escape, angka besar, nesting dalam, trailing comma (ditolak dengan pesan jelas).

## 5. Keputusan analyst (auto-approve)
1. **Format template = XML**, bukan JSON: teks campuran (paragraf dengan potongan tebal)
   jauh lebih alami di XML dan mudah diedit manusia. **Data = objek C# atau JSON.**
2. Ekspresi sengaja kecil (path, literal, perbandingan, logika, filter) — tidak ada
   aritmetika bebas atau pemanggilan method; kebutuhan hitung diselesaikan dengan filter
   agregasi atau disiapkan di data.
3. Data yang hilang → string kosong (ramah untuk template bersama); `Strict = true` untuk
   menguji template.
