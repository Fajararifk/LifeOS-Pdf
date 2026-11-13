using System;
using System.Collections.Generic;
using LifeOS.Pdf.Images.Png;
using ZXingCpp;

namespace LifeOS.Pdf.BarcodeScanning
{
    /// <summary>How pixel bytes are laid out for <see cref="BarcodeReader.Read"/>.</summary>
    public enum PixelFormat
    {
        /// <summary>One byte per pixel, grayscale.</summary>
        Gray8,

        /// <summary>Three bytes per pixel, RGB.</summary>
        Rgb24,

        /// <summary>Four bytes per pixel, RGBA.</summary>
        Rgba32,

        /// <summary>Four bytes per pixel, BGRA.</summary>
        Bgra32,
    }

    /// <summary>Reads QR codes and barcodes from images (zxing-cpp, Apache-2.0).</summary>
    public sealed class BarcodeReader
    {
        /// <summary>Decodes one barcode from raw pixel data; null when nothing is found.</summary>
        public BarcodeReadResult? Read(byte[] pixels, int width, int height, PixelFormat format)
        {
            var results = ReadAll(pixels, width, height, format);
            return results.Count > 0 ? results[0] : null;
        }

        /// <summary>Decodes every barcode from raw pixel data.</summary>
        public IReadOnlyList<BarcodeReadResult> ReadAll(byte[] pixels, int width, int height, PixelFormat format)
        {
            using var opts = new ReaderOptions { TryHarder = true, TryRotate = true, TryInvert = true, TryDownscale = true };
            var view = new ImageView(pixels, width, height, Map(format), 0, 0);
            var results = ZXingCpp.BarcodeReader.Read(view, opts);
            try
            {
                var list = new List<BarcodeReadResult>(results.Length);
                foreach (var b in results)
                {
                    list.Add(new BarcodeReadResult(b.Text, Map(b.Format.ToString())));
                }

                return list;
            }
            finally
            {
                foreach (var b in results)
                {
                    b.Dispose();
                }
            }
        }

        /// <summary>Decodes one barcode from PNG bytes; null when the image is invalid or has no barcode.</summary>
        public BarcodeReadResult? ReadPng(byte[] png)
        {
            var results = ReadAllPng(png);
            return results.Count > 0 ? results[0] : null;
        }

        /// <summary>Decodes every barcode from PNG bytes.</summary>
        public IReadOnlyList<BarcodeReadResult> ReadAllPng(byte[] png)
        {
            try
            {
                var image = PngImage.Decode(png, forceDecode: true);
                if (image.DecodedColor == null)
                {
                    return Array.Empty<BarcodeReadResult>();
                }

                return ReadAll(image.DecodedColor, image.Width, image.Height, image.Components == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24);
            }
            catch (Exception)
            {
                return Array.Empty<BarcodeReadResult>();
            }
        }

        private static ImageFormat Map(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.Gray8: return ImageFormat.Lum;
                case PixelFormat.Rgba32: return ImageFormat.RGBA;
                case PixelFormat.Bgra32: return ImageFormat.BGRA;
                default: return ImageFormat.RGB;
            }
        }

        private static BarcodeFormat Map(string name)
        {
            switch (name)
            {
                case "QR Code": return BarcodeFormat.QrCode;
                case "Code 128": return BarcodeFormat.Code128;
                case "Code 39": return BarcodeFormat.Code39;
                case "EAN-13": return BarcodeFormat.Ean13;
                case "EAN-8": return BarcodeFormat.Ean8;
                case "UPC-A": return BarcodeFormat.UpcA;
                case "ITF": return BarcodeFormat.Itf;
                default: return BarcodeFormat.Other;
            }
        }
    }
}
