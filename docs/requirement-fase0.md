# Requirement Fase 0: Core Writer

## 1. Ringkasan & batas scope
Fondasi library: menulis file PDF valid dari nol, dengan API untuk halaman,
teks (14 font standar), bentuk, dan gambar JPEG. Bekerja di netstandard2.0 dan
.NET 5–10 tanpa dependency.

**Tidak masuk** (dipindah): font TrueType/Unicode & PNG (Fase 1), layout
otomatis multi-halaman (Fase 2), link/bookmark (Fase 5), membaca PDF (Fase 6).

## 2. Requirement fungsional
- FR-01 Buat dokumen, tambah halaman dengan ukuran preset (A3, A4, A5, Letter,
  Legal), custom (pt/mm/inch), dan orientasi landscape/portrait.
- FR-02 Simpan ke file, ke `Stream`, atau `byte[]`.
- FR-03 Metadata: Title, Author, Subject, Keywords, Creator, Producer,
  CreationDate, ModDate — termasuk karakter non-ASCII.
- FR-04 Gambar garis (warna, tebal, dash), kotak (stroke/fill/keduanya),
  kotak rounded, elips/lingkaran.
- FR-05 Teks satu baris dengan 14 font standar, ukuran & warna bebas,
  rata kiri/tengah/kanan.
- FR-06 Ukur lebar teks; text box dengan word-wrap, paragraf `\n`, kata
  kepanjangan dipotong; mengembalikan tinggi yang terpakai.
- FR-07 Sisipkan JPEG (grayscale, RGB, CMYK) dengan ukuran bebas atau menjaga
  rasio; gambar yang sama dipakai berulang hanya disimpan sekali.
- FR-08 Kompresi content stream (default aktif, bisa dimatikan untuk debug).

## 3. Requirement non-fungsional
- NFR-01 File lolos qpdf, pypdf, dan PDFium; tampil benar di Chrome dan
  reader lain.
- NFR-02 Build di netstandard2.0, net5.0–net9.0, dan net10.0 (SDK 10).
- NFR-03 Output tidak terpengaruh `CultureInfo` (mis. id-ID memakai koma).
- NFR-04 Output deterministik untuk input yang sama.
- NFR-05 Tanpa dependency NuGet di package inti.
- NFR-06 Input tidak valid → exception dengan pesan jelas; tidak pernah
  menulis file rusak.

## 4. Referensi spesifikasi
- ISO 32000-1 §7.3 objek, §7.5 struktur file (header, xref, trailer)
- §7.4.4 FlateDecode, §7.9.2 text string (UTF-16BE BOM), §7.9.4 tanggal
- §8.4–8.5 graphics state & path, §8.9 image XObject, DCTDecode
- §9.6.2.2 14 font standar, Annex D WinAnsiEncoding, Annex C batas ukuran halaman

## 5. Edge cases
- Dokumen tanpa halaman → exception.
- `default(PageSize)`, ukuran < 3 pt atau > 14400 pt → exception.
- NaN/Infinity/negatif pada koordinat & ukuran → exception.
- Karakter di luar WinAnsi (mis. 中) → '?', bukan crash.
- JPEG terpotong / bukan JPEG / 12-bit → exception yang menyebut alasannya.
- CMYK JPEG dari Photoshop (Adobe APP14) → warna terbalik bila tidak diberi /Decode.
- Teks kosong → tidak menulis operator.

## 6. Pertanyaan klarifikasi (sudah dijawab)
- Nama package: `LifeOS.Pdf`. Repo: github.com/Fajararifk/LifeOS-Pdf. Lisensi MIT.
- Origin koordinat: kiri-atas (seperti PDFsharp & UI), satuan point.
