using Microsoft.AspNetCore.Identity;

namespace HotelManager.Web.Domain;

public sealed class ApplicationUser : IdentityUser
{
    /// <summary>The hotel the user currently works in (last selected in the hotel switcher).</summary>
    public Guid HotelId { get; set; }

    public Hotel Hotel { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<HotelMembership> Memberships { get; set; } = new List<HotelMembership>();
}
