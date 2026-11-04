namespace HotelManager.Web.Domain.Billing;

public sealed class InvoiceSequence : Domain.ITenantEntity
{
    public Guid HotelId { get; set; }

    public int Year { get; set; }

    public int LastNumber { get; set; }
}
