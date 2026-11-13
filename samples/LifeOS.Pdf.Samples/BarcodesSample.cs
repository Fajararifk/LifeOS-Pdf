using System;
using System.IO;
using LifeOS.Pdf.Barcodes;
using LifeOS.Pdf.Drawing;
using LifeOS.Pdf.Fonts;
using LifeOS.Pdf.Images;
using LifeOS.Pdf.Layout;

namespace LifeOS.Pdf.Samples
{
    /// <summary>
    /// Fase 4 showcase: QR codes (with logo, colors, QRIS) and every linear barcode, drawn as
    /// vectors on a page, plus PNG/SVG export and the layout-engine blocks.
    /// </summary>
    internal static class BarcodesSample
    {
        // Sample static QRIS (fictional merchant) with a valid CRC.
        public const string StaticQris = "00020101021126570011ID.DANA.WWW011893600915012345678902091234567890303UMI51440014ID.CO.QRIS.WWW0215ID10200000000000303UMI5204581253033605802ID5920FAJAR DIGITAL STUDIO6015JAKARTA SELATAN610512160630480D9";

        public static void Run(string assets, string outputDir)
        {
            var doc = new PdfDocument();
            doc.Info.Title = "LifeOS.Pdf — QR & Barcode";
            var page = doc.AddPage(PageSize.A4);
            var g = page.Graphics;
            var title = new PdfFont(StandardFont.HelveticaBold, 22);
            var label = new PdfFont(StandardFont.Helvetica, 8.5);
            var muted = PdfColor.FromHex("#6B7280");

            g.DrawText("QR code & barcode", title, 56, 48);
            g.DrawText("Dibuat dari nol (ISO/IEC 18004, 15417, 16388, 15420, 16390) dan digambar sebagai vektor.", label, 56, 80, muted);

            // QR codes.
            var url = QrCode.Encode("https://github.com/Fajararifk/LifeOS-Pdf");
            g.DrawQrCode(url, 56, 110, 130);
            g.DrawText("URL · " + url, label, 56, 246, muted);

            var logo = QrCode.Encode("https://lifeos.app/inv/2026-0042", QrErrorCorrection.High);
            g.DrawQrCode(logo, 226, 110, 130, logo: PdfImage.FromFile(Path.Combine(assets, "badge.png")), logoScale: 0.22);
            g.DrawText("Dengan logo (ECC High)", label, 226, 246, muted);

            var dynamic = Qris.WithAmount(StaticQris, 125000);
            g.DrawQrCode(QrCode.Encode(dynamic), 396, 110, 130, dark: PdfColor.FromHex("#0F766E"));
            g.DrawText("QRIS dinamis Rp125.000", label, 396, 246, muted);

            // Linear barcodes.
            var y = 290.0;
            foreach (var (code, name) in new[]
            {
                (Barcode.Code128("INV-2026-0042"), "Code 128"),
                (Barcode.Code39("LIFEOS-42", checkDigit: true), "Code 39 + check"),
                (Barcode.Ean13("899123456789"), "EAN-13"),
                (Barcode.Ean8("8991234"), "EAN-8"),
                (Barcode.UpcA("03600029145"), "UPC-A"),
                (Barcode.Itf14("1899123456789"), "ITF-14"),
            })
            {
                g.DrawText(name, label, 56, y + 22, muted);
                g.DrawBarcode(code, 150, y, 240, 58);
                y += 76;
            }

            doc.Save(Path.Combine(outputDir, "barcodes.pdf"));
            File.WriteAllBytes(Path.Combine(outputDir, "qr.png"), url.ToPng(moduleSize: 8));
            File.WriteAllText(Path.Combine(outputDir, "qr.svg"), url.ToSvg());
            File.WriteAllBytes(Path.Combine(outputDir, "code128.png"), Barcode.Code128("INV-2026-0042").ToPng(moduleWidth: 3, height: 90));
            Console.WriteLine("Wrote " + Path.Combine(outputDir, "barcodes.pdf") + " (+ qr.png, qr.svg, code128.png)");

            // Layout engine: QR and barcode blocks in the flow.
            var flow = new FlowDocument();
            flow.Info.Title = "Tiket";
            var section = flow.AddSection(new PageSetup { PageSize = PageSize.A5.Landscape });
            section.AddHeading("Tiket .NET Conf Indonesia 2026");
            section.AddParagraph("Tunjukkan QR ini di pintu masuk. Kode cadangan tercetak di bawah.");
            section.AddQrCode("TICKET-2026-000731", 110, ParagraphAlignment.Center);
            section.AddBarcode(Barcode.Code128("TICKET-2026-000731"), 220, 46, ParagraphAlignment.Center);
            flow.Save(Path.Combine(outputDir, "ticket.pdf"));
            Console.WriteLine("Wrote " + Path.Combine(outputDir, "ticket.pdf"));
        }
    }
}
