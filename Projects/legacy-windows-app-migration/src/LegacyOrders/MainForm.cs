using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LegacyOrders
{
    public sealed class MainForm : Form
    {
        private readonly Storage storage;
        private List<Order> orders;
        private Order current;
        private bool dirty, loading;
        private readonly ListBox orderList = new ListBox { Dock = DockStyle.Fill, Name = "OrderList" };
        private readonly TextBox reference = new TextBox { Dock = DockStyle.Fill, Name = "Reference" };
        private readonly TextBox customer = new TextBox { Dock = DockStyle.Fill, Name = "Customer" };
        private readonly NumericUpDown tax = new NumericUpDown { DecimalPlaces = 2, Maximum = 100, Dock = DockStyle.Fill, Name = "Tax" };
        private readonly Label state = new Label { AutoSize = true };
        private readonly Label totals = new Label { AutoSize = true, Text = "Totals: not calculated" };
        private readonly DataGridView lines = new DataGridView {
            Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Name = "Lines", RowHeadersWidth = 25
        };
        private readonly ListBox attachments = new ListBox { Dock = DockStyle.Fill, Name = "Attachments" };

        public MainForm(Storage storage)
        {
            this.storage = storage;
            orders = storage.Load();
            Text = "Legacy Orders - WinForms / Win32 / COM baseline";
            Size = new Size(1250, 820);
            MinimumSize = new Size(1050, 700);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(new Label { Text = "Orders - select, then Open", AutoSize = true }, 0, 0);
            left.Controls.Add(orderList, 0, 1);
            FlowLayoutPanel listActions = Toolbar();
            AddButton(listActions, "New", "New", delegate { if (CanDiscard()) LoadEditor(Order.Create()); });
            AddButton(listActions, "Open", "Open", OpenSelected);
            AddButton(listActions, "Import CSV", "ImportCsv", ChooseImport);
            left.Controls.Add(listActions, 0, 2);
            orderList.DoubleClick += delegate { Run(OpenSelected); };
            layout.Controls.Add(left, 0, 0);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(right, 1, 0);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 4 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(header, 0, "Order reference", reference);
            AddField(header, 1, "Customer", customer);
            AddField(header, 2, "Tax % (USD)", tax);
            AddField(header, 3, "Status", state);
            right.Controls.Add(header, 0, 0);
            lines.Columns.Add("Description", "Description");
            lines.Columns.Add("Quantity", "Quantity");
            lines.Columns.Add("UnitPrice", "Unit price (USD)");
            lines.Columns.Add("Discount", "Discount %");
            lines.Columns[0].FillWeight = 220;
            right.Controls.Add(lines, 0, 1);
            FlowLayoutPanel actions = Toolbar();
            AddButton(actions, "Save draft", "Save", SaveDraft);
            AddButton(actions, "Calculate (COM)", "Calculate", Calculate);
            AddButton(actions, "Approve", "Approve", Approve);
            AddButton(actions, "Generate invoice", "Invoice", Invoice);
            AddButton(actions, "Excel export", "Excel", delegate { ExportOffice(false); });
            AddButton(actions, "Word invoice", "Word", delegate { ExportOffice(true); });
            right.Controls.Add(actions, 0, 2);
            right.Controls.Add(totals, 0, 3);
            GroupBox documents = new GroupBox { Text = "Supporting documents (maximum 25 MiB each)", Dock = DockStyle.Fill };
            documents.Controls.Add(attachments);
            right.Controls.Add(documents, 0, 4);
            FlowLayoutPanel documentActions = Toolbar();
            AddButton(documentActions, "Attach file", "Attach", Attach);
            AddButton(documentActions, "Open attachment", "OpenAttachment", OpenAttachment);
            AddButton(documentActions, "Reveal in Explorer", "Reveal", RevealAttachment);
            AddButton(documentActions, "Order folder", "Folder", delegate { EnsureSaved(); NativeMethods.Open(storage.OrderDirectory(current)); });
            right.Controls.Add(documentActions, 0, 5);
            reference.TextChanged += Changed;
            customer.TextChanged += Changed;
            tax.ValueChanged += Changed;
            lines.CellValueChanged += delegate { MarkDirty(); };
            lines.RowsRemoved += delegate { MarkDirty(); };
            lines.UserAddedRow += delegate { MarkDirty(); };
            lines.CurrentCellDirtyStateChanged += delegate { MarkDirty(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!CanDiscard()) e.Cancel = true; };
            RefreshOrders();
            LoadEditor(Order.Create());
        }

        private static FlowLayoutPanel Toolbar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        }

        private void AddButton(FlowLayoutPanel panel, string text, string name, Action action)
        {
            Button button = new Button { Text = text, Name = name, AutoSize = true, Height = 30 };
            button.Click += delegate { Run(action); };
            panel.Controls.Add(button);
        }

        private static void AddField(TableLayoutPanel panel, int row, string label, Control control)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 8) }, 0, row);
            panel.Controls.Add(control, 1, row);
        }

        public void Run(Action action)
        {
            try { action(); }
            catch (Exception exception) { Program.ReportError(this, exception); }
        }

        private void Changed(object sender, EventArgs args) { MarkDirty(); }
        private void MarkDirty()
        {
            if (loading) return;
            dirty = true;
            totals.Text = "Totals: changed - calculate again";
        }

        private bool CanDiscard()
        {
            return !dirty || MessageBox.Show(this, "Discard unsaved order changes?", "Unsaved changes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void RefreshOrders()
        {
            orderList.DisplayMember = "Label";
            orderList.ValueMember = "Id";
            orderList.DataSource = orders.OrderByDescending(o => o.CreatedUtc)
                .Select(o => new OrderListItem { Id = o.Id, Label = o.Reference + " | " + o.Customer + " | " + o.Status }).ToList();
        }

        private sealed class OrderListItem
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }

        private void OpenSelected()
        {
            OrderListItem item = orderList.SelectedItem as OrderListItem;
            if (item == null) throw new InvalidOperationException("Select an order first.");
            if (CanDiscard()) LoadEditor(orders.Single(o => o.Id == item.Id));
        }

        private void LoadEditor(Order order)
        {
            loading = true;
            try
            {
                current = order;
                reference.Text = order.Reference;
                customer.Text = order.Customer;
                tax.Value = order.TaxPercent;
                state.Text = order.Status;
                lines.Rows.Clear();
                foreach (OrderLine line in order.Lines)
                    lines.Rows.Add(line.Description, line.Quantity, line.UnitPrice, line.DiscountPercent);
                bool editable = order.Status == "Draft";
                reference.ReadOnly = !editable;
                customer.ReadOnly = !editable;
                tax.Enabled = editable;
                lines.ReadOnly = !editable;
                lines.AllowUserToAddRows = editable;
                lines.AllowUserToDeleteRows = editable;
                attachments.DataSource = null;
                attachments.DataSource = order.Attachments.ToList();
                totals.Text = "Totals: not calculated (USD)";
                dirty = false;
            }
            finally { loading = false; }
        }

        private Order ReadEditor()
        {
            if (!lines.EndEdit()) throw new InvalidOperationException("Finish editing the current line.");
            Order order = new Order {
                Id = current.Id, Reference = reference.Text.Trim(), Customer = customer.Text.Trim(),
                CreatedUtc = current.CreatedUtc, Status = current.Status, TaxPercent = tax.Value,
                Lines = new List<OrderLine>(), Attachments = current.Attachments.ToList()
            };
            foreach (DataGridViewRow row in lines.Rows)
            {
                if (row.IsNewRow) continue;
                int quantity;
                decimal price, discount;
                if (!Int32.TryParse(Convert.ToString(row.Cells[1].Value, CultureInfo.CurrentCulture), out quantity) ||
                    !Decimal.TryParse(Convert.ToString(row.Cells[2].Value, CultureInfo.CurrentCulture), out price) ||
                    !Decimal.TryParse(Convert.ToString(row.Cells[3].Value, CultureInfo.CurrentCulture), out discount))
                    throw new InvalidOperationException("Line " + (row.Index + 1) + ": enter quantity, unit price and discount as numbers.");
                order.Lines.Add(new OrderLine {
                    Description = Convert.ToString(row.Cells[0].Value, CultureInfo.CurrentCulture).Trim(),
                    Quantity = quantity, UnitPrice = price, DiscountPercent = discount
                });
            }
            order.Validate();
            return order;
        }

        private void Persist(Order order)
        {
            List<Order> updated = orders.Where(o => o.Id != order.Id).ToList();
            updated.Add(order);
            storage.Save(updated);
            orders = updated;
            RefreshOrders();
            LoadEditor(order);
        }

        private void SaveDraft()
        {
            if (current.Status != "Draft") throw new InvalidOperationException("Approved and invoiced orders are read-only.");
            Persist(ReadEditor());
        }

        private void EnsureSaved()
        {
            if (current.Status == "Draft") SaveDraft();
            else if (!orders.Any(o => o.Id == current.Id)) throw new InvalidOperationException("Save the order first.");
        }

        private void Calculate()
        {
            Totals result = PricingService.Calculate(ReadEditor());
            totals.Text = String.Format(CultureInfo.CurrentCulture, "Subtotal: {0:N2}   Tax: {1:N2}   Total: {2:N2} USD", result.Subtotal, result.Tax, result.Total);
        }

        private void Approve()
        {
            if (current.Status != "Draft") throw new InvalidOperationException("Only drafts can be approved.");
            Order order = ReadEditor();
            PricingService.Calculate(order);
            if (MessageBox.Show(this, "Approve this order? Its pricing and lines will become read-only.", "Approve order",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            order.Status = "Approved";
            Persist(order);
            Calculate();
        }

        private void Invoice()
        {
            if (current.Status == "Draft") throw new InvalidOperationException("Approve the order before generating an invoice.");
            Order order = ReadEditor();
            Totals result = PricingService.Calculate(order);
            string path = Path.Combine(storage.OrderDirectory(order), "invoice.html");
            Documents.WriteInvoice(path, order, result);
            order.Status = "Invoiced";
            Persist(order);
            Calculate();
            NativeMethods.Open(path);
        }

        private void ChooseImport()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Order CSV (*.csv)|*.csv", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) Import(dialog.FileName);
        }

        public void Import(string path)
        {
            if (!CanDiscard()) return;
            List<Order> imported = CsvImport.Read(path);
            List<Order> updated = orders.Concat(imported).ToList();
            storage.Save(updated);
            orders = updated;
            RefreshOrders();
            LoadEditor(imported[0]);
        }

        private void Attach()
        {
            EnsureSaved();
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Documents|*.pdf;*.txt;*.csv;*.png;*.jpg;*.jpeg;*.docx;*.xlsx", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Order order = ReadEditor();
                Attachment attachment = storage.CopyAttachment(order, dialog.FileName);
                order.Attachments.Add(attachment);
                bool saved = false;
                try { Persist(order); saved = true; }
                finally
                {
                    if (!saved) File.Delete(Path.Combine(storage.OrderDirectory(order), attachment.StoredName));
                }
            }
        }

        private string SelectedAttachment()
        {
            Attachment attachment = attachments.SelectedItem as Attachment;
            if (attachment == null) throw new InvalidOperationException("Select an attachment first.");
            if (!Storage.IsAttachmentName(attachment.StoredName)) throw new InvalidDataException("Invalid attachment path.");
            return Path.Combine(storage.OrderDirectory(current), attachment.StoredName);
        }

        private void OpenAttachment()
        {
            string path = SelectedAttachment();
            if (MessageBox.Show(this, "Open this document with its Windows application? Only open files you trust.\n\n" + path,
                "Open document", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                NativeMethods.Open(path);
        }

        private void RevealAttachment() { NativeMethods.Reveal(SelectedAttachment()); }

        private void ExportOffice(bool word)
        {
            EnsureSaved();
            if (word && current.Status == "Draft") throw new InvalidOperationException("Approve the order before exporting a Word invoice.");
            using (SaveFileDialog dialog = new SaveFileDialog {
                Filter = word ? "Word document (*.docx)|*.docx" : "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = word ? "docx" : "xlsx", AddExtension = true, FileName = word ? "invoice.docx" : "order.xlsx",
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (word) OfficeExport.Word(current, PricingService.Calculate(current), dialog.FileName);
                else OfficeExport.Excel(current, dialog.FileName);
                MessageBox.Show(this, "Export created:\n" + dialog.FileName, "Office export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
