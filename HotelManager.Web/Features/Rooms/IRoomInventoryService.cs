namespace HotelManager.Web.Features.Rooms;

public interface IRoomInventoryService
{
    Task<IReadOnlyList<RoomInventoryItem>> GetRoomsAsync(CancellationToken cancellationToken);

    Task<RoomInventoryItem> CreateRoomAsync(RoomUpsertRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomInventoryItem>> CreateRoomSeriesAsync(RoomSeriesRequest request, CancellationToken cancellationToken);

    Task<RoomInventoryItem> UpdateRoomAsync(Guid roomId, RoomUpsertRequest request, CancellationToken cancellationToken);

    Task<RoomInventoryItem> SetRoomActiveAsync(Guid roomId, bool isActive, CancellationToken cancellationToken);

    Task DeleteRoomAsync(Guid roomId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken);

    Task<RoomCategoryItem> CreateCategoryAsync(RoomCategoryUpsertRequest request, CancellationToken cancellationToken);

    Task<RoomCategoryItem> UpdateCategoryAsync(Guid categoryId, RoomCategoryUpsertRequest request, CancellationToken cancellationToken);

    Task DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken);
}
