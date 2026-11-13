# Requirement Fase 9: Keamanan

## 1. Ringkasan & batas scope

Menambah keamanan PDF: **enkripsi standard security handler** (password) ke package inti — menulis PDF
terenkripsi (RC4-128, AES-128, AES-256/R6), membaca PDF terenkripsi (dekripsi dengan password), izin
(permission), dan hapus password. Ini menjadikan rilis **v1.0**.

**Diusulkan dipisah jadi dua iterasi** (fase besar; lihat §6):

- **Fase 9 (v1.0, sekarang)** — **Enkripsi** di package inti, **tanpa dependency baru**.
- **Fase 9b (backlog)** — **Tanda tangan digital** (`LifeOS.Pdf.Signing`) + **simpan incremental** (append).
  Alasannya: penandatanganan membutuhkan (a) mode simpan incremental (perubahan foundational di writer,
  karena tanda tangan menandatangani byte yang sudah ada), dan (b) dependency kriptografi CMS/PKCS#7 di
  package terpisah. Spesifikasinya dirangkum di §2b, detail menyusul di requirement-9b.

**Tidak masuk Fase 9:** enkripsi sertifikat/public-key (§7.6.4) — Fase 9b; tanda tangan & incremental — Fase 9b;
aplikasi permission secara aktif (perpustakaan tidak memaksa; permission hanya ditulis/diekspos).

## 2. Requirement fungsional (Fase 9 — Enkripsi)

- FR-01 **Opsi enkripsi saat save**: `PdfSaveOptions.Encryption` (tipe `PdfEncryptionOptions`): password
  user, password owner (opsional = sama dengan user), algoritma (`RC4-128`, `AES-128`, `AES-256`),
  permission (`PdfPermissions` enum: print, modify, copy, annotate, fillForm, extract, assemble,
  printHighQuality), dan `EncryptMetadata` (default true). Menulis `/Encrypt` + `/O` `/U` (`/OE` `/UE`
  untuk R6) + `/P` + trailer `/ID`.
- FR-02 **Enkripsi string & stream**: string di objek dan data stream dienkripsi sesuai `/StrF`/`/StmF`
  (crypt filter). Objek non-string (angka, nama, boolean, array, dict) **tidak** dienkripsi; `/Encrypt`,
  `/ID`, xref tidak dienkripsi. `EncryptMetadata=false` → stream metadata pakai crypt filter `Identity`.
- FR-03 **Algoritma** (semuanya dari nol / `System.Security.Cryptography`):
  - RC4-40 (R2) & RC4-128 (R3) — RC4 ditulis manual (tidak ada di .NET), key dari MD5 (Algorithm 2).
  - AES-128 (R4) — AES-CBC (Algorithm 1), key 16 byte.
  - AES-256 (R6, PDF 2.0) — SHA-256/384/512 + AES-CBC-256 (Algorithm 2.A/2.B), `/Perms` + `/OE` `/UE`.
- FR-04 **Baca PDF terenkripsi**: `PdfReadOptions.Password`. Menurunkan file key dari password + `/O`
  `/U` + `/P` + `/ID`, lalu mendekripsi string & stream saat parse. Password salah → exception jelas.
- FR-05 **Password kosong**: PDF dengan user password kosong (hanya owner password) bisa dibuka tanpa
  password (dengan izin terbatas).
- FR-06 **Hapus password**: buka dengan password (user/owner) lalu simpan tanpa `Encryption` → file
  tidak terenkripsi. (Tersedia otomatis begitu dekripsi jalan.)
- FR-07 **Ekspos permission**: `PdfDocument.Permissions` (hasil baca dari `/P`) dan penanda apakah file
  terenkripsi.
- FR-08 **Round-trip**: tulis terenkripsi → buka dengan password → konten/teks identik (dicek via
  `ExtractText`/render).

## 2b. Ringkasan Fase 9b — Tanda tangan digital (backlog)

- `LifeOS.Pdf.Signing` (package terpisah, dependency: `System.Security.Cryptography.Pkcs` atau
  BouncyCastle, lisensi MIT/Apache dicatat di `THIRD_PARTY_NOTICES.md`).
