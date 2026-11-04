using System.Net.Mail;
using System.Security.Claims;
using HotelManager.Web.Domain;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Users;

/// <summary>
/// Manages the user accounts of the current hotel. Each operation runs in its own DI scope so the
/// Identity stores never keep a long-lived DbContext inside an interactive circuit.
/// </summary>
public sealed class UserAdministrationService(
    TenantContext tenantContext,
    IServiceScopeFactory scopeFactory,
    IStringLocalizer<UserAdministrationService> localizer) : IUserAdministrationService
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        await using var scope = CreateScope(hotelId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var users = await userManager.Users
            .Where(user => user.HotelId == hotelId)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);

        var result = new List<UserSummary>(users.Count);
        foreach (var user in users)
        {
            result.Add(await ToSummaryAsync(userManager, user));
        }

        return result;
    }

    public async Task<UserPasswordResult> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        var email = NormalizeEmail(request.Email);
        var displayName = NormalizeDisplayName(request.DisplayName);
        var role = NormalizeRole(request.Role);

        await using var scope = CreateScope(hotelId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new UserAdministrationException(localizer["A user with this email address already exists."]);
        }

        var user = new ApplicationUser
        {
            HotelId = hotelId,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName
        };
        var password = PasswordGenerator.Create();
        EnsureSucceeded(await userManager.CreateAsync(user, password));
        EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
        return new UserPasswordResult(await ToSummaryAsync(userManager, user), password);
    }

    public async Task<UserSummary> UpdateAsync(string userId, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        var displayName = NormalizeDisplayName(request.DisplayName);
        var role = NormalizeRole(request.Role);

        await using var scope = CreateScope(hotelId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await FindUserAsync(userManager, hotelId, userId);
        var currentRoles = await userManager.GetRolesAsync(user);

        if (currentRoles.Contains(HotelRoles.Admin) && role != HotelRoles.Admin)
        {
            if (IsCurrentUser(user))
            {
                throw new UserAdministrationException(localizer["You cannot remove your own administrator role."]);
            }

            await EnsureAnotherActiveAdminAsync(userManager, hotelId, user);
        }

        user.DisplayName = displayName;
        EnsureSucceeded(await userManager.UpdateAsync(user));

        if (!currentRoles.SequenceEqual([role]))
        {
            EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, currentRoles));
            EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
            // Ends existing sessions so the new permissions apply immediately.
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        }

        return await ToSummaryAsync(userManager, user);
    }

    public async Task<UserSummary> SetLockedAsync(string userId, bool locked, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        await using var scope = CreateScope(hotelId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await FindUserAsync(userManager, hotelId, userId);

        if (locked)
        {
            if (IsCurrentUser(user))
            {
                throw new UserAdministrationException(localizer["You cannot lock your own account."]);
            }

            if (await userManager.IsInRoleAsync(user, HotelRoles.Admin))
            {
                await EnsureAnotherActiveAdminAsync(userManager, hotelId, user);
            }

            EnsureSucceeded(await userManager.SetLockoutEnabledAsync(user, true));
            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        }
        else
        {
            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null));
            EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user));
        }

        return await ToSummaryAsync(userManager, user);
    }

    public async Task<UserPasswordResult> ResetPasswordAsync(string userId, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        await using var scope = CreateScope(hotelId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await FindUserAsync(userManager, hotelId, userId);

        var password = PasswordGenerator.Create();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        EnsureSucceeded(await userManager.ResetPasswordAsync(user, token, password));
        return new UserPasswordResult(await ToSummaryAsync(userManager, user), password);
    }

    private Guid RequireHotel() =>
        tenantContext.HotelId ?? throw new UserAdministrationException(localizer["No hotel selected."]);

    private AsyncServiceScope CreateScope(Guid hotelId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        var scopedTenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        scopedTenant.SetUser(tenantContext.User);
        scopedTenant.UseHotel(hotelId);
        return scope;
    }

    private bool IsCurrentUser(ApplicationUser user) =>
        string.Equals(tenantContext.User?.FindFirstValue(ClaimTypes.NameIdentifier), user.Id, StringComparison.Ordinal);

    private async Task<ApplicationUser> FindUserAsync(UserManager<ApplicationUser> userManager, Guid hotelId, string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is not null && user.HotelId == hotelId
            ? user
            : throw new UserAdministrationException(localizer["User not found."]);
    }

    private async Task EnsureAnotherActiveAdminAsync(UserManager<ApplicationUser> userManager, Guid hotelId, ApplicationUser user)
    {
        var admins = await userManager.GetUsersInRoleAsync(HotelRoles.Admin);
        var now = DateTimeOffset.UtcNow;
        var hasOtherActiveAdmin = admins.Any(admin =>
            admin.HotelId == hotelId &&
            admin.Id != user.Id &&
            (admin.LockoutEnd is null || admin.LockoutEnd <= now));

        if (!hasOtherActiveAdmin)
        {
            throw new UserAdministrationException(localizer["The hotel needs at least one active administrator."]);
        }
    }

    private async Task<UserSummary> ToSummaryAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var role = HotelRoles.All.FirstOrDefault(roles.Contains) ?? string.Empty;
        return new UserSummary(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            role,
            user.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow,
            user.CreatedAtUtc,
            IsCurrentUser(user));
    }

    private string NormalizeEmail(string? email)
    {
        var value = email?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 256 || !MailAddress.TryCreate(value, out var address) || address.Address != value)
        {
            throw new UserAdministrationException(localizer["Enter a valid email address."]);
        }

        return value;
    }

    private string NormalizeDisplayName(string? displayName)
    {
        var value = displayName?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new UserAdministrationException(localizer["Name is required."]);
        }

        if (value.Length > 120)
        {
            throw new UserAdministrationException(localizer["Name cannot exceed 120 characters."]);
        }

        return value;
    }

    private string NormalizeRole(string? role) =>
        HotelRoles.All.FirstOrDefault(item => string.Equals(item, role, StringComparison.OrdinalIgnoreCase))
        ?? throw new UserAdministrationException(localizer["Select a valid role."]);

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new UserAdministrationException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }
}
