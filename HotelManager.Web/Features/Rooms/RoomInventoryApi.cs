using HotelManager.Web.Features.RoomPlanner;

namespace HotelManager.Web.Features.Rooms;

public static class RoomInventoryApi
{
    public static IEndpointRouteBuilder MapRoomInventoryApi(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup("/api/rooms").WithTags("Rooms");

        rooms.MapGet("/", (IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.GetRoomsAsync(cancellationToken)));

        rooms.MapPost("/", (RoomUpsertRequest request, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.CreateRoomAsync(request, cancellationToken)));

        rooms.MapPost("/series", (RoomSeriesRequest request, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.CreateRoomSeriesAsync(request, cancellationToken)));

        rooms.MapPut("/{roomId:guid}", (Guid roomId, RoomUpsertRequest request, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.UpdateRoomAsync(roomId, request, cancellationToken)));

        rooms.MapPut("/{roomId:guid}/active", (Guid roomId, RoomActiveUpdateRequest request, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.SetRoomActiveAsync(roomId, request.IsActive, cancellationToken)));

        rooms.MapDelete("/{roomId:guid}", (Guid roomId, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(async () =>
            {
                await service.DeleteRoomAsync(roomId, cancellationToken);
                return new { deleted = roomId };
            }));

        var categories = app.MapGroup("/api/room-categories").WithTags("Room categories");

        categories.MapGet("/", (IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.GetCategoriesAsync(cancellationToken)));

        categories.MapPost("/", (RoomCategoryUpsertRequest request, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.CreateCategoryAsync(request, cancellationToken)));

        categories.MapPut("/{categoryId:guid}", (Guid categoryId, RoomCategoryUpsertRequest request, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(() => service.UpdateCategoryAsync(categoryId, request, cancellationToken)));

        categories.MapDelete("/{categoryId:guid}", (Guid categoryId, IRoomInventoryService service, CancellationToken cancellationToken) =>
            ExecuteAsync(async () =>
            {
                await service.DeleteCategoryAsync(categoryId, cancellationToken);
                return new { deleted = categoryId };
            }));

        return app;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> operation)
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
