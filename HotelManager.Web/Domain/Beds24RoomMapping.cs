namespace HotelManager.Web.Domain;

/// <summary>Assigns a Beds24 room type to a HotelManager room category.</summary>
public sealed class Beds24RoomMapping : ITenantEntity
{
    public Guid HotelId { get; set; }

    public int Beds24RoomId { get; set; }

    public string Beds24RoomName { get; set; } = string.Empty;

    /// <summary>Name of the <see cref="RoomCategory"/>; rooms reference categories by name.</summary>
    public string? CategoryName { get; set; }
}
