using System.Globalization;

namespace HotelManager.Web.Features.Shared;

public static class MoneyFormatter
{
    // Neutral UI cultures such as "en" have no region and would render the generic "¤" sign.
    public static string Format(decimal amount, string currency = "EUR")
    {
        var numberFormat = (NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone();
        numberFormat.CurrencySymbol = currency.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            _ => currency
        };

        return amount.ToString("C", numberFormat);
    }
}
