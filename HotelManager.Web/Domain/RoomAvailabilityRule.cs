namespace HotelManager.Web.Domain;

public sealed class RoomAvailabilityRule
{
    public Guid Id { get; set; }

    public Guid? RoomId { get; set; }

    public Room? Room { get; set; }

    public string? RoomType { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public bool StopSell { get; set; }

    public bool ClosedToArrival { get; set; }

    public bool ClosedToDeparture { get; set; }

    public int? MinStayNights { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
