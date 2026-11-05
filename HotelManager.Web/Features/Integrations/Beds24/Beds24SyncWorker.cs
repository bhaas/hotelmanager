using HotelManager.Web.Data;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.Integrations.Beds24;

/// <summary>Syncs all connected hotels every few minutes and right after Beds24 announced a change via webhook.</summary>
public sealed class Beds24SyncWorker(
    IServiceScopeFactory scopeFactory,
    Beds24Coordinator coordinator,
    IConfiguration configuration,
    ILogger<Beds24SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(configuration.GetValue("Beds24:SyncIntervalMinutes", 10), 1));
        try
        {
            // Leave the start-up (migrations, first requests) alone.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            var nextRun = DateTimeOffset.UtcNow;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextRun)
                {
                    await SyncAllAsync(stoppingToken);
                    nextRun = DateTimeOffset.UtcNow + interval;
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(nextRun - DateTimeOffset.UtcNow is var delay && delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
                try
                {
                    var hotelId = await coordinator.Requests.ReadAsync(wait.Token);
                    // Beds24 often sends several webhooks for one change; collect them before syncing.
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    var hotelIds = new HashSet<Guid> { hotelId };
                    while (coordinator.Requests.TryRead(out var next))
                    {
                        hotelIds.Add(next);
                    }

                    foreach (var id in hotelIds)
                    {
                        await SyncHotelAsync(id, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        List<Guid> hotelIds;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await using var dbContext = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HotelDbContext>>().CreateDbContextAsync(cancellationToken);
            hotelIds = await dbContext.Beds24Connections.AsNoTracking()
                .Where(connection => connection.IsEnabled && connection.PropertyId != null &&
                                     dbContext.Hotels.Any(hotel => hotel.Id == connection.HotelId && hotel.IsActive))
                .Select(connection => connection.HotelId)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Beds24 connections could not be read.");
            return;
        }

        foreach (var hotelId in hotelIds)
        {
            await SyncHotelAsync(hotelId, cancellationToken);
        }
    }

    private async Task SyncHotelAsync(Guid hotelId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().UseHotel(hotelId);
            await using (var dbContext = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HotelDbContext>>().CreateDbContextAsync(cancellationToken))
            {
                if (!await dbContext.Beds24Connections.AnyAsync(connection => connection.HotelId == hotelId && connection.IsEnabled, cancellationToken))
                {
                    return;
                }
            }

            await scope.ServiceProvider.GetRequiredService<IBeds24SyncService>().RunScheduledSyncAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Beds24 sync of hotel {HotelId} failed.", hotelId);
        }
    }
}
