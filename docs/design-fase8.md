# Design Fase 8: Form (AcroForm)

**Menyusul requirement-fase8.md.** Keputusan klarifikasi (disetujui user, "jawaban terbaik menurut architect"):
1. Pemecahan: **Fase 8** = buat + isi + flatten + JSON; **Fase 8b** (backlog) = FDF/XFDF, XFA, penandatanganan.
2. Aksi push button: hanya **ResetForm** (tanpa JS). Tidak ada SubmitForm/JS.
3. Field tanda tangan: **buat field + appearance placeholder** sekarang; penandatanganan di Fase 9.
4. `/DA` default: **Helvetica 10** (`/Helv 10 Tf 0 g`), bisa di-override per form/per field.
5. Skema JSON: **map sederhana** `{ "Nama.Field": nilai }` (nilai = `string` | `bool` | `string[]`).

Prioritas lintas sektor (dari user): **performa terbaik** — lihat §6.

## 1. API publik

Namespace `LifeOS.Pdf.Forms`. Semua tipe publik, satu tipe per file. Titik masuk di `PdfDocument` dan `PdfPage`.

```csharp
// PdfDocument — tambah properti:
public PdfForm Form { get; }                       // selalu tersedia (baru & dokumen yang dibuka)

// PdfPage — factory method (pola sama dengan AddLink/AddNote/AddStamp):
public PdfTextField  AddTextField(string name, double x, double y, double w, double h);
public PdfCheckBox   AddCheckBox(string name, double x, double y, double w, double h);
public PdfRadioButton AddRadioButton(string groupName, string onValue, double x, double y, double w, double h);
public PdfChoiceField AddComboBox(string name, double x, double y, double w, double h, params string[] options);
public PdfChoiceField AddListBox(string name, double x, double y, double w, double h, params string[] options);
public PdfButton     AddButton(string name, double x, double y, double w, double h, string caption);
public PdfSignatureField AddSignatureField(string name, double x, double y, double w, double h);
```

Tipe field (ringkas; `PdfFont` default Helvetica 10, warna default hitam):

```csharp
public abstract class PdfFormField
{
    public string Name { get; }          // nama parsial lengkap ("Customer.Nama")
    public bool ReadOnly { get; set; }
    public bool Required { get; set; }
    public string? Tooltip { get; set; } // → /TU
}

public sealed class PdfTextField : PdfFormField
{
    public string? Value { get; set; }
    public string? DefaultValue { get; set; }   // → /DV
    public PdfFont Font { get; set; }
    public double FontSize { get; set; }
    public PdfColor TextColor { get; set; }
    public Forms.TextAlign Align { get; set; }  // Left/Center/Right → /Q 0/1/2
    public bool Multiline { get; set; }
    public bool Password { get; set; }
    public bool Comb { get; set; }
    public int? MaxLength { get; set; }         // → /MaxLen
    public PdfColor? Background { get; set; }
    public PdfColor? BorderColor { get; set; }
}

public sealed class PdfCheckBox : PdfFormField
{
    public bool Checked { get; set; }
    public string OnValue { get; set; }         // default "Yes"
    public CheckStyle Style { get; set; }       // Check, Cross, Star, Circle, Square
    public PdfColor? Background { get; set; }
    public PdfColor? BorderColor { get; set; }
}

public sealed class PdfRadioButton : PdfFormField   // satu widget; grup = nama sama + onValue beda
{
    public string OnValue { get; }
    public bool Selected { get; set; }
    public CheckStyle Style { get; set; }
    public PdfColor? Background { get; set; }
    public PdfColor? BorderColor { get; set; }
}

public sealed class PdfChoiceField : PdfFormField
{
    public IReadOnlyList<PdfChoiceOption> Options { get; }
    public string[] SelectedValues { get; set; }
    public bool MultiSelect { get; set; }        // list box (list) → /Ff 0x200000
    public bool Editable { get; set; }           // combo box (combo) → /Ff 0x40000
    public PdfFont Font { get; set; }
    public double FontSize { get; set; }
    public PdfColor TextColor { get; set; }
    public PdfColor? Background { get; set; }
    public PdfColor? BorderColor { get; set; }
}

public sealed class PdfChoiceOption
{
    public string Display { get; }               // → /Opt [value display]
    public string Value { get; }
}

public sealed class PdfButton : PdfFormField
{
    public string Caption { get; set; }
    public PdfFont Font { get; set; }
    public double FontSize { get; set; }
    public PdfColor TextColor { get; set; }
    public PdfColor? Background { get; set; }
    public PdfColor? BorderColor { get; set; }
    public ButtonAction Action { get; set; }     // None | Reset
}

public sealed class PdfSignatureField : PdfFormField
{
    public string? Caption { get; set; }         // placeholder visual
}
```

