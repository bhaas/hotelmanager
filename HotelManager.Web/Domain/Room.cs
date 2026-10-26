namespace HotelManager.Web.Domain;

public sealed class Room
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Floor { get; set; }

    public HousekeepingStatus HousekeepingStatus { get; set; } = HousekeepingStatus.Clean;

    /// <summary>Comma-separated feature tags, e.g. "Balcony, Sea view".</summary>
    public string? Features { get; set; }

    public string? Notes { get; set; }

    /// <summary>Archived rooms are hidden from the planner and cannot be booked, but keep their history.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public ICollection<RoomAvailabilityRule> AvailabilityRules { get; set; } = new List<RoomAvailabilityRule>();
}
