using System.Data;
using System.Globalization;
using System.Text;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Guests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Imports;

/// <summary>
/// Imports reservations from a Booking.com extranet export (Reservations → Download). Each booked unit becomes a
/// reservation keyed by its booking number, so the same file (or an overlapping newer one) can be imported repeatedly.
/// </summary>
public sealed class BookingComImportService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<BookingComImportService> localizer) : IBookingComImportService
{
    private const int MaxRoomsPerBooking = 20;

    /// <summary>Normalized header names (lower case, letters and digits only, no accents); first match wins.</summary>
    private static readonly (BookingComField Field, string[] Aliases)[] ColumnAliases =
    [
        (BookingComField.BookingNumber, ["booknumber", "bookingnumber", "reservationnumber", "bookingid", "reservationid", "buchungsnummer", "reservierungsnummer"]),
        (BookingComField.GuestName, ["guestnames", "guestname", "nameofguest", "nameofguests", "gastname", "gastnamen", "namedesgastes", "namedergaste", "gaste", "gast"]),
        (BookingComField.BookedBy, ["bookedby", "booker", "bookername", "gebuchtvon", "buchender", "namedesbuchers", "nameofbooker"]),
        (BookingComField.CheckIn, ["checkin", "checkindate", "arrival", "arrivaldate", "anreise", "anreisedatum"]),
        (BookingComField.CheckOut, ["checkout", "checkoutdate", "departure", "departuredate", "abreise", "abreisedatum"]),
        (BookingComField.Status, ["status", "bookingstatus", "reservationstatus", "buchungsstatus"]),
        (BookingComField.Rooms, ["rooms", "numberofrooms", "units", "numberofunits", "zimmer", "anzahlzimmer", "einheiten"]),
        (BookingComField.Persons, ["persons", "people", "numberofguests", "personen", "anzahlpersonen", "anzahlgaste"]),
        (BookingComField.Adults, ["adults", "erwachsene"]),
        (BookingComField.Children, ["children", "kinder"]),
        (BookingComField.UnitType, ["unittype", "roomtype", "roomname", "unitname", "zimmertyp", "zimmerart", "zimmerkategorie", "unterkunftstyp", "einheitentyp", "einheit"]),
        (BookingComField.Price, ["price", "totalprice", "totalamount", "preis", "gesamtpreis", "gesamtbetrag", "betrag"]),
        (BookingComField.Commission, ["commissionamount", "commission", "provisionsbetrag", "provision", "kommission"]),
        (BookingComField.Remarks, ["remarks", "specialrequests", "comments", "notes", "anmerkungen", "bemerkungen", "besonderewunsche"]),
        (BookingComField.Email, ["email", "emailaddress", "guestemail", "emailadresse"]),
        (BookingComField.Phone, ["phone", "phonenumber", "telephone", "telefon", "telefonnummer"]),
        (BookingComField.Country, ["bookercountry", "country", "guestcountry", "landdesbuchenden", "herkunftsland", "land"]),
        (BookingComField.Address, ["address", "adresse", "anschrift"])
    ];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "dd/MM/yyyy", "d/M/yyyy",
        "d MMM yyyy", "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy", "ddd, d MMM yyyy", "ddd d MMM yyyy",
        // Booking.com Extranet export in German, e.g. "12. November 2026" or "Do., 12. Nov. 2026".
        "d. MMMM yyyy", "d. MMM yyyy", "ddd, d. MMMM yyyy", "ddd, d. MMM yyyy", "dddd, d. MMMM yyyy"
    ];

    // de-DE uses "Januar", de-AT "Jänner"; Booking.com exports use either depending on the account language.
    private static readonly CultureInfo[] DateCultures =
        new[] { "", "de-DE", "de-AT", "en-GB", "en-US" }.Select(CultureInfo.GetCultureInfo).ToArray();

    public IReadOnlyDictionary<BookingComField, int> DetectColumns(IReadOnlyList<string> headers)
    {
        var normalized = headers.Select(Normalize).ToList();
        var used = new HashSet<int>();
        var columns = new Dictionary<BookingComField, int>();

        foreach (var (field, aliases) in ColumnAliases)
        {
            foreach (var alias in aliases)
            {
                var index = normalized.FindIndex(header => header == alias);
                if (index >= 0 && used.Add(index))
                {
                    columns[field] = index;
                    break;
                }
            }
        }

        return columns;
    }

    public async Task<IReadOnlyDictionary<string, string?>> SuggestCategoriesAsync(
        ImportTable table,
        IReadOnlyDictionary<BookingComField, int> columns,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var categories = await dbContext.RoomCategories.AsNoTracking()
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
            .Select(category => new { category.Name, category.Code })
            .ToListAsync(cancellationToken);

        var unitTypes = ParseUnits(table, columns, requireMapping: false).Select(unit => unit.UnitType).Distinct();
        return unitTypes.ToDictionary(
            unitType => unitType,
            unitType =>
            {
                var key = Normalize(unitType);
                if (key.Length == 0)
                {
                    return categories.Count == 1 ? categories[0].Name : null;
                }

                return (categories.FirstOrDefault(category => Normalize(category.Name) == key || Normalize(category.Code) == key)
                        ?? categories.FirstOrDefault(category => key.Contains(Normalize(category.Name)) || Normalize(category.Name).Contains(key)))
                    ?.Name;
            });
    }

    public async Task<BookingComImportPreview> PreviewAsync(ImportTable table, BookingComImportOptions options, CancellationToken cancellationToken = default)
    {
        var units = ParseUnits(table, options.Columns, requireMapping: true);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var context = await LoadContextAsync(dbContext, cancellationToken);

        var references = units.Select(unit => unit.Reference).ToList();
        var existing = await dbContext.Reservations.AsNoTracking()
            .Where(reservation => reservation.Source == ReservationSource.BookingCom && references.Contains(reservation.ExternalReference!))
            .Select(ExistingProjection)
            .ToDictionaryAsync(reservation => reservation.ExternalReference, cancellationToken);

        // Simulates the import against current occupancy so the preview predicts room conflicts between rows, too.
        var datedUnits = units.Where(unit => unit.CheckIn is not null && unit.CheckOut is not null).ToList();
        var occupancy = new List<(Guid ReservationId, Guid RoomId, DateOnly CheckIn, DateOnly CheckOut)>();
        if (datedUnits.Count > 0)
        {
            var from = datedUnits.Min(unit => unit.CheckIn!.Value);
            var to = datedUnits.Max(unit => unit.CheckOut!.Value);
            var existingStays = await dbContext.Reservations.AsNoTracking()
                .Where(reservation =>
                    reservation.Status != ReservationStatus.CheckedOut &&
                    reservation.Status != ReservationStatus.Cancelled &&
                    reservation.CheckIn < to &&
                    reservation.CheckOut > from)
                .Select(reservation => new { reservation.Id, reservation.RoomId, reservation.CheckIn, reservation.CheckOut })
                .ToListAsync(cancellationToken);
            occupancy.AddRange(existingStays.Select(stay => (stay.Id, stay.RoomId, stay.CheckIn, stay.CheckOut)));
        }

        var rows = new List<BookingComImportRow>();
        foreach (var unit in units)
        {
            var current = existing.GetValueOrDefault(unit.Reference);
            var outcome = await PlanAsync(unit, current, context, options, (roomId, checkIn, checkOut, excludedId) =>
                Task.FromResult(!occupancy.Any(stay =>
                    stay.RoomId == roomId && stay.ReservationId != excludedId && stay.CheckIn < checkOut && stay.CheckOut > checkIn)));

            var reservationId = current?.Id ?? Guid.NewGuid();
            if (outcome.Action is BookingComImportAction.Create or BookingComImportAction.Update or BookingComImportAction.Cancel)
            {
                occupancy.RemoveAll(stay => stay.ReservationId == reservationId);
            }

            if (outcome.Action is BookingComImportAction.Create or BookingComImportAction.Update)
            {
                occupancy.Add((reservationId, outcome.RoomId!.Value, unit.CheckIn!.Value, unit.CheckOut!.Value));
            }

            rows.Add(ToRow(unit, outcome));
        }

        return new BookingComImportPreview(
            rows,
            units.Select(unit => unit.UnitType).Distinct().Order().ToList(),
            context.Categories);
    }

    public async Task<BookingComImportResult> ImportAsync(ImportTable table, BookingComImportOptions options, CancellationToken cancellationToken = default)
    {
        var units = ParseUnits(table, options.Columns, requireMapping: true);
        await using var strategyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var context = await LoadContextAsync(strategyContext, cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var guestsByBooking = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var rows = new List<BookingComImportRow>();

        foreach (var unit in units)
        {
            try
            {
                var (outcome, guestId) = await strategy.ExecuteAsync(() => ImportUnitAsync(unit, context, options, guestsByBooking, cancellationToken));
                if (guestId is { } id)
                {
                    guestsByBooking[unit.BookingNumber] = id;
                }

                rows.Add(ToRow(unit, outcome));
            }
            catch (Exception exception) when (exception is DbUpdateException or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException)
            {
                rows.Add(ToRow(unit, new Outcome(BookingComImportAction.Error, localizer["The reservation could not be saved. Try the import again."], null, null)));
            }
        }

        return new BookingComImportResult(rows);
    }

    private async Task<(Outcome Outcome, Guid? GuestId)> ImportUnitAsync(
        ParsedUnit unit,
        ImportContext context,
        BookingComImportOptions options,
        IReadOnlyDictionary<string, Guid> guestsByBooking,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var existing = await dbContext.Reservations.AsNoTracking()
            .Where(reservation => reservation.Source == ReservationSource.BookingCom && reservation.ExternalReference == unit.Reference)
            .Select(ExistingProjection)
            .SingleOrDefaultAsync(cancellationToken);

        var outcome = await PlanAsync(unit, existing, context, options, async (roomId, checkIn, checkOut, excludedId) =>
            !await dbContext.Reservations.AnyAsync(reservation =>
                reservation.RoomId == roomId &&
                reservation.Id != excludedId &&
                reservation.Status != ReservationStatus.CheckedOut &&
                reservation.Status != ReservationStatus.Cancelled &&
                reservation.CheckIn < checkOut &&
                reservation.CheckOut > checkIn,
                cancellationToken));

        Guid? guestId = null;
        switch (outcome.Action)
        {
            case BookingComImportAction.Create:
                guestId = options.CreateGuestProfiles ? await ResolveGuestAsync(dbContext, unit, guestsByBooking, cancellationToken) : null;
                dbContext.Reservations.Add(new Reservation
                {
                    Id = Guid.NewGuid(),
                    RoomId = outcome.RoomId!.Value,
                    GuestName = unit.GuestName,
                    CheckIn = unit.CheckIn!.Value,
                    CheckOut = unit.CheckOut!.Value,
                    Occupancy = unit.Occupancy,
                    Status = ReservationStatus.Confirmed,
                    Notes = BuildNotes(unit),
                    GuestId = guestId,
                    Source = ReservationSource.BookingCom,
                    ExternalReference = unit.Reference
                });
                break;
            case BookingComImportAction.Update:
                var reservation = await dbContext.Reservations.SingleAsync(item => item.Id == existing!.Id, cancellationToken);
                reservation.RoomId = outcome.RoomId!.Value;
                reservation.GuestName = unit.GuestName;
                reservation.CheckIn = unit.CheckIn!.Value;
                reservation.CheckOut = unit.CheckOut!.Value;
                reservation.Occupancy = unit.Occupancy;
                break;
            case BookingComImportAction.Cancel:
                var cancelled = await dbContext.Reservations.SingleAsync(item => item.Id == existing!.Id, cancellationToken);
                cancelled.Status = ReservationStatus.Cancelled;
                break;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (outcome, guestId);
    }

    private async Task<Outcome> PlanAsync(
        ParsedUnit unit,
        ExistingReservation? existing,
        ImportContext context,
        BookingComImportOptions options,
        Func<Guid, DateOnly, DateOnly, Guid, Task<bool>> isRoomFree)
    {
        if (unit.Error is not null)
        {
            return new Outcome(BookingComImportAction.Error, unit.Error, null, null);
        }

        var checkIn = unit.CheckIn!.Value;
        var checkOut = unit.CheckOut!.Value;

        if (unit.State != BookingState.Ok)
        {
            var reason = unit.State == BookingState.NoShow ? localizer["No-show."].Value : null;
            return existing switch
            {
                null => new Outcome(BookingComImportAction.Skip, reason ?? localizer["Cancelled on Booking.com; not in HotelManager."], null, null),
                { Status: ReservationStatus.Cancelled } => new Outcome(BookingComImportAction.Unchanged, reason, existing.RoomNumber, existing.RoomId),
                { Status: ReservationStatus.Confirmed } => new Outcome(BookingComImportAction.Cancel, reason, existing.RoomNumber, existing.RoomId),
                _ => new Outcome(BookingComImportAction.Skip, localizer["Cancelled on Booking.com, but the guest has already checked in. Check manually."], existing.RoomNumber, existing.RoomId)
            };
        }

        if (existing is not null)
        {
            if (existing.Status == ReservationStatus.Cancelled)
            {
                return new Outcome(BookingComImportAction.Skip, localizer["Cancelled in HotelManager; not reactivated."], existing.RoomNumber, existing.RoomId);
            }

            if (existing.Status != ReservationStatus.Confirmed)
            {
                return new Outcome(BookingComImportAction.Unchanged, localizer["Guest has already checked in."], existing.RoomNumber, existing.RoomId);
            }

            if (existing.CheckIn == checkIn && existing.CheckOut == checkOut && existing.Occupancy == unit.Occupancy &&
                string.Equals(existing.GuestName, unit.GuestName, StringComparison.Ordinal))
            {
                return new Outcome(BookingComImportAction.Unchanged, null, existing.RoomNumber, existing.RoomId);
            }

            if (await isRoomFree(existing.RoomId, checkIn, checkOut, existing.Id))
            {
                return new Outcome(BookingComImportAction.Update, null, existing.RoomNumber, existing.RoomId);
            }

            var category = MappedCategory(unit, context, options) ?? existing.RoomType;
            var alternative = await FindFreeRoomAsync(context, category, checkIn, checkOut, existing.Id, isRoomFree);
            return alternative is null
                ? new Outcome(BookingComImportAction.Error, localizer["No free room in category {0} for the new dates.", category], existing.RoomNumber, null)
                : new Outcome(BookingComImportAction.Update, localizer["Moved to room {0}.", alternative.Number], alternative.Number, alternative.Id);
        }

        if (options.SkipPastStays && checkOut < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return new Outcome(BookingComImportAction.Skip, localizer["Stay is in the past."], null, null);
        }

        var mappedCategory = MappedCategory(unit, context, options);
        if (mappedCategory is null)
        {
            return new Outcome(
                BookingComImportAction.Error,
                unit.UnitType.Length == 0
                    ? localizer["Choose a room category for bookings without unit type."]
                    : localizer["Choose a room category for unit type \"{0}\".", unit.UnitType],
                null,
                null);
        }

        var room = await FindFreeRoomAsync(context, mappedCategory, checkIn, checkOut, Guid.Empty, isRoomFree);
        return room is null
            ? new Outcome(BookingComImportAction.Error, localizer["No free room in category {0}.", mappedCategory], null, null)
            : new Outcome(BookingComImportAction.Create, null, room.Number, room.Id);
    }

    private static string? MappedCategory(ParsedUnit unit, ImportContext context, BookingComImportOptions options) =>
        options.UnitTypeCategories.TryGetValue(unit.UnitType, out var category) && category is not null && context.Categories.Contains(category)
            ? category
            : null;

    private static async Task<ImportRoom?> FindFreeRoomAsync(
        ImportContext context,
        string category,
        DateOnly checkIn,
        DateOnly checkOut,
        Guid excludedReservationId,
        Func<Guid, DateOnly, DateOnly, Guid, Task<bool>> isRoomFree)
    {
        foreach (var room in context.Rooms.Where(room => room.Type == category))
        {
            if (await isRoomFree(room.Id, checkIn, checkOut, excludedReservationId))
            {
                return room;
            }
        }

        return null;
    }

    private static async Task<Guid> ResolveGuestAsync(
        HotelDbContext dbContext,
        ParsedUnit unit,
        IReadOnlyDictionary<string, Guid> guestsByBooking,
        CancellationToken cancellationToken)
    {
        if (guestsByBooking.TryGetValue(unit.BookingNumber, out var known))
        {
            return known;
        }

        if (unit.Email is not null)
        {
            var email = unit.Email.ToLowerInvariant();
            var match = await dbContext.Guests
                .Where(guest => guest.AnonymizedAtUtc == null && guest.Email != null && guest.Email.ToLower() == email)
                .Select(guest => (Guid?)guest.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (match is { } id)
            {
                return id;
            }
        }

        var (firstName, lastName) = GuestNames.Split(unit.GuestName);
        var guest = new Guest
        {
            Id = Guid.NewGuid(),
            FirstName = Truncate(firstName, 80),
            LastName = Truncate(lastName, 120)!,
            Email = unit.Email,
            Phone = Truncate(unit.Phone, 40),
            Country = Truncate(unit.Country, 100),
            Street = Truncate(unit.Address, 200)
        };
        dbContext.Guests.Add(guest);
        return guest.Id;
    }

    private string BuildNotes(ParsedUnit unit)
    {
        var parts = new List<string> { $"Booking.com {unit.BookingNumber}" };
        if (unit.Price is not null)
        {
            parts.Add(localizer["Price: {0}", unit.Price]);
        }

        if (unit.Commission is not null)
        {
            parts.Add(localizer["Commission: {0}", unit.Commission]);
        }

        if (unit.Remarks is not null)
        {
            parts.Add(unit.Remarks);
        }

        return Truncate(string.Join(" · ", parts), 500)!;
    }

    private List<ParsedUnit> ParseUnits(ImportTable table, IReadOnlyDictionary<BookingComField, int> columns, bool requireMapping)
    {
        int? Column(BookingComField field) => columns.TryGetValue(field, out var index) ? index : null;

        if (requireMapping)
        {
            var missing = new[] { BookingComField.BookingNumber, BookingComField.CheckIn, BookingComField.CheckOut }
                .Where(field => Column(field) is null)
                .ToList();
            if (Column(BookingComField.GuestName) is null && Column(BookingComField.BookedBy) is null)
            {
                missing.Add(BookingComField.GuestName);
            }

            if (missing.Count > 0)
            {
                throw new ImportFileException(localizer["Assign the required columns: {0}.", string.Join(", ", missing)]);
            }
        }

        var units = new List<ParsedUnit>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            string? Text(BookingComField field) => table.Cell(row, Column(field)) is { Length: > 0 } value ? value : null;

            var bookingNumber = Text(BookingComField.BookingNumber);
            if (bookingNumber is null)
            {
                continue;
            }

            // Excel stores long booking numbers as numbers ("4123456789" may arrive as "4123456789.0").
            if (bookingNumber.EndsWith(".0", StringComparison.Ordinal) && bookingNumber[..^2].All(char.IsDigit))
            {
                bookingNumber = bookingNumber[..^2];
            }

            var guestName = Truncate(Text(BookingComField.GuestName) ?? Text(BookingComField.BookedBy), 120) ?? string.Empty;
            var checkIn = ParseDate(Text(BookingComField.CheckIn));
            var checkOut = ParseDate(Text(BookingComField.CheckOut));
            var statusText = Normalize(Text(BookingComField.Status) ?? string.Empty);
            var state = statusText.Contains("noshow") || statusText.Contains("nichtangereist")
                ? BookingState.NoShow
                : statusText.Contains("cancel") || statusText.Contains("storn")
                    ? BookingState.Cancelled
                    : BookingState.Ok;

            string? error = null;
            if (bookingNumber.Length > 32)
            {
                error = localizer["Booking number is too long."];
            }
            else if (guestName.Length == 0)
            {
                error = localizer["Guest name is missing."];
            }
            else if (checkIn is null || checkOut is null)
            {
                error = localizer["Check-in or check-out date is not readable."];
            }
            else if (checkOut <= checkIn)
            {
                error = localizer["Check-out must be after check-in."];
            }

            var rooms = Math.Clamp(ParseInt(Text(BookingComField.Rooms)) ?? 1, 1, MaxRoomsPerBooking);
            var persons = ParseInt(Text(BookingComField.Persons))
                ?? (ParseInt(Text(BookingComField.Adults)) + (ParseInt(Text(BookingComField.Children)) ?? 0))
                ?? rooms;
            persons = Math.Max(persons, rooms);

            var unitTypeText = Text(BookingComField.UnitType) ?? string.Empty;
            var unitTypes = unitTypeText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var email = Text(BookingComField.Email);

            for (var unitIndex = 0; unitIndex < rooms; unitIndex++)
            {
                var unitType = rooms > 1 && unitTypes.Length == rooms ? unitTypes[unitIndex] : unitTypeText.Trim();
                units.Add(new ParsedUnit(
                    rowIndex + 2,
                    unitIndex == 0 ? bookingNumber : $"{bookingNumber}-{unitIndex + 1}",
                    bookingNumber,
                    guestName,
                    checkIn,
                    checkOut,
                    persons / rooms + (unitIndex < persons % rooms ? 1 : 0),
                    unitType,
                    state,
                    Text(BookingComField.Price),
                    Text(BookingComField.Commission),
                    Text(BookingComField.Remarks),
                    email is not null && email.Contains('@') && email.Length <= 256 ? email : null,
                    Text(BookingComField.Phone),
                    Text(BookingComField.Country),
                    Text(BookingComField.Address),
                    error));
            }
        }

        return units;
    }

    private static async Task<ImportContext> LoadContextAsync(HotelDbContext dbContext, CancellationToken cancellationToken)
    {
        var categories = await dbContext.RoomCategories.AsNoTracking()
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
            .Select(category => category.Name)
            .ToListAsync(cancellationToken);
        var rooms = await dbContext.Rooms.AsNoTracking()
            .Where(room => room.IsActive)
            .OrderBy(room => room.Floor).ThenBy(room => room.Number)
            .Select(room => new ImportRoom(room.Id, room.Number, room.Type))
            .ToListAsync(cancellationToken);
        return new ImportContext(categories, rooms);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Reservation, ExistingReservation>> ExistingProjection =
        reservation => new ExistingReservation(
            reservation.Id,
            reservation.ExternalReference!,
            reservation.RoomId,
            reservation.Room.Number,
            reservation.Room.Type,
            reservation.GuestName,
            reservation.CheckIn,
            reservation.CheckOut,
            reservation.Occupancy,
            reservation.Status);

    private static BookingComImportRow ToRow(ParsedUnit unit, Outcome outcome) => new(
        unit.RowNumber,
        unit.Reference,
        unit.GuestName,
        unit.CheckIn,
        unit.CheckOut,
        unit.Occupancy,
        unit.UnitType,
        unit.State != BookingState.Ok,
        outcome.Action,
        outcome.RoomNumber,
        outcome.Message);

    internal static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        foreach (var culture in DateCultures)
        {
            if (DateTime.TryParseExact(text, DateFormats, culture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            {
                return DateOnly.FromDateTime(exact);
            }
        }

        return null;
    }

    private static int? ParseInt(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number >= 0 && number < 1000
            ? (int)number
            : null;

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];

    internal static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private enum BookingState
    {
        Ok,
        Cancelled,
        NoShow
    }

    private sealed record ParsedUnit(
        int RowNumber,
        string Reference,
        string BookingNumber,
        string GuestName,
        DateOnly? CheckIn,
        DateOnly? CheckOut,
        int Occupancy,
        string UnitType,
        BookingState State,
        string? Price,
        string? Commission,
        string? Remarks,
        string? Email,
        string? Phone,
        string? Country,
        string? Address,
        string? Error);

    private sealed record ExistingReservation(
        Guid Id,
        string ExternalReference,
        Guid RoomId,
        string RoomNumber,
        string RoomType,
        string GuestName,
        DateOnly CheckIn,
        DateOnly CheckOut,
        int Occupancy,
        ReservationStatus Status);

    private sealed record ImportRoom(Guid Id, string Number, string Type);

    private sealed record ImportContext(IReadOnlyList<string> Categories, IReadOnlyList<ImportRoom> Rooms);

    private sealed record Outcome(BookingComImportAction Action, string? Message, string? RoomNumber, Guid? RoomId);
}
