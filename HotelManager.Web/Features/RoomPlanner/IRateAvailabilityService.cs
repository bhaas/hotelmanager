namespace HotelManager.Web.Features.RoomPlanner;

public interface IRateAvailabilityService
{
    Task<RoomRateAvailabilitySnapshot> GetSnapshotAsync(DateOnly startDate, int days, CancellationToken cancellationToken);

    Task<RoomRateRuleDto> CreateRateRuleAsync(RoomRateRuleUpsertRequest request, CancellationToken cancellationToken);

    Task<RoomAvailabilityRuleDto> CreateAvailabilityRuleAsync(RoomAvailabilityRuleUpsertRequest request, CancellationToken cancellationToken);

    Task<RoomAvailabilityEvaluation> EvaluateAvailabilityAsync(
        Guid roomId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken);

    Task<RoomRateQuote> QuoteAsync(RoomRateQuoteRequest request, CancellationToken cancellationToken);
}
