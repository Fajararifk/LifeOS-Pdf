using System;
using LifeOS.Pdf;
using LifeOS.Pdf.Forms;
using Xunit;

namespace LifeOS.Pdf.Tests
{
    public class FormTests
    {
        [Fact]
        public void CreateFields_WritesAcroFormAndWidgets()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.AddTextField("Nama", 72, 72, 200, 20).Value = "Budi";
            page.AddCheckBox("Setuju", 72, 110, 16, 16).Checked = true;
            page.AddRadioButton("Gender", "Pria", 72, 140, 14, 14);
            page.AddRadioButton("Gender", "Wanita", 100, 140, 14, 14);
            page.AddComboBox("Kota", 72, 170, 150, 20, "Jakarta", "Bandung");

            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Equal(1, pdf.CountObjects("/AcroForm"));
            Assert.Equal(1, pdf.CountObjects("/FT /Tx"));
            Assert.Equal(2, pdf.CountObjects("/FT /Btn")); // checkbox + radio group parent
            Assert.Equal(1, pdf.CountObjects("/FT /Ch"));
            Assert.Equal(5, pdf.CountObjects("/Subtype /Widget"));
        }

        [Fact]
        public void RadioButtons_ShareOneField()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var group = page.AddRadioButton("Gender", "Pria", 72, 140, 14, 14);
            page.AddRadioButton("Gender", "Wanita", 100, 140, 14, 14);
            group.Value = "Pria";

            Assert.Single(doc.Form.Fields);
            var radio = Assert.IsType<PdfRadioButton>(doc.Form.GetField("Gender"));
            Assert.Equal("Pria", radio.Value);

            Assert.Equal(2, radio.Widgets.Count);
            PdfStructure.Verify(doc.ToArray());
        }

        [Fact]
        public void OpenAndFill_RoundTrips()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.AddTextField("Nama", 72, 72, 200, 20).Value = "Budi";
            page.AddCheckBox("Setuju", 72, 110, 16, 16);

            var opened = PdfDocument.Open(doc.ToArray());
            Assert.Equal("Budi", ((PdfTextField)opened.Form.GetField("Nama")!).Value);

            opened.Form.SetValue("Nama", "Siti");
            opened.Form.SetValue("Setuju", false);
            var filled = PdfDocument.Open(opened.ToArray());

            Assert.Equal("Siti", ((PdfTextField)filled.Form.GetField("Nama")!).Value);
            Assert.False(((PdfCheckBox)filled.Form.GetField("Setuju")!).Checked);
        }

        [Fact]
        public void Flatten_BakesAppearanceAndRemovesForm()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.AddTextField("Nama", 72, 72, 200, 20).Value = "Budi";
            page.AddCheckBox("Setuju", 72, 110, 16, 16).Checked = true;
            doc.Form.Flatten();

            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Equal(0, pdf.CountObjects("/AcroForm"));
            Assert.Equal(0, pdf.CountObjects("/Subtype /Widget"));

            var reopened = PdfDocument.Open(doc.ToArray());
            Assert.Null(reopened.Form.GetField("Nama"));
            Assert.Contains("Budi", reopened.ExtractText());
        }

        [Fact]
        public void ExportImportJson_RoundTrips()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.AddTextField("Nama", 72, 72, 200, 20).Value = "Budi";
            page.AddCheckBox("Setuju", 72, 110, 16, 16).Checked = true;
            page.AddComboBox("Kota", 72, 170, 150, 20, "Jakarta", "Bandung").SelectedValues = new[] { "Bandung" };

            var json = doc.Form.ExportJson();
            Assert.Contains("\"Nama\":\"Budi\"", json);

            var doc2 = new PdfDocument();
            var p2 = doc2.AddPage();
            p2.AddTextField("Nama", 72, 72, 200, 20);
            p2.AddCheckBox("Setuju", 72, 110, 16, 16);
            p2.AddComboBox("Kota", 72, 170, 150, 20, "Jakarta", "Bandung");
            doc2.Form.ImportJson(json);

            Assert.Equal("Budi", ((PdfTextField)doc2.Form.GetField("Nama")!).Value);
            Assert.True(((PdfCheckBox)doc2.Form.GetField("Setuju")!).Checked);
            Assert.Equal(new[] { "Bandung" }, ((PdfChoiceField)doc2.Form.GetField("Kota")!).SelectedValues);
        }

        [Fact]
        public void CheckBox_AppearanceStateMatchesValue()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.AddCheckBox("Setuju", 72, 110, 16, 16).Checked = true; // OnValue = "Yes"

            var pdf = PdfStructure.Verify(doc.ToArray());
            Assert.Contains("/Yes", pdf.Text); // /AP /N state key, not a literal "/On"
            Assert.Contains("/Off", pdf.Text);
        }

        [Fact]
        public void ReadOnly_SetValue_Throws()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.AddTextField("Nama", 72, 72, 200, 20).ReadOnly = true;

            Assert.Throws<InvalidOperationException>(() => doc.Form.SetValue("Nama", "X"));
        }

        [Fact]
        public void SetValue_MissingField_Throws()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            Assert.Throws<ArgumentException>(() => doc.Form.SetValue("TidakAda", "X"));
        }

        [Fact]
        public void SameForm_ProducesIdenticalBytes()
        {
            byte[] Build()
            {
                var doc = new PdfDocument();
                var page = doc.AddPage();
                page.AddTextField("Nama", 72, 72, 200, 20).Value = "Budi";
                page.AddRadioButton("Gender", "Pria", 72, 140, 14, 14);
                return doc.ToArray();
            }

            Assert.Equal(Build(), Build());
        }
    }
}
