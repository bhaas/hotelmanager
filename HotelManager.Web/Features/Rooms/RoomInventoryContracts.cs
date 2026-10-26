using HotelManager.Web.Domain;

namespace HotelManager.Web.Features.Rooms;

public sealed record RoomInventoryItem(
    Guid RoomId,
    string Number,
    string Category,
    int Floor,
    string? Features,
    string? Notes,
    bool IsActive,
    HousekeepingStatus HousekeepingStatus,
    int MaxOccupancy,
    int UpcomingReservations,
    int TotalReservations);

public sealed record RoomUpsertRequest(
    string Number,
    string Category,
    int Floor,
    string? Features,
    string? Notes,
    HousekeepingStatus HousekeepingStatus = HousekeepingStatus.Clean);

public sealed record RoomSeriesRequest(
    int FromNumber,
    int ToNumber,
    string Category,
    int Floor,
    string? Features,
    string? Prefix = null);

public sealed record RoomActiveUpdateRequest(bool IsActive);

public sealed record RoomCategoryItem(
    Guid CategoryId,
    string Code,
    string Name,
    string? Description,
    int StandardOccupancy,
    int MaxOccupancy,
    int SortOrder,
    int RoomCount,
    int ActiveRoomCount,
    int ActiveRateRuleCount);

public sealed record RoomCategoryUpsertRequest(
    string Code,
    string Name,
    string? Description,
    int StandardOccupancy,
    int MaxOccupancy,
    int SortOrder);
