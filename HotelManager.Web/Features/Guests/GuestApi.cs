using HotelManager.Web.Infrastructure.Identity;

namespace HotelManager.Web.Features.Guests;

public static class GuestApi
{
    public static IEndpointRouteBuilder MapGuestApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guests")
            .WithTags("Guests")
            .RequireAuthorization(HotelPolicies.FrontOffice);

        group.MapGet("/", (string? query, IGuestService guests, CancellationToken cancellationToken) =>
            ExecuteAsync(() => guests.SearchAsync(query, cancellationToken: cancellationToken)));

        group.MapGet("/{guestId:guid}", (Guid guestId, IGuestService guests, CancellationToken cancellationToken) =>
            ExecuteAsync(() => guests.GetAsync(guestId, cancellationToken)));

        group.MapPost("/", (GuestUpsertRequest request, IGuestService guests, CancellationToken cancellationToken) =>
            ExecuteAsync(() => guests.CreateAsync(request, cancellationToken)));

        group.MapPut("/{guestId:guid}", (Guid guestId, GuestUpsertRequest request, IGuestService guests, CancellationToken cancellationToken) =>
            ExecuteAsync(() => guests.UpdateAsync(guestId, request, cancellationToken)));

        group.MapGet("/{guestId:guid}/export", async (Guid guestId, IGuestService guests, CancellationToken cancellationToken) =>
        {
            try
            {
                var export = await guests.ExportAsync(guestId, cancellationToken);
                return Results.File(export.Content, "application/json", export.FileName);
            }
            catch (GuestNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
        }).RequireAuthorization(HotelPolicies.ManageProperty);

        group.MapDelete("/{guestId:guid}", (Guid guestId, IGuestService guests, CancellationToken cancellationToken) =>
            ExecuteAsync(async () => new { result = await guests.EraseAsync(guestId, cancellationToken) }))
            .RequireAuthorization(HotelPolicies.ManageProperty);

        return app;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (GuestValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (GuestNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }
}
