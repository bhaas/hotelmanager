using HotelManager.Web.Domain.Billing;

namespace HotelManager.Web.Features.Billing;

public sealed record BillingFolioSnapshot(
    Guid ReservationId,
    string GuestName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    decimal PaidAmount,
    decimal OpenAmount,
    string Currency,
    IReadOnlyList<BillingFolioEntryItem> Entries,
    IReadOnlyList<BillingPaymentItem> Payments,
    IReadOnlyList<BillingInvoiceSummary> Invoices);

public sealed record BillingFolioEntryItem(
    Guid EntryId,
    string Description,
    FolioEntryType EntryType,
    decimal Quantity,
    decimal UnitNetAmount,
    decimal VatRatePercent,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    DateOnly ServiceDate,
    string? TaxExemptionReason);

public sealed record BillingPaymentItem(
    Guid PaymentId,
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset PaidAtUtc,
    string? Reference,
    string? Notes);

public sealed record BillingInvoiceSummary(
    Guid InvoiceId,
    string InvoiceNumber,
    DateTimeOffset IssuedAtUtc,
    DateOnly DueDate,
    decimal GrossAmount,
    decimal OpenAmount);

public sealed record BillingAddFolioEntryRequest(
    string Description,
    FolioEntryType EntryType,
    decimal Quantity,
    decimal UnitNetAmount,
    decimal VatRatePercent,
    DateOnly ServiceDate,
    string? TaxExemptionReason);

public sealed record BillingRegisterPaymentRequest(
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset? PaidAtUtc,
    string? Reference,
    string? Notes);

public sealed record BillingIssueInvoiceRequest(
    string BuyerName,
    string BuyerAddress,
    string? BuyerVatId,
    DateOnly DueDate);

public sealed record BillingInvoiceDocumentDto(
    Guid InvoiceId,
    string InvoiceNumber,
    DateTimeOffset IssuedAtUtc,
    string SellerName,
    string SellerAddress,
    string SellerVatId,
    string BuyerName,
    string BuyerAddress,
    string? BuyerVatId,
    DateOnly ServiceFrom,
    DateOnly ServiceTo,
    DateOnly DueDate,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    IReadOnlyList<BillingInvoiceLineDto> Lines);

public sealed record BillingInvoiceLineDto(
    string Description,
    decimal Quantity,
    decimal UnitNetAmount,
    decimal VatRatePercent,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    string? TaxExemptionReason);
