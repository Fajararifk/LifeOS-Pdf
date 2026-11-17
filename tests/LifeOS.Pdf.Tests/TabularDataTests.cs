using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LifeOS.Pdf.Templates;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    /// <summary>TabularData (CSV/XLSX) and the generic table report template.</summary>
    public class TabularDataTests
    {
        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

        private const string Csv = "Tanggal,No. Faktur,Pelanggan,Jumlah\r\n" +
                                   "2026-11-02,INV-001,\"PT Sinar Jaya, Cabang\",1250000\r\n" +
                                   "2026-11-05,INV-002,\"CV \"\"Mitra\"\" Abadi\",875000\r\n";

        // ------------------------------------------------------------------ CSV

        [Fact]
        public void Csv_ParsesQuotesEscapesAndCrlf()
        {
            var data = TabularData.FromCsv(Csv);

            Assert.Equal(new[] { "Tanggal", "No. Faktur", "Pelanggan", "Jumlah" }, data.Columns);
            Assert.Equal(new[] { "Tanggal", "No_Faktur", "Pelanggan", "Jumlah" }, data.Fields);
            Assert.Equal(2, data.Rows.Count);
            Assert.Equal("PT Sinar Jaya, Cabang", data.Rows[0]["Pelanggan"]);
            Assert.Equal("CV \"Mitra\" Abadi", data.Rows[1]["Pelanggan"]);
            Assert.Equal("INV-002", data.Rows[1]["No_Faktur"]); // case-insensitive, sanitized key
            Assert.Equal("1250000", data.Rows[0]["Jumlah"]);
        }

        [Fact]
        public void Csv_DetectsSemicolonAndTab()
        {
            var semicolons = TabularData.FromCsv("A;B;C\n1;2;3\n");
            Assert.Equal(new[] { "A", "B", "C" }, semicolons.Columns);
            Assert.Equal("3", semicolons.Rows[0]["C"]);

            var tabs = TabularData.FromCsv("A\tB\n1\t2\n");
            Assert.Equal(new[] { "A", "B" }, tabs.Columns);
            Assert.Equal("2", tabs.Rows[0]["B"]);
        }

        [Fact]
        public void Csv_WithoutHeader_UsesGeneratedColumns()
        {
            var data = TabularData.FromCsv("1,2\n3,4\n", hasHeader: false);

            Assert.Equal(new[] { "Column1", "Column2" }, data.Columns);
            Assert.Equal(2, data.Rows.Count);
            Assert.Equal("3", data.Rows[1]["Column1"]);
        }

        [Fact]
        public void Csv_SanitizesDuplicateAndNumericHeaders()
        {
            var data = TabularData.FromCsv("Jumlah,Jumlah,2026,\"\",Nama Lengkap\n1,2,3,4,Ani\n");

            Assert.Equal(new[] { "Jumlah", "Jumlah_2", "_2026", "Column4", "Nama_Lengkap" }, data.Fields);
            Assert.Equal("2", data.Rows[0]["Jumlah_2"]);
        }

        [Fact]
        public void Csv_ComputesAlignsAndTotals()
        {
            var data = TabularData.FromCsv("Nama,Qty,Harga\nAni,2,1500\nBudi,3,2500\n");

            Assert.Equal(new[] { "left", "right", "right" }, data.Aligns);
            Assert.True(data.HasTotals);
            Assert.Equal(5m, data.Totals["Qty"]);
            Assert.Equal(4000m, data.Totals["Harga"]);
            Assert.False(data.Totals.ContainsKey("Nama"));
        }

        [Fact]
        public void Csv_MixedColumnIsNotNumeric()
        {
            var data = TabularData.FromCsv("Nilai\n100\nn/a\n");

            Assert.Equal(new[] { "left" }, data.Aligns);
            Assert.False(data.HasTotals);
        }

        [Fact]
        public void Csv_Empty_GivesNoColumns()
        {
            var data = TabularData.FromCsv(string.Empty);

            Assert.Empty(data.Columns);
            Assert.Empty(data.Rows);
            Assert.False(data.HasTotals);
        }

        // ------------------------------------------------------------------ XLSX

        [Fact]
        public void Xlsx_ReadsTextNumbersBooleansAndDates()
        {
            var number = 1250.5;
            var date = 46000.0; // OLE automation date
            var sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                        Row(1, TextCell("A1", 0) + TextCell("B1", 1) + TextCell("C1", 2) + TextCell("D1", 3)) +
                        Row(2, TextCell("A2", 4) + "<c r=\"B2\"><v>" + number.ToString(CultureInfo.InvariantCulture) + "</v></c>" +
                                "<c r=\"C2\" s=\"1\"><v>" + date.ToString(CultureInfo.InvariantCulture) + "</v></c>" +
                                "<c r=\"D2\" t=\"b\"><v>1</v></c>") +
                        "</sheetData></worksheet>";

            var data = TabularData.FromXlsx(BuildXlsx(sheet));

            Assert.Equal(new[] { "Nama", "Nilai", "Tanggal", "Aktif" }, data.Columns);
            var row = data.Rows.Single();
            Assert.Equal("Ani", row["Nama"]);
            Assert.Equal("1250.5", row["Nilai"]);
            Assert.Equal(DateTime.FromOADate(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row["Tanggal"]);
            Assert.Equal("true", row["Aktif"]);
            Assert.Equal("right", data.Aligns[1]);
            Assert.Equal("left", data.Aligns[2]); // dates are text in the report
        }

        [Fact]
        public void Xlsx_ReadsIsoDateCell()
        {
            var sheet = "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                        Row(1, "<c r=\"A1\" t=\"inlineStr\"><is><t>Tanggal</t></is></c>") +
                        Row(2, "<c r=\"A2\" t=\"d\"><v>2026-11-02T00:00:00</v></c>") +
                        "</sheetData></worksheet>";

            var data = TabularData.FromXlsx(BuildXlsx(sheet));

            Assert.Equal(new[] { "Tanggal" }, data.Columns);
            Assert.Equal("2026-11-02", data.Rows.Single()["Tanggal"]);
        }

        [Fact]
        public void Xlsx_ReadsNamedSheet()
        {
            var sheet = "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                        Row(1, TextCell("A1", 0)) + Row(2, "<c r=\"A2\" t=\"inlineStr\"><is><t>Data</t></is></c>") +
                        "</sheetData></worksheet>";
            var other = "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                        Row(1, TextCell("A1", 5)) + "</sheetData></worksheet>";

            var workbook = "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                           "<sheets><sheet name=\"Ringkasan\" sheetId=\"1\" r:id=\"rId1\"/>" +
                           "<sheet name=\"Detail\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>";
            var rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                       "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                       "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"/xl/worksheets/sheet2.xml\"/>" +
                       "</Relationships>";

            var data = TabularData.FromXlsx(BuildXlsx(sheet: sheet, other: other, workbook: workbook, rels: rels), "Detail");

            Assert.Equal(new[] { "Kolom" }, data.Columns);
            Assert.Empty(data.Rows);
        }

        // ------------------------------------------------------------------ template

        [Fact]
        public void TableReport_RendersEveryColumnFromCsv()
        {
            var data = TabularData.FromCsv(Csv);
            data.Title = "Rekap Faktur";
            data.Subtitle = "November 2026";

            var pdf = BuiltInTemplates.TableReport.Render(data);
            var text = pdf.ExtractText();
            PdfStructure.Verify(pdf.ToArray());

            Assert.Contains("Rekap Faktur", text);
            Assert.Contains("November 2026", text);
            Assert.Contains("No. Faktur", text);
            Assert.Contains("PT Sinar Jaya, Cabang", text);
            Assert.Contains("TOTAL", text);
            Assert.Contains("2.125.000", text); // 1.250.000 + 875.000
            Assert.Contains("Halaman", text);
        }

        [Fact]
        public void TableReport_HandlesAnyColumnCount()
        {
            var header = string.Join(",", Enumerable.Range(1, 8).Select(i => "Kolom" + i));
            var values = string.Join(",", Enumerable.Range(1, 8).Select(i => (i * 100).ToString(CultureInfo.InvariantCulture)));
            var data = TabularData.FromCsv(header + "\n" + values + "\n");

            var pdf = BuiltInTemplates.TableReport.Render(data);
            var text = pdf.ExtractText();
            PdfStructure.Verify(pdf.ToArray());

            Assert.Contains("Kolom8", text);
            Assert.Contains("800", text);
        }

        [Fact]
        public void TableReport_EmptyData_RendersWithoutTable()
        {
            var data = TabularData.FromCsv(string.Empty);
            data.Title = "Kosong";

            var pdf = BuiltInTemplates.TableReport.Render(data);
            var text = pdf.ExtractText();
            PdfStructure.Verify(pdf.ToArray());

            Assert.Contains("Kosong", text);
            Assert.Contains("Tidak ada data", text);
        }

        [Fact]
        public void CustomTemplate_ReadsSanitizedFieldsByName()
        {
            var data = TabularData.FromCsv(Csv);
            var template = PdfTemplate.Parse(
                "<document><section><table><column width='*'/><column width='*'/>" +
                "<row><cell>No</cell><cell>Jumlah</cell></row>" +
                "<foreach in='Rows' as='row'><row>" +
                "<cell>{{ row.No_Faktur }}</cell><cell>{{ row.Jumlah | rupiah }}</cell>" +
                "</row></foreach></table></section></document>");

            var text = template.Render(data).ExtractText();

            Assert.Contains("INV-001", text);
            Assert.Contains("Rp1.250.000", text);
        }

        [Fact]
        public void TableReport_SampleCsvFile_IsValid()
        {
            var data = TabularData.FromCsvFile(Asset(Path.Combine("data", "table-report.csv")));
            data.Title = "Rekap Penjualan";

            Assert.True(data.Rows.Count >= 3);
            Assert.Equal("right", data.Aligns[data.Aligns.Count - 1]);

            var pdf = BuiltInTemplates.TableReport.Render(data);
            Assert.Contains("Rekap Penjualan", pdf.ExtractText());
            PdfStructure.Verify(pdf.ToArray());
        }

        // ------------------------------------------------------------------ helpers

        private static string Row(int index, string cells) => "<row r=\"" + index + "\">" + cells + "</row>";

        private static string TextCell(string reference, int sharedStringIndex) =>
            "<c r=\"" + reference + "\" t=\"s\"><v>" + sharedStringIndex + "</v></c>";

        private static byte[] BuildXlsx(string sheet, string? other = null, string? workbook = null, string? rels = null)
        {
            var shared = new[] { "Nama", "Nilai", "Tanggal", "Aktif", "Ani", "Kolom" };
            var sharedXml = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                            string.Concat(shared.Select(s => "<si><t>" + s + "</t></si>")) + "</sst>";
            var styles = "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                         "<cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>";

            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "xl/worksheets/sheet1.xml", sheet);
                if (other != null)
                {
                    Add(zip, "xl/worksheets/sheet2.xml", other);
                }

                Add(zip, "xl/sharedStrings.xml", sharedXml);
                Add(zip, "xl/styles.xml", styles);
                if (workbook != null)
                {
                    Add(zip, "xl/workbook.xml", workbook);
                }

                if (rels != null)
                {
                    Add(zip, "xl/_rels/workbook.xml.rels", rels);
                }
            }

            return stream.ToArray();
        }

        private static void Add(ZipArchive zip, string path, string xml)
        {
            var entry = zip.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
    }
}
