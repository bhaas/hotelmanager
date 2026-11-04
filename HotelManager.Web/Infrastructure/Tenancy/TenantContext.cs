using System.Security.Claims;
using HotelManager.Web.Infrastructure.Identity;

namespace HotelManager.Web.Infrastructure.Tenancy;

/// <summary>Read-only view of the hotel the current request or circuit works on.</summary>
public interface ITenantContext
{
    Guid? HotelId { get; }
}

/// <summary>
/// Scoped tenant holder. The hotel comes from the signed-in user's <c>hotel_id</c> claim
/// (set by <see cref="TenantMiddlewareExtensions"/> for HTTP requests and by
/// <see cref="TenantCircuitHandler"/> for interactive circuits), or from an explicit
/// <see cref="UseHotel"/> override for system work such as seeding and background jobs.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? overrideHotelId;

    public ClaimsPrincipal? User { get; private set; }

    public Guid? HotelId => overrideHotelId ?? ReadHotelId(User);

    public void SetUser(ClaimsPrincipal? user) => User = user;

    public void UseHotel(Guid hotelId) => overrideHotelId = hotelId;

    private static Guid? ReadHotelId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(user.FindFirstValue(HotelClaimTypes.HotelId), out var hotelId) ? hotelId : null;
    }
}
