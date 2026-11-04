namespace HotelManager.Web.Features.Hotels;

public sealed record HotelProfile(
    Guid Id,
    string Name,
    string? LegalName,
    string? Street,
    string? PostalCode,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    string? VatId,
    string TimeZone,
    string Currency);

public sealed record HotelProfileUpdate(
    string Name,
    string? LegalName,
    string? Street,
    string? PostalCode,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    string? VatId,
    string TimeZone);

public sealed record PlatformHotelSummary(
    Guid Id,
    string Name,
    string? City,
    string? Country,
    bool IsActive,
    int MemberCount,
    DateTimeOffset CreatedAtUtc,
    bool IsCurrent);

public sealed record PlatformHotelCreateRequest(
    string Name,
    string? City,
    string? Country,
    string AdminEmail,
    string AdminDisplayName,
    bool SeedDemoData);

/// <summary><see cref="AdminPassword"/> is null when an existing account was added as administrator.</summary>
public sealed record PlatformHotelCreateResult(PlatformHotelSummary Hotel, string AdminEmail, string? AdminPassword);

public sealed class HotelValidationException(string message) : Exception(message);

public interface IHotelProfileService
{
    Task<HotelProfile> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<HotelProfile> UpdateCurrentAsync(HotelProfileUpdate update, CancellationToken cancellationToken = default);
}

public interface IPlatformHotelService
{
    Task<IReadOnlyList<PlatformHotelSummary>> GetHotelsAsync(CancellationToken cancellationToken = default);

    Task<PlatformHotelCreateResult> CreateAsync(PlatformHotelCreateRequest request, CancellationToken cancellationToken = default);

    Task<PlatformHotelSummary> SetActiveAsync(Guid hotelId, bool isActive, CancellationToken cancellationToken = default);
}

public static class HotelTimeZones
{
    public static readonly IReadOnlyList<string> Common =
    [
        "Europe/Vienna", "Europe/Berlin", "Europe/Zurich", "Europe/Rome", "Europe/Ljubljana", "Europe/Zagreb",
        "Europe/Prague", "Europe/Budapest", "Europe/Amsterdam", "Europe/Paris", "Europe/Madrid", "Europe/London", "UTC"
    ];
}