`PdfForm` (dokumen):

```csharp
public sealed class PdfForm
{
    public IReadOnlyList<PdfFormField> Fields { get; }   // urutan pembuatan / urutan dokumen
    public string? DefaultAppearance { get; set; }       // raw /DA; default "/Helv 10 Tf 0 g"
    public PdfFont? DefaultFont { get; set; }            // convenience → /DA + /DR /Font

    public PdfFormField? GetField(string name);          // nama parsial (titik = hierarki)
    public void SetValue(string name, string? value);
    public void SetValue(string name, bool value);       // checkbox/radio
    public void SetValue(string name, string[] values);  // multi-select choice

    public void Flatten();                               // bakar tampilan, buang AcroForm

    public string ExportJson();
    public void ImportJson(string json);
}
```

Contoh pemakaian (buat + isi + flatten):

```csharp
var doc = new PdfDocument();
var page = doc.AddPage();
page.AddTextField("Customer.Nama", 72, 72, 200, 20).Value = "Budi";
page.AddCheckBox("Terms", 72, 110, 16, 16).Checked = true;
page.AddRadioButton("Gender", "Pria", 72, 140, 14, 14).Selected = true;
page.AddRadioButton("Gender", "Wanita", 100, 140, 14, 14);
page.AddComboBox("Kota", 72, 170, 150, 20, "Jakarta", "Bandung").SelectedValues = new[] { "Bandung" };
doc.Save("form.pdf");

var filled = PdfDocument.Open("form.pdf");
filled.Form.SetValue("Customer.Nama", "Siti");
filled.Form.SetValue("Terms", false);
filled.Form.Flatten();
filled.Save("flat.pdf");
```

**Perubahan breaking:** tidak ada. Hanya penambahan API. `PdfPage.IsPristine` harus ikut memperhitungkan widget form (field yang ditambah = konten baru, tidak pristine).

## 2. Desain internal

### 2.1 Objek PDF yang ditulis (§12.7)

- **`/AcroForm`** di katalog (ditulis oleh `FormWriter`): `<< /Fields [...] /DA <str> /DR << /Font << /Helv <ref> >> >> /NeedAppearances false >>`.
  Ditulis hanya bila ada field.
- **Field sederhana** (text/checkbox/combo/list/button/signature): **satu objek merangkap** field dictionary + widget annotation
  (`/Type /Annot /Subtype /Widget` + `/FT /T /V /DV /Ff /DA /Q /TU /MK /BS /AP /P /Rect /Parent`). Masuk di `/Fields` **dan** di `/Annots` halaman.
- **Radio group**: satu field dictionary (`/FT /Btn`, `/T`, `/V`, `/Kids [widget...]`) di `/Fields`; tiap widget adalah
  objek `/Annot /Subtype /Widget` terpisah dengan `/Parent` ke field, `/AS` (nama on-state), masuk `/Annots` halaman.
- **Appearance stream** (`/AP /N`): Form XObject `<< /Type /XObject /Subtype /Form /BBox [0 0 w h] /Resources << /Font ... >> >> stream ... endstream`.
  Checkbox/radio memakai sub-dictionary state: `/AP << /N << /On <stream> /Off <stream> >> >>`.
- **Variable text** (`/DA` + `/Q` di dict) tetap ditulis agar viewer bisa regenerate, tapi `/AP` tetap digenerate
  eksplisit supaya tampilan deterministik & konsisten antar viewer (mengikuti kebijakan anotasi Fase 5).

### 2.2 Alur data

- **Buat → save**: `page.AddTextField(...)` membuat `PdfTextField`, menambahkan ke `Form.Fields` dan mencatat
  `FormWidget` (referensi halaman + rect + field) di `PdfPage`. Saat `DocumentWriter.Write`, `FormWriter`:
  1. menulis objek field + widget + appearance (resource font lewat `ResourceRegistry` yang sudah ada),
  2. menyisipkan referensi widget ke `/Annots` halaman (page dict sudah dibangun),
  3. menyisipkan `/AcroForm` ke katalog.
