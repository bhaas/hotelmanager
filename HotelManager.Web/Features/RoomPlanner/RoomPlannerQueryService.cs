using HotelManager.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.RoomPlanner;

public sealed class RoomPlannerQueryService(IDbContextFactory<HotelDbContext> dbContextFactory) : IRoomPlannerQueryService
{
    private const int MaxDaysInWindow = 31;

    public async Task<RoomPlannerSnapshot> GetSnapshotAsync(DateOnly startDate, int days, CancellationToken cancellationToken)
    {
        var normalizedDays = Math.Clamp(days, 1, MaxDaysInWindow);
        var endDate = startDate.AddDays(normalizedDays);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rooms = await dbContext.Rooms
            .AsNoTracking()
            .OrderBy(room => room.Floor)
            .ThenBy(room => room.Number)
            .Select(room => new RoomPlannerRoom(
                room.Id,
                room.Number,
                room.Type,
                room.Floor,
                room.Reservations
                    .Where(reservation =>
                        reservation.Status != Domain.ReservationStatus.CheckedOut &&
                        reservation.Status != Domain.ReservationStatus.Cancelled &&
                        reservation.CheckIn < endDate &&
                        reservation.CheckOut > startDate)
                    .OrderBy(reservation => reservation.CheckIn)
                    .Select(reservation => new RoomPlannerReservation(
                        reservation.Id,
                        reservation.GuestName,
                        reservation.CheckIn,
                        reservation.CheckOut,
                        reservation.Occupancy,
                        reservation.Status,
                        reservation.Notes))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new RoomPlannerSnapshot(startDate, normalizedDays, rooms);
    }
}
