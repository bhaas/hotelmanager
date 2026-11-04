using System.Net.Mail;
using System.Security.Claims;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Users;

/// <summary>
/// Manages the members of the current hotel. Roles and access are per hotel (<see cref="HotelMembership"/>);
/// account-wide changes (name, password) are only allowed for accounts that belong to this hotel alone.
/// Each operation runs in its own DI scope so the Identity stores never keep a long-lived DbContext
/// inside an interactive circuit.
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
        var context = new OperationContext(scope);

        var memberships = await context.Db.HotelMemberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.HotelId == hotelId)
            .OrderBy(membership => membership.User.DisplayName)
            .ThenBy(membership => membership.User.Email)
            .ToListAsync(cancellationToken);

        var result = new List<UserSummary>(memberships.Count);
        foreach (var membership in memberships)
        {
            result.Add(await ToSummaryAsync(context, membership, cancellationToken));
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
        var context = new OperationContext(scope);
        string? password = null;

        var user = await context.Users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                HotelId = hotelId,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName
            };
            password = PasswordGenerator.Create();
            EnsureSucceeded(await context.Users.CreateAsync(user, password));
        }
        else if (await context.Db.HotelMemberships.AnyAsync(item => item.UserId == user.Id && item.HotelId == hotelId, cancellationToken))
        {
            throw new UserAdministrationException(localizer["This user already belongs to this hotel."]);
        }

        var membership = new HotelMembership { UserId = user.Id, HotelId = hotelId, Role = role, User = user };
        context.Db.HotelMemberships.Add(membership);
        await context.Db.SaveChangesAsync(cancellationToken);
        if (password is null)
        {
            // An existing account gets a new hotel: refresh its sessions so the hotel switcher shows it.
            EnsureSucceeded(await context.Users.UpdateSecurityStampAsync(user));
        }

        return new UserPasswordResult(await ToSummaryAsync(context, membership, cancellationToken), password);
    }

    public async Task<UserSummary> UpdateAsync(string userId, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        var displayName = NormalizeDisplayName(request.DisplayName);
        var role = NormalizeRole(request.Role);

        await using var scope = CreateScope(hotelId);
        var context = new OperationContext(scope);
        var membership = await FindMembershipAsync(context, hotelId, userId, cancellationToken);

        if (membership.Role == HotelRoles.Admin && role != HotelRoles.Admin)
        {
            if (IsCurrentUser(membership.UserId))
            {
                throw new UserAdministrationException(localizer["You cannot remove your own administrator role."]);
            }

            await EnsureAnotherActiveAdminAsync(context, hotelId, membership.UserId, cancellationToken);
        }

        if (!string.Equals(membership.User.DisplayName, displayName, StringComparison.Ordinal))
        {
            if (!await CanManageAccountAsync(context, membership.User, hotelId, cancellationToken))
            {
                throw new UserAdministrationException(localizer["This account also belongs to other hotels; only the platform administrator can rename it."]);
            }

            membership.User.DisplayName = displayName;
            EnsureSucceeded(await context.Users.UpdateAsync(membership.User));
        }

        if (membership.Role != role)
        {
            membership.Role = role;
            await context.Db.SaveChangesAsync(cancellationToken);
            // Ends existing sessions so the new permissions apply immediately.
            EnsureSucceeded(await context.Users.UpdateSecurityStampAsync(membership.User));
        }

        return await ToSummaryAsync(context, membership, cancellationToken);
    }

    public async Task<UserSummary> SetLockedAsync(string userId, bool locked, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        await using var scope = CreateScope(hotelId);
        var context = new OperationContext(scope);
        var membership = await FindMembershipAsync(context, hotelId, userId, cancellationToken);

        if (locked)
        {
            if (IsCurrentUser(membership.UserId))
            {
                throw new UserAdministrationException(localizer["You cannot lock your own account."]);
            }

            if (membership.Role == HotelRoles.Admin)
            {
                await EnsureAnotherActiveAdminAsync(context, hotelId, membership.UserId, cancellationToken);
            }

            membership.IsActive = false;
            await context.Db.SaveChangesAsync(cancellationToken);
            EnsureSucceeded(await context.Users.UpdateSecurityStampAsync(membership.User));
        }
        else
        {
            membership.IsActive = true;
            await context.Db.SaveChangesAsync(cancellationToken);
            // Also lift a sign-in lockout (e.g. after too many wrong passwords).
            EnsureSucceeded(await context.Users.SetLockoutEndDateAsync(membership.User, null));
            EnsureSucceeded(await context.Users.ResetAccessFailedCountAsync(membership.User));
        }

        return await ToSummaryAsync(context, membership, cancellationToken);
    }

    public async Task<UserPasswordResult> ResetPasswordAsync(string userId, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        await using var scope = CreateScope(hotelId);
        var context = new OperationContext(scope);
        var membership = await FindMembershipAsync(context, hotelId, userId, cancellationToken);
        if (!await CanManageAccountAsync(context, membership.User, hotelId, cancellationToken))
        {
            throw new UserAdministrationException(localizer["This account also belongs to other hotels; only the platform administrator can reset its password."]);
        }

        var password = PasswordGenerator.Create();
        var token = await context.Users.GeneratePasswordResetTokenAsync(membership.User);
        EnsureSucceeded(await context.Users.ResetPasswordAsync(membership.User, token, password));
        return new UserPasswordResult(await ToSummaryAsync(context, membership, cancellationToken), password);
    }

    private sealed class OperationContext(AsyncServiceScope scope)
    {
        public HotelDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<HotelDbContext>();

        public UserManager<ApplicationUser> Users { get; } = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    private Guid RequireHotel() =>
        tenantContext.HotelId ?? throw new UserAdministrationException(localizer["No hotel selected."]);

    private bool CallerIsPlatformAdmin => tenantContext.User?.IsInRole(PlatformRoles.PlatformAdmin) == true;

    private AsyncServiceScope CreateScope(Guid hotelId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        var scopedTenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        scopedTenant.SetUser(tenantContext.User);
        scopedTenant.UseHotel(hotelId);
        return scope;
    }

    private bool IsCurrentUser(string userId) =>
        string.Equals(tenantContext.User?.FindFirstValue(ClaimTypes.NameIdentifier), userId, StringComparison.Ordinal);

    private async Task<HotelMembership> FindMembershipAsync(OperationContext context, Guid hotelId, string userId, CancellationToken cancellationToken) =>
        await context.Db.HotelMemberships
            .Include(membership => membership.User)
            .SingleOrDefaultAsync(membership => membership.HotelId == hotelId && membership.UserId == userId, cancellationToken)
        ?? throw new UserAdministrationException(localizer["User not found."]);

    private async Task<bool> CanManageAccountAsync(OperationContext context, ApplicationUser user, Guid hotelId, CancellationToken cancellationToken)
    {
        if (CallerIsPlatformAdmin || IsCurrentUser(user.Id))
        {
            return true;
        }

        return await CountOtherHotelsAsync(context, user.Id, hotelId, cancellationToken) == 0 &&
               !await context.Users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);
    }

    private static Task<int> CountOtherHotelsAsync(OperationContext context, string userId, Guid hotelId, CancellationToken cancellationToken) =>
        context.Db.HotelMemberships.CountAsync(membership => membership.UserId == userId && membership.HotelId != hotelId, cancellationToken);

    private async Task EnsureAnotherActiveAdminAsync(OperationContext context, Guid hotelId, string userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var hasOtherActiveAdmin = await context.Db.HotelMemberships.AnyAsync(membership =>
            membership.HotelId == hotelId &&
            membership.UserId != userId &&
            membership.Role == HotelRoles.Admin &&
            membership.IsActive &&
            (membership.User.LockoutEnd == null || membership.User.LockoutEnd <= now), cancellationToken);

        if (!hasOtherActiveAdmin)
        {
            throw new UserAdministrationException(localizer["The hotel needs at least one active administrator."]);
        }
    }

    private async Task<UserSummary> ToSummaryAsync(OperationContext context, HotelMembership membership, CancellationToken cancellationToken)
    {
        var user = membership.User;
        return new UserSummary(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            membership.Role,
            !membership.IsActive || (user.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow),
            user.CreatedAtUtc,
            IsCurrentUser(user.Id),
            await CountOtherHotelsAsync(context, user.Id, membership.HotelId, cancellationToken),
            await CanManageAccountAsync(context, user, membership.HotelId, cancellationToken));
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
