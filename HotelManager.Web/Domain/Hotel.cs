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

    /// <summary>Prefix of invoice numbers, e.g. "AT" → "AT-2026-000001".</summary>
    public string InvoiceNumberPrefix { get; set; } = "AT";

    /// <summary>Default days until an invoice is due.</summary>
    public int PaymentTermDays { get; set; } = 14;

    /// <summary>Largest accepted single cash payment (anti-money-laundering rule).</summary>
    public decimal CashPaymentLimit { get; set; } = 10_000m;

    public TimeOnly CheckInTime { get; set; } = new(15, 0);

    public TimeOnly CheckOutTime { get; set; } = new(11, 0);

    /// <summary>Printed at the bottom of invoices, e.g. bank details (IBAN/BIC) or payment notes.</summary>
    public string? InvoiceFooter { get; set; }

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
