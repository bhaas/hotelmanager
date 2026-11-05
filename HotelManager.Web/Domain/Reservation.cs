namespace HotelManager.Web.Domain;

public sealed class Reservation : ITenantEntity
{
    public Guid HotelId { get; set; }

    public Guid Id { get; set; }

    public Guid RoomId { get; set; }

    public Room Room { get; set; } = null!;

    public string GuestName { get; set; } = string.Empty;

    public DateOnly CheckIn { get; set; }

    public DateOnly CheckOut { get; set; }

    public int Occupancy { get; set; } = 1;

    public ReservationStatus Status { get; set; }

    public string? Notes { get; set; }

    public Guid? GuestId { get; set; }

    public Guest? Guest { get; set; }

    public ReservationSource Source { get; set; } = ReservationSource.Direct;

    /// <summary>Booking number of the channel (e.g. Booking.com), unique per source; used to re-import idempotently.</summary>
    public string? ExternalReference { get; set; }

    public ICollection<Billing.FolioEntry> FolioEntries { get; set; } = new List<Billing.FolioEntry>();

    public ICollection<Billing.PaymentTransaction> Payments { get; set; } = new List<Billing.PaymentTransaction>();

    public ICollection<Billing.InvoiceDocument> Invoices { get; set; } = new List<Billing.InvoiceDocument>();
}
