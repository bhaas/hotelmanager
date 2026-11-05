using HotelManager.Web.Domain;

namespace HotelManager.Web.Features.Guests;

public sealed record GuestSummary(
    Guid Id,
    string DisplayName,
    string? Company,
    string? Email,
    string? Phone,
    string? City,
    string? Country,
    bool IsVip,
    bool IsAnonymized,
    int StayCount,
    DateOnly? LastArrival,
    DateOnly? NextArrival);

public sealed record GuestDetail(
    Guid Id,
    string? FirstName,
    string LastName,
    string DisplayName,
    string? Company,
    string? Email,
    string? Phone,
    string? Street,
    string? PostalCode,
    string? City,
    string? Country,
    string? Nationality,
    DateOnly? DateOfBirth,
    string? Language,
    string? VatId,
    string? Notes,
    bool IsVip,
    bool MarketingConsent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? AnonymizedAtUtc,
    string PostalAddress,
    decimal InvoicedGrossAmount,
    int NightCount,
    IReadOnlyList<GuestStay> Stays)
{
    public bool IsAnonymized => AnonymizedAtUtc is not null;

    /// <summary>Buyer name for invoices: the company when set, otherwise the guest.</summary>
    public string InvoiceName => string.IsNullOrWhiteSpace(Company) ? DisplayName : Company;
}

public sealed record GuestStay(
    Guid ReservationId,
    string RoomNumber,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Occupancy,
    ReservationStatus Status,
    ReservationSource Source,
    string? ExternalReference,
    string? InvoiceNumber,
    decimal? InvoiceGrossAmount);

public sealed record GuestUpsertRequest(
    string? FirstName,
    string LastName,
    string? Company = null,
    string? Email = null,
    string? Phone = null,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? Nationality = null,
    DateOnly? DateOfBirth = null,
    string? Language = null,
    string? VatId = null,
    string? Notes = null,
    bool IsVip = false,
    bool MarketingConsent = false);

public sealed record GuestExport(string FileName, byte[] Content);

public enum GuestErasureResult
{
    Deleted,
    Anonymized
}

public sealed class GuestValidationException(string message) : Exception(message);

public sealed class GuestNotFoundException(string message) : Exception(message);

public interface IGuestService
{
    Task<IReadOnlyList<GuestSummary>> SearchAsync(string? query, bool includeAnonymized = true, int take = 100, CancellationToken cancellationToken = default);

    Task<GuestDetail> GetAsync(Guid guestId, CancellationToken cancellationToken = default);

    Task<GuestDetail> CreateAsync(GuestUpsertRequest request, CancellationToken cancellationToken = default);

    Task<GuestDetail> UpdateAsync(Guid guestId, GuestUpsertRequest request, CancellationToken cancellationToken = default);

    /// <summary>Links a reservation to a guest profile (or unlinks it when <paramref name="guestId"/> is null).</summary>
    Task LinkReservationAsync(Guid reservationId, Guid? guestId, CancellationToken cancellationToken = default);

    /// <summary>All personal data held about the guest as JSON (GDPR Art. 15 / 20).</summary>
    Task<GuestExport> ExportAsync(Guid guestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the guest (GDPR Art. 17). Profiles without stays are deleted; otherwise personal data is removed from the
    /// profile and its reservations while issued invoices are kept for the statutory retention period.
    /// </summary>
    Task<GuestErasureResult> EraseAsync(Guid guestId, CancellationToken cancellationToken = default);
}

public static class GuestNames
{
    /// <summary>Splits "Max Mustermann" or "Mustermann, Max" into first and last name.</summary>
    public static (string? FirstName, string LastName) Split(string? fullName)
    {
        var name = string.Join(' ', (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var comma = name.IndexOf(',');
        if (comma > 0)
        {
            var first = name[(comma + 1)..].Trim();
            return (first.Length == 0 ? null : first, name[..comma].Trim());
        }

        var space = name.LastIndexOf(' ');
        return space > 0 ? (name[..space], name[(space + 1)..]) : (null, name);
    }
}
