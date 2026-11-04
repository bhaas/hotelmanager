using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Infrastructure;
using HotelManager.Web.Infrastructure.Hosting;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HotelManager.Tests;

/// <summary>
/// Creates a throw-away PostgreSQL database per test run (migrated with the real migrations).
/// Connection: env <c>HOTELMANAGER_TEST_DB</c>, otherwise the web project's user-secrets/env settings.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; private set; } = null!;

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(HotelDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var baseConnection = configuration["HOTELMANAGER_TEST_DB"] is { Length: > 0 } explicitConnection
            ? DatabaseConnectionString.Normalize(explicitConnection)
            : DatabaseConnectionString.Resolve(configuration);

        connectionString = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = $"hotelmanager_test_{Guid.NewGuid():N}"[..40]
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddHotelManagerCore(connectionString);
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HotelDbContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HotelDbContext>>().CreateDbContextAsync())
        {
            await db.Database.EnsureDeletedAsync();
        }

        await Services.DisposeAsync();
    }

    /// <summary>Creates a scope bound to a hotel (or to no hotel when <paramref name="hotelId"/> is null).</summary>
    public AsyncServiceScope CreateScope(Guid? hotelId)
    {
        var scope = Services.CreateAsyncScope();
        if (hotelId is { } id)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().UseHotel(id);
        }

        return scope;
    }

    public static Task<HotelDbContext> CreateDbAsync(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IDbContextFactory<HotelDbContext>>().CreateDbContextAsync();

    public async Task<Guid> CreateHotelAsync(string name)
    {
        await using var scope = CreateScope(null);
        await using var db = await CreateDbAsync(scope);
        var hotel = new Hotel { Id = Guid.NewGuid(), Name = name };
        db.Hotels.Add(hotel);
        await db.SaveChangesAsync();
        return hotel.Id;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