- PAdES B-B/B-T/B-LT/B-LTA; CMS/PKCS#7 (RFC 5652); timestamp RFC 3161 (TSA); verifikasi (termasuk
  OCSP/CRL untuk LTV); tanda tangan visual (appearance); tanda tangan berganda & certification signature.
- Prasyarat: **simpan incremental** (`PdfSaveOptions.IncrementalUpdate`, §7.5.6) di package inti.

## 3. Requirement non-fungsional

- NFR-01 Valid di qpdf + pypdf + PDFium: file terenkripsi bisa dibuka qpdf (`--password`), pypdf, dan
  PDFium dengan password; teks terbaca benar setelah dekripsi.
- NFR-02 Jalan di netstandard2.0 + net5–net10, **tanpa dependency baru** (RC4 manual, AES/SHA/MD5 dari
  `System.Security.Cryptography` yang sudah dipakai).
- NFR-03 Deterministik bila memungkinkan: input + password sama → byte sama (R6 memakai salt; bila
  random, diizinkan menyimpang hanya untuk garam/kunci — dicatat).
- NFR-04 Aman untuk input tidak terpercaya: `/Encrypt` rusak/tidak dikenal → `PdfEncryptedException`
  atau `PdfReadException` yang jelas, tanpa hang/OOM (batas panjang password, panjang `/O`/`/U`).
- NFR-05 Performa: enkripsi/dekripsi aliran tidak membuat alokasi besar per objek; reuse buffer bila
  masuk akal (AES/RC4 diproses per blok/byte).
- NFR-06 Culture-invariant & angka lewat `PdfFormat` (tidak relevan langsung, tapi jaga konsistensi).

## 4. Referensi spesifikasi

- ISO 32000-1 §7.6 Encryption; §7.6.2 (algoritma umum), §7.6.3 (standard security handler + password
  algorithm 2/3/4/5), §7.6.5 (crypt filters).
- ISO 32000-2 §7.6.4 (AES-256, Revision 6) untuk `/OE` `/UE` `/Perms`.
- RFC 2898 PBKDF2 (turunan kunci R6); RFC 5652 CMS/PKCS#7; RFC 3161 TSA; RFC 5280 X.509; RFC 6960 OCSP
  (semua untuk Fase 9b).
- §12.8 Digital signatures (Fase 9b).

## 5. Edge cases

- Password kosong; password salah; user vs owner password; owner password sama dengan user.
- `EncryptMetadata` true/false (metadata ikut terenkripsi atau tidak).
- Stream ber-`/Filter` (kompresi) — enkripsi diterapkan **setelah** kompresi; `/Length` = panjang
  terenkripsi; dekripsi dilakukan **sebelum** filter decode.
- Crypt filter `Identity` (mis. `/Metadata`) — tidak dienkripsi.
- `/Encrypt` tanpa `/O`/`/U`, atau `/R`/`/V` tidak dikenal → exception jelas (input tidak terpercaya).
- Permission bit 1–2 wajib 1; bit yang tidak didukung ditolak/dipertahankan.
- String vs stream memakai filter beda (`/StrF` vs `/StmF`); `/CF` menyimpan definisi filter.
- File linearized / xref-stream yang juga terenkripsi.
- MD5 tidak tersedia di FIPS → fallback SHA (dicatat) atau penolakan jelas.

## 6. Pertanyaan klarifikasi

1. **Pemecahan** — setujui Fase 9 = enkripsi (v1.0), Fase 9b = tanda tangan + incremental save?
2. **Algoritma minimum** — wajib RC4-128 + AES-128 + AES-256, atau cukup AES-128/256 (RC4 cuma untuk
   baca file lama)? (Saran: tulis AES-128/256; baca RC4/AES semua.)
3. **RC4 untuk baca** — implementasi RC4 manual (untuk membuka file RC4 lama) wajib? (Saran: ya.)
4. **Permission default** saat menulis tanpa menyebut permission — semua diizinkan (default PDF)?
5. **Determinisme R6** — izinkan salt random (output tidak deterministik) untuk R6, atau pakai salt
   deterministik (Saran: salt random yang aman; determinisme dikorbankan untuk R6 saja.)
6. **Nama API** — `PdfEncryptionOptions` + `PdfPermissions` + `PdfReadOptions.Password` + `PdfSaveOptions.Encryption`?
