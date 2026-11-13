# QA Report: FASE 5 — Navigasi & Interaktif

**Tanggal:** 2026-11-13
**Review terhadap:** requirement-fase5.md & design-fase5.md (termasuk "Revisi saat implementasi")
**Mode:** auto-approve

## Verdict: ⚠️ PASS DENGAN CATATAN

Link (URI & internal), named destination, outline bertingkat, page label, viewer preferences,
lampiran, anotasi (note, highlight, stamp) dan fitur layout (link di paragraf, anchor, bookmark
otomatis dari heading, daftar isi) berfungsi dan lolos semua validator. Catatan di bawah
menyangkut cek manual dan CI.

## Temuan

### 🔴 Blocker
- Tidak ada.

### Ditemukan selama fase
- `PdfStructure.PageContents()` mengenali halaman hanya bila dictionary diakhiri `/Contents`;
  halaman dengan `/Annots` terlewat → regex diperluas.
- Nama tree lampiran: kunci UTF-16 (`<FEFF…>`) diurutkan setelah kunci ASCII karena urutan
  byte (0xFE > huruf), sesuai §7.9.6 — test disesuaikan, bukan kode.
- Template: helper `ParseEnum` sudah ada, duplikat dihapus.

### 🟡 Warning
- **CI GitHub masih tidak jalan** (akun terkunci karena billing). Langkah validasi
  `navigation.pdf` sudah ditambahkan ke workflow.
- **net10.0 tidak terverifikasi di mesin ini**: SDK terpasang 9.0.203 (NETSDK1045). Library dan
  sample dibangun & divalidasi di netstandard2.0 dan net5.0–net9.0.
- **Belum dicek manual** di Chrome & reader kedua (Acrobat/Edge/SumatraPDF) — wajib sebelum rilis
  (lihat rencana di bawah). Render PDFium otomatis menampilkan TOC, note, highlight dan stamp.
- Highlight memakai blend mode Multiply di appearance stream; reader yang mengabaikan `/AP`
  akan menggambar highlight versinya sendiri (tetap benar, warna bisa sedikit beda).
- TOC hanya mendaftar heading di badan section (bukan heading di sel tabel); bookmark otomatis
  tetap mencakup heading di mana saja.

### 🔵 Saran
- Aksi lain (`/GoToR`, `/Launch`, JavaScript) sengaja tidak didukung; `GoToR` ke PDF lain bisa
  jadi kandidat setelah parser (Fase 6).
- Opsi nomor bab otomatis (1, 1.1, …) untuk heading bisa menyusul di backlog layout.

## Hasil verifikasi otomatis
- build Release: ✅ netstandard2.0, net5.0–net9.0 — 0 warning (warnings as errors)
- test: ✅ 452/452 di net8.0 dan net9.0 (37 test baru di `NavigationTests`), termasuk:
  - link URI di-escape (`%20`), skema `javascript:`/`file:`/relatif ditolak
  - destinasi `/XYZ`, `/Fit`, `/FitH` dengan konversi koordinat; halaman dokumen lain → exception
  - name tree `/Dests` terurut; link ke nama tak terdefinisi → exception dengan petunjuk
  - outline `/Count` positif/negatif sesuai §12.3.3, `/C`, `/F`, judul Unicode UTF-16
  - page labels `/Nums` (+ awalan desimal otomatis bila tidak mulai di 0), format roman/huruf
  - viewer preferences & `/OpenAction`; dokumen polos tetap `<< /Type /Catalog /Pages 2 0 R >>`
  - anotasi dengan `/AP` Form XObject (font Helvetica-Bold di resources stamp), QuadPoints
  - lampiran: isi, `/Size`, MIME `application#2Fjson`, nama Unicode di `/UF`
  - output deterministik; layout: outline bersarang, TOC dengan nomor halaman final (tanpa "?"),
    link + anchor, anchor duplikat → `LayoutException`; template `<a>`, `anchor`, `<toc>`,
    `<attachment>`, atribut document
- validator: ✅ 55 PDF (11 × 5 runtime) lolos qpdf + pypdf + PDFium + fontTools + OTS;
  `navigation.pdf` lolos `--expect-outline`, `--expect-link`, `--expect-attachment` (bookmark
  resolve ke halaman, link `/Dest` ke nama yang ada)
- pack: ✅ `LifeOS.Pdf.0.6.0.nupkg`

## Rencana test manual
1. Buka `out/net8.0/navigation.pdf` di Chrome: panel bookmark terbuka otomatis; klik entri daftar
   isi (judul dan nomor) → pindah ke heading; label halaman "i" untuk halaman pertama dan
   "Lampiran A" untuk halaman terakhir (terlihat di kotak nomor halaman).
2. Klik link "repositori LifeOS.Pdf" → membuka GitHub; "tim kami" → membuka aplikasi email.
3. Halaman Anotasi: hover/klik ikon catatan → teks "Bandingkan dengan bulan Oktober."; stamp
   LUNAS/DRAFT tampil; highlight tidak menutupi teks.
4. Ulangi di Acrobat Reader atau SumatraPDF; di Acrobat buka panel Attachments →
   `pengeluaran.csv` & `ringkasan.json` dapat dibuka.
