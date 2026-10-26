namespace HotelManager.Web.Features.Billing;

public interface IBillingService
{
    Task<BillingFolioSnapshot> GetFolioAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<BillingFolioSnapshot> AddFolioEntryAsync(Guid reservationId, BillingAddFolioEntryRequest request, CancellationToken cancellationToken);

    Task<BillingFolioSnapshot> RegisterPaymentAsync(Guid reservationId, BillingRegisterPaymentRequest request, CancellationToken cancellationToken);

    Task<BillingInvoiceDocumentDto> IssueInvoiceAsync(Guid reservationId, BillingIssueInvoiceRequest request, CancellationToken cancellationToken);

    Task<BillingInvoiceDocumentDto> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);
}
