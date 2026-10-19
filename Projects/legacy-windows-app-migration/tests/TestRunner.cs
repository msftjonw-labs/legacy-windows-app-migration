using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LegacyOrders.Pricing;

namespace LegacyOrders.Tests
{
    internal static class TestRunner
    {
        private static int count;
        private static readonly string CsvHeader = "OrderReference,Customer,Description,Quantity,UnitPrice,DiscountPercent,TaxPercent\n";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Check("COM engine arithmetic and midpoint rounding", delegate {
                    PricingEngine engine = new PricingEngine();
                    Equal("2250.00", engine.CalculateLineTotal("1250", 2, "10"));
                    Equal("0.01", engine.CalculateLineTotal("0.01", 1, "50"));
                    Reject<ArgumentOutOfRangeException>(delegate { engine.CalculateLineTotal("1", 0, "0"); });
                    Reject<ArgumentOutOfRangeException>(delegate { engine.CalculateLineTotal("1", 1, "101"); });
                    Reject<FormatException>(delegate { engine.CalculateLineTotal("1,25", 1, "0"); });
                });
                Check("CSV grouping, quotes, escaped quotes, multiline and trailing newline", delegate {
                    List<Order> orders = CsvImport.Parse(CsvHeader +
                        "PO-1,Contoso,\"Kit, \"\"large\"\"\",2,10.50,5,8.25\r\n" +
                        "PO-1,Contoso,\"Support\nservice\",1,5,0,8.25\r\n");
                    Equal(1, orders.Count);
                    Equal(2, orders[0].Lines.Count);
                    Equal("Kit, \"large\"", orders[0].Lines[0].Description);
                    Equal("Support\nservice", orders[0].Lines[1].Description);
                });
                Check("CSV rejects malformed, inconsistent and invalid records", delegate {
                    foreach (string body in new[] {
                        "A,C,D,0,1,0,0", "A,C,D,1,-1,0,0", "A,C,D,1,1,101,0",
                        "A,C,D,1,1,0,101", "A,C,D,1,1,0", "A,C,\"unfinished",
                        "A,C,\"closed\"x,1,1,0,0", "A,C,D,1,1,0,0\nA,Other,D,1,1,0,0",
                        ",C,D,1,1,0,0", "A,,D,1,1,0,0"
                    }) Reject<Exception>(delegate { CsvImport.Parse(CsvHeader + body); });
                    Reject<InvalidDataException>(delegate { CsvImport.Parse("Wrong,Header\nx,y"); });
                });
                Check("Persistence, atomic replacement, uniqueness, attachment and lock", StorageChecks);
                Check("Corrupt database is preserved and reported", delegate {
                    InTemporaryDirectory(delegate(string directory) {
                        string path = Path.Combine(directory, "orders.json");
                        File.WriteAllText(path, "not-json");
                        using (Storage storage = new Storage(directory))
                            Reject<Exception>(delegate { storage.Load(); });
                        Equal("not-json", File.ReadAllText(path));
                    });
                });
                Check("HTML encoding and invoice totals", delegate {
                    Order order = Sample();
                    order.Customer = "<script>alert('x')</script>";
                    order.Lines[0].Description = "A&B <part>";
                    string html = Documents.InvoiceHtml(order, new Totals { Subtotal = 2400m, Tax = 198m });
                    True(!html.Contains("<script>"), "Customer HTML must be encoded.");
                    True(html.Contains("A&amp;B &lt;part&gt;"), "Line HTML must be encoded.");
                    True(html.Contains("2598.00 USD"), "Invoice must contain expected total.");
                    InTemporaryDirectory(delegate(string directory) {
                        string path = Path.Combine(directory, "invoice.html");
                        Documents.WriteInvoice(path, order, new Totals());
                        Documents.WriteInvoice(path, order, new Totals { Subtotal = 2400m, Tax = 198m });
                        Equal(html, File.ReadAllText(path));
                        True(!File.Exists(path + ".tmp"), "Temporary invoice should be cleaned up.");
                    });
                });
                Check("Attachment traversal and extension validation", delegate {
                    True(!Storage.IsAttachmentName("..\\file.txt"), "Traversal rejected.");
                    True(!Storage.IsAttachmentName(Guid.NewGuid().ToString("N") + ".exe"), "Executable rejected.");
                    True(!Storage.IsAttachmentName("invoice.txt"), "Non-generated name rejected.");
                    True(Storage.IsAttachmentName(Guid.NewGuid().ToString("N") + ".pdf"), "Generated PDF accepted.");
                });
                if (args.Contains("--com"))
                    Check("Actual registered x86 COM activation and order totals", delegate {
                        Totals result = PricingService.Calculate(Sample());
                        Equal(2400m, result.Subtotal);
                        Equal(198m, result.Tax);
                        Equal(2598m, result.Total);
                    });
                if (args.Contains("--ui"))
                    Check("WinForms import, editing, save, COM calculation and read-only orders", UiChecks);
                Console.WriteLine("PASS: " + count + " checks.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception);
                return 1;
            }
        }

