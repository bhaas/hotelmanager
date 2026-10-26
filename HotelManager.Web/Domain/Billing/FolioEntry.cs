namespace HotelManager.Web.Domain.Billing;

public sealed class FolioEntry
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }

    public Reservation Reservation { get; set; } = null!;

    public FolioEntryType EntryType { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitNetAmount { get; set; }

    public decimal VatRatePercent { get; set; }

    public DateOnly ServiceDate { get; set; }

    public string Currency { get; set; } = "EUR";

    public string? TaxExemptionReason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
