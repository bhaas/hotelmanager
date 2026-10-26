namespace HotelManager.Web.Domain;

public sealed class Room
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Floor { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public ICollection<RoomAvailabilityRule> AvailabilityRules { get; set; } = new List<RoomAvailabilityRule>();
}
