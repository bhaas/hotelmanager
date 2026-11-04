using System.Security.Claims;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HotelManager.Web.Infrastructure.Identity;

/// <summary>Adds the tenant (hotel) and display name to the authentication cookie.</summary>
public sealed class HotelUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    HotelDbContext dbContext)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var hotelName = await dbContext.Hotels
            .Where(hotel => hotel.Id == user.HotelId)
            .Select(hotel => hotel.Name)
            .SingleOrDefaultAsync();

        identity.AddClaim(new Claim(HotelClaimTypes.HotelId, user.HotelId.ToString()));
        identity.AddClaim(new Claim(HotelClaimTypes.HotelName, hotelName ?? string.Empty));
        identity.AddClaim(new Claim(HotelClaimTypes.DisplayName, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? string.Empty : user.DisplayName));
        return identity;
    }
}
