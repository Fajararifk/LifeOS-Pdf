# Design Fase 5: Navigasi & Interaktif

## 1. API publik

```csharp
// Link & anotasi (koordinat kiri-atas, point)
page.AddLink(72, 700, 200, 14, "https://lifeos.app");
page.AddLink(72, 720, 200, 14, PdfDestination.ToPage(page3, top: 120));
page.AddLink(72, 740, 200, 14, PdfDestination.Named("bab-2"));
page.AddNote(500, 80, "Cek ulang angka ini", author: "Fajar");
page.AddHighlight(72, 300, 180, 14, comment: "Penting");
page.AddStamp(380, 60, 150, 50, "LUNAS", PdfColor.Green);

// Navigasi dokumen
doc.NamedDestinations["bab-2"] = PdfDestination.ToPage(page2);
var bab1 = doc.Outline.Add("Bab 1 — Ringkasan", PdfDestination.ToPage(page1));
bab1.Add("1.1 Pemasukan", PdfDestination.ToPage(page1, top: 300));
doc.PageLabels.Add(0, PageLabelStyle.LowerRoman);        // i, ii
doc.PageLabels.Add(2, PageLabelStyle.Decimal);           // 1, 2, …
doc.ViewerPreferences.PageMode = PageMode.UseOutlines;
doc.ViewerPreferences.InitialZoom = InitialZoom.FitWidth;
doc.Attachments.Add("transaksi.json", bytes, "application/json", "Data mentah laporan");

// Layout engine
section.AddTableOfContents("Daftar Isi", maxLevel: 2);
section.AddHeading("Ringkasan").Anchor = "ringkasan";
section.AddParagraph().AddText("Lihat ").AddLink("bagian ringkasan", "#ringkasan").AddText(" atau ")
       .AddLink("situs LifeOS", "https://lifeos.app");
flowDoc.GenerateOutline = true;   // default
```

Template: `<a href="https://…">teks</a>`, `<h1 anchor="ringkasan">`, `<toc title="Daftar Isi" maxLevel="2"/>`,
`<attachment name="data.json" src="{{ Json }}" mime="application/json"/>`,
`<document outline="true" pageMode="outlines" zoom="width">`.

## 2. Desain internal
- **Writer**: objek halaman dialokasikan lebih dulu supaya destinasi, outline dan anotasi bisa
  mereferensi halaman. Catalog mendapat `/Outlines`, `/Names` (`/Dests`, `/EmbeddedFiles` —
  name tree satu daun, kunci diurutkan byte-order), `/PageLabels` (number tree), `/PageMode`,
  `/PageLayout`, `/OpenAction`, `/ViewerPreferences`.
- **Destinasi** eksplisit `[page /XYZ left top zoom]`, `/Fit`, `/FitH top`; koordinat dikonversi
  dari kiri-atas. Named destination → string.
- **Outline**: item dengan `/Parent /Prev /Next /First /Last /Count` (positif = terbuka, negatif =
  tertutup, §12.3.3), `/C`, `/F` (1 italic, 2 bold).
- **Anotasi**: `/Annots` per halaman. Link `/Border [0 0 0]`, aksi `/URI` (ASCII, di-escape) atau
  `/Dest`. Text, Highlight (`/QuadPoints` urutan Acrobat: kiri-atas, kanan-atas, kiri-bawah,
  kanan-bawah), Stamp — semua dengan `/AP /N` Form XObject; highlight memakai blend mode
  Multiply (ExtGState) supaya teks di bawahnya tetap terbaca. `/F 4` (print) kecuali catatan.
- **Lampiran**: `/EmbeddedFile` (Flate, `/Params` Size + CheckSum MD5), Filespec dengan `/F` & `/UF`.
- **Layout**: `ResolvedStyle.LinkTarget`; garis yang berisi potongan ber-link menghasilkan
  `LinkItem` per run per baris. Paragraf dengan anchor/heading menghasilkan `AnchorItem` yang
  saat digambar mendaftarkan named destination & entri outline (urutan dokumen). Daftar isi
  (`TocLayouter`) memakai nomor halaman heading dari pass sebelumnya; renderer mengulang layout
  sampai jumlah halaman **dan** nomor halaman heading stabil (maks. 5 pass).

## 3. Struktur file
`Navigation/` PdfDestination, PdfOutline, PdfOutlineItem, PdfPageLabels, PageLabelStyle,
PdfViewerPreferences, PageMode, PageLayout, InitialZoom · `Annotations/` NoteIcon + internal
annotation model · `Attachments/` PdfAttachment, PdfAttachmentCollection ·
`Internal/InteractiveWriter.cs` · Layout: TocBlock, LinkItem/AnchorItem, TocLayouter.

## 4. Strategi test
Struktur PDF (`PdfStructure` + pembacaan objek), qpdf, **pypdf** (outline bertingkat, label
halaman, lampiran + isinya, URI link, named dest), render PDFium (anotasi tampil), dan test
layout: link terpotong dua baris → dua area, TOC dengan nomor halaman yang benar setelah
pergeseran, outline otomatis mengikuti tingkat heading.

## 5. Revisi saat implementasi
- Objek halaman **tidak** dipra-alokasi: anotasi, outline dan name tree ditulis setelah loop
  halaman (semua objek halaman sudah ada), sehingga nomor objek dokumen tanpa fitur Fase 5
  tetap sama seperti v0.5. `/Annots` ditambahkan ke dictionary halaman sesudahnya.
- `AnchorItem` tidak menggambar apa pun; `Renderer` memprosesnya setelah halaman PDF dibuat
  (named destination + outline bersarang per level heading; level 1 terbuka, level 2–3 tertutup).
  Anchor yang dipakai dua paragraf → `LayoutException`.
- Daftar isi memakai named destination otomatis `_heading-N` (atau `Anchor` milik heading) dan
  hanya mendaftarkannya bila dokumen punya TOC. Heading yang ikut TOC = heading di badan section
  (bukan di sel tabel). Renderer maks. 6 pass; nomor yang belum diketahui tampil "?".
- `Inline.Link` + `LinkStyled` (gaya `DocumentStyles.Link` hanya untuk `AddLink`, tidak untuk
  entri TOC). `DocumentStyles.TableOfContentsTitle` untuk judul TOC.
- Template: `<attachment>` menerima `src` **atau** `content`; `<styles><link/>`; judul `toc` default
  "Daftar Isi"; atribut `dots`, `indent` pada `toc`.
- `PdfNull` ditambahkan ke model COS (destinasi `/XYZ null …`).
