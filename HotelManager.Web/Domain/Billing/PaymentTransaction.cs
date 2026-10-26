namespace HotelManager.Web.Domain.Billing;

public sealed class PaymentTransaction
{
    public Guid Id { get; set; }

    public Guid ReservationId { get; set; }

    public Reservation Reservation { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "EUR";

    public PaymentMethod Method { get; set; }

    public DateTimeOffset PaidAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? Reference { get; set; }

    public string? Notes { get; set; }
}
