# QA Report: FASE 6 — Parser & Manipulasi (+ performa)

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase6.md & design-fase6.md (termasuk "Revisi saat implementasi"),
docs/performance.md
**Mode:** auto-approve

## Verdict: ⚠️ PASS DENGAN CATATAN

FR-01 s.d. FR-09 terpenuhi kecuali downscale gambar (ditunda ke Fase 10, dicatat). File buatan
qpdf (object stream + xref stream ber-predictor, linearized, plain) dan file kita sendiri terbuka,
bisa diedit dan tersimpan valid. Input rusak/berbahaya tidak membuat hang atau crash.

## Temuan

### 🔴 Blocker
- Tidak ada.

### Ditemukan selama fase
- `PdfName` menulis karakter ≤ U+00FF sebagai satu byte supaya nama dari file (dibaca Latin-1)
  tersimpan persis sama; nama > U+00FF tetap UTF-8. Tidak ada nama non-ASCII yang kita buat sendiri,
  jadi output fase sebelumnya tidak berubah.
- `PdfStructure.PageContents()` diperluas untuk `/Contents` berupa array (halaman yang diedit).
- Rebuild xref awalnya bisa membaca "n g obj" di dalam data stream → sekarang badan objek dilewati.
- Pencocokan string di skrip edit (backslash) sempat merusak literal `\n`; diperbaiki dan
  diverifikasi lewat build + test.

### 🟡 Warning
- **CI GitHub masih tidak jalan** (billing). Langkah validasi merged/letterhead sudah ditambahkan.
- **Belum dicek manual** di Chrome dan reader kedua (Acrobat/SumatraPDF): `merged.pdf`
  (bookmark per file, halaman diputar), `report-booklet.pdf`, `invoice-letterhead.pdf`.
- PDF terenkripsi (termasuk password kosong) ditolak (`PdfEncryptedException`) — sering dijumpai
  pada PDF bank/e-statement; dekripsi di Fase 9.
- Form (AcroForm), struktur tag, JavaScript dan metadata XMP sumber tidak ikut tersalin; widget form
  tetap tampil lewat appearance stream tapi tidak bisa diisi (Fase 8).
- Koordinat `PdfGraphics` pada halaman yang di-import tetap ruang halaman tak diputar; alat stempel
  menangani rotasi, gambar manual di halaman `/Rotate` perlu memperhitungkannya sendiri.

### 🔵 Saran
- Simpan incremental (append-only) untuk tanda tangan digital — Fase 9.
- Opsi menyalin named destination sumber saat merge (sekarang link sudah diubah jadi eksplisit).

## Hasil verifikasi otomatis
- build Release: ✅ netstandard2.0, net5.0–net10.0 — 0 warning
- test: ✅ 495/495 di net8.0, net9.0 dan net10.0 (41 test baru: `ParserTests`, `ManipulationTests`,
  plus `FormatEquivalenceTests`), termasuk:
  - lexer (angka longgar, nama `#xx`, escape/oktal/kurung bersarang, hex ganjil)
  - atribut warisan pohon halaman, info Unicode + PDFDocEncoding + tanggal
  - object stream kita sendiri, 3 file qpdf, incremental update (objek terbaru menang)
  - xref rusak/hilang, sampah sebelum header, `/Length` salah, terenkripsi, bukan PDF
  - nesting 20 000, bom dekompresi 50 MB, siklus referensi/pohon/`/Next`, fuzz 300 iterasi (< 60 s)
  - filter: vektor LZW dari spesifikasi, ASCII85, ASCIIHex, RunLength, Flate + predictor PNG
  - merge/split/extract terbalik, bookmark ikut, link internal ikut atau dibuang, aksi JavaScript dibuang,
    nomor halaman/Bates/watermark (termasuk halaman diputar), underlay, n-up, booklet, overlay
- validator: ✅ 108 PDF (18 × 6 runtime) lolos qpdf + pypdf + PDFium + fontTools + OTS; merged
  (teks, bookmark sumber, link) dan invoice ber-kop (QRIS masih terbaca zxing) di semua runtime
- output Fase 0–5 identik byte-per-byte dengan v0.6.0 (kecuali versi di /Producer dan /ID turunannya)
- benchmark: lihat docs/performance.md (2–5× lebih cepat); buka file 2000 halaman ≈ 13 ms
- pack: ✅ `LifeOS.Pdf.0.7.0.nupkg`

## Rencana test manual
1. `out/net8.0/merged.pdf` di Chrome: panel bookmark berisi 3 dokumen dengan bookmark aslinya;
   watermark "SALINAN" diagonal; "Halaman X dari 12" dan "LIFEOS-0000NN" di bawah — juga pada halaman
   terakhir yang diputar 90°; link "repositori LifeOS.Pdf" masih bisa diklik.
2. `report-booklet.pdf`: cetak bolak-balik (flip short edge), lipat — urutan halaman 1…6 benar.
3. `invoice-letterhead.pdf`: pita kop di belakang isi invoice; teks invoice tetap bisa diseleksi.
4. Ulangi 1–3 di Acrobat Reader atau SumatraPDF.
