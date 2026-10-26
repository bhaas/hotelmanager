using System.Data;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.RoomPlanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Rooms;

public sealed partial class RoomInventoryService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<RoomInventoryService> localizer) : IRoomInventoryService
{
    public const int MaxSeriesSize = 100;
    public const int MaxOccupancyLimit = 20;
    private const int MinFloor = -5;
    private const int MaxFloor = 200;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-./]*$")]
    private static partial Regex RoomNumberPattern();

    [GeneratedRegex("^[A-Za-z0-9]*$")]
    private static partial Regex AlphanumericPattern();

    // ----- Rooms -----

    public async Task<IReadOnlyList<RoomInventoryItem>> GetRoomsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ProjectRooms(dbContext, dbContext.Rooms)
            .ToListAsync(cancellationToken);
    }

    public async Task<RoomInventoryItem> CreateRoomAsync(RoomUpsertRequest request, CancellationToken cancellationToken)
    {
        var number = ValidateRoomNumber(request.Number);
        var details = ValidateRoomDetails(request.Category, request.Floor, request.Features, request.Notes, request.HousekeepingStatus);

        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var category = await GetCategoryByNameAsync(dbContext, details.Category, cancellationToken);
            await EnsureRoomNumbersAvailableAsync(dbContext, [number], excludedRoomId: null, cancellationToken);

            var room = new Room
            {
                Id = Guid.NewGuid(),
                Number = number,
                Type = category.Name,
                Floor = request.Floor,
                Features = details.Features,
                Notes = details.Notes,
                HousekeepingStatus = request.HousekeepingStatus,
                IsActive = true
            };

            dbContext.Rooms.Add(room);
            await dbContext.SaveChangesAsync(cancellationToken);
            return await GetRoomItemAsync(dbContext, room.Id, cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<RoomInventoryItem>> CreateRoomSeriesAsync(RoomSeriesRequest request, CancellationToken cancellationToken)
    {
        var numbers = BuildSeriesNumbers(request);
        var details = ValidateRoomDetails(request.Category, request.Floor, request.Features, notes: null, HousekeepingStatus.Clean);

        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var category = await GetCategoryByNameAsync(dbContext, details.Category, cancellationToken);
            await EnsureRoomNumbersAvailableAsync(dbContext, numbers, excludedRoomId: null, cancellationToken);

            var rooms = numbers
                .Select(number => new Room
                {
                    Id = Guid.NewGuid(),
                    Number = number,
                    Type = category.Name,
                    Floor = request.Floor,
                    Features = details.Features,
                    IsActive = true
                })
                .ToList();

            dbContext.Rooms.AddRange(rooms);
            await dbContext.SaveChangesAsync(cancellationToken);

            var ids = rooms.Select(room => room.Id).ToList();
            return (IReadOnlyList<RoomInventoryItem>)await ProjectRooms(dbContext, dbContext.Rooms.Where(room => ids.Contains(room.Id)))
                .ToListAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<RoomInventoryItem> UpdateRoomAsync(Guid roomId, RoomUpsertRequest request, CancellationToken cancellationToken)
    {
        var number = ValidateRoomNumber(request.Number);
        var details = ValidateRoomDetails(request.Category, request.Floor, request.Features, request.Notes, request.HousekeepingStatus);

        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var room = await FindRoomAsync(dbContext, roomId, cancellationToken);
            var category = await GetCategoryByNameAsync(dbContext, details.Category, cancellationToken);
            await EnsureRoomNumbersAvailableAsync(dbContext, [number], room.Id, cancellationToken);

            if (!string.Equals(room.Type, category.Name, StringComparison.Ordinal))
            {
                await EnsureUpcomingOccupancyFitsAsync(
                    dbContext,
                    reservation => reservation.RoomId == room.Id,
                    category.MaxOccupancy,
                    cancellationToken);
            }

            room.Number = number;
            room.Type = category.Name;
            room.Floor = request.Floor;
            room.Features = details.Features;
            room.Notes = details.Notes;
            room.HousekeepingStatus = request.HousekeepingStatus;

            await dbContext.SaveChangesAsync(cancellationToken);
            return await GetRoomItemAsync(dbContext, room.Id, cancellationToken);
        }, cancellationToken);
    }

    public async Task<RoomInventoryItem> SetRoomActiveAsync(Guid roomId, bool isActive, CancellationToken cancellationToken)
    {
        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var room = await FindRoomAsync(dbContext, roomId, cancellationToken);
            if (room.IsActive && !isActive)
            {
                var today = Today;
                var upcoming = await dbContext.Reservations.CountAsync(
                    reservation => reservation.RoomId == room.Id &&
                                   reservation.Status != ReservationStatus.CheckedOut &&
                                   reservation.Status != ReservationStatus.Cancelled &&
                                   reservation.CheckOut > today,
                    cancellationToken);

                if (upcoming > 0)
                {
                    throw new ReservationConflictException(localizer[
                        "Room {0} has {1} current or upcoming reservation(s). Move them to another room before archiving.",
                        room.Number,
                        upcoming]);
                }
            }

            room.IsActive = isActive;
            await dbContext.SaveChangesAsync(cancellationToken);
            return await GetRoomItemAsync(dbContext, room.Id, cancellationToken);
        }, cancellationToken);
    }

    public async Task DeleteRoomAsync(Guid roomId, CancellationToken cancellationToken)
    {
        await ExecuteInTransactionAsync(async dbContext =>
        {
            var room = await FindRoomAsync(dbContext, roomId, cancellationToken);

            // Reservations carry folios and invoices that must be retained, so rooms with history are archived instead.
            var hasHistory = await dbContext.Reservations.AnyAsync(reservation => reservation.RoomId == room.Id, cancellationToken);
            if (hasHistory)
            {
                throw new ReservationConflictException(localizer[
                    "Room {0} has reservation history and cannot be deleted. Archive it instead.",
                    room.Number]);
            }

            dbContext.Rooms.Remove(room);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    // ----- Categories -----

    public async Task<IReadOnlyList<RoomCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ProjectCategories(dbContext, dbContext.RoomCategories)
            .ToListAsync(cancellationToken);
    }

    public async Task<RoomCategoryItem> CreateCategoryAsync(RoomCategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var normalized = ValidateCategory(request);

        return await ExecuteInTransactionAsync(async dbContext =>
        {
            await EnsureCategoryUniqueAsync(dbContext, normalized.Code, normalized.Name, excludedCategoryId: null, cancellationToken);

            var category = new RoomCategory
            {
                Id = Guid.NewGuid(),
                Code = normalized.Code,
                Name = normalized.Name,
                Description = normalized.Description,
                StandardOccupancy = request.StandardOccupancy,
                MaxOccupancy = request.MaxOccupancy,
                SortOrder = request.SortOrder
            };

            dbContext.RoomCategories.Add(category);
            await dbContext.SaveChangesAsync(cancellationToken);
            return await GetCategoryItemAsync(dbContext, category.Id, cancellationToken);
        }, cancellationToken);
    }

    public async Task<RoomCategoryItem> UpdateCategoryAsync(Guid categoryId, RoomCategoryUpsertRequest request, CancellationToken cancellationToken)
    {
        var normalized = ValidateCategory(request);

        return await ExecuteInTransactionAsync(async dbContext =>
        {
            var category = await FindCategoryAsync(dbContext, categoryId, cancellationToken);
            await EnsureCategoryUniqueAsync(dbContext, normalized.Code, normalized.Name, category.Id, cancellationToken);

            var oldName = category.Name;
            if (request.MaxOccupancy < category.MaxOccupancy)
            {
                await EnsureUpcomingOccupancyFitsAsync(
                    dbContext,
                    reservation => reservation.Room.Type == oldName,
                    request.MaxOccupancy,
                    cancellationToken);
            }

            if (!string.Equals(oldName, normalized.Name, StringComparison.Ordinal))
            {
                // Rooms and rules reference the category by name; keep them consistent on rename.
                var newName = normalized.Name;
                await dbContext.Rooms
                    .Where(room => room.Type == oldName)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(room => room.Type, newName), cancellationToken);
                await dbContext.RoomRateRules
                    .Where(rule => rule.RoomType == oldName)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(rule => rule.RoomType, newName), cancellationToken);
                await dbContext.RoomAvailabilityRules
                    .Where(rule => rule.RoomType == oldName)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(rule => rule.RoomType, newName), cancellationToken);
            }

            category.Code = normalized.Code;
            category.Name = normalized.Name;
            category.Description = normalized.Description;
            category.StandardOccupancy = request.StandardOccupancy;
            category.MaxOccupancy = request.MaxOccupancy;
            category.SortOrder = request.SortOrder;

            await dbContext.SaveChangesAsync(cancellationToken);
            return await GetCategoryItemAsync(dbContext, category.Id, cancellationToken);
        }, cancellationToken);
    }

    public async Task DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        await ExecuteInTransactionAsync(async dbContext =>
        {
            var category = await FindCategoryAsync(dbContext, categoryId, cancellationToken);
            var name = category.Name;
            var roomCount = await dbContext.Rooms.CountAsync(room => room.Type == name, cancellationToken);
            if (roomCount > 0)
            {
                throw new ReservationConflictException(localizer[
                    "Category {0} is still assigned to {1} room(s). Reassign or delete these rooms first.",
                    name,
                    roomCount]);
            }

            await dbContext.RoomRateRules
                .Where(rule => rule.RoomType == name)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.RoomAvailabilityRules
                .Where(rule => rule.RoomId == null && rule.RoomType == name)
                .ExecuteDeleteAsync(cancellationToken);

            dbContext.RoomCategories.Remove(category);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    // ----- Queries -----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static IQueryable<RoomInventoryItem> ProjectRooms(HotelDbContext dbContext, IQueryable<Room> rooms)
    {
        var today = Today;
        return rooms
            .AsNoTracking()
            .OrderBy(room => room.Floor)
            .ThenBy(room => room.Number)
            .Select(room => new RoomInventoryItem(
                room.Id,
                room.Number,
                room.Type,
                room.Floor,
                room.Features,
                room.Notes,
                room.IsActive,
                room.HousekeepingStatus,
                dbContext.RoomCategories
                    .Where(category => category.Name == room.Type)
                    .Select(category => category.MaxOccupancy)
                    .FirstOrDefault(),
                room.Reservations.Count(reservation =>
                    reservation.Status != ReservationStatus.CheckedOut &&
                    reservation.Status != ReservationStatus.Cancelled &&
                    reservation.CheckOut > today),
                room.Reservations.Count()));
    }

    private static IQueryable<RoomCategoryItem> ProjectCategories(HotelDbContext dbContext, IQueryable<RoomCategory> categories)
    {
        var today = Today;
        return categories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new RoomCategoryItem(
                category.Id,
                category.Code,
                category.Name,
                category.Description,
                category.StandardOccupancy,
                category.MaxOccupancy,
                category.SortOrder,
                dbContext.Rooms.Count(room => room.Type == category.Name),
                dbContext.Rooms.Count(room => room.Type == category.Name && room.IsActive),
                dbContext.RoomRateRules.Count(rule => rule.RoomType == category.Name && rule.EndDate > today)));
    }

    private static Task<RoomInventoryItem> GetRoomItemAsync(HotelDbContext dbContext, Guid roomId, CancellationToken cancellationToken)
    {
        return ProjectRooms(dbContext, dbContext.Rooms.Where(room => room.Id == roomId)).SingleAsync(cancellationToken);
    }

    private static Task<RoomCategoryItem> GetCategoryItemAsync(HotelDbContext dbContext, Guid categoryId, CancellationToken cancellationToken)
    {
        return ProjectCategories(dbContext, dbContext.RoomCategories.Where(category => category.Id == categoryId)).SingleAsync(cancellationToken);
    }

    private async Task<Room> FindRoomAsync(HotelDbContext dbContext, Guid roomId, CancellationToken cancellationToken)
    {
        return await dbContext.Rooms.SingleOrDefaultAsync(room => room.Id == roomId, cancellationToken)
            ?? throw new ReservationNotFoundException(localizer["Room not found."]);
    }

    private async Task<RoomCategory> FindCategoryAsync(HotelDbContext dbContext, Guid categoryId, CancellationToken cancellationToken)
    {
        return await dbContext.RoomCategories.SingleOrDefaultAsync(category => category.Id == categoryId, cancellationToken)
            ?? throw new ReservationNotFoundException(localizer["Room category not found."]);
    }

    private async Task<RoomCategory> GetCategoryByNameAsync(HotelDbContext dbContext, string name, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        return await dbContext.RoomCategories
                   .AsNoTracking()
                   .SingleOrDefaultAsync(category => category.Name.ToLower() == lowered, cancellationToken)
               ?? throw new ReservationValidationException(localizer["Room category {0} does not exist.", name]);
    }

    private async Task EnsureRoomNumbersAvailableAsync(
        HotelDbContext dbContext,
        IReadOnlyCollection<string> numbers,
        Guid? excludedRoomId,
        CancellationToken cancellationToken)
    {
        var lowered = numbers.Select(number => number.ToLowerInvariant()).ToList();
        var taken = await dbContext.Rooms
            .Where(room => lowered.Contains(room.Number.ToLower()) && (excludedRoomId == null || room.Id != excludedRoomId.Value))
            .OrderBy(room => room.Number)
            .Select(room => room.Number)
            .ToListAsync(cancellationToken);

        if (taken.Count > 0)
        {
            throw new ReservationConflictException(localizer["Room number(s) already in use: {0}.", string.Join(", ", taken)]);
        }
    }

    private async Task EnsureCategoryUniqueAsync(
        HotelDbContext dbContext,
        string code,
        string name,
        Guid? excludedCategoryId,
        CancellationToken cancellationToken)
    {
        var loweredCode = code.ToLowerInvariant();
        var loweredName = name.ToLowerInvariant();
        var others = dbContext.RoomCategories.Where(category => excludedCategoryId == null || category.Id != excludedCategoryId.Value);

        if (await others.AnyAsync(category => category.Code.ToLower() == loweredCode, cancellationToken))
        {
            throw new ReservationConflictException(localizer["Category code {0} is already in use.", code]);
        }

        if (await others.AnyAsync(category => category.Name.ToLower() == loweredName, cancellationToken))
        {
            throw new ReservationConflictException(localizer["Category name {0} is already in use.", name]);
        }
    }

    private async Task EnsureUpcomingOccupancyFitsAsync(
        HotelDbContext dbContext,
        Expression<Func<Reservation, bool>> scope,
        int maxOccupancy,
        CancellationToken cancellationToken)
    {
        var today = Today;
        var exceeding = await dbContext.Reservations
            .Where(scope)
            .CountAsync(reservation =>
                reservation.Status != ReservationStatus.CheckedOut &&
                reservation.Status != ReservationStatus.Cancelled &&
                reservation.Status != ReservationStatus.Blocked &&
                reservation.Status != ReservationStatus.Maintenance &&
                reservation.CheckOut > today &&
                reservation.Occupancy > maxOccupancy,
                cancellationToken);

        if (exceeding > 0)
        {
            throw new ReservationConflictException(localizer[
                "{0} current or upcoming reservation(s) exceed the maximum occupancy of {1} guests.",
                exceeding,
                maxOccupancy]);
        }
    }

    // ----- Validation -----

    private string ValidateRoomNumber(string? number)
    {
        var trimmed = number?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ReservationValidationException(localizer["Room number is required."]);
        }

        if (trimmed.Length > 12 || !RoomNumberPattern().IsMatch(trimmed))
        {
            throw new ReservationValidationException(localizer["Room number may contain up to 12 letters, digits, '-', '.' or '/'."]);
        }

        return trimmed;
    }

    private (string Category, string? Features, string? Notes) ValidateRoomDetails(
        string? category,
        int floor,
        string? features,
        string? notes,
        HousekeepingStatus housekeepingStatus)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ReservationValidationException(localizer["Room category is required."]);
        }

        if (floor is < MinFloor or > MaxFloor)
        {
            throw new ReservationValidationException(localizer["Floor must be between {0} and {1}.", MinFloor, MaxFloor]);
        }

        if (!Enum.IsDefined(housekeepingStatus))
        {
            throw new ReservationValidationException(localizer["Unknown housekeeping status."]);
        }

        var normalizedFeatures = NormalizeFeatures(features);
        if (normalizedFeatures?.Length > 300)
        {
            throw new ReservationValidationException(localizer["Features cannot exceed 300 characters."]);
        }

        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalizedNotes?.Length > 500)
        {
            throw new ReservationValidationException(localizer["Notes cannot exceed 500 characters."]);
        }

        return (category.Trim(), normalizedFeatures, normalizedNotes);
    }

    private List<string> BuildSeriesNumbers(RoomSeriesRequest request)
    {
        var prefix = request.Prefix?.Trim() ?? string.Empty;
        if (prefix.Length > 4 || !AlphanumericPattern().IsMatch(prefix))
        {
            throw new ReservationValidationException(localizer["Prefix may contain up to 4 letters or digits."]);
        }

        if (request.FromNumber < 0 || request.ToNumber < request.FromNumber)
        {
            throw new ReservationValidationException(localizer["The last room number must be greater than or equal to the first."]);
        }

        var count = request.ToNumber - request.FromNumber + 1;
        if (count > MaxSeriesSize)
        {
            throw new ReservationValidationException(localizer["A series can contain at most {0} rooms.", MaxSeriesSize]);
        }

        var numbers = Enumerable.Range(request.FromNumber, count)
            .Select(value => prefix + value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        if (numbers.Any(number => number.Length > 12))
        {
            throw new ReservationValidationException(localizer["Room number may contain up to 12 letters, digits, '-', '.' or '/'."]);
        }

        return numbers;
    }

    private (string Code, string Name, string? Description) ValidateCategory(RoomCategoryUpsertRequest request)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length is 0 or > 10 || !AlphanumericPattern().IsMatch(code))
        {
            throw new ReservationValidationException(localizer["Category code must have 1 to 10 letters or digits."]);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new ReservationValidationException(localizer["Category name is required."]);
        }

        if (name.Length > 40)
        {
            throw new ReservationValidationException(localizer["Category name cannot exceed 40 characters."]);
        }

        if (request.StandardOccupancy < 1 || request.MaxOccupancy < request.StandardOccupancy || request.MaxOccupancy > MaxOccupancyLimit)
        {
            throw new ReservationValidationException(localizer[
                "Occupancy must satisfy 1 ≤ standard ≤ maximum ≤ {0}.",
                MaxOccupancyLimit]);
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > 500)
        {
            throw new ReservationValidationException(localizer["Description cannot exceed 500 characters."]);
        }

        return (code, name, description);
    }

    public static string? NormalizeFeatures(string? features)
    {
        if (string.IsNullOrWhiteSpace(features))
        {
            return null;
        }

        var tags = features
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tags.Count == 0 ? null : string.Join(", ", tags);
    }

    public static IReadOnlyList<string> SplitFeatures(string? features)
    {
        return string.IsNullOrWhiteSpace(features)
            ? []
            : features.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<T> ExecuteInTransactionAsync<T>(
        Func<HotelDbContext, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var result = await operation(dbContext);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
