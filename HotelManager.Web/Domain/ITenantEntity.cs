namespace HotelManager.Web.Domain;

/// <summary>
/// Marks data that belongs to exactly one hotel (tenant). The DbContext applies a global
/// query filter and stamps <see cref="HotelId"/> on insert, so services never handle it manually.
/// </summary>
public interface ITenantEntity
{
    Guid HotelId { get; set; }
}
