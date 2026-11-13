using System;
using System.IO;
using System.Text;
using LifeOS.Pdf.Annotations;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Navigation;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 5 showcase: a small handbook with a table of contents, bookmarks from headings,
    /// internal and web links, page labels, attachments and annotations.
    /// </summary>
    internal static class NavigationSample
    {
        public static void Run(string outputDir)
        {
            var doc = new FlowDocument();
            doc.Info.Title = "LifeOS.Pdf — Navigasi & Interaktif";
            doc.Info.Author = "LifeOS.Pdf";
            doc.ViewerPreferences.InitialZoom = InitialZoom.FitWidth;
            doc.Attachments.Add("pengeluaran.csv", Encoding.UTF8.GetBytes("tanggal,kategori,jumlah\n2026-11-01,Makan,45000\n2026-11-02,Transport,23000\n"),
                "text/csv", "Data mentah laporan");
            doc.Attachments.Add("ringkasan.json", Encoding.UTF8.GetBytes("{\"bulan\":\"2026-11\",\"total\":68000}"), "application/json");

            var section = doc.AddSection();
            section.Footer.AddParagraph().AddPageNumber().Format.Alignment = ParagraphAlignment.Center;
            section.AddTableOfContents("Daftar Isi");
            section.AddPageBreak();

            var chapters = new[]
            {
                ("Pendahuluan", new[] { "Tentang buku ini", "Cara membaca" }),
                ("Mengelola Keuangan", new[] { "Mencatat pengeluaran", "Anggaran bulanan", "Laporan" }),
                ("Kebiasaan & Tujuan", new[] { "Membangun kebiasaan", "Meninjau tujuan" }),
            };

            var c = 0;
            foreach (var (chapter, subs) in chapters)
            {
                c++;
                var h = section.AddHeading(c + ". " + chapter);
                h.Anchor = "bab-" + c;
                if (c > 1)
                {
                    h.Format.PageBreakBefore = true;
                }

                var s = 0;
                foreach (var sub in subs)
                {
                    s++;
                    section.AddHeading(c + "." + s + " " + sub, 2);
                    for (var i = 0; i < 4; i++)
                    {
                        section.AddParagraph(
                            "Setiap bagian dari buku ini dapat dibuka langsung dari daftar isi atau panel bookmark. " +
                            "Teks seperti ini mengalir otomatis ke halaman berikutnya bila ruangnya habis.").Format.Alignment = ParagraphAlignment.Justify;
                    }
                }

                section.AddParagraph()
                    .AddText("Lanjut ke ")
                    .AddLink(c < chapters.Length ? "bab berikutnya" : "awal buku", c < chapters.Length ? "#bab-" + (c + 1) : "#bab-1")
                    .AddText(", buka ")
                    .AddLink("repositori LifeOS.Pdf", "https://github.com/Fajararifk/LifeOS-Pdf")
                    .AddText(" atau kirim email ke ")
                    .AddLink("tim kami", "mailto:halo@example.com?subject=LifeOS.Pdf");
            }

            var pdf = doc.Render();

            // Front matter in roman numerals, the body starting at 1.
            pdf.PageLabels.Add(0, PageLabelStyle.LowerRoman);
            pdf.PageLabels.Add(1, PageLabelStyle.Decimal);

            // An annotated page at the end.
            var page = pdf.AddPage(PageSize.A4);
            var g = page.Graphics;
            var title = new PdfFont(StandardFont.HelveticaBold, 18);
            var body = new PdfFont(StandardFont.Helvetica, 11);
            g.DrawText("Anotasi", title, 56, 56);
            g.DrawText("Total pengeluaran November: Rp68.000", body, 56, 100);
            page.AddHighlight(54, 97, 218, 16, comment: "Angka ini sudah dicek", author: "LifeOS");
            page.AddNote(290, 96, "Bandingkan dengan bulan Oktober.", "LifeOS", icon: NoteIcon.Comment);
            page.AddStamp(360, 160, 170, 64, "LUNAS");
            page.AddStamp(360, 240, 170, 64, "DRAFT", PdfColor.FromHex("#1D4ED8"));
            g.DrawText("Klik untuk kembali ke daftar isi", body, 56, 180, PdfColor.FromHex("#1D4ED8"));
            page.AddLink(56, 168, 170, 16, PdfDestination.FitPage(pdf.Pages[0]));
            pdf.Outline.Add("Anotasi", PdfDestination.ToPage(page)).Italic = true;
            pdf.PageLabels.Add(pdf.Pages.Count - 1, PageLabelStyle.UpperLetters, "Lampiran ");

            var path = Path.Combine(outputDir, "navigation.pdf");
            pdf.Save(path);
            Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");
        }
    }
}
