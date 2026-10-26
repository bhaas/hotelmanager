namespace HotelManager.Web.Features.RoomPlanner;

public static class RoomPlannerApi
{
    public static IEndpointRouteBuilder MapRoomPlannerApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/room-planner")
            .WithTags("Room Planner");

        group.MapGet("/", async (
            DateOnly? startDate,
            int? days,
            IRoomPlannerQueryService queryService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await queryService.GetSnapshotAsync(
                startDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                days ?? 14,
                cancellationToken);

            return Results.Ok(snapshot);
        });

        group.MapPost("/reservations", async (
            RoomPlannerReservationUpsertRequest request,
            IRoomPlannerCommandService commandService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => commandService.CreateReservationAsync(request, cancellationToken));
        });

        group.MapPut("/reservations/{reservationId:guid}", async (
            Guid reservationId,
            RoomPlannerReservationUpsertRequest request,
            IRoomPlannerCommandService commandService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => commandService.UpdateReservationAsync(reservationId, request, cancellationToken));
        });

        group.MapPost("/reservations/{reservationId:guid}/check-in", async (
            Guid reservationId,
            IRoomPlannerCommandService commandService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => commandService.CheckInAsync(reservationId, cancellationToken));
        });

        group.MapPost("/reservations/{reservationId:guid}/check-out", async (
            Guid reservationId,
            IRoomPlannerCommandService commandService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => commandService.CheckOutAsync(reservationId, cancellationToken));
        });

        group.MapPost("/reservations/{reservationId:guid}/cancel", async (
            Guid reservationId,
            IRoomPlannerCommandService commandService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => commandService.CancelAsync(reservationId, cancellationToken));
        });

        group.MapGet("/rate-availability", async (
            DateOnly? startDate,
            int? days,
            IRateAvailabilityService rateAvailabilityService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => rateAvailabilityService.GetSnapshotAsync(
                    startDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    days ?? 14,
                    cancellationToken));
        });

        group.MapPost("/rate-rules", async (
            RoomRateRuleUpsertRequest request,
            IRateAvailabilityService rateAvailabilityService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => rateAvailabilityService.CreateRateRuleAsync(request, cancellationToken));
        });

        group.MapPost("/availability-rules", async (
            RoomAvailabilityRuleUpsertRequest request,
            IRateAvailabilityService rateAvailabilityService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => rateAvailabilityService.CreateAvailabilityRuleAsync(request, cancellationToken));
        });

        group.MapPost("/rate-quote", async (
            RoomRateQuoteRequest request,
            IRateAvailabilityService rateAvailabilityService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteRoomPlannerOperationAsync(
                () => rateAvailabilityService.QuoteAsync(request, cancellationToken));
        });

        return app;
    }

    private static async Task<IResult> ExecuteRoomPlannerOperationAsync<T>(
        Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ReservationValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ReservationConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ReservationNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }
}
