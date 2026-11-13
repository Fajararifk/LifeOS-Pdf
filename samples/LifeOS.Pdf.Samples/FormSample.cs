using System;
using System.IO;
using LifeOS.Pdf;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Forms;

namespace LifeOS.Pdf.Samples
{
    /// <summary>Builds a fillable form and its flattened twin (Fase 8).</summary>
    internal static class FormSample
    {
        public static void Run(string outputDir)
        {
            var form = BuildForm();
            var path = Path.Combine(outputDir, "form.pdf");
            form.Save(path);
            Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");

            var flattened = BuildForm();
            flattened.Form.Flatten();
            var flatPath = Path.Combine(outputDir, "form-flattened.pdf");
            flattened.Save(flatPath);
            Console.WriteLine("Wrote " + flatPath + " (" + new FileInfo(flatPath).Length + " bytes)");
        }

        private static PdfDocument BuildForm()
        {
            var doc = new PdfDocument();
            doc.Info.Title = "LifeOS.Pdf — Form";
            doc.Info.Subject = "Contoh AcroForm (Fase 8)";
            var page = doc.AddPage();
            var g = page.Graphics;
            var ink = PdfColor.FromHex("#1F2937");

            g.FillRectangle(PdfColor.FromHex("#0F766E"), 0, 0, page.Width, 8);
            g.DrawText("Formulir Pendaftaran", new PdfFont(StandardFont.HelveticaBold, 22), 56, 48, ink);
            g.DrawText("Form interaktif — bisa diisi di viewer, lalu di-flatten jadi statis.",
                new PdfFont(StandardFont.Helvetica, 10), 56, 78, PdfColor.FromHex("#6B7280"));

            var y = 116.0;
            Label(g, "Nama Lengkap", y);
            page.AddTextField("Nama", 56, y + 18, 280, 22).Value = "Budi Santoso";
            y += 64;

            Label(g, "Jenis Kelamin", y);
            var gender = page.AddRadioButton("Gender", "Pria", 56, y + 18, 14, 14);
            page.AddRadioButton("Gender", "Wanita", 130, y + 18, 14, 14);
            gender.Value = "Pria";
            g.DrawText("Pria", new PdfFont(StandardFont.Helvetica, 10), 76, y + 22, ink);
            g.DrawText("Wanita", new PdfFont(StandardFont.Helvetica, 10), 150, y + 22, ink);
            y += 64;

            Label(g, "Kota", y);
            page.AddComboBox("Kota", 56, y + 18, 200, 22, "Jakarta", "Bandung", "Surabaya").SelectedValues = new[] { "Bandung" };
            y += 64;

            Label(g, "Persetujuan", y);
            page.AddCheckBox("Setuju", 56, y + 18, 16, 16).Checked = true;
            g.DrawText("Saya setuju dengan syarat & ketentuan", new PdfFont(StandardFont.Helvetica, 10), 80, y + 22, ink);
            y += 64;

            page.AddButton("Reset", 56, y + 18, 110, 26, "Reset Form").Action = ButtonAction.Reset;
            page.AddSignatureField("TandaTangan", 360, y + 8, 180, 40);

            return doc;
        }

        private static void Label(PdfGraphics g, string text, double y)
        {
            g.DrawText(text.ToUpperInvariant(), new PdfFont(StandardFont.HelveticaBold, 9), 56, y, PdfColor.FromHex("#0F766E"));
        }
    }
}
