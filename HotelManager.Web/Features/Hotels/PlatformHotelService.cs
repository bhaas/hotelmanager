using System.Net.Mail;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.RoomPlanner;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Hotels;

/// <summary>Installation-wide hotel management for platform administrators.</summary>
public sealed class PlatformHotelService(
    TenantContext tenantContext,
    IServiceScopeFactory scopeFactory,
    IStringLocalizer<PlatformHotelService> localizer) : IPlatformHotelService
{
    public async Task<IReadOnlyList<PlatformHotelSummary>> GetHotelsAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdmin();
        await using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        return (await LoadSummariesAsync(dbContext, null, cancellationToken))
            .OrderBy(hotel => hotel.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<PlatformHotelCreateResult> CreateAsync(PlatformHotelCreateRequest request, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdmin();
        var name = Required(request.Name, 120, localizer["Hotel name is required."]);
        var email = NormalizeEmail(request.AdminEmail);
        var displayName = Required(request.AdminDisplayName, 120, localizer["Administrator name is required."]);

        await using var scope = CreateScope();
        var provider = scope.ServiceProvider;
        var dbContext = provider.GetRequiredService<HotelDbContext>();
        var normalizedName = name.ToLowerInvariant();
        if (await dbContext.Hotels.AnyAsync(hotel => hotel.Name.ToLower() == normalizedName, cancellationToken))
        {
            throw new HotelValidationException(localizer["A hotel with this name already exists."]);
        }

        var hotel = new Hotel
        {
            Id = Guid.NewGuid(),
            Name = name,
            City = Optional(request.City, 100),
            Country = Optional(request.Country, 100)
        };
        // Everything below (membership, demo data) belongs to the new hotel.
        provider.GetRequiredService<TenantContext>().UseHotel(hotel.Id);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        string? password = null;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.Hotels.Add(hotel);
            await dbContext.SaveChangesAsync(cancellationToken);

            var admin = await userManager.FindByEmailAsync(email);
            if (admin is null)
            {
                password = PasswordGenerator.Create();
                admin = new ApplicationUser
                {
                    HotelId = hotel.Id,
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = displayName
                };
                EnsureSucceeded(await userManager.CreateAsync(admin, password));
            }

            dbContext.HotelMemberships.Add(new HotelMembership { UserId = admin.Id, HotelId = hotel.Id, Role = HotelRoles.Admin });
            await dbContext.SaveChangesAsync(cancellationToken);

            if (request.SeedDemoData)
            {
                await RoomPlannerSeedService.SeedAsync(dbContext, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });

        var summary = (await LoadSummariesAsync(dbContext, hotel.Id, cancellationToken)).Single();
        return new PlatformHotelCreateResult(summary, email, password);
    }

    public async Task<PlatformHotelSummary> SetActiveAsync(Guid hotelId, bool isActive, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdmin();
        if (!isActive && hotelId == tenantContext.HotelId)
        {
            throw new HotelValidationException(localizer["Switch to another hotel before deactivating this one."]);
        }

        await using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        var hotel = await dbContext.Hotels.SingleOrDefaultAsync(item => item.Id == hotelId, cancellationToken)
            ?? throw new HotelValidationException(localizer["Hotel not found."]);
        hotel.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Ends open sessions so members immediately lose (or regain) access.
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var memberIds = await dbContext.HotelMemberships
            .Where(membership => membership.HotelId == hotelId)
            .Select(membership => membership.UserId)
            .ToListAsync(cancellationToken);
        foreach (var memberId in memberIds)
        {
            if (await userManager.FindByIdAsync(memberId) is { } member)
            {
                await userManager.UpdateSecurityStampAsync(member);
            }
        }

        return (await LoadSummariesAsync(dbContext, hotelId, cancellationToken)).Single();
    }

    private async Task<List<PlatformHotelSummary>> LoadSummariesAsync(HotelDbContext dbContext, Guid? hotelId, CancellationToken cancellationToken)
    {
        var currentHotelId = tenantContext.HotelId;
        var rows = await dbContext.Hotels
            .AsNoTracking()
            .Where(hotel => hotelId == null || hotel.Id == hotelId)
            .Select(hotel => new
            {
                hotel.Id,
                hotel.Name,
                hotel.City,
                hotel.Country,
                hotel.IsActive,
                MemberCount = dbContext.HotelMemberships.Count(membership => membership.HotelId == hotel.Id && membership.IsActive),
                hotel.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new PlatformHotelSummary(
                row.Id, row.Name, row.City, row.Country, row.IsActive, row.MemberCount, row.CreatedAtUtc, row.Id == currentHotelId))
            .ToList();
    }

    private AsyncServiceScope CreateScope()
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(tenantContext.User);
        return scope;
    }

    private void RequirePlatformAdmin()
    {
        if (tenantContext.User?.IsInRole(PlatformRoles.PlatformAdmin) != true)
        {
            throw new HotelValidationException(localizer["Only platform administrators can manage hotels."]);
        }
    }

    private string Required(string? value, int maxLength, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new HotelValidationException(message);
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new HotelValidationException(localizer["A field exceeds the maximum length."]);
    }

    private string? Optional(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new HotelValidationException(localizer["A field exceeds the maximum length."]);
    }

    private string NormalizeEmail(string? email)
    {
        var value = email?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 256 || !MailAddress.TryCreate(value, out var address) || address.Address != value)
        {
            throw new HotelValidationException(localizer["Enter a valid email address."]);
        }

        return value;
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new HotelValidationException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }
}
