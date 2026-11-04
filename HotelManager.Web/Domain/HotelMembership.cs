namespace HotelManager.Web.Domain;

/// <summary>
/// Grants a user access to one hotel with one role. A user can be member of several hotels
/// (e.g. a regional manager) and switches between them in the header.
/// </summary>
public sealed class HotelMembership
{
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public Guid HotelId { get; set; }

    public Hotel Hotel { get; set; } = null!;

    /// <summary>One of <c>HotelRoles</c>.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Deactivated memberships keep the history but no longer grant access to the hotel.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
