# Requirement Fase 2: Layout Engine

## 1. Ringkasan & batas scope
Fase 0–1 memberi kanvas (`PdfGraphics`): pemanggil harus menghitung sendiri setiap
koordinat. Fase 2 menambah **layout engine**: pemanggil menyusun dokumen sebagai
alur konten (paragraf, tabel, gambar, daftar) dan library yang menghitung posisi,
memotong baris, membuat halaman baru, mengulang header tabel, dan menulis
header/footer "Halaman X dari Y". Ini use case #2 dan fondasi template laporan (Fase 3).

Juga masuk (dipindah dari Fase 1): **kerning**, **font fallback per karakter**,
dan perbaikan QA Fase 1 (ToUnicode untuk glyph `.notdef`).

**Tidak masuk:** shaping kompleks (GSUB: ligature, Arab, Devanagari, Thai) → backlog;
bookmark/outline otomatis dari heading → Fase 5; hyphenation berbasis kamus →
backlog (hanya soft hyphen U+00AD); baris tabel yang terpotong lintas halaman →
backlog (satu baris selalu utuh); footnote, float/wrap teks di sekitar gambar → backlog.

## 2. Requirement fungsional

### Dokumen & halaman
- FR-01 `FlowDocument` berisi satu atau lebih **section**; tiap section punya ukuran
  kertas, orientasi, margin, jumlah kolom (1–n) dan jarak kolom sendiri, dan selalu
  dimulai di halaman baru.
- FR-02 Header & footer per section (boleh berisi paragraf, tabel, gambar), dengan
  field **nomor halaman** dan **jumlah halaman** ("Halaman 3 dari 12"). Opsi
  header/footer berbeda untuk halaman pertama section.
- FR-03 Hasil berupa `PdfDocument` biasa (bisa ditambah gambar bebas sebelum disimpan),
  atau langsung `Save(path)`.

### Teks & paragraf
- FR-04 Paragraf berisi banyak potongan teks dengan gaya berbeda dalam satu baris
  (font family, ukuran, tebal, miring, warna, garis bawah, coret).
- FR-05 Gaya bertingkat: style dokumen (Normal, Heading 1–3) → paragraf → potongan
  teks; properti yang tidak diisi diwarisi.
- FR-06 Font family: Regular/Bold/Italic/BoldItalic dari 14 font standar atau file
  TrueType; varian yang tidak ada → pakai Regular.
- FR-07 **Font fallback**: daftar family cadangan; karakter yang tidak ada di font
  utama otomatis memakai family cadangan pertama yang punya glyph-nya.
- FR-08 Perataan kiri, tengah, kanan, **justify**; indent kiri/kanan, indent baris
  pertama; spasi sebelum/sesudah; jarak baris.
- FR-09 Pemotongan baris di spasi, setelah tanda hubung, di soft hyphen (muncul "-"
  bila dipotong di situ), di zero-width space, dan di antara karakter CJK. Spasi tak
  terpotong (U+00A0) dihormati. Kata yang lebih lebar dari kolom dipotong per huruf.
- FR-10 Paginasi otomatis; **keep together** (paragraf tidak dipecah), **keep with
  next** (paragraf ikut pindah bersama blok berikutnya, mis. judul + isi), page break.
- FR-11 **Kerning** untuk font TrueType (tabel `kern` dan GPOS `kern`), aktif secara
  default di layout, bisa dimatikan per gaya.

### Blok lain
- FR-12 Daftar bullet dan bernomor, bisa bertingkat.
- FR-13 Gambar sebagai blok (ukuran alami, lebar tertentu, atau penuh kolom; perataan);
  gambar lebih besar dari area halaman diperkecil otomatis.
- FR-14 Garis horizontal dan spasi kosong.

### Tabel
- FR-15 Lebar kolom: tetap (pt), persen, relatif (bobot), dan **otomatis** (mengikuti
  isi). Lebar tabel penuh atau tertentu.
