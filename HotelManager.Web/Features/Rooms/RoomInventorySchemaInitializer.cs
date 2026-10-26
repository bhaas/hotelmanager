using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.Rooms;

public static class RoomInventorySchemaInitializer
{
    public static async Task EnsureSchemaAsync(HotelDbContext dbContext, CancellationToken cancellationToken = default)
    {
        const string sql = """
                           ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "Features" character varying(300);
                           ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "Notes" character varying(500);
                           ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE;

                           CREATE TABLE IF NOT EXISTS "RoomCategories" (
                               "Id" uuid NOT NULL,
                               "Code" character varying(10) NOT NULL,
                               "Name" character varying(40) NOT NULL,
                               "Description" character varying(500),
                               "StandardOccupancy" integer NOT NULL,
                               "MaxOccupancy" integer NOT NULL,
                               "SortOrder" integer NOT NULL,
                               CONSTRAINT "PK_RoomCategories" PRIMARY KEY ("Id"),
                               CONSTRAINT "CK_RoomCategories_StandardOccupancyPositive" CHECK ("StandardOccupancy" > 0),
                               CONSTRAINT "CK_RoomCategories_MaxOccupancyValid" CHECK ("MaxOccupancy" >= "StandardOccupancy")
                           );

                           CREATE UNIQUE INDEX IF NOT EXISTS "IX_RoomCategories_Code" ON "RoomCategories" ("Code");
                           CREATE UNIQUE INDEX IF NOT EXISTS "IX_RoomCategories_Name" ON "RoomCategories" ("Name");
                           """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Creates a category for every room type that is used by rooms but not yet defined
    /// (existing installations and seed data predate the category master data).
    /// </summary>
    public static async Task BackfillCategoriesAsync(HotelDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.RoomCategories
            .Select(category => new { category.Code, category.Name })
            .ToListAsync(cancellationToken);
        var existingNames = existing.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCodes = existing.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingTypes = (await dbContext.Rooms
                .Select(room => room.Type)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Where(type => !existingNames.Contains(type))
            .OrderBy(type => type)
            .ToList();

        if (missingTypes.Count == 0)
        {
            return;
        }

        var sortOrder = existing.Count * 10;
        foreach (var type in missingTypes)
        {
            var isSuite = type.Contains("suite", StringComparison.OrdinalIgnoreCase);
            var isSingle = type.Contains("single", StringComparison.OrdinalIgnoreCase);
            sortOrder += 10;

            dbContext.RoomCategories.Add(new RoomCategory
            {
                Id = Guid.NewGuid(),
                Code = CreateUniqueCode(type, usedCodes),
                Name = type,
                StandardOccupancy = isSingle ? 1 : 2,
                MaxOccupancy = isSuite ? 4 : isSingle ? 1 : 2,
                SortOrder = sortOrder
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string CreateUniqueCode(string name, HashSet<string> usedCodes)
    {
        var words = name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var initials = new string(words.Select(word => char.ToUpperInvariant(word[0])).Where(char.IsLetterOrDigit).ToArray());
        var baseCode = initials.Length >= 2
            ? initials
            : new string(name.Where(char.IsLetterOrDigit).Take(3).Select(char.ToUpperInvariant).ToArray());
        baseCode = baseCode.Length == 0 ? "CAT" : baseCode[..Math.Min(baseCode.Length, 8)];

        var code = baseCode;
        for (var suffix = 2; usedCodes.Contains(code); suffix++)
        {
            code = $"{baseCode}{suffix}";
        }

        usedCodes.Add(code);
        return code;
    }
}
