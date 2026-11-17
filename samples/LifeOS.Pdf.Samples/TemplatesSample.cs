using System;
using System.IO;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 3 showcase: renders every built-in template with the JSON data in Assets/data,
    /// plus the generic table report from <c>data/table-report.csv</c> (same path for XLSX).
    /// The JSON uses "logo" as the image name; it is supplied through TemplateOptions.Images,
    /// and Noto Sans is added as a fallback font for non-Latin names.
    /// </summary>
    internal static class TemplatesSample
    {
        public static void Run(string assets, string outputDir)
        {
            var options = new TemplateOptions();
            options.Images["logo"] = PdfImage.FromFile(Path.Combine(assets, "badge.png"));
            options.FallbackFonts.Add(FontFamily.FromFiles(Path.Combine(assets, "NotoSans-sample.ttf")));

            Render(BuiltInTemplate.Invoice, "invoice", assets, outputDir, options);
            Render(BuiltInTemplate.Receipt, "receipt", assets, outputDir, options);
            Render(BuiltInTemplate.Payslip, "payslip", assets, outputDir, options);
            Render(BuiltInTemplate.MonthlyReport, "monthly-report", assets, outputDir, options);
            Render(BuiltInTemplate.Certificate, "certificate", assets, outputDir, options);

            // CSV/XLSX: the table report takes its columns and rows from the data.
            var sales = TabularData.FromCsvFile(Path.Combine(assets, "data", "table-report.csv"));
            sales.Title = "Rekap Penjualan";
            sales.Subtitle = "November 2026";
            var tablePath = Path.Combine(outputDir, "template-table-report.pdf");
            BuiltInTemplates.TableReport.Render(sales).Save(tablePath);
            Console.WriteLine("Wrote " + tablePath + " (" + new FileInfo(tablePath).Length + " bytes)");
        }

        private static void Render(BuiltInTemplate template, string name, string assets, string outputDir, TemplateOptions options)
        {
            var data = TemplateData.FromJson(File.ReadAllText(Path.Combine(assets, "data", name + ".json")));
            var path = Path.Combine(outputDir, "template-" + name + ".pdf");
            BuiltInTemplates.Get(template, options).Render(data).Save(path);
            Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");
        }
    }
}
