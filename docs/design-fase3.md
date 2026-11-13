# Design Fase 3: Template Report

## 1. API publik (namespace `LifeOS.Pdf.Templates`)

```csharp
// Template dari file (gambar & font relatif terhadap folder template)
var template = PdfTemplate.Load("templates/invoice.xml");

// Data dari objek C#…
var pdf = template.Render(new { Number = "INV-001", Customer = new { Name = "Fajar" }, Items = items });
// …atau dari JSON
var pdf2 = template.Render(TemplateData.FromJson(File.ReadAllText("invoice.json")));
pdf.Save("invoice.pdf");

// Masih bisa diubah sebelum render
FlowDocument doc = template.Bind(data);

// Template siap pakai
BuiltInTemplates.Invoice.Render(data).Save("invoice.pdf");
// BuiltInTemplates.Receipt (kwitansi), Payslip (slip gaji), MonthlyReport, Certificate

// Opsi
var t = PdfTemplate.Parse(xml, new TemplateOptions
{
    Language = TemplateLanguage.Indonesian,    // atau English
    Strict = false,                            // true: data yang hilang = error
    BaseDirectory = "templates",               // untuk src gambar/font relatif
    Fonts = { ["noto"] = FontFamily.FromFiles("NotoSans-Regular.ttf") },
});
```

| Tipe | Tanggung jawab |
|---|---|
| `PdfTemplate` | `Load`, `Parse`, `Bind(data) → FlowDocument`, `Render(data) → PdfDocument` |
| `TemplateOptions`, `TemplateLanguage` | bahasa format, strict, folder dasar, font terdaftar |
| `TemplateData` | `FromJson(string)` → struktur dictionary/list/decimal/string/bool/null |
| `TemplateException` | pesan + `Line`, `Column` |
| `BuiltInTemplates` | 5 template bawaan (embedded resource) |
| `TextFormats` | formatter publik: `Rupiah`, `Number`, `Date`, `Terbilang`, `Words` — dipakai juga di luar template |

## 2. Bahasa template (XML)

```xml
<document title="Invoice {{ Number }}" author="{{ Company.Name }}">
  <styles>
    <font name="noto" regular="fonts/NotoSans-Regular.ttf" bold="fonts/NotoSans-Bold.ttf"/>
    <normal family="Helvetica" size="10" fallback="noto" color="#111827"/>
    <heading level="1" size="20" color="#0F766E"/>
  </styles>
  <section size="A4" orientation="portrait" margin="20mm" columns="1">
    <footer><p align="center">Halaman <pageNumber/> dari <pageCount/></p></footer>
    <h1>Invoice {{ Number }}</h1>
    <p>Kepada <b>{{ Customer.Name }}</b>, total <b>{{ Total | rupiah }}</b>
       (<i>{{ Total | terbilang }} rupiah</i>).</p>
    <table headerRows="1" zebra="#F9FAFB">
      <column width="auto"/><column width="*"/><column width="90" align="right"/>
      <row><cell>No</cell><cell>Barang</cell><cell>Jumlah</cell></row>
      <foreach in="Items" as="item" index="i">
        <row><cell>{{ i + 1 }}</cell><cell>{{ item.Name }}</cell><cell>{{ item.Amount | rupiah }}</cell></row>
      </foreach>
      <row if="Discount > 0"><cell colspan="2" align="right">Diskon</cell><cell>{{ Discount | rupiah }}</cell></row>
    </table>
    <if test="Notes | count"> <list><foreach in="Notes" as="n"><item>{{ n }}</item></foreach></list> </if>
    <else><p><i>Tidak ada catatan.</i></p></else>
  </section>
</document>
```

**Elemen blok:** `section` (size, orientation, margin, columns, columnGap, differentFirstPage),
`header`, `footer`, `firstHeader`, `firstFooter`, `h1`–`h3`, `p`, `list` (kind=bullet|numbered,
start) + `item`, `table` (width, headerRows, zebra, headerBackground, border, grid, padding)
+ `column` (width: `auto`, `*`, `2*`, `80`, `30%`, `20mm`; align) + `row` (bg, minHeight,
header) + `cell` (colspan, rowspan, bg, valign, align), `image` (src, width, align),
`hr`, `spacer` (height), `pagebreak`.
**Inline:** teks, `b`, `i`, `u`, `s`, `span` (family, size, color, bold, italic), `br`,
`pageNumber`, `pageCount`.
**Kontrol:** `foreach` (in, as, index) dan `if` (test) / `else` di posisi blok, baris, item,
maupun inline; atribut `if="…"` di elemen apa pun.
**Paragraf:** `align` (left|center|right|justify), `spaceBefore`, `spaceAfter`, `indent`,
`keepWithNext`, `keepTogether`, `pageBreakBefore`, gaya karakter (`size`, `color`, `bold`, …).
**Satuan:** angka polos = pt; `mm`, `cm`, `in`, `pt`.

