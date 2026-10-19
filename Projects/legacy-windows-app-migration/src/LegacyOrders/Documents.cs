using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace LegacyOrders
{
    public static class Documents
    {
        public static string InvoiceHtml(Order order, Totals totals)
        {
            StringBuilder html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Invoice</title><style>body{font:16px Segoe UI,sans-serif;margin:40px;color:#222}table{border-collapse:collapse;width:100%}td,th{padding:10px;border-bottom:1px solid #ccc;text-align:left}.number{text-align:right}@media print{button{display:none}}</style></head><body>");
            html.Append("<h1>Invoice ").Append(Encode(order.Reference)).Append("</h1><p>Customer: ")
                .Append(Encode(order.Customer)).Append("</p><p>Order created (UTC): ")
                .Append(order.CreatedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append("</p><p>Currency: USD</p><table><thead><tr><th>Description</th><th>Quantity</th><th>Unit price</th><th>Discount %</th></tr></thead><tbody>");
            foreach (OrderLine line in order.Lines)
                html.Append("<tr><td>").Append(Encode(line.Description)).Append("</td><td>")
                    .Append(line.Quantity).Append("</td><td>").Append(Money(line.UnitPrice))
                    .Append("</td><td>").Append(line.DiscountPercent.ToString(CultureInfo.InvariantCulture)).Append("</td></tr>");
            html.Append("</tbody></table><p>Subtotal: ").Append(Money(totals.Subtotal))
                .Append("</p><p>Tax (").Append(order.TaxPercent.ToString(CultureInfo.InvariantCulture))
                .Append("%): ").Append(Money(totals.Tax)).Append("</p><h2>Total: ")
                .Append(Money(totals.Total)).Append(" USD</h2></body></html>");
            return html.ToString();
        }

        public static string InvoiceText(Order order, Totals totals)
        {
            StringBuilder text = new StringBuilder("INVOICE " + order.Reference + "\r\nCustomer: " + order.Customer + "\r\n\r\n");
            foreach (OrderLine line in order.Lines)
                text.AppendLine(line.Description + " | Qty " + line.Quantity + " | USD " + Money(line.UnitPrice) + " | Discount " + line.DiscountPercent + "%");
            return text.AppendLine().AppendLine("Subtotal: USD " + Money(totals.Subtotal))
                .AppendLine("Tax: USD " + Money(totals.Tax)).AppendLine("Total: USD " + Money(totals.Total)).ToString();
        }

        public static void WriteInvoice(string path, Order order, Totals totals)
        {
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, InvoiceHtml(order, totals), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Encode(string text) { return WebUtility.HtmlEncode(text); }
        private static string Money(decimal value) { return value.ToString("0.00", CultureInfo.InvariantCulture); }
    }
}
