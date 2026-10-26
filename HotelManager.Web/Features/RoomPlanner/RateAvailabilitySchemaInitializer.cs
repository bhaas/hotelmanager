using HotelManager.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.RoomPlanner;

public static class RateAvailabilitySchemaInitializer
{
    public static async Task EnsureSchemaAsync(HotelDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var sql = """
                  ALTER TABLE "Reservations"
                  ADD COLUMN IF NOT EXISTS "Occupancy" integer NOT NULL DEFAULT 1;

                  DO $$
                  BEGIN
                      IF NOT EXISTS (
                          SELECT 1
                          FROM pg_constraint
                          WHERE conname = 'CK_Reservations_OccupancyPositive'
                      ) THEN
                          ALTER TABLE "Reservations"
                          ADD CONSTRAINT "CK_Reservations_OccupancyPositive" CHECK ("Occupancy" > 0);
                      END IF;
                  END $$;

                  CREATE TABLE IF NOT EXISTS "RoomRateRules" (
                      "Id" uuid NOT NULL,
                      "RoomType" character varying(40) NOT NULL,
                      "StartDate" date NOT NULL,
                      "EndDate" date NOT NULL,
                      "OccupancyFrom" integer NOT NULL,
                      "OccupancyTo" integer,
                      "NightlyNetAmount" numeric(12,2) NOT NULL,
                      "VatRatePercent" numeric(5,2) NOT NULL,
                      "Currency" character varying(3) NOT NULL,
                      "Priority" integer NOT NULL,
                      "CreatedAtUtc" timestamp with time zone NOT NULL,
                      CONSTRAINT "PK_RoomRateRules" PRIMARY KEY ("Id"),
                      CONSTRAINT "CK_RoomRateRules_EndAfterStart" CHECK ("EndDate" > "StartDate"),
                      CONSTRAINT "CK_RoomRateRules_OccupancyFromPositive" CHECK ("OccupancyFrom" > 0),
                      CONSTRAINT "CK_RoomRateRules_OccupancyToValid" CHECK ("OccupancyTo" IS NULL OR "OccupancyTo" >= "OccupancyFrom"),
                      CONSTRAINT "CK_RoomRateRules_NightlyNetNonNegative" CHECK ("NightlyNetAmount" >= 0),
                      CONSTRAINT "CK_RoomRateRules_VatRateAllowed" CHECK ("VatRatePercent" IN (0, 10, 13, 20)),
                      CONSTRAINT "CK_RoomRateRules_CurrencyEur" CHECK ("Currency" = 'EUR')
                  );

                  CREATE TABLE IF NOT EXISTS "RoomAvailabilityRules" (
                      "Id" uuid NOT NULL,
                      "RoomId" uuid,
                      "RoomType" character varying(40),
                      "StartDate" date NOT NULL,
                      "EndDate" date NOT NULL,
                      "StopSell" boolean NOT NULL,
                      "ClosedToArrival" boolean NOT NULL,
                      "ClosedToDeparture" boolean NOT NULL,
                      "MinStayNights" integer,
                      "CreatedAtUtc" timestamp with time zone NOT NULL,
                      CONSTRAINT "PK_RoomAvailabilityRules" PRIMARY KEY ("Id"),
                      CONSTRAINT "FK_RoomAvailabilityRules_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE CASCADE,
                      CONSTRAINT "CK_RoomAvailabilityRules_EndAfterStart" CHECK ("EndDate" > "StartDate"),
                      CONSTRAINT "CK_RoomAvailabilityRules_ScopeRequired" CHECK ("RoomId" IS NOT NULL OR "RoomType" IS NOT NULL),
                      CONSTRAINT "CK_RoomAvailabilityRules_MinStayPositive" CHECK ("MinStayNights" IS NULL OR "MinStayNights" > 0)
                  );

                  CREATE INDEX IF NOT EXISTS "IX_RoomRateRules_RoomType_StartDate_EndDate"
                      ON "RoomRateRules" ("RoomType", "StartDate", "EndDate");

                  CREATE INDEX IF NOT EXISTS "IX_RoomAvailabilityRules_RoomId_StartDate_EndDate"
                      ON "RoomAvailabilityRules" ("RoomId", "StartDate", "EndDate");

                  CREATE INDEX IF NOT EXISTS "IX_RoomAvailabilityRules_RoomType_StartDate_EndDate"
                      ON "RoomAvailabilityRules" ("RoomType", "StartDate", "EndDate");
                  """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
