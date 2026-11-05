using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HotelManager.Web.Features.Integrations.Beds24;

/// <summary>
/// Process-wide state of the Beds24 integration: one sync per hotel at a time, cached access tokens and the queue of
/// hotels whose sync was requested by a webhook.
/// </summary>
public sealed class Beds24Coordinator
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();
    private readonly ConcurrentDictionary<Guid, (string Token, DateTimeOffset ExpiresAtUtc)> tokens = new();
    private readonly Channel<Guid> requests = Channel.CreateBounded<Guid>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.DropWrite
    });

    public SemaphoreSlim GetLock(Guid hotelId) => locks.GetOrAdd(hotelId, _ => new SemaphoreSlim(1, 1));

    public string? GetCachedToken(Guid hotelId) =>
        tokens.TryGetValue(hotelId, out var entry) && entry.ExpiresAtUtc > DateTimeOffset.UtcNow ? entry.Token : null;

    public void CacheToken(Guid hotelId, string token, int expiresInSeconds) =>
        // Renew well before expiry so a token never runs out in the middle of a sync.
        tokens[hotelId] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresInSeconds - 600, 60)));

    public void ForgetToken(Guid hotelId) => tokens.TryRemove(hotelId, out _);

    public void RequestSync(Guid hotelId) => requests.Writer.TryWrite(hotelId);

    public ChannelReader<Guid> Requests => requests.Reader;
}
