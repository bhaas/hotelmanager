using System.Data.Common;
using HotelManager.Web.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HotelManager.Web.Infrastructure.Tenancy;

/// <summary>
/// Second line of defence behind the EF Core query filters: every connection opened by a
/// <see cref="HotelDbContext"/> switches to the restricted <see cref="AppRole"/> and binds the
/// PostgreSQL session to the current hotel. Row-level security policies on all tenant tables then
/// only expose rows of that hotel, even for raw SQL or a forgotten filter.
/// </summary>
/// <remarks>
/// Npgsql resets session state (DISCARD ALL) when a pooled connection is reused, so the values never
/// leak between requests. Requires a session-mode connection (Neon: the unpooled URL, not PgBouncer).
/// </remarks>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    /// <summary>NOLOGIN role without BYPASSRLS, created by the <c>MultiHotel</c> migration.</summary>
    public const string AppRole = "hotelmanager_app";

    /// <summary>Session setting read by the row-level security policies.</summary>
    public const string HotelIdSetting = "app.hotel_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (BuildSessionSql(eventData) is { } sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (BuildSessionSql(eventData) is { } sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string? BuildSessionSql(ConnectionEndEventData eventData)
    {
        if (eventData.Context is not HotelDbContext dbContext)
        {
            return null;
        }

        if (dbContext.BypassRowLevelSecurity)
        {
            return $"RESET ROLE; SELECT set_config('{HotelIdSetting}', '', false);";
        }

        // A Guid formats to hex digits and hyphens only, so inlining it is injection-safe.
        var hotelId = dbContext.SessionHotelId?.ToString("D") ?? string.Empty;
        return $"SET ROLE {AppRole}; SELECT set_config('{HotelIdSetting}', '{hotelId}', false);";
    }
}
