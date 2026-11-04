namespace HotelManager.Web.Domain;

/// <summary>A property (tenant). One installation can host many hotels with fully isolated data.</summary>
public sealed class Hotel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TimeZone { get; set; } = "Europe/Vienna";

    public string Currency { get; set; } = "EUR";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
