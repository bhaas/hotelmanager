using HotelManager.Web.Components;
using HotelManager.Web.Data;
using HotelManager.Web.Features.Billing;
using HotelManager.Web.Features.RoomPlanner;
using HotelManager.Web.Features.Rooms;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
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

var connectionString = builder.Configuration.GetConnectionString("HotelDb")
    ?? builder.Configuration["HOTEL_DB_CONNECTION"]
    ?? throw new InvalidOperationException("Database connection string missing. Set ConnectionStrings:HotelDb or HOTEL_DB_CONNECTION.");
var initializeDatabaseOnStartup = builder.Environment.IsDevelopment() ||
                                  builder.Configuration.GetValue("Database:InitializeOnStartup", false);
var useHttpsRedirection = builder.Configuration.GetValue("Http:UseHttpsRedirection", builder.Environment.IsDevelopment());
var supportedCultureNames = new[] { "en", "de" };

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<BillingComplianceOptions>(builder.Configuration.GetSection(BillingComplianceOptions.SectionName));
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
builder.Services.AddDbContextFactory<HotelDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
    });
});
builder.Services.AddScoped<IRoomPlannerQueryService, RoomPlannerQueryService>();
builder.Services.AddScoped<IRoomPlannerCommandService, RoomPlannerCommandService>();
builder.Services.AddScoped<IRateAvailabilityService, RateAvailabilityService>();
builder.Services.AddScoped<IRoomInventoryService, RoomInventoryService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddSingleton<IInvoicePdfService, InvoicePdfService>();
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
app.UseAntiforgery();

if (initializeDatabaseOnStartup)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HotelDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await RateAvailabilitySchemaInitializer.EnsureSchemaAsync(dbContext);
        await BillingSchemaInitializer.EnsureSchemaAsync(dbContext);
        await HousekeepingSchemaInitializer.EnsureSchemaAsync(dbContext);
        await RoomInventorySchemaInitializer.EnsureSchemaAsync(dbContext);
        await RoomPlannerSeedService.SeedAsync(dbContext);
        await RoomInventorySchemaInitializer.BackfillCategoriesAsync(dbContext);
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(
            exception,
            "Database initialization on startup failed. The app will continue running and retry on room planner requests.");
    }
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
app.MapHealthChecks("/health");
app.MapRoomPlannerApi();
app.MapRoomInventoryApi();
app.MapBillingApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveWebRootPath(string baseDirectory)
{
    var localWebRoot = Path.Combine(baseDirectory, "wwwroot");
    if (!Directory.Exists(localWebRoot))
    {
        Directory.CreateDirectory(localWebRoot);
    }

    return localWebRoot;
}
