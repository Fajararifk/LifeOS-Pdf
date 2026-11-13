# Requirement Fase 6: Parser & Manipulasi

## 1. Ringkasan & batas scope
Membuka PDF yang sudah ada (buatan aplikasi lain) dan mengubahnya: gabung, pisah, ambil
halaman, putar, urutkan ulang, hapus; menambah watermark/stempel, nomor halaman dan nomor
Bates; memakai halaman sebagai Form XObject (n-up, booklet, kop surat); dan memperkecil file.
Fondasinya adalah parser COS yang tahan terhadap file rusak atau berbahaya.

**Tidak masuk:** dekripsi (PDF terenkripsi → exception jelas, Fase 9); ekstraksi teks/gambar
(Fase 7); membaca/mengisi form (Fase 8); tanda tangan & menyimpan sebagai incremental update
(Fase 9 — butuh untuk tanda tangan); render ke gambar (Fase 10); downscale gambar JPEG (butuh
decoder JPEG → Fase 10, backlog); struktur tag/aksesibilitas dari file sumber (Fase 12).

## 2. Requirement fungsional
- FR-01 **Parser**: lexer COS lengkap (§7.2–7.3), xref tabel (§7.5.4) dan xref stream
  (§7.5.8), hybrid (`/XRefStm`), object stream (§7.5.7), rantai incremental update (`/Prev`,
  §7.5.6), header tidak di offset 0, `/Length` salah/indirect.
- FR-02 **Pemulihan**: xref rusak/hilang → bangun ulang dengan memindai `n g obj`; objek rusak
  → null (dokumen tetap terbuka bila halaman masih bisa dibaca).
- FR-03 **Filter**: FlateDecode (+ predictor PNG/TIFF), LZWDecode, ASCII85, ASCIIHex,
  RunLength untuk stream yang perlu didekode (xref/objstm/konten); DCT/JPX/CCITT/JBIG2 disalin
  apa adanya.
- FR-04 **Buka dokumen**: `PdfDocument.Open(path|stream|byte[])` → info (judul, penulis,
  tanggal), jumlah & ukuran halaman (MediaBox, CropBox, Rotate, atribut warisan dari pohon
  halaman), bookmark sumber jadi `PdfOutline` yang bisa diubah.
- FR-05 **Operasi halaman**: import dari dokumen lain, sisip, hapus, pindah, putar (0/90/180/270),
  split per halaman / per rentang (`"1-3,5,8-"`) / per N halaman, extract.
- FR-06 **Menggambar di halaman yang ada** dengan `page.Graphics` (watermark teks/gambar, stempel,
  nomor halaman, Bates) dan menambah link/anotasi; konten lama tidak tersentuh.
- FR-07 **Halaman sebagai Form XObject**: `DrawPage(sourcePage, x, y, w, h)` → n-up, booklet,
  latar/kop surat.
- FR-08 **Kompres/optimasi**: objek tak terpakai hilang otomatis saat simpan, stream tanpa
  filter dikompres ulang, opsi object stream + xref stream (PDF 1.5) untuk file lebih kecil.
- FR-09 Alat siap pakai: merge banyak file, split, watermark teks, nomor halaman, nomor Bates.

## 3. Requirement non-fungsional
- NFR-01 **Input tidak terpercaya** (CLAUDE.md #7): batas kedalaman (array/dict bersarang,
  rantai referensi), ukuran stream setelah dekompresi, jumlah objek, panjang rantai `/Prev`;
  tidak ada infinite loop (referensi melingkar, `/Prev` melingkar, pohon halaman melingkar),
  tidak ada OOM; semua kegagalan jadi `PdfReadException` dengan pesan jelas.
- NFR-02 Output lolos 5 validator; file sumber dari library lain (pypdf, qpdf, Chrome/Skia,
  dokumen kita sendiri, file dengan object stream) terbuka dan tersimpan valid.
- NFR-03 Deterministik: input yang sama → output byte yang sama.
- NFR-04 Performa: membuka dokumen 1000 halaman & merge < 2 detik di mesin dev; parsing lazy.
- NFR-05 netstandard2.0 + net5–net10, tanpa dependency.

## 4. Referensi
ISO 32000-1 §7.2 lexical, §7.3 objects, §7.4 filters (§7.4.4 LZW/Flate + predictor),
§7.5 file structure (§7.5.4 xref, §7.5.6 incremental, §7.5.7 object streams, §7.5.8 xref
streams), §7.7.3 page tree & atribut warisan (tabel 30), §8.10 form XObjects, §14.3.2 metadata
(tidak disalin), Annex C (batas implementasi), §7.9.4 tanggal.

## 5. Edge cases
File kosong / bukan PDF; header setelah sampah; `startxref` salah; xref offset meleset; xref
stream dengan predictor; object stream berisi object stream (tolak); `/Length` indirect atau
terlalu panjang; objek hilang; referensi melingkar `a → b → a`; pohon `/Kids` melingkar atau
sangat dalam; `/Count` bohong; MediaBox hilang/terbalik/nol; Rotate bukan kelipatan 90; dokumen
terenkripsi; bom dekompresi (stream kecil → GB); angka sangat besar; string tidak seimbang;
nama dengan `#00`; halaman dengan `/Contents` array atau kosong; resource diwarisi dari node
Pages; halaman sumber dengan anotasi yang menunjuk halaman lain (link internal, popup).

## 6. Keputusan analyst (auto-approve)
1. Model: dokumen yang dibuka tetap `PdfDocument` biasa — halaman yang di-import menyimpan
   referensi ke objek sumber dan disalin (deep copy) saat simpan. Satu API untuk buat & edit.
2. Simpan selalu menulis file baru penuh (bukan incremental); incremental write di Fase 9.
3. Bookmark sumber dipertahankan (diubah ke `PdfOutline`); form, struktur tag, JavaScript,
   `/AA`, thumbnail dan metadata XMP sumber tidak disalin (aman & sederhana; form di Fase 8).
4. PDF terenkripsi (termasuk password kosong) ditolak dengan pesan yang menyebut Fase 9.
