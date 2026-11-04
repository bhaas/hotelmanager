using Microsoft.AspNetCore.Identity;

namespace HotelManager.Web.Domain;

public sealed class ApplicationUser : IdentityUser
{
    public Guid HotelId { get; set; }

    public Hotel Hotel { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
