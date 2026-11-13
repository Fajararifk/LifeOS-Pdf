using System;
using System.IO;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 3 showcase: renders every built-in template with the JSON data in Assets/data.
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
