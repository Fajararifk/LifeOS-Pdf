# Requirement Fase 7: Ekstraksi Konten

## 1. Ringkasan & batas scope
Membaca isi halaman PDF yang sudah ada: teks beserta posisi dan font dalam urutan baca, pencarian
teks, gambar (JPEG apa adanya, lainnya jadi PNG), tabel (mutasi rekening, invoice) ke CSV/JSON,
konversi PDF → teks/HTML sederhana, dan redaksi sungguhan (konten di area dihapus dari file,
bukan sekadar ditutup kotak hitam).

**Tidak masuk:** OCR untuk PDF hasil scan (Fase 10); teks vertikal CJK yang sempurna; dekode JPEG
/JPEG2000/JBIG2/CCITT (disalin apa adanya bila bisa); redaksi piksel di dalam gambar (gambar yang
tersentuh area dihapus utuh); PDF terenkripsi (Fase 9).

## 2. Requirement fungsional
- FR-01 **Interpreter content stream**: operator grafis & teks (§8, §9.4) — q/Q, cm, BT/ET,
  Tf/Tc/Tw/Tz/TL/Ts/Tr, Td/TD/Tm/T*, Tj/TJ/'/", Do (form XObject rekursif & gambar), inline image,
  path (m/l/re/h + paint) untuk garis tabel.
- FR-02 **Decoding font**: ToUnicode CMap (bfchar/bfrange, codespace 1–4 byte), font sederhana
  (Standard/WinAnsi/MacRoman + /Differences + nama glyph AGL/uniXXXX), Type0/CID (Identity-H,
  /W, /DW), Type3 (/FontMatrix), lebar dari /Widths atau metrik 14 font standar.
- FR-03 **Teks**: per glyph/kata/baris dengan kotak (koordinat kiri-atas, halaman seperti
  ditampilkan), font & ukuran; teks halaman dalam urutan baca (baris atas→bawah, kolom), mode
  "layout" yang mempertahankan jarak horizontal; filter per area.
- FR-04 **Pencarian** teks (case-insensitive opsional, lintas spasi/baris) → halaman + kotak.
- FR-05 **Gambar**: daftar gambar per halaman (ukuran piksel, kotak di halaman, format), bytes
  JPEG/JPX apa adanya; Flate/LZW/RunLength + DeviceGray/RGB/CMYK/Indexed/ICCBased, 1/2/4/8/16
  bit, /Decode, ImageMask, SMask → PNG.
- FR-06 **Tabel**: deteksi dengan garis (lattice) dan tanpa garis (perataan kolom) → baris & sel;
  export CSV (RFC 4180) dan JSON.
- FR-07 **Konversi**: PDF → teks; PDF → HTML sederhana (judul berdasar ukuran font, paragraf, tabel).
- FR-08 **Redaksi**: area (atau hasil pencarian) dihapus dari content stream — glyph di area
  dibuang (posisi teks lain tetap), gambar yang bersinggungan dihapus, anotasi di area dihapus,
  lalu area ditutup kotak warna.

## 3. Requirement non-fungsional
- NFR-01 Aman untuk input tidak terpercaya: batas operator per halaman, kedalaman form XObject,
  siklus form, ukuran gambar hasil dekode (`PdfReadOptions`).
- NFR-02 Teks dari PDF kita sendiri, qpdf, dan dokumen nyata (Word/Chrome/LibreOffice) terbaca
  benar (dibandingkan dengan pypdf/pdfium di test).
- NFR-03 Hasil redaksi: teks yang diredaksi **tidak** bisa ditemukan oleh pypdf/pdfium/qpdf.
- NFR-04 netstandard2.0 + net5–net10, tanpa dependency.

## 4. Referensi
ISO 32000-1 §8.2–8.4 (grafis), §8.9 (gambar, §8.9.5 Decode, §8.9.6 mask), §8.10 (form), §9.2–9.4
(teks), §9.6–9.7 (font sederhana & composite), §9.10 (ekstraksi teks, ToUnicode), Annex D
(encoding), Adobe Glyph List (BSD-3), Adobe Tech Note 5014 (CMap).

## 5. Edge cases
Spasi tidak ditulis (posisi saja) → disisipkan dari jarak; teks tebal palsu (ditulis dua kali) →
dobel dibuang; ligatur (ﬁ) dari ToUnicode multi-karakter; font tanpa ToUnicode & encoding custom
(fallback nama glyph, lalu kode); Tz/Ts/Tc/Tw; teks dirotasi; halaman /Rotate; CropBox ≠ MediaBox;
form XObject bersarang & dipakai berulang; inline image dengan filter singkat (/F /Fl); gambar 1-bit
mask; Indexed palet pendek; tabel tanpa garis dengan sel kosong; sel multi-baris; redaksi di dalam
form XObject; kata terpotong area redaksi.

## 6. Keputusan analyst (auto-approve)
1. Koordinat hasil ekstraksi = halaman **seperti ditampilkan**, kiri-atas, point — sama dengan
   yang diterima redaksi, jadi hasil pencarian bisa langsung diredaksi.
2. API di namespace `LifeOS.Pdf.Extraction` (+ method praktis di `PdfPage`/`PdfDocument`).
3. Halaman yang dibuat di memori diekstrak dari snapshot (simpan → buka), sama seperti Fase 6.
