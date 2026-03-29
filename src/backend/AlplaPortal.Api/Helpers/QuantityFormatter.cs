using System.Globalization;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Api.Helpers;

public static class QuantityFormatter
{
    private static readonly CultureInfo AngolanCulture = CreateAngolanCulture();

    private static CultureInfo CreateAngolanCulture()
    {
        var culture = CultureInfo.GetCultureInfo("pt-AO");
        // Clone to allow modification
        var clone = (CultureInfo)culture.Clone();
        clone.NumberFormat.NumberDecimalSeparator = ",";
        clone.NumberFormat.NumberGroupSeparator = ".";
        return clone;
    }

    public static string FormatQuantity(decimal quantity, Unit? unit)
    {
        if (unit == null || !unit.AllowsDecimalQuantity)
        {
            // For non-fractional units: format as integer
            // N0 uses thousands separator (e.g., 1.000 for one thousand in pt-AO)
            return quantity.ToString("N0", AngolanCulture);
        }

        // For fractional units: format with 3 fixed decimal places
        // N3 uses thousands separator and 3 decimals (e.g., 1,000 or 1.500,250 in pt-AO)
        return quantity.ToString("N3", AngolanCulture);
    }
}
