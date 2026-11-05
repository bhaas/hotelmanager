namespace HotelManager.Web.Domain;

/// <summary>Guest profile (CRM). Reservations reference it; personal data can be exported and anonymized (GDPR).</summary>
public sealed class Guest : ITenantEntity
{
    public Guid HotelId { get; set; }

    public Guid Id { get; set; }

    public string? FirstName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string? Company { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Nationality { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Preferred correspondence language, e.g. "de" or "en".</summary>
    public string? Language { get; set; }

    public string? VatId { get; set; }

    public string? Notes { get; set; }

    public bool IsVip { get; set; }

    public bool MarketingConsent { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the personal data was erased on request; the profile only keeps the stay history.</summary>
    public DateTimeOffset? AnonymizedAtUtc { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public string DisplayName => string.IsNullOrWhiteSpace(FirstName) ? LastName : $"{FirstName} {LastName}";

    public string PostalAddress => string.Join(", ", new[]
    {
        Street,
        string.Join(" ", new[] { PostalCode, City }.Where(part => !string.IsNullOrWhiteSpace(part))),
        Country
    }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
