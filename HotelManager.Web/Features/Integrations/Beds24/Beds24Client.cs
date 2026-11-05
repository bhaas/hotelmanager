using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotelManager.Web.Features.Integrations.Beds24;

/// <summary>Minimal client for the Beds24 API v2 (https://beds24.com/api/v2/), read access only.</summary>
public interface IBeds24Client
{
    /// <summary>Exchanges a one-time invite code for a refresh token (and a first access token).</summary>
    Task<Beds24Tokens> SetupAsync(string inviteCode, CancellationToken cancellationToken = default);

    Task<Beds24AccessToken> GetAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Beds24Property>> GetPropertiesAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Beds24Booking>> GetBookingsAsync(string accessToken, Beds24BookingQuery query, CancellationToken cancellationToken = default);
}

public sealed record Beds24Tokens(string AccessToken, int ExpiresIn, string RefreshToken);

public sealed record Beds24AccessToken(string Token, int ExpiresIn);

public sealed record Beds24BookingQuery(int PropertyId, DateOnly DepartureFrom, DateTimeOffset? ModifiedFrom);

public sealed class Beds24Property
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public List<Beds24RoomType> RoomTypes { get; set; } = [];
}

public sealed class Beds24RoomType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public int? Qty { get; set; }

    public int? MaxPeople { get; set; }

    public List<Beds24Unit> Units { get; set; } = [];
}

public sealed class Beds24Unit
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

public sealed class Beds24Booking
{
    public int Id { get; set; }

    public int? MasterId { get; set; }

    public int PropertyId { get; set; }

    public int RoomId { get; set; }

    public int? UnitId { get; set; }

    public int? RoomQty { get; set; }

    public string? Status { get; set; }

    public string? Arrival { get; set; }

    public string? Departure { get; set; }

    public int? NumAdult { get; set; }

    public int? NumChild { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Mobile { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Postcode { get; set; }

    public string? Country { get; set; }

    public string? Country2 { get; set; }

    public string? Comments { get; set; }

    public decimal? Price { get; set; }

    public decimal? Commission { get; set; }

    public string? Channel { get; set; }

    /// <summary>Booking number of the channel, e.g. the Booking.com reservation number.</summary>
    public string? ApiReference { get; set; }
}

/// <summary>Beds24 rejected a request (invalid invite code, expired token, missing scope ...).</summary>
public sealed class Beds24ApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

public sealed class Beds24Client(HttpClient httpClient) : IBeds24Client
{
    public const string BaseAddress = "https://beds24.com/api/v2/";

    private const int MaxPages = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task<Beds24Tokens> SetupAsync(string inviteCode, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "authentication/setup");
        request.Headers.Add("code", inviteCode);
        request.Headers.Add("deviceName", "HotelManager");
        var response = await SendAsync<TokenResponse>(request, cancellationToken);
        if (string.IsNullOrEmpty(response.Token) || string.IsNullOrEmpty(response.RefreshToken))
        {
            throw new Beds24ApiException("Beds24 did not return a refresh token.");
        }

        return new Beds24Tokens(response.Token, response.ExpiresIn, response.RefreshToken);
    }

    public async Task<Beds24AccessToken> GetAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "authentication/token");
        request.Headers.Add("refreshToken", refreshToken);
        var response = await SendAsync<TokenResponse>(request, cancellationToken);
        if (string.IsNullOrEmpty(response.Token))
        {
            throw new Beds24ApiException("Beds24 did not return an access token.", HttpStatusCode.Unauthorized);
        }

        return new Beds24AccessToken(response.Token, response.ExpiresIn);
    }

    public async Task<IReadOnlyList<Beds24Property>> GetPropertiesAsync(string accessToken, CancellationToken cancellationToken = default) =>
        await GetPagedAsync<Beds24Property>(accessToken, "properties?includeAllRooms=true", cancellationToken);

    public async Task<IReadOnlyList<Beds24Booking>> GetBookingsAsync(string accessToken, Beds24BookingQuery query, CancellationToken cancellationToken = default)
    {
        var path = $"bookings?propertyId={query.PropertyId}" +
                   $"&departureFrom={query.DepartureFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                   "&status=confirmed&status=new&status=cancelled";
        if (query.ModifiedFrom is { } modifiedFrom)
        {
            path += $"&modifiedFrom={Uri.EscapeDataString(modifiedFrom.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))}";
        }

        return await GetPagedAsync<Beds24Booking>(accessToken, path, cancellationToken);
    }

    private async Task<List<T>> GetPagedAsync<T>(string accessToken, string path, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        for (var page = 1; page <= MaxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, page == 1 ? path : $"{path}&page={page}");
            request.Headers.Add("token", accessToken);
            var response = await SendAsync<ListResponse<T>>(request, cancellationToken);
            items.AddRange(response.Data ?? []);
            if (response.Pages?.NextPageExists != true)
            {
                break;
            }
        }

        return items;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        where T : ApiResponse
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new Beds24ApiException($"Beds24 is not reachable: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Beds24ApiException("Beds24 did not answer in time.");
        }

        using (response)
        {
            T? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            {
            }

            if (!response.IsSuccessStatusCode || body is null || body.Success == false)
            {
                var message = body?.Error ?? body?.Message ?? response.ReasonPhrase ?? "Unknown error";
                throw new Beds24ApiException($"Beds24: {message} ({(int)response.StatusCode})", response.StatusCode);
            }

            return body;
        }
    }

    private class ApiResponse
    {
        public bool? Success { get; set; }

        public string? Error { get; set; }

        public string? Message { get; set; }
    }

    private sealed class TokenResponse : ApiResponse
    {
        public string? Token { get; set; }

        public int ExpiresIn { get; set; }

        public string? RefreshToken { get; set; }
    }

    private sealed class ListResponse<T> : ApiResponse
    {
        public List<T>? Data { get; set; }

        public PageInfo? Pages { get; set; }
    }

    private sealed class PageInfo
    {
        public bool NextPageExists { get; set; }
    }
}
