# Requirement Fase 8: Form (AcroForm)

## 1. Ringkasan & batas scope

Menambah dukungan form interaktif (AcroForm, ISO 32000-1 §12.7) ke package inti, tanpa dependency baru:
membuat field (text, checkbox, radio, combo/list, button), mengisi form PDF yang sudah ada (set nilai +
regenerasi appearance stream), flatten (bakar tampilan field ke konten halaman dan buang AcroForm),
serta export/import data field via JSON.

**Tidak masuk Fase 8** (diusulkan jadi **Fase 8b**, backlog):
- FDF dan XFDF (interop Acrobat/alat lain) — hanya JSON di Fase 8.
- Field tanda tangan (signature): pembuatan field + appearance placeholder dipertimbangkan; penandatanganan
  kriptografis adalah Fase 9. Lihat pertanyaan klarifikasi #3.
- Ratakan form XFA (dibaca → jadi konten statis).
- JavaScript / aksi `Run` (dilarang oleh kebijakan proyek). Aksi `ResetForm`/`SubmitForm` (bukan JS)
  dibahas di pertanyaan #2.

**Catatan ukuran scope:** fase ini besar (± sebesar Fase 7). Usulan pemecahan di atas membagi "buat/isisi/flatten/JSON"
(Fase 8) dari "interop data (FDF/XFDF), signature field, XFA" (Fase 8b) agar tiap iterasi bisa lolos QA.

## 2. Requirement fungsional

Pembuatan form:

- FR-01 **Buat text field** di halaman: nilai default, font & ukuran (default appearance `/DA`), warna teks,
  alignment kiri/tengah/kanan (`/Q`), multiline, password, comb (sel karakter), batas panjang (`/MaxLen`),
  read-only, required. API usulan: `page.AddTextField(x, y, w, h, name)` → `PdfTextField` (namespace `LifeOS.Pdf.Forms`).
- FR-02 **Buat checkbox**: nilai export (`/V` dan `/AS`), state on/off, gaya centang (paling tidak "check",
  opsional cross/star/dot), label tampilan. `page.AddCheckBox(...)` → `PdfCheckBox`.
- FR-03 **Buat radio button group**: satu nama field, beberapa widget (bisa lintas halaman) dengan nilai on
  berbeda (`/AS`), state off. `page.AddRadioButton(...)` → `PdfRadioButton`.
- FR-04 **Buat combo box & list box**: daftar opsi (tampilan + nilai export), pilihan terpilih,
  multi-select (list box), editable (combo). `page.AddComboBox(...)`, `page.AddListBox(...)` → `PdfChoiceField`.
