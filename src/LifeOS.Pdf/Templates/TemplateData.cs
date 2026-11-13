using LifeOS.Pdf.Templates.Json;

namespace LifeOS.Pdf.Templates
{
    /// <summary>Helpers for template data.</summary>
    public static class TemplateData
    {
        /// <summary>
        /// Parses JSON into data a template can bind to: objects become dictionaries with
        /// case-insensitive keys, arrays become lists, numbers become decimals.
        /// Throws <see cref="TemplateException"/> with the line and column of invalid JSON.
        /// </summary>
        public static object? FromJson(string json) => JsonParser.Parse(json);
    }
}
