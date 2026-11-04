using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Billing;
using HotelManager.Web.Features.Hotels;
using HotelManager.Web.Features.RoomPlanner;
using HotelManager.Web.Features.Rooms;
using HotelManager.Web.Features.Users;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Infrastructure;

public static class HotelManagerServiceCollectionExtensions
{
    /// <summary>Database, tenancy and domain services. Shared by the web host and the integration tests.</summary>
    public static IServiceCollection AddHotelManagerCore(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());

        // Scoped factory so every DbContext receives the tenant of its request/circuit scope.
        services.AddSingleton<TenantSessionInterceptor>();
        services.AddDbContextFactory<HotelDbContext>(
            (provider, options) => options
                .UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                })
                .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()),
            ServiceLifetime.Scoped);

        services.AddScoped<IRoomPlannerQueryService, RoomPlannerQueryService>();
        services.AddScoped<IRoomPlannerCommandService, RoomPlannerCommandService>();
        services.AddScoped<IRateAvailabilityService, RateAvailabilityService>();
        services.AddScoped<IRoomInventoryService, RoomInventoryService>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IHotelProfileService, HotelProfileService>();
        services.AddScoped<IPlatformHotelService, PlatformHotelService>();
        services.AddSingleton<IInvoicePdfService, InvoicePdfService>();
        return services;
    }

    /// <summary>ASP.NET Core Identity with hotel-aware claims, role policies and persisted data protection keys.</summary>
    public static IServiceCollection AddHotelManagerIdentity(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddScoped<CircuitHandler, TenantCircuitHandler>();
        services.AddScoped<HotelAccessService>();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<HotelDbContext>()
            .AddSignInManager<HotelSignInManager>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<HotelUserClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
            options.Cookie.Name = "HotelManager.Auth";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => RespondOrRedirect(context, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => RespondOrRedirect(context, StatusCodes.Status403Forbidden)
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(HotelPolicies.FrontOffice, policy => policy.RequireRole(HotelRoles.Admin, HotelRoles.Manager, HotelRoles.FrontDesk))
            .AddPolicy(HotelPolicies.ManageProperty, policy => policy.RequireRole(HotelRoles.Admin, HotelRoles.Manager))
            .AddPolicy(HotelPolicies.ManageUsers, policy => policy.RequireRole(HotelRoles.Admin))
            .AddPolicy(HotelPolicies.ManagePlatform, policy => policy.RequireRole(PlatformRoles.PlatformAdmin));

        // Containers have no persistent disk; keys in the database keep cookies valid across deploys and instances.
        services.AddDataProtection()
            .SetApplicationName("HotelManager")
            .PersistKeysToDbContext<HotelDbContext>();

        return services;
    }

    private static Task RespondOrRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}

