using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Barcodes
{
    /// <summary>
    /// Helpers for QRIS payloads (Indonesia's QR payment standard, based on the EMVCo
    /// Merchant-Presented QR specification): a sequence of tag-length-value fields ending in a
    /// CRC-16/CCITT-FALSE checksum (tag 63).
    /// <code>
    /// // A merchant's static QRIS (from the bank/PJP) → a dynamic one for one invoice
    /// var payload = Qris.WithAmount(staticQris, 125000);
    /// page.Graphics.DrawQrCode(QrCode.Encode(payload), 400, 60, 140);
    /// </code>
    /// The library only edits the amount; merchant data must come from your payment provider.
    /// </summary>
    public static class Qris
    {
        /// <summary>CRC-16/CCITT-FALSE (polynomial 0x1021, initial value 0xFFFF) of the ASCII text.</summary>
        public static ushort ComputeCrc(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var crc = 0xFFFF;
            foreach (var c in text)
            {
                crc ^= (byte)c << 8;
                for (var i = 0; i < 8; i++)
                {
                    crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
                    crc &= 0xFFFF;
                }
            }

            return (ushort)crc;
        }

        /// <summary>True when the payload is a well-formed TLV sequence starting with tag 00 and its CRC (tag 63) is correct.</summary>
        public static bool IsValid(string payload)
        {
            if (payload == null || payload.Length < 12 || !payload.StartsWith("000201", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var fields = Parse(payload);
                if (fields.Count == 0 || fields[fields.Count - 1].Key != "63" || fields[fields.Count - 1].Value.Length != 4)
                {
                    return false;
                }

                var expected = ComputeCrc(payload.Substring(0, payload.Length - 4)).ToString("X4", CultureInfo.InvariantCulture);
                return string.Equals(expected, fields[fields.Count - 1].Value, StringComparison.OrdinalIgnoreCase);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>The top-level fields (tag, value) in order. Throws <see cref="FormatException"/> for broken TLV data.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Parse(string payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var fields = new List<KeyValuePair<string, string>>();
            var pos = 0;
            while (pos < payload.Length)
            {
                if (pos + 4 > payload.Length)
                {
                    throw new FormatException("QRIS payload is truncated at position " + pos + ".");
                }

                var tag = payload.Substring(pos, 2);
                if (!int.TryParse(payload.Substring(pos + 2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var length) || !IsDigits(tag))
                {
                    throw new FormatException("QRIS payload has an invalid tag or length at position " + pos + ".");
                }

                if (pos + 4 + length > payload.Length)
                {
                    throw new FormatException("QRIS field " + tag + " at position " + pos + " is longer than the payload.");
                }

                fields.Add(new KeyValuePair<string, string>(tag, payload.Substring(pos + 4, length)));
                pos += 4 + length;
            }

            return fields;
        }

        /// <summary>
        /// Turns a static QRIS into a dynamic one for a fixed amount: point of initiation (tag 01)
        /// becomes "12", the amount is written to tag 54 and the CRC is recalculated.
        /// </summary>
        /// <param name="payload">A valid QRIS payload (static or dynamic).</param>
        /// <param name="amount">Amount in rupiah, greater than zero (at most 2 decimals).</param>
        public static string WithAmount(string payload, decimal amount)
        {
            if (!IsValid(payload))
            {
                throw new ArgumentException("This is not a valid QRIS payload (it must start with 000201 and end with a correct CRC field 6304….).", nameof(payload));
            }

            if (amount <= 0 || decimal.Round(amount, 2) != amount)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be greater than zero with at most 2 decimals.");
            }

            var amountText = amount.ToString(amount == decimal.Truncate(amount) ? "0" : "0.##", CultureInfo.InvariantCulture);
            if (amountText.Length > 13)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount is too large for a QRIS field (13 characters).");
            }

            var fields = new List<KeyValuePair<string, string>>(Parse(payload));
            fields.RemoveAll(f => f.Key == "63" || f.Key == "54");
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Key == "01")
                {
                    fields[i] = new KeyValuePair<string, string>("01", "12");
                }
            }

            if (!fields.Exists(f => f.Key == "01"))
            {
                fields.Insert(1, new KeyValuePair<string, string>("01", "12"));
            }

            // Tag 54 follows the currency (53); without it, it goes before the country code (58).
            var after = fields.FindIndex(f => f.Key == "53");
            var index = after >= 0 ? after + 1 : Math.Max(0, fields.FindIndex(f => string.CompareOrdinal(f.Key, "54") > 0));
            fields.Insert(index, new KeyValuePair<string, string>("54", amountText));

            var sb = new StringBuilder();
            foreach (var f in fields)
            {
                sb.Append(f.Key).Append(f.Value.Length.ToString("00", CultureInfo.InvariantCulture)).Append(f.Value);
            }

            sb.Append("6304");
            sb.Append(ComputeCrc(sb.ToString()).ToString("X4", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool IsDigits(string s)
        {
            foreach (var c in s)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
