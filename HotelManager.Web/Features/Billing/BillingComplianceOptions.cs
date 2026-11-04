namespace HotelManager.Web.Features.Billing;

/// <summary>Installation-wide billing rules. Seller name, address and VAT ID come from the hotel profile.</summary>
public sealed class BillingComplianceOptions
{
    public const string SectionName = "BillingCompliance";

    public decimal CashPaymentLimitEur { get; set; } = 10_000m;
}
