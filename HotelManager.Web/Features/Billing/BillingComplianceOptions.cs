namespace HotelManager.Web.Features.Billing;

public sealed class BillingComplianceOptions
{
    public const string SectionName = "BillingCompliance";

    public string SellerName { get; set; } = "HotelManager GmbH";

    public string SellerAddress { get; set; } = "Musterstraße 1, 1010 Wien, Österreich";

    public string SellerVatId { get; set; } = "ATU00000000";

    public decimal CashPaymentLimitEur { get; set; } = 10_000m;
}
