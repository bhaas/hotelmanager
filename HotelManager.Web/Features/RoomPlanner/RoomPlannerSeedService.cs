using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.RoomPlanner;

public static class RoomPlannerSeedService
{
    public static async Task SeedAsync(HotelDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var hasRooms = await dbContext.Rooms.AnyAsync(cancellationToken);

        if (!hasRooms)
        {
            var rooms = new[]
            {
                new Room { Id = Guid.NewGuid(), Number = "101", Type = "Classic King", Floor = 1 },
                new Room { Id = Guid.NewGuid(), Number = "102", Type = "Classic Twin", Floor = 1 },
                new Room { Id = Guid.NewGuid(), Number = "201", Type = "Deluxe King", Floor = 2 },
                new Room { Id = Guid.NewGuid(), Number = "202", Type = "Deluxe Twin", Floor = 2 },
                new Room { Id = Guid.NewGuid(), Number = "301", Type = "Suite", Floor = 3 },
                new Room { Id = Guid.NewGuid(), Number = "302", Type = "Executive Suite", Floor = 3 }
            };

            dbContext.Rooms.AddRange(rooms);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var reservations = new[]
            {
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = rooms[0].Id,
                    GuestName = "Laura Stein",
                    CheckIn = today.AddDays(-1),
                    CheckOut = today.AddDays(2),
                    Occupancy = 2,
                    Status = ReservationStatus.CheckedIn,
                    Notes = "Early breakfast"
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = rooms[1].Id,
                    GuestName = "Noah Richter",
                    CheckIn = today.AddDays(2),
                    CheckOut = today.AddDays(5),
                    Occupancy = 1,
                    Status = ReservationStatus.Confirmed
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = rooms[2].Id,
                    GuestName = "Sofia Keller",
                    CheckIn = today,
                    CheckOut = today.AddDays(4),
                    Occupancy = 2,
                    Status = ReservationStatus.Confirmed
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = rooms[3].Id,
                    GuestName = "Housekeeping",
                    CheckIn = today.AddDays(5),
                    CheckOut = today.AddDays(7),
                    Occupancy = 1,
                    Status = ReservationStatus.Maintenance,
                    Notes = "Deep cleaning"
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = rooms[4].Id,
                    GuestName = "Jonas Maier",
                    CheckIn = today.AddDays(1),
                    CheckOut = today.AddDays(6),
                    Occupancy = 2,
                    Status = ReservationStatus.Confirmed
                },
                new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = rooms[5].Id,
                    GuestName = "Revenue block",
                    CheckIn = today.AddDays(8),
                    CheckOut = today.AddDays(11),
                    Occupancy = 1,
                    Status = ReservationStatus.Blocked,
                    Notes = "Contracted group hold"
                }
            };

            dbContext.Reservations.AddRange(reservations);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (await dbContext.RoomRateRules.AnyAsync(cancellationToken))
        {
            return;
        }

        var roomTypeRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Classic King"] = 110m,
            ["Classic Twin"] = 105m,
            ["Deluxe King"] = 145m,
            ["Deluxe Twin"] = 135m,
            ["Suite"] = 210m,
            ["Executive Suite"] = 275m
        };

        var roomTypes = await dbContext.Rooms
            .AsNoTracking()
            .Select(room => room.Type)
            .Distinct()
            .ToListAsync(cancellationToken);

        var seasonStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        var seasonEnd = seasonStart.AddDays(395);
        foreach (var roomType in roomTypes)
        {
            var baseRate = roomTypeRates.TryGetValue(roomType, out var configuredRate) ? configuredRate : 120m;

            dbContext.RoomRateRules.Add(new RoomRateRule
            {
                Id = Guid.NewGuid(),
                RoomType = roomType,
                StartDate = seasonStart,
                EndDate = seasonEnd,
                OccupancyFrom = 1,
                OccupancyTo = 1,
                NightlyNetAmount = baseRate,
                VatRatePercent = 10m,
                Currency = "EUR",
                Priority = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            dbContext.RoomRateRules.Add(new RoomRateRule
            {
                Id = Guid.NewGuid(),
                RoomType = roomType,
                StartDate = seasonStart,
                EndDate = seasonEnd,
                OccupancyFrom = 2,
                OccupancyTo = 4,
                NightlyNetAmount = baseRate + 24m,
                VatRatePercent = 10m,
                Currency = "EUR",
                Priority = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
