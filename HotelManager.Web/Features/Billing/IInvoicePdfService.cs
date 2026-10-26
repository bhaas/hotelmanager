namespace HotelManager.Web.Features.Billing;

public interface IInvoicePdfService
{
    byte[] Generate(BillingInvoiceDocumentDto invoice);
}
