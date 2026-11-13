using System;
using System.IO;

namespace LifeOS.Pdf.Templates
{
    /// <summary>
    /// Ready-made templates. Each documents its data fields at the top of its XML
    /// (see <c>samples/LifeOS.Pdf.Samples/Assets/data/*.json</c> for complete examples).
    /// <code>
    /// BuiltInTemplates.Invoice.Render(TemplateData.FromJson(json)).Save("invoice.pdf");
    /// </code>
    /// </summary>
    public static class BuiltInTemplates
    {
        public static PdfTemplate Invoice => Get(BuiltInTemplate.Invoice);

        public static PdfTemplate Receipt => Get(BuiltInTemplate.Receipt);

        public static PdfTemplate Payslip => Get(BuiltInTemplate.Payslip);

        public static PdfTemplate MonthlyReport => Get(BuiltInTemplate.MonthlyReport);

        public static PdfTemplate Certificate => Get(BuiltInTemplate.Certificate);

        /// <summary>A built-in template with custom options (language, fallback fonts, named images…).</summary>
        public static PdfTemplate Get(BuiltInTemplate template, TemplateOptions? options = null)
        {
            using (var stream = typeof(BuiltInTemplates).Assembly.GetManifestResourceStream(ResourceName(template)))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Built-in template resource '" + ResourceName(template) + "' is missing from the assembly.");
                }

                return PdfTemplate.FromStream(stream, options);
            }
        }

        /// <summary>The XML source of a built-in template, as a starting point for your own.</summary>
        public static string GetSource(BuiltInTemplate template)
        {
            using (var stream = typeof(BuiltInTemplates).Assembly.GetManifestResourceStream(ResourceName(template)))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Built-in template resource is missing.")))
            {
                return reader.ReadToEnd();
            }
        }

        private static string ResourceName(BuiltInTemplate template)
        {
            switch (template)
            {
                case BuiltInTemplate.Invoice: return "LifeOS.Pdf.Templates.invoice.xml";
                case BuiltInTemplate.Receipt: return "LifeOS.Pdf.Templates.receipt.xml";
                case BuiltInTemplate.Payslip: return "LifeOS.Pdf.Templates.payslip.xml";
                case BuiltInTemplate.MonthlyReport: return "LifeOS.Pdf.Templates.monthly-report.xml";
                case BuiltInTemplate.Certificate: return "LifeOS.Pdf.Templates.certificate.xml";
                default: throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown built-in template.");
            }
        }
    }
}
