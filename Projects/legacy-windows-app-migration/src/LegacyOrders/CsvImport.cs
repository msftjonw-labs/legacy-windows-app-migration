using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LegacyOrders
{
    public static class CsvImport
    {
        private static readonly string[] Header = {
            "OrderReference", "Customer", "Description", "Quantity", "UnitPrice", "DiscountPercent", "TaxPercent"
        };

        public static List<Order> Read(string path)
        {
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                return Parse(reader.ReadToEnd());
        }

        public static List<Order> Parse(string text)
        {
            List<string[]> rows = ReadRows(text);
            if (rows.Count < 2 || !rows[0].SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("CSV requires this header and at least one data row:\n" + String.Join(",", Header));
            List<Order> orders = new List<Order>();
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length != Header.Length) throw new InvalidDataException("CSV record " + (i + 1) + " must have seven fields.");
                int quantity;
                decimal price, discount, tax;
                if (!Int32.TryParse(row[3], NumberStyles.None, CultureInfo.InvariantCulture, out quantity) ||
                    !TryDecimal(row[4], out price) || !TryDecimal(row[5], out discount) || !TryDecimal(row[6], out tax))
                    throw new InvalidDataException("Invalid numeric value in CSV record " + (i + 1) + ". Use a decimal point, without currency symbols.");
                string reference = row[0].Trim();
                string customer = row[1].Trim();
                Order order = orders.FirstOrDefault(o => String.Equals(o.Reference, reference, StringComparison.OrdinalIgnoreCase));
                if (order == null)
                {
                    order = Order.Create();
                    order.Reference = reference;
                    order.Customer = customer;
                    order.TaxPercent = tax;
                    orders.Add(order);
                }
                else if (order.Customer != customer || order.TaxPercent != tax)
                    throw new InvalidDataException("Customer and tax must match for every line of order " + reference + ".");
                order.Lines.Add(new OrderLine {
                    Description = row[2].Trim(), Quantity = quantity, UnitPrice = price, DiscountPercent = discount
                });
                order.Validate();
            }
            return orders;
        }

        private static bool TryDecimal(string value, out decimal number)
        {
            return Decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
        }

        private static List<string[]> ReadRows(string text)
        {
            List<string[]> rows = new List<string[]>();
            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else { quoted = false; closed = true; }
                    }
                    else field.Append(c);
                }
                else if (c == ',' || c == '\r' || c == '\n')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    closed = false;
                    if (c != ',')
                    {
                        rows.Add(fields.ToArray());
                        fields.Clear();
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    }
                }
                else if (c == '"' && field.Length == 0 && !closed) quoted = true;
                else
                {
                    if (closed || c == '"') throw new InvalidDataException("Malformed CSV quoting.");
                    field.Append(c);
                }
            }
            if (quoted) throw new InvalidDataException("Unterminated quoted CSV field.");
            if (field.Length > 0 || fields.Count > 0 || closed)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }
    }
}
