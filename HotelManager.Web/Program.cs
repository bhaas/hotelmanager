using HotelManager.Web.Components;
using HotelManager.Web.Data;
using HotelManager.Web.Features.Billing;
using HotelManager.Web.Features.Guests;
using HotelManager.Web.Features.Integrations.Beds24;
using HotelManager.Web.Features.RoomPlanner;
using HotelManager.Web.Features.Rooms;
using HotelManager.Web.Infrastructure;
using HotelManager.Web.Infrastructure.Hosting;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var baseDirectory = AppContext.BaseDirectory;
var webRootPath = ResolveWebRootPath(baseDirectory);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = baseDirectory,
    WebRootPath = webRootPath
});
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
QuestPDF.Settings.License = LicenseType.Community;

if (!builder.Environment.IsDevelopment())
{
    // Lets the locally built exe reuse the developer's secrets; containers have no secrets file.
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
}

var connectionString = DatabaseConnectionString.Resolve(builder.Configuration);
var migrateDatabaseOnStartup = builder.Configuration.GetValue("Database:MigrateOnStartup", true);
var bootstrapOptions = builder.Configuration.GetSection(BootstrapOptions.SectionName).Get<BootstrapOptions>() ?? new BootstrapOptions();
var useHttpsRedirection = builder.Configuration.GetValue("Http:UseHttpsRedirection", builder.Environment.IsDevelopment());
var supportedCultureNames = new[] { "en", "de" };

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = supportedCultureNames
        .Select(culture => new CultureInfo(culture))
        .ToList();

    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddHotelManagerCore(connectionString);
builder.Services.AddHotelManagerIdentity();
builder.Services.AddHostedService<Beds24SyncWorker>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<HotelDbContext>("postgres");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var requestLocalizationOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseRequestLocalization(requestLocalizationOptions);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseTenantContext();
app.UseAuthorization();
app.UseAntiforgery();

if (migrateDatabaseOnStartup)
{
    // Fail fast: a container that cannot reach or migrate its database should not report healthy.
    await HotelBootstrapper.RunAsync(app.Services, bootstrapOptions, app.Logger);
}

app.MapStaticAssets();
app.MapGet("/set-culture", (string culture, string? redirectUri, HttpContext httpContext) =>
{
    if (!supportedCultureNames.Contains(culture, StringComparer.OrdinalIgnoreCase))
    {
        culture = "en";
    }

    httpContext.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true
        });

    var localRedirect = string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri;
    if (!Uri.IsWellFormedUriString(localRedirect, UriKind.Relative) || !localRedirect.StartsWith('/'))
    {
        localRedirect = "/";
    }

    return Results.LocalRedirect(localRedirect);
});
app.MapPost("/account/logout", async (SignInManager<HotelManager.Web.Domain.ApplicationUser> signInManager, [FromForm] string? returnUrl) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/account/login");
});
app.MapPost("/account/switch-hotel", async (
    [FromForm] Guid? hotelId,
    [FromForm] string? returnUrl,
    HttpContext httpContext,
    UserManager<HotelManager.Web.Domain.ApplicationUser> userManager,
    SignInManager<HotelManager.Web.Domain.ApplicationUser> signInManager,
    HotelAccessService hotelAccess) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user is null)
    {
        return Results.LocalRedirect("/account/login");
    }

    // Without a hotel id the current sign-in is only refreshed (e.g. after the hotel was renamed).
    if (hotelId is { } targetHotelId && targetHotelId != user.HotelId)
    {
        var accessible = await hotelAccess.GetAccessibleHotelsAsync(user, httpContext.RequestAborted);
        if (accessible.All(hotel => hotel.HotelId != targetHotelId))
        {
            return Results.Forbid();
        }

        user.HotelId = targetHotelId;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return Results.Problem("The hotel could not be switched.");
        }
    }

    await signInManager.RefreshSignInAsync(user);
    return Results.LocalRedirect(SafeLocalPath(returnUrl));
}).RequireAuthorization();
// Re-issues the caller's own cookie (e.g. after renaming the hotel); changes no data, so GET is safe.
app.MapGet("/account/refresh-sign-in", async (
    string? returnUrl,
    HttpContext httpContext,
    UserManager<HotelManager.Web.Domain.ApplicationUser> userManager,
    SignInManager<HotelManager.Web.Domain.ApplicationUser> signInManager) =>
{
    var user = await userManager.GetUserAsync(httpContext.User);
    if (user is null)
    {
        return Results.LocalRedirect("/account/login");
    }

    await signInManager.RefreshSignInAsync(user);
    return Results.LocalRedirect(SafeLocalPath(returnUrl));
}).RequireAuthorization();
app.MapHealthChecks("/health");
app.MapRoomPlannerApi();
app.MapRoomInventoryApi();
app.MapBillingApi();
app.MapGuestApi();
app.MapBeds24Webhook();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string SafeLocalPath(string? path) =>
    !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.StartsWith("//") && !path.StartsWith("/\\") &&
    Uri.IsWellFormedUriString(path, UriKind.Relative)
        ? path
        : "/";

static string ResolveWebRootPath(string baseDirectory)
{
    var localWebRoot = Path.Combine(baseDirectory, "wwwroot");
    if (!Directory.Exists(localWebRoot))
    {
        Directory.CreateDirectory(localWebRoot);
    }

    return localWebRoot;
}
