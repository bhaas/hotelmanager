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
/// its administrator account and (optionally) demo data.
/// </summary>
public static class HotelBootstrapper
{
    public static async Task RunAsync(IServiceProvider services, BootstrapOptions options, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var dbContext = provider.GetRequiredService<HotelDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in HotelRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"create role {role}");
            }
        }

        if (await dbContext.Hotels.AnyAsync(cancellationToken))
        {
            return;
        }

        var hotel = new Hotel { Id = Guid.NewGuid(), Name = options.HotelName.Trim() };
        dbContext.Hotels.Add(hotel);
        await dbContext.SaveChangesAsync(cancellationToken);
        provider.GetRequiredService<TenantContext>().UseHotel(hotel.Id);

        var generatedPassword = string.IsNullOrWhiteSpace(options.AdminPassword);
        var password = generatedPassword ? PasswordGenerator.Create() : options.AdminPassword!;
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = new ApplicationUser
        {
            HotelId = hotel.Id,
            UserName = options.AdminEmail.Trim(),
            Email = options.AdminEmail.Trim(),
            EmailConfirmed = true,
            DisplayName = "Administrator"
        };
        EnsureSucceeded(await userManager.CreateAsync(admin, password), "create administrator");
        EnsureSucceeded(await userManager.AddToRoleAsync(admin, HotelRoles.Admin), "assign administrator role");

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

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Bootstrap failed to {action}: {string.Join(" ", result.Errors.Select(error => error.Description))}");
        }
    }
}
