# QA Report: FASE 8 — Form (AcroForm)

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase8.md & design-fase8.md
**Mode:** developer melakukan implementasi, lalu QA read-only me-review perubahan.

## Verdict: ✅ PASS

Ditemukan 2 blocker saat review, keduanya sudah diperbaiki (penjelasan di bawah), lalu build/test/validator
hijau kembali. Sisa temuan adalah warning/saran yang tidak menghalangi penutupan fase.

## Temuan

### 🔴 Blocker (ditemukan & diperbaiki selama review)
- **Appearance state checkbox/radio salah nama** (`FormWriter.Appearance`): `/AP /N` ditulis dengan key
  literal `/On`, padahal `/AS` memakai on-value sebenarnya (`/Yes`, `/Pria`). Viewer mencari state `/Yes`
  di `/AP /N` tapi cuma menemukan `/On`/`/Off` → checkbox/radio tampil kosong/salah. Diperbaiki: key
  state sekarang `c.OnValue` (checkbox) dan `widget.OnState` (radio). Dibuktikan oleh test
  `CheckBox_AppearanceStateMatchesValue` (`/Yes` ada, bukan `/On`).
- **`/DR /Helv` hilang saat form tanpa text/choice field** (`FormWriter.Write`): `/DA` default
  `/Helv 10 Tf 0 g` tapi `/DR /Font` tidak mendefinisikan `/Helv` untuk form yang cuma berisi
  checkbox/button. Diperbaiki: `/Helv` selalu didaftarkan di `/DR`.

### 🟡 Warning
- **Font embedded di field → fallback Helvetica.** `FormReader.ParseDa` hanya mengenali nama font standar
  (Helv/TiRo/Cour/…). Field yang `/DA`-nya memakai font `/DR` custom di-fallback ke Helvetica saat diisi.
  Tidak merusak file; tampilan berbeda dari aslinya (catatan untuk Fase 8b).
- **Widget tanpa `/P` di-drop.** Beberapa PDF nyata menulis widget di `/Annots` halaman tanpa `/P`.
  Field seperti itu tidak terbaca. Jarang, tapi perlu penanganan (scan halaman) bila ditemukan di lapangan.
- **Appearance selalu diregenerasi saat save** (untuk form yang dibuka lalu disimpan). Deterministik dan
  valid, tapi appearance custom yang rumit diganti jadi bersih. Sesuai keputusan design.
- **`Comb` tanpa `MaxLen` tidak divalidasi** — flag `/Ff Comb` ditulis tanpa `/MaxLen` (secara teknis
  melanggar §12.7.3.2). Tampilan tetap benar karena appearance comb hanya aktif bila `MaxLen` di-set.
- **Korupsi tak sengaja di `PdfGraphics.cs`** (bukan bagian Fase 8): baris `private readonly PdfPage _page;`
  ketimpa teks perintah `dotnet pack …`. Ditemukan saat build gagal dan sudah dikembalikan ke semula
  (hasil `git diff` bersih untuk file itu).

### 🔵 Saran
- Validasi di API publik: `Comb == true` wajib `MaxLength` di-set (throw `ArgumentException`).
- Dukungan widget tanpa `/P` (fallback: cari nomor objek widget di `/Annots` tiap halaman).
- Parsing font `/DR` custom di `ParseDa` (mapping ke font yang bisa dipakai) — Fase 8b.
- Pertimbangkan test asset PDF form nyata (Acrobat/LibreOffice/Chrome) untuk round-trip fill — saat ini
  round-trip memakai form buatan sendiri.

## Rencana test manual
1. Buka `out/net8.0/form.pdf` di Chrome & Acrobat: field teks bisa diklik/isi; radio "Pria"/"Wanita"
   bisa dipilih dan hanya satu yang menyala; checkbox tercentang; combo menampilkan "Bandung"; tombol
   Reset mengembalikan nilai default.
2. Copy-paste teks "Budi Santoso" → hasil benar (ToUnicode jalan).
3. Buka `out/net8.0/form-flattened.pdf`: tampilan statis identik dengan form terisi; Ctrl+F "Budi"
   ketemu; tidak ada field interaktif (tidak bisa diklik).
4. Ulangi langkah 1 di satu reader lain (Edge/Firefox/SumatraPDF).

## Hasil verifikasi otomatis
- build Release: ✅ netstandard2.0, net5.0–net10.0 — 0 warning / 0 error
- test: ✅ **521/521** di net8.0, net9.0, net10.0 (9 test baru `FormTests`: struktur AcroForm/widget,
  radio group, round-trip fill, flatten, JSON, read-only, determinisme, appearance-state)
- sample: ✅ `form.pdf` (5.5 KB) + `form-flattened.pdf` (3.6 KB) dihasilkan
- validator: ✅ `form.pdf` + `form-flattened.pdf` (lolos `--expect-text "Budi Santoso"`) + `showcase.pdf`
  lolos qpdf + pypdf + PDFium + fontTools + OTS
- determinisme: ✅ test `SameForm_ProducesIdenticalBytes`