- **Open → fill → save**: `PdfDocument.Open` memanggil `FormReader` yang membaca `/AcroForm /Fields` (tree, iteratif,
  batas kedalaman/jumlah seperti `OutlineImporter`) dan widget dari `/Annots` tiap halaman, membangun model
  `PdfFormField` yang sama. `SetValue` mengubah nilai + menandai field kotor. Saat save, `FormWriter` menulis ulang
  field (dengan `/V`/`/AS` baru) + `/AP` baru; `ImportedPageWriter` **melewatkan** widget form milik model (via set
  nomor objek yang di-skip, mekanisme sama dengan `RemovedAnnotations`) supaya tidak dobel ditulis.
- **Flatten**: untuk tiap widget yang visible, salin `/AP /N` sebagai Form XObject di resource halaman, tambahkan
  `/Name Do` (dengan `/Matrix` translasi ke `/Rect` widget, memperhitungkan `/Rotate`) ke content stream; lalu hapus
  widget dari `/Annots` dan hapus `/AcroForm`. Untuk halaman hasil import, injeksi content memakai mekanisme overlay
  yang sudah ada di `ImportedPageWriter` (form XObject overlay). **Flatten = salin bytes, tanpa re-render** → cepat.

### 2.3 Keputusan & alternatif yang ditolak

- **Rewrite penuh, bukan incremental update** saat save. Incremental update (append) hanya wajib di Fase 9 (signing);
  untuk form, rewrite penuh lebih deterministik, bebas objek yatim, dan sudah jadi jalur `DocumentWriter` yang ada.
- **Field sederhana dirangkap field+widget** (bukan field terpisah + widget terpisah): konvensi paling umum
  (Acrobat/Chromium), lebih hemat objek, dan mengurangi kerja `FormReader` saat membaca dokumen nyata.
- **`/AP` selalu digenerate eksplisit** (bukan hanya set `/NeedAppearances true`): menjamin tampilan identik di
  qpdf/pypdf/PDFium/Chrome/Acrobat (NFR-01), selaras kebijakan anotasi Fase 5.
- **Appearance pakai font yang sama dengan field** (bukan font terpisah): satu resource, konsisten, hemat ukuran.
- **JSON pakai `Templates/Json/JsonParser`** (sudah ada di repo, Fase 3): tanpa dependency baru.

## 3. Struktur file

```
src/LifeOS.Pdf/Forms/
  PdfForm.cs                — collection + /DA + /DR + SetValue/Flatten/ExportJson/ImportJson
  PdfFormField.cs           — abstract base (Name, ReadOnly, Required, Tooltip)
  PdfTextField.cs
  PdfCheckBox.cs
  PdfRadioButton.cs
  PdfChoiceField.cs
  PdfChoiceOption.cs
  PdfButton.cs
  PdfSignatureField.cs
  CheckStyle.cs             — enum gaya centang
  ButtonAction.cs           — None | Reset
  TextAlign.cs              — Left/Center/Right (0/1/2); tidak memakai Layout.ParagraphAlignment
  PdfFormData.cs            — JSON export/import (map nama → nilai)
  Internal/FormWriter.cs    — tulis /AcroForm + field + widget + appearance
  Internal/FormReader.cs    — baca /AcroForm + /Annots → model field
  Internal/FieldAppearance.cs — builder appearance per tipe (border/bg/teks/check/radio)
```
Ubah:
```
src/LifeOS.Pdf/PdfDocument.cs      — + public PdfForm Form { get; }
src/LifeOS.Pdf/PdfPage.cs          — + factory method, internal FormWidgets, IsPristine ikut hitung form
src/LifeOS.Pdf/Internal/DocumentWriter.cs — panggil FormWriter setelah InteractiveWriter.Complete
src/LifeOS.Pdf/Parsing/ImportedPageWriter.cs — lewati widget form milik model (skip set)
src/LifeOS.Pdf/Parsing/SourceDocument.cs     — baca /AcroForm (helper untuk FormReader)
```
Tidak ada package baru, tidak ada dependency baru.

## 4. Kompatibilitas target framework

- Hanya memakai `string`, `List`, `Dictionary`, `StringBuilder`, `SortedDictionary` — aman di netstandard2.0.
- **Tidak** memakai `record`, `init`, `Span<T>`, `ZLibStream`, `Encoding.Latin1`, `HashCode` di API publik.
- `PdfChoiceOption` = `sealed class` biasa (bukan struct/record) demi netstandard2.0.
- Angka lewat `PdfFormat.Number`; teks metadata lewat `PdfString.Text`; nama field non-ASCII → encoding nama PDF
  (UTF-16BE bila perlu) di `/T` dan `/TU`.

## 5. Strategi test

