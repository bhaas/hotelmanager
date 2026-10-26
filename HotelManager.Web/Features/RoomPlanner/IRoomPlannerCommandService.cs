using HotelManager.Web.Domain;

namespace HotelManager.Web.Features.RoomPlanner;

public interface IRoomPlannerCommandService
{
    Task<RoomPlannerReservation> CreateReservationAsync(RoomPlannerReservationUpsertRequest request, CancellationToken cancellationToken);

    Task<RoomPlannerReservation> UpdateReservationAsync(Guid reservationId, RoomPlannerReservationUpsertRequest request, CancellationToken cancellationToken);

    Task<RoomPlannerReservation> CheckInAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<RoomPlannerReservation> CheckOutAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<RoomPlannerReservation> CancelAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<RoomHousekeepingState> SetHousekeepingStatusAsync(Guid roomId, HousekeepingStatus status, CancellationToken cancellationToken);
}
