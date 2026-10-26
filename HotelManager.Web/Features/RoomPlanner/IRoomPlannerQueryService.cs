namespace HotelManager.Web.Features.RoomPlanner;

public interface IRoomPlannerQueryService
{
    Task<RoomPlannerSnapshot> GetSnapshotAsync(DateOnly startDate, int days, CancellationToken cancellationToken);
}