**Ekspresi** (`{{ }}`, `test`, `in`): path (`a.b[0].c`, nama tidak case-sensitive),
literal (`12`, `3.5`, `'teks'`, `"teks"`, `true`, `false`, `null`), `+ -` (hanya angka,
untuk indeks/penomoran), perbandingan `== != > < >= <=`, logika `&& || !`, kurung, filter
`| nama:arg1:arg2`. Truthy: bukan null/false/0/""/list kosong.

**Filter:** `rupiah`, `currency:"$"`, `number:2`, `percent:1`, `date:"d MMMM yyyy"`,
`terbilang`, `words`, `upper`, `lower`, `title`, `default:"-"`, `count`, `sum:"Amount"`,
`avg:"x"`, `min:"x"`, `max:"x"`, `groupby:"Category"` (→ item `Key`, `Items`), `sort:"x"`,
`sortdesc:"x"`, `first`, `last`, `join:", "`, `where:"Field":nilai`.

### Revisi saat implementasi (auto-approve, dicatat untuk QA)
Saat menulis 5 template bawaan ternyata dibutuhkan:
- **`* /`** di ekspresi (qty × harga, PPN = subtotal × 11%). Tetap tanpa efek samping;
  bagi nol → kosong.
- **`<set name value>`**: variabel untuk sibling berikutnya (Subtotal, Total, …).
- **Filter mengikat paling longgar** (seperti Liquid/Jinja): `a * b | rupiah` memformat hasil
  kali; bandingkan nilai ber-filter dengan kurung `(Items | count) > 3`.
- **Akses setelah kurung**: `(Items | sort:'Date')[0].Name`.
- Filter baru **`sumproduct`**, dan `groupby` dengan argumen kedua opsional → `g.Total`.
- **`TemplateOptions.FallbackFonts`** supaya template bawaan (Helvetica) bisa menampilkan
  nama non-Latin.

## 3. Desain internal
```
Templates/
  PdfTemplate.cs  TemplateOptions.cs  TemplateLanguage.cs  TemplateData.cs  TemplateException.cs
  BuiltInTemplates.cs  TextFormats.cs
  Json/JsonParser.cs                     RFC 8259, ketat, batas kedalaman 256
  Expressions/Lexer.cs Parser.cs Nodes.cs Evaluator.cs Filters.cs ValueAccess.cs
  Markup/TemplateNode.cs (AST) TemplateReader.cs (XML → AST, posisi baris) Binder.cs (AST + data → FlowDocument)
  Resources/*.xml                        5 template bawaan (EmbeddedResource)
```
- Template di-parse **sekali** menjadi AST (termasuk semua ekspresi ter-parse); `Bind`
  hanya mengevaluasi → aman dipakai ulang & paralel (AST immutable).
- `ValueAccess`: `IDictionary` (case-insensitive lookup), `IDictionary<string,object>`,
  `IList`/`IEnumerable`, properti/field publik instance via reflection (cache per tipe).
  Tidak pernah memanggil method → template tidak bisa menjalankan kode.
- Angka dinormalisasi ke `decimal` (uang tanpa error floating point); `double` di luar
  jangkauan decimal tetap `double`.
- XML dibaca dengan `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`,
  posisi baris dari `IXmlLineInfo`. Whitespace di teks campuran dinormalisasi seperti HTML
  (spasi berurutan → satu spasi; spasi di tepi paragraf dibuang).
- Path file (`src`, font) di-resolve relatif ke `BaseDirectory`; path absolut atau `..` keluar
  folder → `TemplateException` (bisa diizinkan dengan `AllowFileAccessOutsideBaseDirectory`).
- Format id-ID ditulis sendiri (pemisah ribuan `.`, desimal `,`, nama bulan/hari) supaya
  benar juga di container Linux dengan `InvariantGlobalization`.

## 4. Kompatibilitas
`System.Xml`, reflection, `decimal` tersedia di netstandard2.0. Tidak ada `System.Text.Json`
(butuh package di netstandard2.0) → parser JSON sendiri.

## 5. Strategi test
- JSON: kasus valid/invalid RFC 8259 (escape, unicode surrogate, angka, kedalaman, trailing comma).
- Ekspresi: presedensi, filter berantai, null-safe path, indeks, perbandingan string/angka/tanggal.
- Format: rupiah/number/percent/date (id & en), terbilang 0…triliun & negatif, determinisme
  di bawah `CultureInfo` ar-SA/id-ID/de-DE.
- Markup: setiap elemen → struktur FlowDocument yang benar; foreach/if/else di semua posisi;
  error dengan baris/kolom; XXE/DTD ditolak; path traversal ditolak.
- Template bawaan: render dengan contoh JSON → validator 5 engine + cek visual.
- Performa: invoice 500 baris < 1 s.
