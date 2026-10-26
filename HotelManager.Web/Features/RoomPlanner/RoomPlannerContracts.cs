using HotelManager.Web.Domain;

namespace HotelManager.Web.Features.RoomPlanner;

public sealed record RoomPlannerSnapshot(
    DateOnly StartDate,
    int Days,
    IReadOnlyList<RoomPlannerRoom> Rooms);

public sealed record RoomPlannerRoom(
    Guid RoomId,
    string RoomNumber,
    string RoomType,
    int Floor,
    IReadOnlyList<RoomPlannerReservation> Reservations,
    HousekeepingStatus HousekeepingStatus,
    string? Features = null,
    int MaxOccupancy = 0);

public sealed record RoomHousekeepingUpdateRequest(HousekeepingStatus Status);

public sealed record RoomHousekeepingState(Guid RoomId, string RoomNumber, HousekeepingStatus Status);

public sealed record RoomPlannerReservation(
    Guid ReservationId,
    string GuestName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Occupancy,
    ReservationStatus Status,
    string? Notes);

public sealed record RoomPlannerReservationUpsertRequest(
    Guid RoomId,
    string GuestName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Occupancy,
    ReservationStatus Status,
    string? Notes);

public sealed record RoomRateAvailabilitySnapshot(
    IReadOnlyList<RoomRateRuleDto> RateRules,
    IReadOnlyList<RoomAvailabilityRuleDto> AvailabilityRules);

public sealed record RoomRateRuleDto(
    Guid RateRuleId,
    string RoomType,
    DateOnly StartDate,
    DateOnly EndDate,
    int OccupancyFrom,
    int? OccupancyTo,
    decimal NightlyNetAmount,
    decimal VatRatePercent,
    string Currency,
    int Priority);

public sealed record RoomAvailabilityRuleDto(
    Guid AvailabilityRuleId,
    Guid? RoomId,
    string? RoomNumber,
    string? RoomType,
    DateOnly StartDate,
    DateOnly EndDate,
    bool StopSell,
    bool ClosedToArrival,
    bool ClosedToDeparture,
    int? MinStayNights);

public sealed record RoomRateRuleUpsertRequest(
    string RoomType,
    DateOnly StartDate,
    DateOnly EndDate,
    int OccupancyFrom,
    int? OccupancyTo,
    decimal NightlyNetAmount,
    decimal VatRatePercent,
    int Priority);

public sealed record RoomAvailabilityRuleUpsertRequest(
    Guid? RoomId,
    string? RoomType,
    DateOnly StartDate,
    DateOnly EndDate,
    bool StopSell,
    bool ClosedToArrival,
    bool ClosedToDeparture,
    int? MinStayNights);

public sealed record RoomRateQuoteRequest(
    Guid RoomId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Occupancy);

public sealed record RoomRateQuote(
    Guid RoomId,
    string RoomNumber,
    string RoomType,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Occupancy,
    int Nights,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    string Currency,
    bool IsQuoteable,
    string? Message,
    IReadOnlyList<RoomRateQuoteNight> NightlyBreakdown);

public sealed record RoomRateQuoteNight(
    DateOnly Date,
    decimal NetAmount,
    decimal VatRatePercent,
    decimal VatAmount,
    decimal GrossAmount);

public sealed record RoomAvailabilityEvaluation(
    bool IsAvailable,
    string? Message,
    int? MinimumStayNights);
