namespace HotelManager.Web.Domain;

/// <summary>
/// Sellable room category (e.g. "Deluxe King"). Rooms, rate rules and availability rules
/// reference a category by its <see cref="Name"/>; renames are cascaded by the inventory service.
/// </summary>
public sealed class RoomCategory
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int StandardOccupancy { get; set; } = 2;

    public int MaxOccupancy { get; set; } = 2;

    public int SortOrder { get; set; }
}
