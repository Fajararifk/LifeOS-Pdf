using System;
using System.Globalization;
using System.IO;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 2 showcase: a multi-page monthly finance report built with the layout engine
    /// (headings, mixed styles, justify, lists, tables with spans and repeating header,
    /// image, two-column section, header/footer with "Halaman X dari Y").
    /// </summary>
    internal static class ReportSample
    {
        private static readonly PdfColor Accent = PdfColor.FromHex("#0F766E");
        private static readonly PdfColor Muted = PdfColor.FromHex("#6B7280");

        public static FlowDocument Build(string assets)
        {
            var noto = FontFamily.FromFiles(Path.Combine(assets, "NotoSans-sample.ttf"));

            var doc = new FlowDocument();
            doc.Info.Title = "Laporan Keuangan November 2026";
            doc.Info.Author = "LifeOS";
            doc.Styles.Normal = new TextStyle { Family = FontFamily.Helvetica, Size = 10, Fallbacks = { noto } };
            doc.Styles.Heading1.Text.Color = Accent;
            doc.Styles.Heading2.Text.Color = Accent;

            var section = doc.AddSection(new PageSetup { Margins = Margins.FromMillimeters(20, 15, 20, 15), DifferentFirstPage = true });
            Header(section.Header, "Laporan Keuangan · November 2026");
            Footer(section.Footer);
            Footer(section.FirstPageFooter);

            var cover = section.AddParagraph("LifeOS", new TextStyle { Size = 11, Bold = true, Color = Accent });
            cover.Format.SpaceAfter = 2;
            section.AddHeading("Laporan Keuangan November 2026");
            section.AddParagraph("Dibuat otomatis dari data transaksi · 30 November 2026", new TextStyle { Color = Muted, Size = 9 });

            section.AddHeading("Ringkasan", 2);
            var summary = section.AddParagraph()
                .AddText("Total pengeluaran bulan ini ")
                .AddText("Rp7.482.500", new TextStyle { Bold = true })
                .AddText(", turun ")
                .AddText("12%", new TextStyle { Bold = true, Color = PdfColor.Green })
                .AddText(" dibanding Oktober. Saving rate naik menjadi ")
                .AddText("31%", new TextStyle { Bold = true })
                .AddText(" karena pengeluaran makan dan transportasi berkurang, sementara tagihan tetap stabil. " +
                         "Paragraf ini rata kiri-kanan (justify): spasi antarkata diperlebar supaya kedua tepi lurus, " +
                         "kecuali baris terakhir yang tetap rapat seperti biasa.");
            summary.Format.Alignment = ParagraphAlignment.Justify;

            var highlights = section.AddList();
            highlights.AddItem("Pengeluaran terbesar: Makan (Rp2.150.000)");
            highlights.AddItem("Tagihan rutin lunas tepat waktu");
            var detail = highlights.AddList(ListKind.Numbered);
            detail.AddItem("Listrik — token PLN");
            detail.AddItem("Internet — langganan bulanan");
            highlights.AddItem("Catatan dalam bahasa lain tetap tampil berkat font fallback: Tiếng Việt, Ελληνικά, Русский");

            section.AddHeading("Anggaran per kategori", 2);
            BudgetTable(section);

            section.AddHeading("Grafik", 2);
            section.AddImage(PdfImage.FromFile(Path.Combine(assets, "landscape.jpg")), width: 260, alignment: ParagraphAlignment.Center);
            section.AddParagraph("Gambar: ilustrasi (gambar ditempatkan dalam alur dan diperkecil otomatis bila perlu).",
                new TextStyle { Italic = true, Size = 8, Color = Muted }).Format.Alignment = ParagraphAlignment.Center;

            section.AddHeading("Daftar transaksi", 2);
            TransactionTable(section);

            // Second section: two-column notes on a landscape page.
            var notes = doc.AddSection(new PageSetup { PageSize = PageSize.A4.Landscape, Columns = 2, ColumnGap = 24, Margins = Margins.FromMillimeters(15, 15, 15, 15) });
            Header(notes.Header, "Catatan & metodologi");
            Footer(notes.Footer);
            notes.AddHeading("Catatan & metodologi");
            for (var i = 1; i <= 14; i++)
            {
                notes.AddHeading(i + ". Catatan bagian " + i, 3);
                var p = notes.AddParagraph("Setiap angka di laporan ini dihitung dari transaksi yang tercatat, tanpa pembulatan sampai tahap tampilan. " +
                    "Teks mengalir dari kolom kiri ke kolom kanan, lalu ke halaman berikutnya. Judul selalu menempel dengan paragraf sesudahnya " +
                    "(keep with next), jadi tidak pernah tertinggal sendirian di bawah kolom.");
                p.Format.Alignment = ParagraphAlignment.Justify;
            }

            return doc;
        }

        private static void Header(HeaderFooter header, string title)
        {
            var table = header.AddTable();
            table.Border = null;
            table.GridLines = null;
            table.HeaderBackground = null;
            table.CellPadding = new Margins(0);
            table.SpaceBefore = table.SpaceAfter = 0;
            table.AddColumn(ColumnWidth.Relative());
            table.AddColumn(ColumnWidth.Auto, ParagraphAlignment.Right);
            var row = table.AddRow("LifeOS", title);
            ((Paragraph)row.Cells[0].Blocks[0]).Style = new TextStyle { Bold = true, Color = Accent, Size = 8 };
            ((Paragraph)row.Cells[1].Blocks[0]).Style = new TextStyle { Color = Muted, Size = 8 };
            header.AddHorizontalLine().SpaceAfter = 0;
        }

        private static void Footer(HeaderFooter footer)
        {
            var p = footer.AddParagraph(null, new TextStyle { Size = 8, Color = Muted })
                .AddText("Halaman ").AddPageNumber().AddText(" dari ").AddPageCount();
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.SpaceAfter = 0;
        }

        private static void BudgetTable(Section section)
        {
            var t = section.AddTable();
            t.AddColumn(ColumnWidth.Auto);
            t.AddColumn(ColumnWidth.Relative());
            t.AddColumn(ColumnWidth.Fixed(90), ParagraphAlignment.Right);
            t.AddColumn(ColumnWidth.Fixed(90), ParagraphAlignment.Right);
            t.HeaderRows = 1;
            t.AddRow("Kelompok", "Kategori", "Anggaran", "Realisasi");

            AddGroup(t, "Kebutuhan", new[] { "Makan", "Listrik", "Internet" }, new[] { 2_400_000L, 400_000L, 350_000L }, new[] { 2_150_000L, 300_000L, 350_000L });
            AddGroup(t, "Mobilitas", new[] { "Transportasi", "BBM" }, new[] { 800_000L, 600_000L }, new[] { 620_000L, 540_000L });
            AddGroup(t, "Lainnya", new[] { "Pendidikan", "Kesehatan", "Hiburan" }, new[] { 1_500_000L, 500_000L, 1_000_000L }, new[] { 1_499_000L, 125_500L, 1_898_000L });

            var total = t.AddRow(null!, null!, Rupiah(7_550_000), Rupiah(7_482_500));
            total.Cells[0].AddParagraph("Total", new TextStyle { Bold = true }).Format.SpaceAfter = 0;
            total.Cells[0].ColSpan = 2;
            total.Background = PdfColor.FromHex("#ECFDF5");
        }

        private static void AddGroup(Table t, string group, string[] categories, long[] budget, long[] actual)
        {
            for (var i = 0; i < categories.Length; i++)
            {
                var row = t.AddRow(i == 0 ? group : null!, categories[i], Rupiah(budget[i]), Rupiah(actual[i]));
                if (i == 0)
                {
                    row.Cells[0].RowSpan = categories.Length;
                    row.Cells[0].VerticalAlignment = VerticalAlignment.Middle;
                }

                if (actual[i] > budget[i])
                {
                    ((Paragraph)row.Cells[3].Blocks[0]).Style = new TextStyle { Color = PdfColor.Red, Bold = true };
                }
            }
        }

        private static void TransactionTable(Section section)
        {
            var t = section.AddTable();
            t.AddColumn(ColumnWidth.Auto);
            t.AddColumn(ColumnWidth.Auto);
            t.AddColumn(ColumnWidth.Relative());
            t.AddColumn(ColumnWidth.Fixed(80), ParagraphAlignment.Right);
            t.HeaderRows = 1;
            t.AlternateRowBackground = PdfColor.FromHex("#F9FAFB");
            t.AddRow("Tanggal", "Kategori", "Catatan", "Nominal");

            var categories = new[] { "Makan", "Transportasi", "Internet", "Listrik", "Kesehatan", "Pendidikan", "Hiburan", "BBM" };
            var notes = new[] { "Belanja mingguan", "Isi saldo e-money", "Langganan bulanan", "Token PLN", "Vitamin & obat", "Kursus online .NET", "Bioskop", "Pertalite" };
            var random = new Random(2026);
            for (var i = 0; i < 90; i++)
            {
                var k = random.Next(categories.Length);
                var day = 1 + i / 3;
                t.AddRow("2026-11-" + day.ToString("00", CultureInfo.InvariantCulture), categories[k], notes[k], Rupiah(random.Next(10, 600) * 1000L));
            }
        }

        private static string Rupiah(long amount) => "Rp" + amount.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
    }
}