- FR-05 **Buat push button**: teks caption, warna teks/latar, border. Aksi terbatas (lihat pertanyaan #2).
  `page.AddButton(...)` → `PdfButton`.
- FR-06 **AcroForm di katalog**: `/AcroForm` dengan `/Fields` (urutan penambahan), `/DA` dan `/DR` (font
  default + resources) default, `/NeedAppearances false`. Tiap field = field dictionary (`/FT`, `/T`, `/V`,
  `/Ff`, `/DA`, `/Q`, dst.) + widget annotation (`/Type /Annot /Subtype /Widget`, `/Rect`, `/P`, `/Parent`,
  `/AP`) di halaman. Field bersama (radio) memakai `/Kids` + satu field dictionary.
- FR-07 **Appearance stream** dibuat untuk semua state yang relevan: nilai teks (variable text `/DA`+`/Q`),
  checkbox on/off, radio on/off, opsi combo/list terpilih, caption button. Appearance normal (`/AP /N`),
  deterministik, memakai resource font yang sama dengan field.
- FR-08 **Validasi input** di API publik: nama field unik dalam satu halaman (atau dianggap nama parsial),
  koordinat/ukuran positif & finite, nilai yang tidak cocok tipe field → `ArgumentException` dengan pesan jelas.

Pengisian form yang sudah ada:

- FR-09 **Baca struktur form** dari PDF yang dibuka: daftar field (nama parsial lengkap, tipe, nilai saat ini,
  opsi, flags, rect, halaman). `doc.Form.Fields` → `IReadOnlyList<PdfFormField>`.
- FR-10 **Set nilai field** berdasarkan nama parsial (titik = hierarki), mis.
  `doc.Form.SetValue("Customer.Nama", "Budi")`; menangani tipe: text (string), checkbox/radio (nama on-state),
  combo/list (string atau array untuk multi-select).
- FR-11 **Regenerasi appearance** setelah nilai berubah (set `/NeedAppearances true` **atau** tulis ulang
  `/AP`), memakai `/DA`, `/DR`, dan font yang diwarisi (inherited) dari parent field / root AcroForm.
- FR-12 **Hormati field read-only**: field ber-flag read-only menolak perubahan (lempar exception dengan pesan
  yang menyebut field-nya).

Flatten:

- FR-13 **Flatten**: tampilan tiap widget (dari `/AP /N`) digambar ke content stream halaman (sebagai Form
  XObject `/Do`), lalu widget annotation dan `/AcroForm` dihapus; hasilnya halaman statis yang tampilannya
  identik. `doc.Form.Flatten()`.
- FR-14 Flatten menghormati field tersembunyi (dibuang, tidak digambar), `/Rotate` halaman, dan memindahkan
  resource yang dipakai appearance ke resource halaman.

Data (JSON):

- FR-15 **Export data field ke JSON** (nama → nilai; array untuk multi-select) dan **import dari JSON**
  (round-trip `ExportJson` → `ImportJson` menghasilkan field yang sama).

## 3. Requirement non-fungsional

- NFR-01 Valid di qpdf + pypdf + PDFium tanpa warning; tampil benar di Chrome & minimal satu reader lain.
- NFR-02 Jalan di netstandard2.0 dan net5–net10, **tanpa dependency baru** di package inti.
- NFR-03 Deterministik: input sama → byte output sama (tidak ada GUID/waktu; `/ID` dari isi file).
- NFR-04 Culture-invariant: angka lewat `PdfFormat.Number`, teks metadata lewat `PdfString.Text` (id-ID koma
  tidak merusak PDF).
- NFR-05 Aman untuk input tidak terpercaya saat membaca form: batas jumlah field, kedalaman hierarki nama
  parsial, panjang nama/nilai (pakai `PdfReadOptions` bila perlu).
- NFR-06 Round-trip: buat form → save → open → fill → flatten → masih valid dan tampilan konsisten.
- NFR-07 File yang dibuat sendiri, file qpdf, dan PDF form nyata (mis. dari Acrobat/LibreOffice/Chrome)
  bisa dibaca dan diisi dengan benar.

## 4. Referensi spesifikasi

- ISO 32000-1 §12.7 Interactive Forms
- §12.7.2 Field dictionaries (`FT`, `T`, `V`, `DA`, `DR`, `Ff`, `Kids`, `Parent`)
- §12.7.3 Field types: §12.7.3.2 text, §12.7.3.3 button (push/checkbox/radio), §12.7.3.4 choice (combo/list),
  §12.7.3.5 signature
- §12.7.4 Widget annotations & appearance streams; §12.7.4.3 variable text; §12.7.4.4 appearance dictionaries
- §12.5.5 appearance streams; §12.5.6.19 widget annotations
- §12.7.5 actions (`ResetForm`, `SubmitForm`)
- §12.7.7 FDF (rujukan untuk Fase 8b); XFDF (Adobe XML Forms Data Format, untuk Fase 8b)
- §7.9.4 dates; §9 fonts (untuk `/DA`)

## 5. Edge cases

- Nama parsial dengan titik/spasi/unicode (encoding PDFDocEncoding vs UTF-16).
- Radio group tersebar lintas halaman; nama sama, on-state berbeda.
- Field tanpa `/AP` (hanya `/NeedAppearances`) → wajib bisa di-generate.
- Font di `/DA`/`/DR` tidak ada di resource → fallback Helvetica.
- Text multiline (`\r`, `\n`, `/Ff` Multiline); comb (sel karakter tetap).
- Checkbox tanpa nilai export → default "Yes".
- `/V` berbentuk array (multi-select list box) vs string/scalar.
- Field tersembunyi (flag), read-only, required.
- Atribut diwarisi (DA/DR/Q) dari parent field / root AcroForm.
- Flatten field yang appearance-nya memakai resource/font yang tidak ada di halaman → resource harus disalin.
- Appearance Form XObject dengan `/Resources` sendiri.
- Rect field di luar halaman, negatif, atau nol.
- Field dengan `/Kids` widget di beberapa halaman.
- Form XFA-only (tanpa field AcroForm) → di luar scope (Fase 8b).
- Nama field duplikat / hierarki tidak konsisten pada PDF rusak.

## 6. Pertanyaan klarifikasi

1. **Pemecahan fase** — setujui Fase 8 = buat + isi + flatten + JSON, dan Fase 8b = FDF/XFDF + signature field + XFA?
2. **Aksi push button** — karena JS dilarang: (a) hanya caption tanpa aksi, (b) izinkan `ResetForm` (reset ke nilai
   default), atau (c) `ResetForm` + `SubmitForm` ke URL yang ditentukan pemanggil?
3. **Field tanda tangan** — (a) buat field + appearance placeholder sekarang (penandatanganan di Fase 9), atau
   (b) tunda seluruh field signature ke Fase 8b?
4. **Default appearance `/DA`** — wajib pemanggil beri `PdfFont`, atau default `Helvetica 10`?
5. **Skema JSON** — pakai skema sederhana milik sendiri (`{ "Nama.Field": "nilai" }`), atau sejajarkan dengan
   struktur yang memuat tipe field?
