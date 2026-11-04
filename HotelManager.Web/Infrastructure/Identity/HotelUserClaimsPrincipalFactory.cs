using System.Security.Claims;
using HotelManager.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HotelManager.Web.Infrastructure.Identity;

/// <summary>
/// Issues the claims of the active hotel: hotel id/name and the role the user has <em>in that hotel</em>.
/// Installation-wide roles (PlatformAdmin) come from ASP.NET Core Identity roles.
/// </summary>
public sealed class HotelUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    HotelAccessService hotelAccess)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(HotelClaimTypes.DisplayName, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? string.Empty : user.DisplayName));

        if (await hotelAccess.GetActiveHotelAsync(user) is { } hotel)
        {
            identity.AddClaim(new Claim(HotelClaimTypes.HotelId, hotel.HotelId.ToString()));
            identity.AddClaim(new Claim(HotelClaimTypes.HotelName, hotel.Name));
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, hotel.Role));
        }

        return identity;
    }
}
