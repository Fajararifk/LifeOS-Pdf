using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Tests
{
    /// <summary>Builds small hand-written PDFs (objects as text) with a correct cross-reference table.</summary>
    internal static class RawPdf
    {
        /// <summary>A one-page document; <paramref name="extra"/> objects are numbered from 4.</summary>
        public static string OnePage(string pageExtra = "", string content = "BT /F1 12 Tf 72 720 Td (Halo) Tj ET", params string[] extra)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Contents " + (4 + extra.Length) + " 0 R " +
                "/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> " + pageExtra + " >>",
            };
            objects.AddRange(extra);
            objects.Add("<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream");
            return Build(objects);
        }

        public static string Build(IList<string> objects, string trailerExtra = "")
        {
            var sb = new StringBuilder("%PDF-1.7\n");
            var offsets = new List<int>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(sb.Length);
                sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }

            var xref = sb.Length;
            sb.Append("xref\n0 ").Append(objects.Count + 1).Append('\n').Append("0000000000 65535 f \n");
            foreach (var o in offsets)
            {
                sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }

            sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R ").Append(trailerExtra).Append(" >>\nstartxref\n")
              .Append(xref).Append("\n%%EOF\n");
            return sb.ToString();
        }

        public static byte[] Bytes(string text) => Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
    }
}
