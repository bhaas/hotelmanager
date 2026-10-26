namespace HotelManager.Web.Domain;

public sealed class Reservation
{
    public Guid Id { get; set; }

    public Guid RoomId { get; set; }

    public Room Room { get; set; } = null!;

    public string GuestName { get; set; } = string.Empty;

    public DateOnly CheckIn { get; set; }

    public DateOnly CheckOut { get; set; }

    public int Occupancy { get; set; } = 1;

    public ReservationStatus Status { get; set; }

    public string? Notes { get; set; }

    public ICollection<Billing.FolioEntry> FolioEntries { get; set; } = new List<Billing.FolioEntry>();

    public ICollection<Billing.PaymentTransaction> Payments { get; set; } = new List<Billing.PaymentTransaction>();

    public ICollection<Billing.InvoiceDocument> Invoices { get; set; } = new List<Billing.InvoiceDocument>();
}
