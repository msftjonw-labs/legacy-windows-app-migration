using System;
using System.Runtime.InteropServices;

namespace LegacyOrders
{
    public static class OfficeExport
    {
        public static void Excel(Order order, string path)
        {
            object application = null, workbooks = null, workbook = null, sheets = null, sheet = null, range = null;
            try
            {
                application = Create("Excel.Application");
                dynamic excel = application;
                excel.Visible = false;
                excel.DisplayAlerts = false;
                excel.AutomationSecurity = 3;
                workbooks = excel.Workbooks;
                workbook = ((dynamic)workbooks).Add();
                sheets = ((dynamic)workbook).Worksheets;
                sheet = ((dynamic)sheets)[1];
                object[,] values = new object[order.Lines.Count + 1, 5];
                string[] headers = { "Order reference", "Description", "Quantity", "Unit price (USD)", "Discount %" };
                for (int c = 0; c < headers.Length; c++) values[0, c] = headers[c];
                for (int r = 0; r < order.Lines.Count; r++)
                {
                    OrderLine line = order.Lines[r];
                    values[r + 1, 0] = order.Reference;
                    values[r + 1, 1] = line.Description;
                    values[r + 1, 2] = line.Quantity;
                    values[r + 1, 3] = line.UnitPrice;
                    values[r + 1, 4] = line.DiscountPercent;
                }
                range = ((dynamic)sheet).Range["A1", "E" + (order.Lines.Count + 1)];
                ((dynamic)range).NumberFormat = "@";
                ((dynamic)range).Value2 = values;
                ((dynamic)workbook).SaveAs(path, 51);
            }
            finally
            {
                try { if (workbook != null) ((dynamic)workbook).Close(false); }
                finally
                {
                    try { if (application != null) ((dynamic)application).Quit(); }
                    finally { Release(range); Release(sheet); Release(sheets); Release(workbook); Release(workbooks); Release(application); }
                }
            }
        }

        public static void Word(Order order, Totals totals, string path)
        {
            object application = null, documents = null, document = null, content = null;
            try
            {
                application = Create("Word.Application");
                dynamic word = application;
                word.Visible = false;
                word.DisplayAlerts = 0;
                word.AutomationSecurity = 3;
                documents = word.Documents;
                document = ((dynamic)documents).Add();
                content = ((dynamic)document).Content;
                ((dynamic)content).Text = Documents.InvoiceText(order, totals);
                ((dynamic)document).SaveAs2(path, 16);
            }
            finally
            {
                try { if (document != null) ((dynamic)document).Close(false); }
                finally
                {
                    try { if (application != null) ((dynamic)application).Quit(); }
                    finally { Release(content); Release(document); Release(documents); Release(application); }
                }
            }
        }

        private static object Create(string progId)
        {
            Type type = Type.GetTypeFromProgID(progId);
            if (type == null) throw new InvalidOperationException(progId + " is not installed. Office exports require locally installed desktop Office.");
            return Activator.CreateInstance(type);
        }

        private static void Release(object value) { if (value != null) Marshal.FinalReleaseComObject(value); }
    }
}
