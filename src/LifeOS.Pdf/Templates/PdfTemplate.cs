using System;
using System.IO;
using System.Text;
using LifeOS.Pdf.Layout;
using LifeOS.Pdf.Templates.Markup;

namespace LifeOS.Pdf.Templates
{
    /// <summary>
    /// A report template (XML) that is filled with data to produce a PDF.
    /// Parse once, render many times; a template is safe to use from several threads.
    /// <code>
    /// var template = PdfTemplate.Load("templates/invoice.xml");
    /// template.Render(invoice).Save("invoice.pdf");
    /// template.Render(TemplateData.FromJson(json)).Save("invoice.pdf");
    /// </code>
    /// </summary>
    public sealed class PdfTemplate
    {
        private readonly TemplateNode _root;
        private readonly TemplateOptions _options;
        private readonly ResourceCache _cache = new ResourceCache();

        private PdfTemplate(TemplateNode root, TemplateOptions options)
        {
            _root = root;
            _options = options;
        }

        /// <summary>Loads a template file. Relative image and font paths resolve against its folder.</summary>
        public static PdfTemplate Load(string path, TemplateOptions? options = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var opts = (options ?? new TemplateOptions()).Clone();
            if (string.IsNullOrEmpty(opts.BaseDirectory))
            {
                opts.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            }

            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return new PdfTemplate(TemplateReader.Read(reader), opts);
            }
        }

        /// <summary>Parses template XML from a string.</summary>
        public static PdfTemplate Parse(string xml, TemplateOptions? options = null)
        {
            if (xml == null)
            {
                throw new ArgumentNullException(nameof(xml));
            }

            using (var reader = new StringReader(xml))
            {
                return new PdfTemplate(TemplateReader.Read(reader), (options ?? new TemplateOptions()).Clone());
            }
        }

        /// <summary>Reads template XML from a stream (UTF-8 unless a BOM says otherwise).</summary>
        public static PdfTemplate FromStream(Stream stream, TemplateOptions? options = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
            {
                return new PdfTemplate(TemplateReader.Read(reader), (options ?? new TemplateOptions()).Clone());
            }
        }

        /// <summary>
        /// Fills the template with <paramref name="data"/> (a C# object, dictionary, or the
        /// result of <see cref="TemplateData.FromJson"/>) and returns the flow document, which
        /// can still be changed before rendering.
        /// </summary>
        public FlowDocument Bind(object? data) => new Binder(_options, _cache).Bind(_root, data);

        /// <summary>Fills the template with data and lays it out into a PDF document.</summary>
        public PdfDocument Render(object? data) => Bind(data).Render();
    }
}
