namespace HotelManager.Web.Domain.Billing;

public sealed class InvoiceDocument
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }

    public Reservation Reservation { get; set; } = null!;

    public string InvoiceNumber { get; set; } = string.Empty;

    public DateOnly ServiceFrom { get; set; }

    public DateOnly ServiceTo { get; set; }

    public DateOnly DueDate { get; set; }

    public DateTimeOffset IssuedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string Currency { get; set; } = "EUR";

    public string SellerName { get; set; } = string.Empty;

    public string SellerAddress { get; set; } = string.Empty;

    public string SellerVatId { get; set; } = string.Empty;

    public string BuyerName { get; set; } = string.Empty;

    public string BuyerAddress { get; set; } = string.Empty;

    public string? BuyerVatId { get; set; }

    public decimal NetAmount { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrossAmount { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
