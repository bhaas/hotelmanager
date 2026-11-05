using HotelManager.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.Integrations.Beds24;

public static class Beds24Endpoints
{
    /// <summary>
    /// Booking webhook for Beds24 (Settings → Properties → Access → Booking Webhook). The secret key in the URL identifies the
    /// hotel; the payload is ignored and only triggers a sync, so a forged call cannot change any data.
    /// </summary>
    public static IEndpointRouteBuilder MapBeds24Webhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/api/integrations/beds24/webhook/{key}", ["GET", "POST"], async (
                string key,
                IDbContextFactory<HotelDbContext> dbContextFactory,
                Beds24Coordinator coordinator,
                CancellationToken cancellationToken) =>
            {
                if (key.Length is < 32 or > 64)
                {
                    return Results.NotFound();
                }

                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var hotelId = await dbContext.Beds24Connections.AsNoTracking()
                    .Where(connection => connection.WebhookKey == key && connection.IsEnabled)
                    .Select(connection => (Guid?)connection.HotelId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (hotelId is null)
                {
                    return Results.NotFound();
                }

                coordinator.RequestSync(hotelId.Value);
                return Results.Ok(new { success = true });
            })
            .AllowAnonymous()
            .DisableAntiforgery();
        return endpoints;
    }
}
