namespace HotelManager.Web.Domain;

public sealed class RoomRateRule
{
    public Guid Id { get; set; }

    public string RoomType { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public int OccupancyFrom { get; set; } = 1;

    public int? OccupancyTo { get; set; }

    public decimal NightlyNetAmount { get; set; }

    public decimal VatRatePercent { get; set; }

    public string Currency { get; set; } = "EUR";

    public int Priority { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
