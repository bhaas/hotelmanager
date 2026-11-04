using HotelManager.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HotelManager.Web.Infrastructure.Identity;

/// <summary>Only users with access to at least one active hotel (or platform admins) can sign in.</summary>
public sealed class HotelSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    HotelAccessService hotelAccess)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
        await base.CanSignInAsync(user) &&
        (await UserManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin) ||
         (await hotelAccess.GetAccessibleHotelsAsync(user)).Count > 0);
}
