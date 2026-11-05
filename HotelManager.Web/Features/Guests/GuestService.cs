using System.Data;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Guests;

public sealed class GuestService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<GuestService> localizer) : IGuestService
{
    /// <summary>Stored instead of the name once a guest has been anonymized.</summary>
    public const string AnonymizedName = "Anonymized";

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<GuestSummary>> SearchAsync(
        string? query,
        bool includeAnonymized = true,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var guests = dbContext.Guests.AsNoTracking();

        if (!includeAnonymized)
        {
            guests = guests.Where(guest => guest.AnonymizedAtUtc == null);
        }

        var term = query?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            guests = guests.Where(guest =>
                EF.Functions.ILike(guest.LastName, pattern, "\\") ||
                EF.Functions.ILike((guest.FirstName ?? string.Empty) + " " + guest.LastName, pattern, "\\") ||
                EF.Functions.ILike(guest.Company ?? string.Empty, pattern, "\\") ||
                EF.Functions.ILike(guest.Email ?? string.Empty, pattern, "\\") ||
                EF.Functions.ILike(guest.Phone ?? string.Empty, pattern, "\\") ||
                EF.Functions.ILike(guest.City ?? string.Empty, pattern, "\\"));
        }

        return await guests
            .OrderBy(guest => guest.LastName)
            .ThenBy(guest => guest.FirstName)
            .Take(Math.Clamp(take, 1, 500))
            .Select(guest => new GuestSummary(
                guest.Id,
                guest.FirstName == null || guest.FirstName == string.Empty ? guest.LastName : guest.FirstName + " " + guest.LastName,
                guest.Company,
                guest.Email,
                guest.Phone,
                guest.City,
                guest.Country,
                guest.IsVip,
                guest.AnonymizedAtUtc != null,
                guest.Reservations.Count(reservation => reservation.Status != ReservationStatus.Cancelled),
                guest.Reservations
                    .Where(reservation => reservation.Status != ReservationStatus.Cancelled && reservation.CheckIn <= today)
                    .Max(reservation => (DateOnly?)reservation.CheckIn),
                guest.Reservations
                    .Where(reservation => reservation.Status != ReservationStatus.Cancelled && reservation.CheckIn > today)
                    .Min(reservation => (DateOnly?)reservation.CheckIn)))
            .ToListAsync(cancellationToken);
    }

    public async Task<GuestDetail> GetAsync(Guid guestId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var guest = await dbContext.Guests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == guestId, cancellationToken)
            ?? throw new GuestNotFoundException(localizer["Guest not found."]);
        return await ToDetailAsync(dbContext, guest, cancellationToken);
    }

    public async Task<GuestDetail> CreateAsync(GuestUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var guest = new Guest { Id = Guid.NewGuid() };
        Apply(guest, request);
        dbContext.Guests.Add(guest);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(dbContext, guest, cancellationToken);
    }

    public async Task<GuestDetail> UpdateAsync(Guid guestId, GuestUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var guest = await dbContext.Guests.SingleOrDefaultAsync(item => item.Id == guestId, cancellationToken)
            ?? throw new GuestNotFoundException(localizer["Guest not found."]);

        if (guest.AnonymizedAtUtc is not null)
        {
            throw new GuestValidationException(localizer["Anonymized guests cannot be edited."]);
        }

        Apply(guest, request);
        guest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(dbContext, guest, cancellationToken);
    }

    public async Task LinkReservationAsync(Guid reservationId, Guid? guestId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var reservation = await dbContext.Reservations.SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken)
            ?? throw new GuestNotFoundException(localizer["Reservation not found."]);

        if (guestId is { } id && !await dbContext.Guests.AnyAsync(guest => guest.Id == id && guest.AnonymizedAtUtc == null, cancellationToken))
        {
            throw new GuestNotFoundException(localizer["Guest not found."]);
        }

        reservation.GuestId = guestId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<GuestExport> ExportAsync(Guid guestId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var guest = await dbContext.Guests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == guestId, cancellationToken)
            ?? throw new GuestNotFoundException(localizer["Guest not found."]);

        var hotel = await dbContext.Hotels.AsNoTracking().SingleAsync(item => item.Id == guest.HotelId, cancellationToken);
        var reservations = await dbContext.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.GuestId == guestId)
            .OrderBy(reservation => reservation.CheckIn)
            .Select(reservation => new
            {
                reservation.Id,
                Room = reservation.Room.Number,
                reservation.GuestName,
                reservation.CheckIn,
                reservation.CheckOut,
                reservation.Occupancy,
                reservation.Status,
                reservation.Source,
                reservation.ExternalReference,
                reservation.Notes,
                Charges = reservation.FolioEntries
                    .OrderBy(entry => entry.ServiceDate)
                    .Select(entry => new
                    {
                        entry.ServiceDate,
                        entry.EntryType,
                        entry.Description,
                        entry.Quantity,
                        entry.UnitNetAmount,
                        entry.VatRatePercent,
                        entry.Currency
                    })
                    .ToList(),
                Payments = reservation.Payments
                    .OrderBy(payment => payment.PaidAtUtc)
                    .Select(payment => new { payment.PaidAtUtc, payment.Amount, payment.Currency, payment.Method, payment.Reference })
                    .ToList(),
                Invoices = reservation.Invoices
                    .Select(invoice => new
                    {
                        invoice.InvoiceNumber,
                        invoice.IssuedAtUtc,
                        invoice.BuyerName,
                        invoice.BuyerAddress,
                        invoice.BuyerVatId,
                        invoice.NetAmount,
                        invoice.VatAmount,
                        invoice.GrossAmount,
                        invoice.Currency
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var document = new
        {
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Controller = new { Name = hotel.InvoiceSellerName, Address = hotel.InvoiceSellerAddress, hotel.Email, hotel.Phone },
            Guest = new
            {
                guest.Id,
                guest.FirstName,
                guest.LastName,
                guest.Company,
                guest.Email,
                guest.Phone,
                guest.Street,
                guest.PostalCode,
                guest.City,
                guest.Country,
                guest.Nationality,
                guest.DateOfBirth,
                guest.Language,
                guest.VatId,
                guest.Notes,
                guest.IsVip,
                guest.MarketingConsent,
                guest.CreatedAtUtc,
                guest.UpdatedAtUtc,
                guest.AnonymizedAtUtc
            },
            Reservations = reservations
        };

        var fileName = $"guest-data-{FileNamePart(guest.LastName)}-{DateTime.UtcNow:yyyyMMdd}.json";
        return new GuestExport(fileName, JsonSerializer.SerializeToUtf8Bytes(document, ExportJsonOptions));
    }

    public async Task<GuestErasureResult> EraseAsync(Guid guestId, CancellationToken cancellationToken = default)
    {
        await using var strategyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var guest = await dbContext.Guests
                .Include(item => item.Reservations)
                .SingleOrDefaultAsync(item => item.Id == guestId, cancellationToken)
                ?? throw new GuestNotFoundException(localizer["Guest not found."]);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (guest.Reservations.Any(reservation =>
                    reservation.Status == ReservationStatus.CheckedIn ||
                    (reservation.Status == ReservationStatus.Confirmed && reservation.CheckOut >= today)))
            {
                throw new GuestValidationException(localizer["Guests with a current or upcoming stay cannot be erased. Complete or cancel the stay first."]);
            }

            GuestErasureResult result;
            if (guest.Reservations.Count == 0)
            {
                dbContext.Guests.Remove(guest);
                result = GuestErasureResult.Deleted;
            }
            else
            {
                guest.FirstName = null;
                guest.LastName = AnonymizedName;
                guest.Company = null;
                guest.Email = null;
                guest.Phone = null;
                guest.Street = null;
                guest.PostalCode = null;
                guest.City = null;
                guest.Country = null;
                guest.Nationality = null;
                guest.DateOfBirth = null;
                guest.Language = null;
                guest.VatId = null;
                guest.Notes = null;
                guest.IsVip = false;
                guest.MarketingConsent = false;
                guest.AnonymizedAtUtc = DateTimeOffset.UtcNow;
                guest.UpdatedAtUtc = guest.AnonymizedAtUtc.Value;

                // Issued invoices are accounting records with a statutory retention period and stay unchanged.
                foreach (var reservation in guest.Reservations)
                {
                    reservation.GuestName = AnonymizedName;
                    reservation.Notes = null;
                }

                result = GuestErasureResult.Anonymized;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private void Apply(Guest guest, GuestUpsertRequest request)
    {
        guest.LastName = Required(request.LastName, 120, localizer["Last name is required."]);
        guest.FirstName = Optional(request.FirstName, 80);
        guest.Company = Optional(request.Company, 200);
        guest.Email = ValidateEmail(Optional(request.Email, 256));
        guest.Phone = Optional(request.Phone, 40);
        guest.Street = Optional(request.Street, 200);
        guest.PostalCode = Optional(request.PostalCode, 16);
        guest.City = Optional(request.City, 100);
        guest.Country = Optional(request.Country, 100);
        guest.Nationality = Optional(request.Nationality, 100);
        guest.Language = Optional(request.Language, 8)?.ToLowerInvariant();
        guest.VatId = Optional(request.VatId, 24)?.Replace(" ", string.Empty).ToUpperInvariant();
        guest.Notes = Optional(request.Notes, 1000);
        guest.IsVip = request.IsVip;
        guest.MarketingConsent = request.MarketingConsent;

        if (request.DateOfBirth is { } birthDate && (birthDate > DateOnly.FromDateTime(DateTime.UtcNow) || birthDate.Year < 1900))
        {
            throw new GuestValidationException(localizer["Enter a valid date of birth."]);
        }

        guest.DateOfBirth = request.DateOfBirth;
    }

    private static async Task<GuestDetail> ToDetailAsync(HotelDbContext dbContext, Guest guest, CancellationToken cancellationToken)
    {
        var stays = await dbContext.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.GuestId == guest.Id)
            .OrderByDescending(reservation => reservation.CheckIn)
            .Select(reservation => new GuestStay(
                reservation.Id,
                reservation.Room.Number,
                reservation.CheckIn,
                reservation.CheckOut,
                reservation.Occupancy,
                reservation.Status,
                reservation.Source,
                reservation.ExternalReference,
                reservation.Invoices.Select(invoice => invoice.InvoiceNumber).FirstOrDefault(),
                reservation.Invoices.Select(invoice => (decimal?)invoice.GrossAmount).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var nights = stays
            .Where(stay => stay.Status != ReservationStatus.Cancelled)
            .Sum(stay => stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);

        return new GuestDetail(
            guest.Id,
            guest.FirstName,
            guest.LastName,
            guest.DisplayName,
            guest.Company,
            guest.Email,
            guest.Phone,
            guest.Street,
            guest.PostalCode,
            guest.City,
            guest.Country,
            guest.Nationality,
            guest.DateOfBirth,
            guest.Language,
            guest.VatId,
            guest.Notes,
            guest.IsVip,
            guest.MarketingConsent,
            guest.CreatedAtUtc,
            guest.UpdatedAtUtc,
            guest.AnonymizedAtUtc,
            guest.PostalAddress,
            stays.Sum(stay => stay.InvoiceGrossAmount ?? 0m),
            nights,
            stays);
    }

    private static string FileNamePart(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length == 0 ? "guest" : cleaned.ToLowerInvariant();
    }

    private string Required(string? value, int maxLength, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new GuestValidationException(message);
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new GuestValidationException(localizer["A field exceeds the maximum length."]);
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
            : throw new GuestValidationException(localizer["A field exceeds the maximum length."]);
    }

    private string? ValidateEmail(string? email) =>
        email is null || (MailAddress.TryCreate(email, out var address) && address.Address == email)
            ? email
            : throw new GuestValidationException(localizer["Enter a valid email address."]);
}
