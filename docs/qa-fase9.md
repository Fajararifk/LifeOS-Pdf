# QA Report: FASE 9 & 9b — Keamanan (Enkripsi + Tanda Tangan)

**Tanggal:** 2026-11-14
**Review terhadap:** requirement-fase9.md & design-fase9.md
**Mode:** developer implementasi → QA read-only review.

## Verdict: ✅ PASS (dengan catatan)

Enkripsi (RC4-128 + AES-128) dan tanda tangan digital (PAdES B-B + verifikasi) terimplementasi, lolos
unit test, dan **cross-validated dua arah** dengan qpdf/pikepdf (writer kita dibaca pikepdf; reader kita
membuka file buatan pikepdf). Sisa item ditunda (lihat 🟡 Warning).

## Temuan

### 🔴 Blocker (ditemukan & diperbaiki selama review)
- **Padding password salah** (`SecurityHandler.PadBytes`): mengisi array dengan padding dulu lalu menimpa
  password → hasilnya `password + padding[tail]`, bukan `password + padding[head]`. Semua `/O` `/U`
  salah. Diperbaiki: `password + Padding[0..(32-n)]` (sesuai qpdf `pad_or_truncate_password_V4`).
- **`/U` 16 byte, bukan 32** dan input MD5-nya salah (pakai password, bukan padding string). Diperbaiki:
  `MD5(Padding(32) + ID[0])` → 20× RC4 → pad 32 byte (validasi hanya 16 byte pertama, seperti qpdf).
- **`/V` RC4-128 = 1, harusnya 2** (qpdf menafsirkan V=1 sebagai RC4-40). Diperbaiki: RC4-128 → `/V 2`.
- **"sAlT" hanya untuk string AES, harusnya string+stream** (§7.6.2 Algorithm 1 step e). Diperbaiki.
- **IV AES tidak ter-set** (`Buffer.BlockCopy(data, 0, aes.IV, ...)` menulis ke *clone* getter
  `SymmetricAlgorithm.IV`). Diperbaiki: salin ke array lokal lalu `aes.IV = iv`.
- **ByteRange off-by-one** saat signing (mencari placeholder tanpa `<`). Diperbaiki: `/Contents` value
  meliputi `<`…`>`; ByteRange = `[0, '<', '>'+1, eof]`.
- **`SignedCms.Decode` gagal karena trailing zero** pada `/Contents` (signature + padding). Diperbaiki:
  verifier memotong trailing zero.

### 🟡 Warning (ditunda, tidak menghalangi penutupan fase)
- **AES-256 (R6)** dikenali tapi belum didukung (tulis/baca → `PdfEncryptedException`). Jarang dipakai;
  perlu cross-validation R6 (§7.6.4.3.3) yang rumit.
- **Incremental save** (`PdfSaveOptions.IncrementalUpdate`) belum diimplementasi — tidak dibutuhkan
  untuk satu tanda tangan (ByteRange + placeholder sudah cukup), tapi dibutuhkan untuk **tanda tangan
  berganda / certification signature**.
- **TSA (RFC 3161), OCSP/CRL, LTV** ditunda.
- `LifeOS.Pdf.Signing` target **net8.0** saja (bukan netstandard2.0/net5–10) karena
  `System.Security.Cryptography.Pkcs`. Dependency `System.Security.Cryptography.Pkcs 8.0.2`
  (versi yang dipatch untuk CVE-2026-84008; 8.0.0/9.0.1 ditolak NU1902/1903).
- `PdfSigner` memakai `DateTimeOffset.Now` → output tidak deterministik (wajar untuk tanda tangan).

### 🔵 Saran
- Baca `SignedCms` signer cert chain (untuk validasi lengkap, bukan `verifySignatureOnly`).
- Tambah `PdfSaveOptions.IncrementalUpdate` + tanda tangan berganda di fase berikutnya.
- Cek kompatibilitas signature dengan Adobe Acrobat (hanya dicek struktur + verifikasi sendiri; belum
  dicek di Acrobat).

## Rencana test manual
1. Buka `encrypted.pdf` di Chrome/Acrobat dengan password `rahasia` → teks terlihat; tanpa password →
   diminta password.
2. `qpdf --password=rahasia --decrypt out/net8.0/encrypted.pdf dec.pdf` → sukses.
3. Buka PDF ter-sign di Acrobat: panel signature menunjukkan valid; ubah 1 byte → tanda tangan invalid.

## Hasil verifikasi otomatis
- build Release: ✅ netstandard2.0 + net5.0–net10.0 (core) + net8.0 (Signing) — 0 warning / 0 error.
- test: ✅ **529/529** di net8.0/net9.0/net10.0 (5 test enkripsi + 3 test signing).
- enkripsi: ✅ RC4-128 & AES-128 ditulis → dibaca **pikepdf** dengan password; file pikepdf dibuka
  reader kita (password diterima). Password salah → `PdfEncryptedException`; hapus password → buka ulang.
- signing: ✅ sign → verify valid + `CoversWholeDocument`; tamper header → invalid; tanpa field → throw.
  PDF ter-sign lolos `PdfStructure.Verify` (struktur valid).
