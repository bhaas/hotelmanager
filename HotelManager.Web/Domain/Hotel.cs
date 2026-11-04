namespace HotelManager.Web.Domain;

/// <summary>A property (tenant). One installation can host many hotels with fully isolated data.</summary>
public sealed class Hotel
{
    public Guid Id { get; set; }

    /// <summary>Display name used in the app, e.g. "Alpenhof".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Company name printed on invoices, e.g. "Alpenhof Betriebs GmbH". Falls back to <see cref="Name"/>.</summary>
    public string? LegalName { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>VAT identification number (UID), printed on invoices.</summary>
    public string? VatId { get; set; }

    public string TimeZone { get; set; } = "Europe/Vienna";

    public string Currency { get; set; } = "EUR";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string InvoiceSellerName => string.IsNullOrWhiteSpace(LegalName) ? Name : LegalName;

    public string InvoiceSellerAddress => string.Join(", ", new[]
    {
        Street,
        string.Join(" ", new[] { PostalCode, City }.Where(part => !string.IsNullOrWhiteSpace(part))),
        Country
    }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
