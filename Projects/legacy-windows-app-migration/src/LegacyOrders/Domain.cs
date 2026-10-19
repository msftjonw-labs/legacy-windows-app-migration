using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace LegacyOrders
{
    [DataContract]
    public sealed class OrderLine
    {
        [DataMember] public string Description { get; set; }
        [DataMember] public int Quantity { get; set; }
        [DataMember] public decimal UnitPrice { get; set; }
        [DataMember] public decimal DiscountPercent { get; set; }
    }

    [DataContract]
    public sealed class Attachment
    {
        [DataMember] public string OriginalName { get; set; }
        [DataMember] public string StoredName { get; set; }
        public override string ToString() { return OriginalName; }
    }

    [DataContract]
    public sealed class Order
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Reference { get; set; }
        [DataMember] public string Customer { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }
        [DataMember] public string Status { get; set; }
        [DataMember] public decimal TaxPercent { get; set; }
        [DataMember] public List<OrderLine> Lines { get; set; }
        [DataMember] public List<Attachment> Attachments { get; set; }

        public static Order Create()
        {
            return new Order {
                Id = Guid.NewGuid().ToString("N"), Reference = "", Customer = "",
                CreatedUtc = DateTime.UtcNow, Status = "Draft",
                Lines = new List<OrderLine>(), Attachments = new List<Attachment>()
            };
        }

        public void Validate()
        {
            Guid id;
            if (!Guid.TryParseExact(Id, "N", out id)) throw new InvalidOperationException("Invalid order identifier.");
            if (String.IsNullOrWhiteSpace(Reference) || Reference.Length > 100)
                throw new InvalidOperationException("Order reference is required (maximum 100 characters).");
            if (String.IsNullOrWhiteSpace(Customer) || Customer.Length > 200)
                throw new InvalidOperationException("Customer is required (maximum 200 characters).");
            if (Status != "Draft" && Status != "Approved" && Status != "Invoiced")
                throw new InvalidOperationException("Invalid order status.");
            if (TaxPercent < 0 || TaxPercent > 100) throw new InvalidOperationException("Tax must be between 0 and 100.");
            if (Lines == null || Lines.Count == 0) throw new InvalidOperationException("Add at least one order line.");
            foreach (OrderLine line in Lines)
            {
                if (line == null || String.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 500 ||
                    line.Quantity < 1 || line.Quantity > 100000 || line.UnitPrice < 0 ||
                    line.UnitPrice > 10000000 || line.DiscountPercent < 0 || line.DiscountPercent > 100)
                    throw new InvalidOperationException("Each line needs a description, quantity 1-100000, price 0-10000000, and discount 0-100.");
            }
            if (Attachments == null) throw new InvalidOperationException("Missing attachment collection.");
            foreach (Attachment attachment in Attachments)
                if (attachment == null || String.IsNullOrWhiteSpace(attachment.OriginalName) ||
                    !Storage.IsAttachmentName(attachment.StoredName))
                    throw new InvalidOperationException("Invalid attachment metadata.");
        }
    }

    public sealed class Totals
    {
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get { return Subtotal + Tax; } }
    }

    public static class PricingService
    {
        public const string ClassId = "8FDE9697-E5CD-4747-A0DC-67770582AC6C";

        public static Totals Calculate(Order order)
        {
            order.Validate();
            Type type = Type.GetTypeFromCLSID(new Guid(ClassId), true);
            object instance = Activator.CreateInstance(type);
            try
            {
                dynamic engine = instance;
                decimal subtotal = 0;
                foreach (OrderLine line in order.Lines)
                {
                    string result = engine.CalculateLineTotal(
                        line.UnitPrice.ToString(CultureInfo.InvariantCulture), line.Quantity,
                        line.DiscountPercent.ToString(CultureInfo.InvariantCulture));
                    subtotal += Decimal.Parse(result, CultureInfo.InvariantCulture);
                }
                return new Totals {
                    Subtotal = subtotal,
                    Tax = Decimal.Round(subtotal * order.TaxPercent / 100, 2, MidpointRounding.AwayFromZero)
                };
            }
            finally
            {
                // The CLR can unwrap an in-process managed COM object instead of returning an RCW.
                if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            }
        }
    }
}
