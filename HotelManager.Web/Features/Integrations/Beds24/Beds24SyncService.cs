using System.Data;
using System.Globalization;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Guests;
using HotelManager.Web.Features.Imports;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Integrations.Beds24;

/// <summary>
/// Imports Beds24 bookings into the room planner. Each booked room becomes a reservation keyed by the Beds24 booking id,
/// so syncs are idempotent; changed dates, guest counts and cancellations are applied to reservations that have not been
/// checked in yet. Reservations imported earlier from a Booking.com file are taken over instead of being duplicated.
/// </summary>
public sealed class Beds24SyncService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    TenantContext tenantContext,
    IBeds24Client client,
    Beds24Coordinator coordinator,
    IDataProtectionProvider dataProtectionProvider,
    IStringLocalizer<Beds24SyncService> localizer,
    ILogger<Beds24SyncService> logger) : IBeds24SyncService
{
    private const int MaxRoomsPerBooking = 20;
    private const int MaxProblems = 30;

    /// <summary>Bookings modified shortly before the last sync are read again, in case Beds24 saved them late.</summary>
    private static readonly TimeSpan SyncOverlap = TimeSpan.FromMinutes(10);

    private IDataProtector Protector => dataProtectionProvider.CreateProtector("HotelManager.Beds24.RefreshToken");

    public async Task<Beds24Status?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await dbContext.Beds24Connections.AsNoTracking().SingleOrDefaultAsync(item => item.HotelId == hotelId, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var categories = await dbContext.RoomCategories.AsNoTracking()
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
            .Select(category => category.Name)
            .ToListAsync(cancellationToken);

        return new Beds24Status(
            connection.PropertyId,
            connection.PropertyName,
            connection.IsEnabled,
            connection.WebhookKey,
            connection.ConnectedAtUtc,
            connection.LastSyncAtUtc,
            connection.LastAttemptAtUtc,
            connection.LastError,
            connection.LastCreated,
            connection.LastUpdated,
            connection.LastCancelled,
            (connection.LastProblems ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries),
            categories);
    }

    public async Task<Beds24Status> ConnectAsync(string inviteCode, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        var code = inviteCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            throw new Beds24IntegrationException(localizer["Enter the invite code from Beds24."]);
        }

        Beds24Tokens tokens;
        IReadOnlyList<Beds24Property> properties;
        try
        {
            tokens = await client.SetupAsync(code, cancellationToken);
            properties = await client.GetPropertiesAsync(tokens.AccessToken, cancellationToken);
        }
        catch (Beds24ApiException exception)
        {
            throw new Beds24IntegrationException(localizer["Beds24 did not accept the invite code: {0}", exception.Message]);
        }

        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var connection = await dbContext.Beds24Connections.SingleOrDefaultAsync(item => item.HotelId == hotelId, cancellationToken);
            if (connection is null)
            {
                connection = new Beds24Connection { HotelId = hotelId, WebhookKey = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)) };
                dbContext.Beds24Connections.Add(connection);
            }

            connection.ProtectedRefreshToken = Protector.Protect(tokens.RefreshToken);
            connection.ConnectedAtUtc = DateTimeOffset.UtcNow;
            connection.IsEnabled = true;
            connection.LastError = null;
            connection.LastSyncAtUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        coordinator.CacheToken(hotelId, tokens.AccessToken, tokens.ExpiresIn);
        if (properties.Count == 1)
        {
            await ApplyPropertyAsync(hotelId, properties[0], cancellationToken);
        }

        return (await GetStatusAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<Beds24PropertyOption>> GetPropertiesAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        var properties = await CallAsync(hotelId, token => client.GetPropertiesAsync(token, cancellationToken), cancellationToken);
        return properties
            .Select(property => new Beds24PropertyOption(property.Id, PropertyName(property), property.RoomTypes.Count))
            .OrderBy(property => property.Name)
            .ToList();
    }

    public async Task SelectPropertyAsync(int propertyId, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        var properties = await CallAsync(hotelId, token => client.GetPropertiesAsync(token, cancellationToken), cancellationToken);
        var property = properties.FirstOrDefault(item => item.Id == propertyId)
            ?? throw new Beds24IntegrationException(localizer["The property was not found in Beds24."]);
        await ApplyPropertyAsync(hotelId, property, cancellationToken);
    }

    public async Task<IReadOnlyList<Beds24RoomMappingRow>> GetRoomMappingsAsync(CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Beds24RoomMappings.AsNoTracking()
            .OrderBy(mapping => mapping.Beds24RoomName)
            .Select(mapping => new Beds24RoomMappingRow(mapping.Beds24RoomId, mapping.Beds24RoomName, mapping.CategoryName))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveRoomMappingsAsync(IReadOnlyDictionary<int, string?> categoriesByRoomId, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var categories = await dbContext.RoomCategories.Select(category => category.Name).ToListAsync(cancellationToken);
        var mappings = await dbContext.Beds24RoomMappings.ToListAsync(cancellationToken);
        foreach (var mapping in mappings)
        {
            if (!categoriesByRoomId.TryGetValue(mapping.Beds24RoomId, out var category))
            {
                continue;
            }

            if (category is not null && !categories.Contains(category))
            {
                throw new Beds24IntegrationException(localizer["Room category {0} does not exist.", category]);
            }

            mapping.CategoryName = category;
        }

        var connection = await dbContext.Beds24Connections.SingleAsync(item => item.HotelId == hotelId, cancellationToken);
        connection.LastSyncAtUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await dbContext.Beds24Connections.SingleAsync(item => item.HotelId == hotelId, cancellationToken);
        connection.IsEnabled = enabled;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Beds24SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        var gate = coordinator.GetLock(hotelId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SyncAsync(hotelId, cancellationToken)
                ?? throw new Beds24IntegrationException(localizer["Choose the Beds24 property first."]);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Beds24SyncResult?> RunScheduledSyncAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = tenantContext.HotelId ?? throw new InvalidOperationException("No hotel selected.");
        var gate = coordinator.GetLock(hotelId);
        if (!await gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return null;
        }

        try
        {
            return await SyncAsync(hotelId, cancellationToken);
        }
        catch (Beds24IntegrationException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var hotelId = RequireAdmin();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Beds24RoomMappings.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Beds24Connections.Where(item => item.HotelId == hotelId).ExecuteDeleteAsync(cancellationToken);
        coordinator.ForgetToken(hotelId);
    }

    private async Task<Beds24SyncResult?> SyncAsync(Guid hotelId, CancellationToken cancellationToken)
    {
        Beds24Connection? connection;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            connection = await dbContext.Beds24Connections.AsNoTracking().SingleOrDefaultAsync(item => item.HotelId == hotelId, cancellationToken);
        }

        if (connection?.PropertyId is not { } propertyId)
        {
            return null;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Beds24SyncResult result;
        try
        {
            var (property, bookings) = await CallAsync(hotelId, async token =>
            {
                var properties = await client.GetPropertiesAsync(token, cancellationToken);
                var bookingList = await client.GetBookingsAsync(
                    token,
                    new Beds24BookingQuery(propertyId, today.AddDays(-1), connection.LastSyncAtUtc - SyncOverlap),
                    cancellationToken);
                return (properties.FirstOrDefault(item => item.Id == propertyId), bookingList);
            }, cancellationToken);

            if (property is not null)
            {
                await UpsertRoomTypesAsync(property, cancellationToken);
            }

            result = await ImportBookingsAsync(property, bookings.Where(booking => booking.PropertyId == propertyId || booking.PropertyId == 0).ToList(), today, cancellationToken);
        }
        catch (Beds24IntegrationException exception)
        {
            await SaveStatusAsync(hotelId, startedAt, null, exception.Message, cancellationToken);
            throw;
        }

        await SaveStatusAsync(hotelId, startedAt, result, null, cancellationToken);
        logger.LogInformation(
            "Beds24 sync of hotel {HotelId}: {Created} created, {Updated} updated, {Cancelled} cancelled, {Problems} problems.",
            hotelId, result.Created, result.Updated, result.Cancelled, result.Problems.Count);
        return result;
    }

    private async Task<Beds24SyncResult> ImportBookingsAsync(
        Beds24Property? property,
        IReadOnlyList<Beds24Booking> bookings,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mappings = await strategyContext.Beds24RoomMappings.AsNoTracking()
            .ToDictionaryAsync(mapping => mapping.Beds24RoomId, cancellationToken);
        var rooms = await strategyContext.Rooms.AsNoTracking()
            .Where(room => room.IsActive)
            .OrderBy(room => room.Number)
            .Select(room => new SyncRoom(room.Id, room.Number, room.Type))
            .ToListAsync(cancellationToken);
        var unitNames = property?.RoomTypes.SelectMany(roomType => roomType.Units)
            .GroupBy(unit => unit.Id)
            .ToDictionary(group => group.Key, group => group.First().Name?.Trim()) ?? [];

        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var guestsByBooking = new Dictionary<int, Guid>();
        int created = 0, updated = 0, cancelled = 0, unchanged = 0;
        var problems = new List<string>();

        foreach (var unit in bookings.SelectMany(booking => ToUnits(booking, mappings, unitNames)))
        {
            try
            {
                var (action, message, guestId) = await strategy.ExecuteAsync(() => ImportUnitAsync(unit, rooms, guestsByBooking, today, cancellationToken));
                if (guestId is { } id)
                {
                    guestsByBooking[unit.GroupId] = id;
                }

                switch (action)
                {
                    case SyncAction.Create:
                        created++;
                        break;
                    case SyncAction.Update:
                        updated++;
                        break;
                    case SyncAction.Cancel:
                        cancelled++;
                        break;
                    case SyncAction.Unchanged:
                        unchanged++;
                        break;
                }

                if (action == SyncAction.Problem && message is not null)
                {
                    problems.Add($"{unit.Label}: {message}");
                }
            }
            catch (Exception exception) when (exception is DbUpdateException or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException)
            {
                logger.LogWarning(exception, "Beds24 booking {Reference} could not be saved.", unit.Reference);
                problems.Add($"{unit.Label}: {localizer["The reservation could not be saved."]}");
            }
        }

        return new Beds24SyncResult(created, updated, cancelled, unchanged, problems);
    }

    private async Task<(SyncAction Action, string? Message, Guid? GuestId)> ImportUnitAsync(
        SyncUnit unit,
        IReadOnlyList<SyncRoom> rooms,
        IReadOnlyDictionary<int, Guid> guestsByBooking,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        async Task<bool> IsRoomFree(Guid roomId, Guid excludedId) =>
            !await dbContext.Reservations.AnyAsync(reservation =>
                reservation.RoomId == roomId &&
                reservation.Id != excludedId &&
                reservation.Status != ReservationStatus.CheckedOut &&
                reservation.Status != ReservationStatus.Cancelled &&
                reservation.CheckIn < unit.CheckOut &&
                reservation.CheckOut > unit.CheckIn,
                cancellationToken);

        async Task<SyncRoom?> FindFreeRoom(string category, Guid excludedId)
        {
            var candidates = rooms.Where(room => room.Type == category)
                .OrderBy(room => string.Equals(room.Number, unit.UnitName, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            foreach (var room in candidates)
            {
                if (await IsRoomFree(room.Id, excludedId))
                {
                    return room;
                }
            }

            return null;
        }

        var reservation = await dbContext.Reservations.Include(item => item.Room)
            .SingleOrDefaultAsync(item => item.Source == ReservationSource.Beds24 && item.ExternalReference == unit.Reference, cancellationToken)
            ?? await AdoptBookingComReservationAsync(dbContext, unit, cancellationToken);

        (SyncAction, string?, Guid?) outcome;
        if (unit.IsCancelled)
        {
            outcome = reservation?.Status switch
            {
                null or ReservationStatus.Cancelled => (SyncAction.Unchanged, null, null),
                ReservationStatus.Confirmed => Cancel(reservation),
                _ => (SyncAction.Problem, localizer["Cancelled in Beds24, but the guest has already checked in. Check manually."].Value, null)
            };
        }
        else if (reservation is not null)
        {
            outcome = await UpdateAsync(reservation);
        }
        else if (unit.CheckOut < today)
        {
            outcome = (SyncAction.Unchanged, null, null);
        }
        else if (unit.CategoryName is null)
        {
            outcome = (SyncAction.Problem, localizer["Assign a room category to the Beds24 room type \"{0}\".", unit.RoomTypeName].Value, null);
        }
        else if (await FindFreeRoom(unit.CategoryName, Guid.Empty) is not { } room)
        {
            outcome = (SyncAction.Problem, localizer["No free room in category {0}.", unit.CategoryName].Value, null);
        }
        else
        {
            var guestId = guestsByBooking.TryGetValue(unit.GroupId, out var known)
                ? known
                : await GuestProfileResolver.FindOrCreateAsync(dbContext, unit.Guest, cancellationToken);
            dbContext.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                GuestName = unit.GuestName,
                CheckIn = unit.CheckIn,
                CheckOut = unit.CheckOut,
                Occupancy = unit.Occupancy,
                Status = ReservationStatus.Confirmed,
                Notes = unit.Notes,
                GuestId = guestId,
                Source = ReservationSource.Beds24,
                ExternalReference = unit.Reference
            });
            outcome = (SyncAction.Create, null, guestId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;

        (SyncAction, string?, Guid?) Cancel(Reservation item)
        {
            item.Source = ReservationSource.Beds24;
            item.ExternalReference = unit.Reference;
            item.Status = ReservationStatus.Cancelled;
            return (SyncAction.Cancel, null, null);
        }

        async Task<(SyncAction, string?, Guid?)> UpdateAsync(Reservation item)
        {
            if (item.Status == ReservationStatus.Cancelled)
            {
                return (SyncAction.Unchanged, null, null);
            }

            var adopted = item.Source != ReservationSource.Beds24 || item.ExternalReference != unit.Reference;
            item.Source = ReservationSource.Beds24;
            item.ExternalReference = unit.Reference;
            if (item.Status != ReservationStatus.Confirmed)
            {
                return (adopted ? SyncAction.Update : SyncAction.Unchanged, null, null);
            }

            if (item.CheckIn == unit.CheckIn && item.CheckOut == unit.CheckOut && item.Occupancy == unit.Occupancy &&
                string.Equals(item.GuestName, unit.GuestName, StringComparison.Ordinal))
            {
                return (adopted ? SyncAction.Update : SyncAction.Unchanged, null, null);
            }

            if (!await IsRoomFree(item.RoomId, item.Id))
            {
                var category = unit.CategoryName ?? item.Room.Type;
                if (await FindFreeRoom(category, item.Id) is not { } alternative)
                {
                    dbContext.ChangeTracker.Clear();
                    return (SyncAction.Problem, localizer["No free room in category {0} for the new dates.", category].Value, null);
                }

                item.RoomId = alternative.Id;
            }

            item.CheckIn = unit.CheckIn;
            item.CheckOut = unit.CheckOut;
            item.Occupancy = unit.Occupancy;
            item.GuestName = unit.GuestName;
            return (SyncAction.Update, null, null);
        }
    }

    /// <summary>Finds a reservation of the Booking.com file import for the same channel booking (matched by its reservation number).</summary>
    private static async Task<Reservation?> AdoptBookingComReservationAsync(HotelDbContext dbContext, SyncUnit unit, CancellationToken cancellationToken)
    {
        if (unit.BookingComReference is not { } reference)
        {
            return null;
        }

        var prefix = reference + "-";
        var candidates = await dbContext.Reservations.Include(item => item.Room)
            .Where(item => item.Source == ReservationSource.BookingCom &&
                           (item.ExternalReference == reference || item.ExternalReference!.StartsWith(prefix)))
            .ToListAsync(cancellationToken);
        return candidates
            .OrderBy(item => item.CheckIn == unit.CheckIn && item.CheckOut == unit.CheckOut ? 0 : 1)
            .ThenBy(item => item.Status == ReservationStatus.Cancelled ? 1 : 0)
            .ThenBy(item => item.ExternalReference!.Length)
            .ThenBy(item => item.ExternalReference, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private IEnumerable<SyncUnit> ToUnits(Beds24Booking booking, IReadOnlyDictionary<int, Beds24RoomMapping> mappings, IReadOnlyDictionary<int, string?> unitNames)
    {
        if (!TryParseDate(booking.Arrival, out var checkIn) || !TryParseDate(booking.Departure, out var checkOut) || checkOut <= checkIn)
        {
            yield break;
        }

        var status = booking.Status?.Trim().ToLowerInvariant();
        if (status is not ("confirmed" or "new" or "cancelled"))
        {
            yield break;
        }

        var rooms = Math.Clamp(booking.RoomQty ?? 1, 1, MaxRoomsPerBooking);
        var persons = Math.Max(Math.Max(booking.NumAdult ?? 0, 0) + Math.Max(booking.NumChild ?? 0, 0), rooms);
        var firstName = booking.FirstName?.Trim();
        var lastName = booking.LastName?.Trim();
        var fullName = string.Join(' ', new[] { firstName, lastName }.Where(part => !string.IsNullOrEmpty(part)));
        if (fullName.Length == 0)
        {
            fullName = $"Beds24 {booking.Id}";
        }

        var mapping = mappings.GetValueOrDefault(booking.RoomId);
        var isBookingCom = string.Equals(booking.Channel, "booking", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(booking.ApiReference);
        var guest = new ChannelGuestData(
            fullName,
            firstName,
            string.IsNullOrEmpty(lastName) ? null : lastName,
            booking.Email,
            string.IsNullOrWhiteSpace(booking.Mobile) ? booking.Phone : booking.Mobile,
            booking.Address,
            booking.Postcode,
            booking.City,
            string.IsNullOrWhiteSpace(booking.Country) ? booking.Country2 : booking.Country);

        for (var index = 0; index < rooms; index++)
        {
            yield return new SyncUnit(
                index == 0 ? booking.Id.ToString(CultureInfo.InvariantCulture) : $"{booking.Id}-{index + 1}",
                booking.MasterId ?? booking.Id,
                Truncate(fullName, 120),
                checkIn,
                checkOut,
                persons / rooms + (index < persons % rooms ? 1 : 0),
                status == "cancelled",
                mapping?.CategoryName,
                mapping?.Beds24RoomName ?? booking.RoomId.ToString(CultureInfo.InvariantCulture),
                index == 0 && booking.UnitId is { } unitId ? unitNames.GetValueOrDefault(unitId) : null,
                isBookingCom ? booking.ApiReference!.Trim() : null,
                BuildNotes(booking),
                guest);
        }
    }

    private string BuildNotes(Beds24Booking booking)
    {
        var channel = booking.Channel?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "booking" => "Booking.com",
            "airbnb" => "Airbnb",
            "expedia" => "Expedia",
            var other => other
        };
        var parts = new List<string>();
        if (channel is not null)
        {
            parts.Add(string.IsNullOrWhiteSpace(booking.ApiReference) ? channel : $"{channel} {booking.ApiReference.Trim()}");
        }

        parts.Add($"Beds24 {booking.Id}");
        if (booking.Price is { } price)
        {
            parts.Add(localizer["Price: {0}", price.ToString("0.00", CultureInfo.InvariantCulture)]);
        }

        if (booking.Commission is { } commission and not 0)
        {
            parts.Add(localizer["Commission: {0}", commission.ToString("0.00", CultureInfo.InvariantCulture)]);
        }

        if (!string.IsNullOrWhiteSpace(booking.Comments))
        {
            parts.Add(booking.Comments.Trim());
        }

        return Truncate(string.Join(" · ", parts), 500);
    }

    private async Task ApplyPropertyAsync(Guid hotelId, Beds24Property property, CancellationToken cancellationToken)
    {
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var connection = await dbContext.Beds24Connections.SingleAsync(item => item.HotelId == hotelId, cancellationToken);
            if (connection.PropertyId != property.Id)
            {
                await dbContext.Beds24RoomMappings.ExecuteDeleteAsync(cancellationToken);
                connection.LastSyncAtUtc = null;
            }

            connection.PropertyId = property.Id;
            connection.PropertyName = Truncate(PropertyName(property), 200);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await UpsertRoomTypesAsync(property, cancellationToken);
    }

    /// <summary>Adds new Beds24 room types (with a suggested category) and refreshes their names.</summary>
    private async Task UpsertRoomTypesAsync(Beds24Property property, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var categories = await dbContext.RoomCategories.AsNoTracking()
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
            .Select(category => new { category.Name, category.Code })
            .ToListAsync(cancellationToken);
        var mappings = await dbContext.Beds24RoomMappings.ToDictionaryAsync(mapping => mapping.Beds24RoomId, cancellationToken);

        foreach (var roomType in property.RoomTypes)
        {
            var name = Truncate(string.IsNullOrWhiteSpace(roomType.Name) ? $"#{roomType.Id}" : roomType.Name.Trim(), 200);
            if (mappings.TryGetValue(roomType.Id, out var mapping))
            {
                if (mapping.Beds24RoomName != name)
                {
                    mapping.Beds24RoomName = name;
                }

                continue;
            }

            var key = BookingComImportService.Normalize(name);
            var suggestion = categories.Count == 1
                ? categories[0].Name
                : (categories.FirstOrDefault(category => BookingComImportService.Normalize(category.Name) == key || BookingComImportService.Normalize(category.Code) == key)
                   ?? categories.FirstOrDefault(category => key.Contains(BookingComImportService.Normalize(category.Name))))?.Name;
            dbContext.Beds24RoomMappings.Add(new Beds24RoomMapping { Beds24RoomId = roomType.Id, Beds24RoomName = name, CategoryName = suggestion });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveStatusAsync(Guid hotelId, DateTimeOffset startedAt, Beds24SyncResult? result, string? error, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var connection = await dbContext.Beds24Connections.SingleOrDefaultAsync(item => item.HotelId == hotelId, CancellationToken.None);
        if (connection is null)
        {
            return;
        }

        connection.LastAttemptAtUtc = startedAt;
        connection.LastError = error is null ? null : Truncate(error, 1000);
        if (result is not null)
        {
            // Bookings with problems are read again by the next sync until they could be imported.
            if (result.Problems.Count == 0)
            {
                connection.LastSyncAtUtc = startedAt;
            }

            connection.LastCreated = result.Created;
            connection.LastUpdated = result.Updated;
            connection.LastCancelled = result.Cancelled;
            connection.LastProblems = result.Problems.Count == 0
                ? null
                : Truncate(string.Join('\n', result.Problems.Take(MaxProblems).Select(problem => problem.Replace('\n', ' '))), 4000);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Calls Beds24 with a (cached) access token; renews it once when Beds24 rejects it.</summary>
    private async Task<T> CallAsync<T>(Guid hotelId, Func<string, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            var token = coordinator.GetCachedToken(hotelId) ?? await RenewTokenAsync(hotelId, cancellationToken);
            try
            {
                return await call(token);
            }
            catch (Beds24ApiException exception) when (exception.IsUnauthorized)
            {
                return await call(await RenewTokenAsync(hotelId, cancellationToken));
            }
        }
        catch (Beds24ApiException exception)
        {
            throw new Beds24IntegrationException(exception.IsUnauthorized
                ? localizer["Beds24 rejected the access ({0}). Connect again with a new invite code.", exception.Message]
                : exception.Message);
        }
    }

    private async Task<string> RenewTokenAsync(Guid hotelId, CancellationToken cancellationToken)
    {
        coordinator.ForgetToken(hotelId);
        string refreshToken;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var protectedToken = await dbContext.Beds24Connections.AsNoTracking()
                .Where(item => item.HotelId == hotelId)
                .Select(item => item.ProtectedRefreshToken)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new Beds24IntegrationException(localizer["This hotel is not connected to Beds24."]);
            try
            {
                refreshToken = Protector.Unprotect(protectedToken);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new Beds24IntegrationException(localizer["The stored Beds24 access cannot be read anymore. Connect again with a new invite code."]);
            }
        }

        var accessToken = await client.GetAccessTokenAsync(refreshToken, cancellationToken);
        coordinator.CacheToken(hotelId, accessToken.Token, accessToken.ExpiresIn);
        return accessToken.Token;
    }

    private Guid RequireAdmin()
    {
        var hotelId = tenantContext.HotelId ?? throw new Beds24IntegrationException(localizer["No hotel selected."]);
        if (tenantContext.User?.IsInRole(HotelRoles.Admin) != true)
        {
            throw new Beds24IntegrationException(localizer["Only administrators can manage the Beds24 connection."]);
        }

        return hotelId;
    }

    private static string PropertyName(Beds24Property property) =>
        string.IsNullOrWhiteSpace(property.Name) ? $"#{property.Id}" : property.Name.Trim();

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private enum SyncAction
    {
        Create,
        Update,
        Cancel,
        Unchanged,
        Problem
    }

    private sealed record SyncRoom(Guid Id, string Number, string Type);

    private sealed record SyncUnit(
        string Reference,
        int GroupId,
        string GuestName,
        DateOnly CheckIn,
        DateOnly CheckOut,
        int Occupancy,
        bool IsCancelled,
        string? CategoryName,
        string RoomTypeName,
        string? UnitName,
        string? BookingComReference,
        string Notes,
        ChannelGuestData Guest)
    {
        public string Label => $"{GuestName} ({CheckIn:yyyy-MM-dd}, Beds24 {Reference})";
    }
}
