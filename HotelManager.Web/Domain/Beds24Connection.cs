namespace HotelManager.Web.Domain;

/// <summary>
/// Link of a hotel to a Beds24 property (channel manager). Not tenant-filtered so the background sync can list all
/// connections; services must always filter by <see cref="HotelId"/>.
/// </summary>
public sealed class Beds24Connection
{
    public Guid HotelId { get; set; }

    /// <summary>Beds24 refresh token, encrypted with ASP.NET Core Data Protection.</summary>
    public string ProtectedRefreshToken { get; set; } = string.Empty;

    public int? PropertyId { get; set; }

    public string? PropertyName { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Secret part of the webhook URL that Beds24 calls when a booking changes.</summary>
    public string WebhookKey { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Start of the last successful sync; the next sync asks for bookings modified since then.</summary>
    public DateTimeOffset? LastSyncAtUtc { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public string? LastError { get; set; }

    public int LastCreated { get; set; }

    public int LastUpdated { get; set; }

    public int LastCancelled { get; set; }

    /// <summary>Bookings of the last sync that need attention (unmapped room type, no free room ...), one per line.</summary>
    public string? LastProblems { get; set; }
}
