using HotelManager.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.RoomPlanner;

public static class HousekeepingSchemaInitializer
{
    public static async Task EnsureSchemaAsync(HotelDbContext dbContext, CancellationToken cancellationToken = default)
    {
        const string sql = """
                           ALTER TABLE "Rooms"
                           ADD COLUMN IF NOT EXISTS "HousekeepingStatus" character varying(16) NOT NULL DEFAULT 'Clean';
                           """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
