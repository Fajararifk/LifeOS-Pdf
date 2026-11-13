# Requirement Fase 5: Navigasi & Interaktif

## 1. Ringkasan & batas scope
Membuat PDF yang mudah dinavigasi dan dipakai di layar: link ke web dan ke bagian lain
dokumen, bookmark (panel outline) bertingkat, daftar isi otomatis dengan nomor halaman,
label halaman (i, ii, 1, 2…), pengaturan tampilan saat dibuka, lampiran file, dan anotasi
(catatan, highlight, stempel).

**Tidak masuk:** JavaScript/aksi selain URI & GoTo (keamanan); anotasi Ink/Polygon/Sound/
Movie/3D; form (Fase 8); PDF 2.0 Associated Files (`/AF`) & PDF/A-3 (Fase 12); membaca/edit
anotasi PDF yang sudah ada (Fase 6).

## 2. Requirement fungsional
- FR-01 Link URI (http, https, mailto, tel) pada area persegi di halaman.
- FR-02 Link internal ke halaman/posisi (destinasi eksplisit: XYZ, Fit, FitH) dan ke
  **named destination**.
- FR-03 **Outline/bookmark** bertingkat tak terbatas: judul Unicode, tujuan, terbuka/tertutup,
  tebal/miring, warna.
- FR-04 **Page label** per rentang halaman: angka, romawi besar/kecil, huruf besar/kecil,
  prefix, nomor awal.
- FR-05 **Viewer preferences**: panel yang dibuka (outline, lampiran, thumbnail, layar penuh),
  tata letak halaman (satu halaman, kontinu, dua halaman), zoom awal (fit page, fit width,
  persen), sembunyikan toolbar/menu, judul di jendela.
- FR-06 **Lampiran file** tingkat dokumen: nama Unicode, tipe MIME, deskripsi, ukuran & checksum.
- FR-07 **Anotasi**: catatan (sticky note) dengan ikon, highlight dengan komentar, stempel
  teks (mis. "LUNAS", "DRAFT") — semuanya dengan appearance stream agar tampil sama di semua viewer.
- FR-08 Layout engine: link inline di paragraf (web & `#anchor`), anchor pada paragraf/judul,
  **outline otomatis dari heading**, **daftar isi** dengan leader titik, nomor halaman dan link.
- FR-09 Template: `<a href>`, atribut `anchor`, `<toc>`, `<attachment>`, pengaturan outline &
  tampilan di `<document>`.

## 3. Requirement non-fungsional
- NFR-01 Lolos validator 5 engine; outline, link, lampiran dan label terbaca oleh pypdf/qpdf.
- NFR-02 Tidak ada aksi berbahaya: hanya skema URI yang diizinkan; JavaScript ditolak.
- NFR-03 Deterministik (lampiran tanpa tanggal acak; nama subset & ID tetap).
- NFR-04 Daftar isi stabil: nomor halaman benar walau daftar isi sendiri menggeser halaman.
- NFR-05 netstandard2.0 + net5–net10, tanpa dependency.

## 4. Referensi
ISO 32000-1 §12.3.2 destinations, §12.3.3 document outline, §12.4.2 page labels,
§7.9.6 name trees, §7.9.7 number trees, §12.2 viewer preferences, §12.3.4 OpenAction,
§7.11.3–4 file specifications & embedded files, §12.5 annotations (§12.5.6.5 Link,
§12.5.6.4 Text, §12.5.6.10 Highlight, §12.5.6.12 Stamp, §12.5.5 appearance streams),
§12.6.4.7 URI actions, RFC 3986 (URI).

## 5. Edge cases
Link ke halaman yang bukan milik dokumen ini; named destination yang tidak ada; URI dengan
spasi/karakter non-ASCII (di-escape), `javascript:` (ditolak); outline sangat dalam/panjang;
judul bookmark kosong; label halaman tumpang tindih/tidak urut; lampiran 0 byte, nama ganda,
nama dengan karakter non-ASCII; daftar isi yang membuat dokumen bertambah halaman (nomor ikut
bergeser); heading di dalam sel tabel (tidak masuk outline); link inline yang terpotong ke
dua baris (dua area link).

## 6. Keputusan analyst (auto-approve)
1. Link & anotasi ditambahkan lewat `PdfPage` (koordinat kiri-atas seperti `PdfGraphics`).
2. Outline dari heading **aktif secara default** di `FlowDocument` (bisa dimatikan).
3. Hanya skema `http`, `https`, `mailto`, `tel` untuk link web — cukup untuk dokumen bisnis,
   menutup celah aksi berbahaya.