- FR-16 Sel berisi blok apa pun (paragraf, gambar, daftar, tabel bersarang).
- FR-17 **Colspan** dan **rowspan**; perataan vertikal sel (atas/tengah/bawah);
  padding; warna latar sel/baris; **baris zebra**.
- FR-18 Garis tepi luar dan garis dalam (pen bisa diatur atau dimatikan).
- FR-19 **Baris header berulang** di setiap halaman tempat tabel berlanjut.
- FR-20 Perataan default per kolom (mis. kolom nominal rata kanan).

### Kesalahan
- FR-21 Konten yang tidak mungkin muat (mis. satu baris tabel lebih tinggi dari
  halaman, margin lebih besar dari kertas) → `LayoutException` dengan pesan yang
  menyebut elemen mana dan cara memperbaikinya. Tidak pernah ada konten yang
  hilang diam-diam atau menumpuk dengan footer.

## 3. Requirement non-fungsional
- NFR-01 Output lolos qpdf, pypdf, PDFium, fontTools, OTS; teks ter-extract utuh.
- NFR-02 netstandard2.0 + net5–net10; package inti tanpa dependency.
- NFR-03 Deterministik (input sama → byte sama).
- NFR-04 Performa: laporan 50 halaman (tabel 1.500 baris) < 1 detik di mesin dev.
- NFR-05 Kerning tidak boleh membuat font gagal dimuat: data GPOS/kern yang rusak
  → kerning dinonaktifkan untuk font itu, bukan exception.
- NFR-06 API `PdfGraphics` Fase 0–1 tidak berubah perilakunya (tanpa kerning).

## 4. Referensi spesifikasi
- ISO 32000-1 §9.4.3 operator `TJ` (penyesuaian posisi glyph), §9.3.3 word spacing
  (tidak berlaku untuk Identity-H → justify memakai `TJ`), §9.10 ToUnicode
- OpenType 1.9: `kern` (format 0), GPOS (FeatureList, LookupList, PairPos format 1/2,
  Extension lookup type 9, Coverage, ClassDef, ValueRecord), `post`
- Unicode UAX #14 (dipakai sebagian: spasi, hyphen, SHY, ZWSP, NBSP, CJK)

## 5. Edge cases
- Paragraf kosong; teks hanya spasi; spasi berurutan; baris diakhiri spasi (tidak
  dihitung lebar); `\n` di tengah teks; justify pada baris terakhir (tidak di-justify).
- Kata lebih lebar dari kolom sempit (mis. kolom tabel 20 pt).
- Field halaman di body maupun header/footer; jumlah halaman berubah digit (9 → 10)
  sehingga lebar berubah.
- Tabel: tanpa baris; header lebih tinggi dari halaman; rowspan melewati batas
  halaman (grup dipindah utuh); colspan melebihi jumlah kolom; kolom auto dengan
  isi kosong; jumlah lebar tetap > lebar tabel.
- Keep-with-next berantai (judul → subjudul → isi); keep-together paragraf lebih
  tinggi dari halaman (dipecah juga, bukan loop tak hingga).
- Section dengan margin lebih besar dari kertas; header lebih tinggi dari halaman.
- Gambar lebih lebar/tinggi dari kolom.

## 6. Keputusan analyst (auto-approve, diambil yang terbaik)
1. Nama API: `FlowDocument` di namespace `LifeOS.Pdf.Layout` — terpisah dari
   `PdfDocument` agar kanvas tetap sederhana.
2. Baris tabel tidak dipotong lintas halaman di v0.3 (paling umum di laporan;
   memotong baris jarang diinginkan). Baris yang terlalu tinggi → `LayoutException`.
3. Kerning default **aktif** di layout (tipografi lebih baik), **mati** di `PdfGraphics`
   (output Fase 0–1 tidak berubah).
4. Kerning untuk 14 font standar tidak dimasukkan (tabel AFM KPX akan menambah
   ratusan KB kode tergenerasi); dicatat sebagai batasan.
