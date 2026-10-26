namespace HotelManager.Web.Domain.Billing;

public sealed class InvoiceLine
{
    public Guid Id { get; set; }

    public Guid InvoiceDocumentId { get; set; }

    public InvoiceDocument InvoiceDocument { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitNetAmount { get; set; }

    public decimal VatRatePercent { get; set; }

    public decimal NetAmount { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrossAmount { get; set; }

    public string? TaxExemptionReason { get; set; }
}
