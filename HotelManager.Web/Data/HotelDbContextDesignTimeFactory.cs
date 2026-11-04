using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HotelManager.Web.Data;

/// <summary>Used by <c>dotnet ef</c> only; creating migrations does not connect to the database.</summary>
public sealed class HotelDbContextDesignTimeFactory : IDesignTimeDbContextFactory<HotelDbContext>
{
    public HotelDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HOTEL_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=hotelmanager_dev;Username=postgres";
        var options = new DbContextOptionsBuilder<HotelDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new HotelDbContext(options);
    }
}