- **Unit test** (`tests/LifeOS.Pdf.Tests`, `Forms/` baru, semua pakai `PdfStructure.Verify`):
  - Buat tiap tipe field → assert dictionary: `/FT`, `/T`, `/V`, `/Ff`, `/Rect`, `/AP`, radio `/Kids`, `/AcroForm`.
  - Appearance: teks terlihat (posisi sesuai `/Q`), checkbox on/off, radio on/off, combo value, button caption.
  - Round-trip: buat → `ToArray` → `Open` → `GetField` sama → `SetValue` → save → `/V` berubah + `/AP` baru.
  - Flatten: setelah `Flatten()`, `/AcroForm` hilang, widget hilang, tapi teks/centang muncul di content (via
    `ExtractText`/render), dan hasil `--forbid-text`/pypdf tidak menemukan field interaktif.
  - JSON: `ExportJson` → `ImportJson` round-trip; multi-select array; nama hierarki bertitik.
  - Edge: nama duplikat, radio lintas halaman, field tanpa `/AP` (di-generate), font `/DR` hilang → fallback Helvetica,
    `\r\n` multiline, comb, `/V` array vs scalar, hidden/read-only/required.
  - Read-only: `SetValue` pada field read-only → `InvalidOperationException`.
- **Asset test**: PDF form nyata (buatan Acrobat/LibreOffice/Chrome) sebagai `TestAssets`, lisensi dicatat. Satu PDF
  form hasil buatan sendiri untuk round-trip.
- **Validasi eksternal** (`tools/validate_pdf.py`): semua output form lolos qpdf + pypdf + PDFium; flatten lolos
  `--expect-text`; file di-fill terbaca benar oleh pypdf (nilai field).
- **Cek manual viewer**: buka di Chrome + satu reader (Acrobat/Edge): field bisa diklik/isi, radio/checkbox berfungsi,
  flatten tampil statis, teks bisa dicopy.

## 6. Performa (prioritas user)

- **Appearance**: satu `StringBuilder` per field; tulis operator langsung (tanpa `string.Join` per glyph); cache lebar
  glyph dari `PdfFont` (sudah ada `MeasureWidth`); font face di-resolve sekali per field.
- **Lookup**: `Dictionary<string, PdfFormField>` (key = nama parsial lengkap) untuk `GetField`/`SetValue` O(1);
  `Fields` tetap list berurutan untuk penulisan.
- **Flatten**: salin bytes `/AP` apa adanya (tanpa decode/re-encode saat kompresi tidak berubah), lalu bungkus `/Do`;
  tidak ada re-render teks. Alokasi minimal, hasil satu Form XObject per widget.
- **Baca**: traversal tree field iteratif (stack) dengan batas kedalaman/jumlah (mengikuti `PdfReadOptions`), tanpa
  rekursi dalam.
- **Tulis**: objek field/widget ditulis sekali; font dibagikan lewat `ResourceRegistry` (satu objek font per face per
  dokumen). Tidak ada alokasi per glyph saat write (string sudah jadi).
- **Determinisme**: urutan `/Fields` = urutan penambahan; appearance tanpa GUID/waktu → output byte deterministik.

## 7. Risiko

- **Flatten di halaman import** (`/Rotate` ≠ 0, CropBox ≠ MediaBox, appearance dengan `/Matrix` sendiri) — area paling
  rawan salah letak. Mitigasi: test dengan halaman rotated + validator render PDFium, bandingkan PNG.
- **PDF form nyata yang aneh** (field tanpa `/AP`, `/NeedAppearances` saja, XFA-only, `/DA` pakai font tidak ada) —
  mitigasi: fallback Helvetica + batas baca; XFA ditolak jelas di Fase 8 (Fase 8b).
- **Ruang nama vs hierarki** (nama parsial bertitik vs nama literal bertitik) — mitigasi: `GetField`/`SetValue`
  memakai nama parsial penuh; konflik dideteksi saat penulisan (exception jelas).
- **Ukuran fase** — sudah dipecah (Fase 8 vs 8b); jika flatten ternyata terlalu besar, flatten bisa digeser ke awal 8b
  (lihat qa nanti).

## Checklist desain
- [x] Tidak ada dependency baru di `LifeOS.Pdf` inti
- [x] Koordinat publik tetap kiri-atas, point (konversi ke kiri-bawah hanya di writer)
- [x] Angka lewat `PdfFormat.Number`, teks lewat `PdfString.Text`
- [x] Font dibagikan lewat `ResourceRegistry`
- [x] Error = exception dengan pesan yang menyebut cara memperbaiki
