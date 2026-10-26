using System.Data;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.RoomPlanner;

public sealed class RoomPlannerCommandService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<RoomPlannerCommandService> localizer,
    IRateAvailabilityService rateAvailabilityService) : IRoomPlannerCommandService
{
    public async Task<RoomPlannerReservation> CreateReservationAsync(RoomPlannerReservationUpsertRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        return await ExecuteSerializableAsync(async dbContext =>
        {
            await EnsureRoomBookableAsync(dbContext, request, cancellationToken);
            await EnsureNoOverlappingReservationAsync(
                dbContext,
                request.RoomId,
                request.CheckIn,
                request.CheckOut,
                excludedReservationId: null,
                cancellationToken);
            await EnsureRateAndAvailabilityAsync(request, cancellationToken);

            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                RoomId = request.RoomId,
                GuestName = request.GuestName.Trim(),
                CheckIn = request.CheckIn,
                CheckOut = request.CheckOut,
                Occupancy = request.Occupancy,
                Status = request.Status,
                Notes = NormalizeNotes(request.Notes)
            };

            dbContext.Reservations.Add(reservation);
            await dbContext.SaveChangesAsync(cancellationToken);

            return MapReservation(reservation);
        }, cancellationToken);
    }

    public async Task<RoomPlannerReservation> UpdateReservationAsync(Guid reservationId, RoomPlannerReservationUpsertRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        return await ExecuteSerializableAsync(async dbContext =>
        {
            var reservation = await dbContext.Reservations
                .SingleOrDefaultAsync(existingReservation => existingReservation.Id == reservationId, cancellationToken)
                ?? throw new ReservationNotFoundException(localizer["Reservation not found."]);

            await EnsureRoomBookableAsync(dbContext, request, cancellationToken);
            await EnsureNoOverlappingReservationAsync(
                dbContext,
                request.RoomId,
                request.CheckIn,
                request.CheckOut,
                reservationId,
                cancellationToken);
            await EnsureRateAndAvailabilityAsync(request, cancellationToken);

            reservation.RoomId = request.RoomId;
            reservation.GuestName = request.GuestName.Trim();
            reservation.CheckIn = request.CheckIn;
            reservation.CheckOut = request.CheckOut;
            reservation.Occupancy = request.Occupancy;
            reservation.Status = request.Status;
            reservation.Notes = NormalizeNotes(request.Notes);

            await dbContext.SaveChangesAsync(cancellationToken);

            return MapReservation(reservation);
        }, cancellationToken);
    }

    public Task<RoomPlannerReservation> CheckInAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(
            reservationId,
            ReservationStatus.CheckedIn,
            currentStatus => currentStatus == ReservationStatus.Confirmed,
            localizer["Only confirmed reservations can be checked in."],
            cancellationToken);
    }

    public Task<RoomPlannerReservation> CheckOutAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(
            reservationId,
            ReservationStatus.CheckedOut,
            currentStatus => currentStatus == ReservationStatus.CheckedIn,
            localizer["Only in-house reservations can be checked out."],
            cancellationToken);
    }

    public Task<RoomPlannerReservation> CancelAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(
            reservationId,
            ReservationStatus.Cancelled,
            currentStatus => currentStatus is ReservationStatus.Confirmed or ReservationStatus.Blocked or ReservationStatus.Maintenance,
            localizer["Only confirmed, blocked, or maintenance reservations can be cancelled."],
            cancellationToken);
    }

    private async Task<RoomPlannerReservation> ChangeStatusAsync(
        Guid reservationId,
        ReservationStatus targetStatus,
        Func<ReservationStatus, bool> transitionGuard,
        string transitionErrorMessage,
        CancellationToken cancellationToken)
    {
        return await ExecuteSerializableAsync(async dbContext =>
        {
            var reservation = await dbContext.Reservations
                .SingleOrDefaultAsync(existingReservation => existingReservation.Id == reservationId, cancellationToken)
                ?? throw new ReservationNotFoundException(localizer["Reservation not found."]);

            if (!transitionGuard(reservation.Status))
            {
                throw new ReservationValidationException(transitionErrorMessage);
            }

            reservation.Status = targetStatus;

            // A departed room needs cleaning before it can be sold again.
            if (targetStatus == ReservationStatus.CheckedOut)
            {
                var room = await dbContext.Rooms.SingleAsync(item => item.Id == reservation.RoomId, cancellationToken);
                room.HousekeepingStatus = HousekeepingStatus.Dirty;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return MapReservation(reservation);
        }, cancellationToken);
    }

    public async Task<RoomHousekeepingState> SetHousekeepingStatusAsync(Guid roomId, HousekeepingStatus status, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ReservationValidationException(localizer["Unknown housekeeping status."]);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var room = await dbContext.Rooms.SingleOrDefaultAsync(item => item.Id == roomId, cancellationToken)
            ?? throw new ReservationNotFoundException(localizer["Selected room does not exist."]);

        room.HousekeepingStatus = status;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RoomHousekeepingState(room.Id, room.Number, room.HousekeepingStatus);
    }

    private void ValidateRequest(RoomPlannerReservationUpsertRequest request)
    {
        if (request.RoomId == Guid.Empty)
        {
            throw new ReservationValidationException(localizer["Room is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.GuestName))
        {
            throw new ReservationValidationException(localizer["Guest name is required."]);
        }

        if (request.GuestName.Trim().Length > 120)
        {
            throw new ReservationValidationException(localizer["Guest name cannot exceed 120 characters."]);
        }

        if (request.CheckOut <= request.CheckIn)
        {
            throw new ReservationValidationException(localizer["Check-out must be after check-in."]);
        }

        if (request.Occupancy <= 0)
        {
            throw new ReservationValidationException(localizer["Occupancy must be greater than 0."]);
        }

        if (request.Notes is not null && request.Notes.Trim().Length > 500)
        {
            throw new ReservationValidationException(localizer["Notes cannot exceed 500 characters."]);
        }
    }

    private async Task EnsureRateAndAvailabilityAsync(
        RoomPlannerReservationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Status is ReservationStatus.Blocked or ReservationStatus.Maintenance)
        {
            return;
        }

        var availability = await rateAvailabilityService.EvaluateAvailabilityAsync(
            request.RoomId,
            request.CheckIn,
            request.CheckOut,
            cancellationToken);

        if (!availability.IsAvailable)
        {
            throw new ReservationConflictException(availability.Message ?? localizer["The selected room is unavailable for the requested date range."]);
        }

        var rateQuote = await rateAvailabilityService.QuoteAsync(
            new RoomRateQuoteRequest(request.RoomId, request.CheckIn, request.CheckOut, request.Occupancy),
            cancellationToken);

        if (!rateQuote.IsQuoteable)
        {
            throw new ReservationValidationException(rateQuote.Message ?? localizer["No room rate is configured for the requested stay."]);
        }
    }

    private async Task EnsureRoomBookableAsync(
        HotelDbContext dbContext,
        RoomPlannerReservationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var room = await dbContext.Rooms
            .AsNoTracking()
            .Where(item => item.Id == request.RoomId)
            .Select(item => new
            {
                item.Number,
                item.IsActive,
                MaxOccupancy = dbContext.RoomCategories
                    .Where(category => category.Name == item.Type)
                    .Select(category => (int?)category.MaxOccupancy)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ReservationValidationException(localizer["Selected room does not exist."]);

        if (!room.IsActive)
        {
            throw new ReservationValidationException(localizer["Room {0} is archived and cannot be booked.", room.Number]);
        }

        var isGuestStay = request.Status is not (ReservationStatus.Blocked or ReservationStatus.Maintenance);
        if (isGuestStay && room.MaxOccupancy is { } maxOccupancy && request.Occupancy > maxOccupancy)
        {
            throw new ReservationValidationException(localizer[
                "Room {0} allows at most {1} guest(s).",
                room.Number,
                maxOccupancy]);
        }
    }

    private async Task EnsureNoOverlappingReservationAsync(
        HotelDbContext dbContext,
        Guid roomId,
        DateOnly checkIn,
        DateOnly checkOut,
        Guid? excludedReservationId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await dbContext.Reservations
            .AnyAsync(existingReservation =>
                existingReservation.RoomId == roomId &&
                (excludedReservationId == null || existingReservation.Id != excludedReservationId.Value) &&
                existingReservation.Status != ReservationStatus.CheckedOut &&
                existingReservation.Status != ReservationStatus.Cancelled &&
                existingReservation.CheckIn < checkOut &&
                existingReservation.CheckOut > checkIn,
                cancellationToken);

        if (hasConflict)
        {
            throw new ReservationConflictException(localizer["The selected room is already booked for the requested date range."]);
        }
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        return notes.Trim();
    }

    private static RoomPlannerReservation MapReservation(Reservation reservation)
    {
        return new RoomPlannerReservation(
            reservation.Id,
            reservation.GuestName,
            reservation.CheckIn,
            reservation.CheckOut,
            reservation.Occupancy,
            reservation.Status,
            reservation.Notes);
    }

    private async Task<T> ExecuteSerializableAsync<T>(
        Func<HotelDbContext, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var result = await operation(dbContext);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