        private static void StorageChecks()
        {
            InTemporaryDirectory(delegate(string directory) {
                using (Storage storage = new Storage(directory))
                {
                    Equal(0, storage.Load().Count);
                    Reject<IOException>(delegate { using (Storage second = new Storage(directory)) { second.Load(); } });
                    Order order = Sample();
                    storage.Save(new List<Order> { order });
                    Equal("PO-TEST", storage.Load()[0].Reference);
                    order.Customer = "Updated";
                    storage.Save(new List<Order> { order });
                    Equal("Updated", storage.Load()[0].Customer);
                    True(File.Exists(Path.Combine(directory, "orders.json.bak")), "Backup should exist.");
                    Order duplicate = Sample();
                    Reject<InvalidDataException>(delegate { storage.Save(new List<Order> { order, duplicate }); });
                    Equal(1, storage.Load().Count);
                    string source = Path.Combine(directory, "source.txt");
                    File.WriteAllText(source, "supporting document");
                    Attachment attachment = storage.CopyAttachment(order, source);
                    Equal("supporting document", File.ReadAllText(Path.Combine(storage.OrderDirectory(order), attachment.StoredName)));
                    order.Attachments.Add(attachment);
                    storage.Save(new List<Order> { order });
                    Equal(1, storage.Load()[0].Attachments.Count);
                    string large = Path.Combine(directory, "large.txt");
                    using (FileStream file = File.Create(large)) file.SetLength(25 * 1024 * 1024 + 1);
                    Reject<InvalidOperationException>(delegate { storage.CopyAttachment(order, large); });
                    order.Id = "..\\escape";
                    Reject<InvalidDataException>(delegate { storage.OrderDirectory(order); });
                }
            });
        }

        private static void UiChecks()
        {
            Application.EnableVisualStyles();
            InTemporaryDirectory(delegate(string directory) {
                string csv = Path.Combine(directory, "import.csv");
                File.WriteAllText(csv, CsvHeader + "UI-1,UI Customer,Test line,2,10,0,5");
                using (Storage storage = new Storage(directory))
                using (MainForm form = new MainForm(storage))
                {
                    form.Show();
                    Application.DoEvents();
                    form.Import(csv);
                    TextBox customer = (TextBox)form.Controls.Find("Customer", true)[0];
                    customer.Text = "Edited UI Customer";
                    ((Button)form.Controls.Find("Save", true)[0]).PerformClick();
                    Equal("Edited UI Customer", storage.Load()[0].Customer);
                    ((Button)form.Controls.Find("Calculate", true)[0]).PerformClick();
                    Application.DoEvents();
                    True(FindLabels(form).Any(l => l.Text.Contains("21.00")), "COM totals should be visible in the form.");
                    form.Close();
                }
                using (Storage storage = new Storage(directory))
                {
                    List<Order> orders = storage.Load();
                    orders[0].Status = "Approved";
                    storage.Save(orders);
                    using (MainForm form = new MainForm(storage))
                    {
                        form.Show();
                        ((Button)form.Controls.Find("Open", true)[0]).PerformClick();
                        True(((TextBox)form.Controls.Find("Customer", true)[0]).ReadOnly, "Approved customer must be read-only.");
                        True(((DataGridView)form.Controls.Find("Lines", true)[0]).ReadOnly, "Approved lines must be read-only.");
                        form.Close();
                    }
                }
            });
        }

        private static IEnumerable<Label> FindLabels(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                Label label = control as Label;
                if (label != null) yield return label;
                foreach (Label child in FindLabels(control)) yield return child;
            }
        }

        private static Order Sample()
        {
            return CsvImport.Parse(CsvHeader +
                "PO-TEST,Contoso,Industrial pump,2,1250,10,8.25\n" +
                "PO-TEST,Contoso,Installation kit,2,75,0,8.25")[0];
        }

        private static void InTemporaryDirectory(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "LegacyOrders.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { action(directory); }
            finally { Directory.Delete(directory, true); }
        }

        private static void Check(string name, Action action)
        {
            action();
            count++;
            Console.WriteLine("PASS " + name);
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
