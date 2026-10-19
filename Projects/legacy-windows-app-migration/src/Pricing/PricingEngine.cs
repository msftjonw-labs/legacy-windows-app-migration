using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace LegacyOrders.Pricing
{
    [ComVisible(true)]
    [Guid("0CFDC66E-6087-48B1-9881-5394B3D11825")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IPricingEngine
    {
        [DispId(1)]
        string CalculateLineTotal(string unitPrice, int quantity, string discountPercent);
    }

    [ComVisible(true)]
    [Guid("8FDE9697-E5CD-4747-A0DC-67770582AC6C")]
    [ProgId("LegacyOrders.PricingEngine")]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(IPricingEngine))]
    public sealed class PricingEngine : IPricingEngine
    {
        public string CalculateLineTotal(string unitPrice, int quantity, string discountPercent)
        {
            decimal price = Decimal.Parse(unitPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            decimal discount = Decimal.Parse(discountPercent, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (price < 0 || price > 10000000 || quantity < 1 || quantity > 100000 || discount < 0 || discount > 100)
                throw new ArgumentOutOfRangeException("unitPrice", "Price, quantity or discount is outside the supported range.");
            return Decimal.Round(price * quantity * (1 - discount / 100), 2, MidpointRounding.AwayFromZero)
                .ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
