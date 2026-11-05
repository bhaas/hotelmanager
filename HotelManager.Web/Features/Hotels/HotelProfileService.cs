using System.Net.Mail;
using HotelManager.Web.Data;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Hotels;

/// <summary>Reads and edits the profile (address, VAT ID, time zone) of the hotel the user currently works in.</summary>
public sealed class HotelProfileService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    TenantContext tenantContext,
    IStringLocalizer<HotelProfileService> localizer) : IHotelProfileService
{
    public async Task<HotelProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hotel = await dbContext.Hotels.AsNoTracking().SingleAsync(item => item.Id == hotelId, cancellationToken);
        return ToProfile(hotel);
    }

    public async Task<HotelProfile> UpdateCurrentAsync(HotelProfileUpdate update, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireHotel();
        if (tenantContext.User?.IsInRole(HotelRoles.Admin) != true)
        {
            throw new HotelValidationException(localizer["Only administrators can change the hotel profile."]);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hotel = await dbContext.Hotels.SingleAsync(item => item.Id == hotelId, cancellationToken);

        hotel.Name = Required(update.Name, 120, localizer["Hotel name is required."]);
        hotel.LegalName = Optional(update.LegalName, 200);
        hotel.Street = Optional(update.Street, 200);
        hotel.PostalCode = Optional(update.PostalCode, 16);
        hotel.City = Optional(update.City, 100);
        hotel.Country = Optional(update.Country, 100);
        hotel.Phone = Optional(update.Phone, 40);
        hotel.Email = ValidateEmail(Optional(update.Email, 256));
        hotel.VatId = Optional(update.VatId, 24)?.Replace(" ", string.Empty).ToUpperInvariant();
        hotel.TimeZone = ValidateTimeZone(update.TimeZone);
        if (update.Operations is { } operations)
        {
            hotel.InvoiceNumberPrefix = ValidateInvoicePrefix(operations.InvoiceNumberPrefix);
            hotel.PaymentTermDays = operations.PaymentTermDays is >= 0 and <= 365
                ? operations.PaymentTermDays
                : throw new HotelValidationException(localizer["Payment terms must be between 0 and 365 days."]);
            hotel.CashPaymentLimit = operations.CashPaymentLimit is >= 0m and <= 1_000_000m
                ? decimal.Round(operations.CashPaymentLimit, 2)
                : throw new HotelValidationException(localizer["The cash payment limit must be between 0 and 1,000,000."]);
            hotel.CheckInTime = operations.CheckInTime;
            hotel.CheckOutTime = operations.CheckOutTime;
            hotel.InvoiceFooter = Optional(operations.InvoiceFooter, 500);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToProfile(hotel);
    }

    internal static HotelProfile ToProfile(Domain.Hotel hotel) => new(
        hotel.Id, hotel.Name, hotel.LegalName, hotel.Street, hotel.PostalCode, hotel.City, hotel.Country,
        hotel.Phone, hotel.Email, hotel.VatId, hotel.TimeZone, hotel.Currency,
        new HotelOperatingSettings(
            hotel.InvoiceNumberPrefix, hotel.PaymentTermDays, hotel.CashPaymentLimit,
            hotel.CheckInTime, hotel.CheckOutTime, hotel.InvoiceFooter));

    private Guid RequireHotel() =>
        tenantContext.HotelId ?? throw new HotelValidationException(localizer["No hotel selected."]);

    private string Required(string? value, int maxLength, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new HotelValidationException(message);
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new HotelValidationException(localizer["A field exceeds the maximum length."]);
    }

    private string? Optional(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new HotelValidationException(localizer["A field exceeds the maximum length."]);
    }

    private string? ValidateEmail(string? email) =>
        email is null || (MailAddress.TryCreate(email, out var address) && address.Address == email)
            ? email
            : throw new HotelValidationException(localizer["Enter a valid email address."]);

    private string ValidateInvoicePrefix(string? prefix)
    {
        var trimmed = prefix?.Trim().ToUpperInvariant() ?? string.Empty;
        return trimmed.Length is >= 1 and <= 10 && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? trimmed.TrimEnd('-')
            : throw new HotelValidationException(localizer["The invoice prefix must have 1-10 letters, digits or hyphens."]);
    }

    private string ValidateTimeZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out _)
            ? timeZone.Trim()
            : throw new HotelValidationException(localizer["Select a valid time zone."]);
}
