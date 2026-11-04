using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.RoomPlanner;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Infrastructure.Hosting;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string HotelName { get; set; } = "Demo Hotel";

    public string AdminEmail { get; set; } = "admin@hotelmanager.local";

    /// <summary>Optional. When empty a random password is generated and logged once.</summary>
    public string? AdminPassword { get; set; }

    public bool SeedDemoData { get; set; } = true;
}

/// <summary>
/// Applies EF Core migrations and, on an empty installation, creates the first hotel,
/// its administrator account (who also becomes platform administrator) and optionally demo data.
/// On existing installations it makes sure there is a platform administrator.
/// </summary>
public static class HotelBootstrapper
{
    public static async Task RunAsync(IServiceProvider services, BootstrapOptions options, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var dbContext = provider.GetRequiredService<HotelDbContext>();

        // The row-level-security role is created by a migration, so migrations run as the connection owner.
        dbContext.BypassRowLevelSecurity = true;
        try
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            dbContext.BypassRowLevelSecurity = false;
        }

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(PlatformRoles.PlatformAdmin))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(PlatformRoles.PlatformAdmin)), "create platform role");
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await dbContext.Hotels.AnyAsync(cancellationToken))
        {
            await EnsurePlatformAdminAsync(dbContext, userManager, options, logger, cancellationToken);
            return;
        }

        var hotel = new Hotel { Id = Guid.NewGuid(), Name = options.HotelName.Trim() };
        dbContext.Hotels.Add(hotel);
        await dbContext.SaveChangesAsync(cancellationToken);
        provider.GetRequiredService<TenantContext>().UseHotel(hotel.Id);

        var generatedPassword = string.IsNullOrWhiteSpace(options.AdminPassword);
        var password = generatedPassword ? PasswordGenerator.Create() : options.AdminPassword!;
        var admin = new ApplicationUser
        {
            HotelId = hotel.Id,
            UserName = options.AdminEmail.Trim(),
            Email = options.AdminEmail.Trim(),
            EmailConfirmed = true,
            DisplayName = "Administrator"
        };
        EnsureSucceeded(await userManager.CreateAsync(admin, password), "create administrator");
        EnsureSucceeded(await userManager.AddToRoleAsync(admin, PlatformRoles.PlatformAdmin), "assign platform administrator role");
        dbContext.HotelMemberships.Add(new HotelMembership { UserId = admin.Id, HotelId = hotel.Id, Role = HotelRoles.Admin });
        await dbContext.SaveChangesAsync(cancellationToken);

        if (generatedPassword)
        {
            logger.LogWarning(
                "Created hotel '{HotelName}' with administrator {AdminEmail} and generated password {AdminPassword}. " +
                "Sign in and change it now; it is not shown again.",
                hotel.Name, admin.Email, password);
        }
        else
        {
            logger.LogInformation("Created hotel '{HotelName}' with administrator {AdminEmail}.", hotel.Name, admin.Email);
        }

        if (options.SeedDemoData)
        {
            await RoomPlannerSeedService.SeedAsync(dbContext, cancellationToken);
        }
    }

    /// <summary>
    /// Installations created before multi-hotel support have no platform administrator:
    /// promote the configured bootstrap administrator, or else the longest-serving hotel administrator.
    /// </summary>
    private static async Task EnsurePlatformAdminAsync(
        HotelDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        BootstrapOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if ((await userManager.GetUsersInRoleAsync(PlatformRoles.PlatformAdmin)).Count > 0)
        {
            return;
        }

        var candidate = await userManager.FindByEmailAsync(options.AdminEmail.Trim());
        if (candidate is null)
        {
            var candidateId = await dbContext.HotelMemberships
                .Where(membership => membership.Role == HotelRoles.Admin && membership.IsActive)
                .OrderBy(membership => membership.CreatedAtUtc)
                .ThenBy(membership => membership.User.CreatedAtUtc)
                .Select(membership => membership.UserId)
                .FirstOrDefaultAsync(cancellationToken);
            candidate = candidateId is null ? null : await userManager.FindByIdAsync(candidateId);
        }

        if (candidate is null)
        {
            logger.LogWarning("No platform administrator could be determined. Assign the {Role} role manually.", PlatformRoles.PlatformAdmin);
            return;
        }

        EnsureSucceeded(await userManager.AddToRoleAsync(candidate, PlatformRoles.PlatformAdmin), "assign platform administrator role");
        logger.LogInformation("{AdminEmail} is now platform administrator.", candidate.Email);
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Bootstrap failed to {action}: {string.Join(" ", result.Errors.Select(error => error.Description))}");
        }
    }
}
