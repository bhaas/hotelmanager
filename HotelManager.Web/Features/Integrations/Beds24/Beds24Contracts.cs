namespace HotelManager.Web.Features.Integrations.Beds24;

/// <summary>Connects the current hotel to a Beds24 property and imports its bookings (one-way: Beds24 → HotelManager).</summary>
public interface IBeds24SyncService
{
    Task<Beds24Status?> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Redeems an invite code from Beds24 (Settings → Apps &amp; Integrations → API). Selects the property if there is only one.</summary>
    Task<Beds24Status> ConnectAsync(string inviteCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Beds24PropertyOption>> GetPropertiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Links the property and loads (or reloads) its room types for the category mapping.</summary>
    Task SelectPropertyAsync(int propertyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Beds24RoomMappingRow>> GetRoomMappingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves room type → category assignments; the next sync re-reads all upcoming bookings.</summary>
    Task SaveRoomMappingsAsync(IReadOnlyDictionary<int, string?> categoriesByRoomId, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<Beds24SyncResult> SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Background sync of the current hotel; returns null when the hotel is not connected or a sync is already running.</summary>
    Task<Beds24SyncResult?> RunScheduledSyncAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed record Beds24Status(
    int? PropertyId,
    string? PropertyName,
    bool IsEnabled,
    string WebhookKey,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset? LastSyncAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    string? LastError,
    int LastCreated,
    int LastUpdated,
    int LastCancelled,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Categories);

public sealed record Beds24PropertyOption(int Id, string Name, int RoomTypeCount);

public sealed record Beds24RoomMappingRow(int Beds24RoomId, string Beds24RoomName, string? CategoryName);

public sealed record Beds24SyncResult(int Created, int Updated, int Cancelled, int Unchanged, IReadOnlyList<string> Problems);

/// <summary>A user-facing error of the Beds24 integration (message is localized).</summary>
public sealed class Beds24IntegrationException(string message) : Exception(message);
