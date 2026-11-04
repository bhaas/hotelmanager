using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Infrastructure.Identity;

public sealed record AccessibleHotel(Guid HotelId, string Name, string? City, string Role);

/// <summary>
/// Single source of truth for "which hotels may this user open and with which role".
/// Members get their membership role; platform admins may open every active hotel as Admin.
/// </summary>
public sealed class HotelAccessService(HotelDbContext dbContext, UserManager<ApplicationUser> userManager)
{
    public async Task<IReadOnlyList<AccessibleHotel>> GetAccessibleHotelsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var memberships = await dbContext.HotelMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == user.Id && membership.IsActive && membership.Hotel.IsActive)
            .Select(membership => new AccessibleHotel(membership.HotelId, membership.Hotel.Name, membership.Hotel.City, membership.Role))
            .ToListAsync(cancellationToken);

        if (await userManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin))
        {
            var memberHotelIds = memberships.Select(hotel => hotel.HotelId).ToHashSet();
            var otherHotels = await dbContext.Hotels
                .AsNoTracking()
                .Where(hotel => hotel.IsActive)
                .Select(hotel => new AccessibleHotel(hotel.Id, hotel.Name, hotel.City, HotelRoles.Admin))
                .ToListAsync(cancellationToken);
            memberships.AddRange(otherHotels.Where(hotel => !memberHotelIds.Contains(hotel.HotelId)));
        }

        return memberships
            .OrderBy(hotel => hotel.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The hotel the user works in: the last selected one if still accessible, otherwise the first accessible.</summary>
    public async Task<AccessibleHotel?> GetActiveHotelAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var hotels = await GetAccessibleHotelsAsync(user, cancellationToken);
        return hotels.FirstOrDefault(hotel => hotel.HotelId == user.HotelId) ?? hotels.FirstOrDefault();
    }
}
