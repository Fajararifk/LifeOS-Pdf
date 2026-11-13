# Template reference

Templates turn **data** (a C# object or JSON) into a PDF without layout code.
A template is an XML file you can edit without recompiling; it is converted into a
`FlowDocument`, so everything the layout engine can do is available.

```csharp
using LifeOS.Pdf.Templates;

var template = PdfTemplate.Load("templates/invoice.xml");          // parse once, reuse
template.Render(invoice).Save("invoice.pdf");                       // C# object…
template.Render(TemplateData.FromJson(json)).Save("invoice.pdf");   // …or JSON

BuiltInTemplates.Invoice.Render(data).Save("invoice.pdf");          // ready-made templates
```

## Built-in templates

| Template | Description | Sample data |
|---|---|---|
| `BuiltInTemplates.Invoice` | Invoice with line items, discount, tax (e.g. PPN 11%), bank details | [invoice.json](../samples/LifeOS.Pdf.Samples/Assets/data/invoice.json) |
| `BuiltInTemplates.Receipt` | Receipt (*kwitansi*) with the amount in words and a signature, A5 landscape | [receipt.json](../samples/LifeOS.Pdf.Samples/Assets/data/receipt.json) |
| `BuiltInTemplates.Payslip` | Payslip (*slip gaji*): earnings, deductions, take-home pay | [payslip.json](../samples/LifeOS.Pdf.Samples/Assets/data/payslip.json) |
| `BuiltInTemplates.MonthlyReport` | Monthly finance report: totals, saving rate, spending by category vs budget, transactions | [monthly-report.json](../samples/LifeOS.Pdf.Samples/Assets/data/monthly-report.json) |
| `BuiltInTemplates.Certificate` | Certificate with up to three signers, A4 landscape | [certificate.json](../samples/LifeOS.Pdf.Samples/Assets/data/certificate.json) |

Each template lists its data fields in a comment at the top of its XML;
`BuiltInTemplates.GetSource(BuiltInTemplate.Invoice)` returns the XML as a starting
point for your own. Built-in templates are in Indonesian.

## Options

```csharp
var options = new TemplateOptions
{
    Language = TemplateLanguage.Indonesian,  // or English: number, date and word formatting
    Strict = false,                          // true: a placeholder with no data throws
    BaseDirectory = "templates",             // for relative image/font paths (set by Load)
};
options.Images["logo"] = PdfImage.FromFile("logo.png");                 // <image src="logo"/>
options.Fonts["noto"] = FontFamily.FromFiles("NotoSans-Regular.ttf");   // family="noto"
options.FallbackFonts.Add(FontFamily.FromFiles("NotoSans-Regular.ttf")); // for non-Latin text

var invoice = BuiltInTemplates.Get(BuiltInTemplate.Invoice, options);
```

## Document structure

```xml
<document title="Invoice {{ Number }}" author="{{ Seller.Name }}" pageMode="outlines" zoom="fitWidth">
  <styles>
    <font name="noto" regular="fonts/NotoSans-Regular.ttf" bold="fonts/NotoSans-Bold.ttf"/>
    <normal family="helvetica" size="10" color="#1F2937" fallback="noto"/>
    <heading level="1" size="20" color="#0F766E" spaceBefore="14" spaceAfter="6"/>
    <tableHeader bold="true"/>
    <link color="#1D4ED8" underline="true"/>
  </styles>
  <attachment name="data.json" content="{{ Json }}" mime="application/json"/>
  <section size="A4" orientation="portrait" margin="20mm" columns="1">
    <header>…</header>
    <footer><p align="center">Page <pageNumber/> of <pageCount/></p></footer>
    …content…
  </section>
</document>
```

### Elements

| Element | Attributes | Content |
|---|---|---|
| `document` | title, author, subject, keywords, creator, outline (`true`/`false`), pageMode (`default` `none` `outlines` `thumbnails` `attachments` `fullScreen`), pageLayout (`singlePage` `oneColumn` `twoColumnLeft` `twoColumnRight` `twoPageLeft` `twoPageRight`), zoom (`fitPage` `fitWidth` `125%`) | `styles`, `attachment`, `section` |
| `attachment` | name, src (file) **or** content (text, UTF-8), mime, description | — |
| `section` | size (`A3` `A4` `A5` `Letter` `Legal` or `210mm x 297mm`), orientation, margin, columns, columnGap, headerSpacing, footerSpacing, differentFirstPage | header/footer + blocks |
| `header` `footer` `firstHeader` `firstFooter` | — | blocks |
| `h1` `h2` `h3` `p` | paragraph + style attributes, anchor | inline |
| `toc` | title (default "Daftar Isi", empty for none), maxLevel (1–3), dots, indent — section body only | — |
| `list` | kind (`bullet` `numbered`), start, indent | `item` |
| `item` | paragraph + style attributes | inline, nested `list` |
| `table` | width, headerRows, zebra, headerBackground, border, grid, padding, spaceBefore, spaceAfter, keepWithNext + style | `column`, `row` |
| `column` | width (`auto`, `*`, `2*`, `30%`, `80`, `25mm`), align | — |
| `row` | bg, minHeight + style | `cell` |
| `cell` | colspan, rowspan, bg, valign (`top` `middle` `bottom`), align, padding + style | blocks or inline |
| `image` | src, width, align, spaceBefore, spaceAfter | — |
| `qrcode` | value, size, ecc (`L` `M` `Q` `H`), align, color, spaceBefore, spaceAfter | — |
| `barcode` | type (`code128` `code39` `ean13` `ean8` `upca` `itf` `itf14`), value, width, height, showText, align, color | — |
| `hr` | color, thickness, spaceBefore, spaceAfter | — |
| `spacer` | height | — |
| `pagebreak` | — | — |
| `b` `i` `u` `s` | — | inline |
| `span` | style attributes | inline |
| `a` | href (`https://…`, `mailto:…`, `tel:…` or `#anchor`) + style attributes | inline |
| `br` `pageNumber` `pageCount` | (style for the two fields) | — |

**Paragraph attributes:** align (`left` `center` `right` `justify`), spaceBefore,
spaceAfter, indent, rightIndent, firstIndent, lineSpacing, keepWithNext, keepTogether,
pageBreakBefore. **Style attributes:** family (`helvetica` `times` `courier` or a declared
font), size, color, bold, italic, underline, strike, kerning.

**Lengths:** plain numbers are points; `mm`, `cm`, `in`, `pt` are accepted. Margins and
padding take 1, 2 or 4 values in CSS order (top right bottom left). **Colors:** `#RRGGBB`,
`#RGB`, `black`, `white`, `gray`, `red`, `green`, `blue`, `none`. **Borders:** `none`,
a color, or `color width` (`#999 0.5`).

Text directly inside a section, header, footer or cell becomes a paragraph. Template
whitespace is collapsed like HTML; values inserted from data keep their own line breaks.

Headings (`h1`–`h3`) become bookmarks unless `outline="false"`. `<a href="#name">` jumps to the
paragraph with `anchor="name"`; a missing anchor is an error when saving. Only `http`,
`https`, `mailto` and `tel` links are accepted.

An empty `value` on `qrcode`/`barcode` draws nothing, like an empty image `src`.

### Images

`src` can be a file path relative to the template folder, a name registered in
`TemplateOptions.Images`, a `data:image/png;base64,…` URI, or a placeholder whose value is
a `byte[]`, a `PdfImage` or one of the above. An empty value skips the image (handy for
optional logos).

## Control flow

```xml
<foreach in="Items" as="item" index="i"> … {{ i + 1 }}. {{ item.Name }} … </foreach>

<if test="Discount > 0"> … </if>
<else> … </else>

<row if="TaxRate > 0"> … </row>          <!-- if="…" works on every element -->
<else><row> … </row></else>

<set name="Subtotal" value="Items | sumproduct:'Quantity':'Price'"/>
<set name="Total" value="Subtotal - Discount"/>   <!-- visible to the following siblings -->
```

`foreach`, `if`, `else` and `set` can be used wherever their parent allows content:
around blocks, rows, cells, list items and inside paragraphs.

## Expressions

Placeholders `{{ … }}` appear in text and attributes; `test`, `in`, `value` and `if`
take an expression without braces.

| Kind | Examples |
|---|---|
| Paths | `Customer.Name`, `Items[0].Price`, `Items[-1]` (last), `Items.Count` — names are case-insensitive; a missing or null step gives an empty value |
| Literals | `12`, `3.5`, `'text'`, `"text"`, `true`, `false`, `null` |
| Arithmetic | `+ - * /` (`+` concatenates when one side is text; dividing by zero gives an empty value) |
| Comparison | `== != > < >= <=` (numbers, text, dates) |
| Logic | `&&` `||` `!` and parentheses |
| Filters | `value \| name:arg1:arg2` — filters apply to everything on their left: `Qty * Price \| rupiah` |
| Access after an expression | `(Items \| sort:'Date')[0].Name` |

Truthy values are anything except null, `false`, `0`, empty text and empty lists. Data is
only read (public properties and fields, dictionaries, lists) — methods are never called,
so a template cannot run code.

### Filters

| Filter | Example | Result |
|---|---|---|
| `rupiah[:decimals]` | `1350000 \| rupiah` | Rp1.350.000 |
| `currency:symbol[:decimals]` | `1350 \| currency:"$"` | $1,350.00 (English) |
| `number[:decimals]` | `1350000.5 \| number:2` | 1.350.000,50 |
| `percent[:decimals]` | `0.315 \| percent:1` | 31,5% |
| `round[:decimals]` | `2.345 \| round:2` | 2.35 |
| `date[:format]` | `"2026-11-13" \| date:"dddd, d MMMM yyyy"` | Jumat, 13 November 2026 |
| `terbilang` | `1350000 \| terbilang` | satu juta tiga ratus lima puluh ribu |
| `words` | `1350000 \| words` | one million three hundred fifty thousand |
| `upper` `lower` `title` | `"fajar arif" \| title` | Fajar Arif |
| `default:value` | `Note \| default:"-"` | - when empty |
| `join[:separator]` | `Tags \| join:", "` | a, b, c |
| `count` | `Items \| count` | 3 |
| `sum[:field]` `avg` `min` `max` | `Items \| sum:"Amount"` | total |
| `sumproduct:a:b` | `Items \| sumproduct:"Quantity":"Price"` | Σ quantity × price |
| `first` `last` | `(Items \| first).Name` | |
| `sort[:field]` `sortdesc[:field]` | `Items \| sort:"Date"` | stable sort |
| `where:field:value` | `Items \| where:"Category":"Makan"` | filtered list |
| `groupby:field[:sumField]` | `Items \| groupby:"Category":"Amount"` | groups with `Key`, `Items`, `Count`, `Total` |

**Date formats:** `d dd ddd dddd`, `M MM MMM MMMM`, `yy yyyy`, `H HH`, `m mm`, `s ss`,
`'literal text'`. Dates can be `DateTime`, `DateTimeOffset` or ISO strings
(`2026-11-13`, `2026-11`). Formatting rules are built in and identical on every machine,
including Linux containers with invariant globalization.

## Errors

Problems are reported as `TemplateException` with `Line` and `Column`: unknown elements,
attributes and filters (with the list of valid names), wrong argument counts, invalid
lengths or colors, `<else>` without `<if>`, too many cells in a row, and — with
`Strict = true` — placeholders whose data does not exist.

## Security

Templates can come from users: DTDs are prohibited (no XXE or entity expansion), file
paths cannot leave the template folder unless `AllowFileAccessOutsideBaseDirectory` is
set, and expressions cannot call methods.
