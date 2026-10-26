using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.RoomPlanner;

public sealed class RateAvailabilityService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<RateAvailabilityService> localizer) : IRateAvailabilityService
{
    private const string RateCurrency = "EUR";
    private static readonly decimal[] AllowedVatRates = [0m, 10m, 13m, 20m];

    public async Task<RoomRateAvailabilitySnapshot> GetSnapshotAsync(DateOnly startDate, int days, CancellationToken cancellationToken)
    {
        var normalizedDays = Math.Clamp(days, 1, 92);
        var endDate = startDate.AddDays(normalizedDays);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rateRules = await dbContext.RoomRateRules
            .AsNoTracking()
            .Where(rule => rule.StartDate < endDate && rule.EndDate > startDate)
            .OrderBy(rule => rule.RoomType)
            .ThenBy(rule => rule.StartDate)
            .ThenByDescending(rule => rule.Priority)
            .Select(rule => MapRateRule(rule))
            .ToListAsync(cancellationToken);

        var availabilityRules = await dbContext.RoomAvailabilityRules
            .AsNoTracking()
            .Include(rule => rule.Room)
            .Where(rule => rule.StartDate < endDate && rule.EndDate > startDate)
            .OrderBy(rule => rule.StartDate)
            .ThenBy(rule => rule.RoomType)
            .ThenBy(rule => rule.RoomId)
            .Select(rule => MapAvailabilityRule(rule))
            .ToListAsync(cancellationToken);

        return new RoomRateAvailabilitySnapshot(rateRules, availabilityRules);
    }

    public async Task<RoomRateRuleDto> CreateRateRuleAsync(RoomRateRuleUpsertRequest request, CancellationToken cancellationToken)
    {
        ValidateRateRuleRequest(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureCategoryExistsAsync(dbContext, request.RoomType.Trim(), cancellationToken);
        var rateRule = new RoomRateRule
        {
            Id = Guid.NewGuid(),
            RoomType = request.RoomType.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            OccupancyFrom = request.OccupancyFrom,
            OccupancyTo = request.OccupancyTo,
            NightlyNetAmount = RoundCurrency(request.NightlyNetAmount),
            VatRatePercent = request.VatRatePercent,
            Currency = RateCurrency,
            Priority = request.Priority,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.RoomRateRules.Add(rateRule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapRateRule(rateRule);
    }

    public async Task<RoomAvailabilityRuleDto> CreateAvailabilityRuleAsync(RoomAvailabilityRuleUpsertRequest request, CancellationToken cancellationToken)
    {
        ValidateAvailabilityRuleRequest(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        Room? room = null;
        if (request.RoomId is not null)
        {
            room = await dbContext.Rooms
                .AsNoTracking()
                .SingleOrDefaultAsync(existingRoom => existingRoom.Id == request.RoomId.Value, cancellationToken)
                ?? throw new ReservationValidationException(localizer["Selected room does not exist."]);
        }

        var roomType = string.IsNullOrWhiteSpace(request.RoomType) ? null : request.RoomType.Trim();
        if (request.RoomId is null && roomType is not null)
        {
            await EnsureCategoryExistsAsync(dbContext, roomType, cancellationToken);
        }

        if (request.RoomId is not null && roomType is null)
        {
            roomType = room?.Type;
        }

        var rule = new RoomAvailabilityRule
        {
            Id = Guid.NewGuid(),
            RoomId = request.RoomId,
            RoomType = roomType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            StopSell = request.StopSell,
            ClosedToArrival = request.ClosedToArrival,
            ClosedToDeparture = request.ClosedToDeparture,
            MinStayNights = request.MinStayNights,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.RoomAvailabilityRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RoomAvailabilityRuleDto(
            rule.Id,
            rule.RoomId,
            room?.Number,
            rule.RoomType,
            rule.StartDate,
            rule.EndDate,
            rule.StopSell,
            rule.ClosedToArrival,
            rule.ClosedToDeparture,
            rule.MinStayNights);
    }

    public async Task<RoomAvailabilityEvaluation> EvaluateAvailabilityAsync(
        Guid roomId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken)
    {
        ValidateStayRequest(checkIn, checkOut);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var room = await dbContext.Rooms
            .AsNoTracking()
            .SingleOrDefaultAsync(existingRoom => existingRoom.Id == roomId, cancellationToken)
            ?? throw new ReservationValidationException(localizer["Selected room does not exist."]);

        return await EvaluateAvailabilityInternalAsync(dbContext, room, checkIn, checkOut, cancellationToken);
    }

    public async Task<RoomRateQuote> QuoteAsync(RoomRateQuoteRequest request, CancellationToken cancellationToken)
    {
        ValidateQuoteRequest(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var room = await dbContext.Rooms
            .AsNoTracking()
            .SingleOrDefaultAsync(existingRoom => existingRoom.Id == request.RoomId, cancellationToken)
            ?? throw new ReservationValidationException(localizer["Selected room does not exist."]);

        var availability = await EvaluateAvailabilityInternalAsync(dbContext, room, request.CheckIn, request.CheckOut, cancellationToken);
        if (!availability.IsAvailable)
        {
            return CreateUnquoteableResult(request, room, availability.Message);
        }

        var rateRules = await dbContext.RoomRateRules
            .AsNoTracking()
            .Where(rule =>
                rule.RoomType == room.Type &&
                rule.StartDate < request.CheckOut &&
                rule.EndDate > request.CheckIn &&
                rule.OccupancyFrom <= request.Occupancy &&
                (rule.OccupancyTo == null || rule.OccupancyTo >= request.Occupancy))
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.StartDate)
            .ThenByDescending(rule => rule.OccupancyFrom)
            .ToListAsync(cancellationToken);

        var nightlyBreakdown = new List<RoomRateQuoteNight>();
        var nights = request.CheckOut.DayNumber - request.CheckIn.DayNumber;

        for (var offset = 0; offset < nights; offset++)
        {
            var date = request.CheckIn.AddDays(offset);
            var applicableRule = SelectApplicableRateRule(rateRules, date);
            if (applicableRule is null)
            {
                return CreateUnquoteableResult(
                    request,
                    room,
                    localizer["No room rate is configured for {0}.", date.ToString("dd.MM.yyyy")]);
            }

            var netAmount = RoundCurrency(applicableRule.NightlyNetAmount);
            var vatAmount = RoundCurrency(netAmount * applicableRule.VatRatePercent / 100m);
            var grossAmount = RoundCurrency(netAmount + vatAmount);

            nightlyBreakdown.Add(new RoomRateQuoteNight(
                date,
                netAmount,
                applicableRule.VatRatePercent,
                vatAmount,
                grossAmount));
        }

        var totalNet = RoundCurrency(nightlyBreakdown.Sum(item => item.NetAmount));
        var totalVat = RoundCurrency(nightlyBreakdown.Sum(item => item.VatAmount));
        var totalGross = RoundCurrency(nightlyBreakdown.Sum(item => item.GrossAmount));

        return new RoomRateQuote(
            room.Id,
            room.Number,
            room.Type,
            request.CheckIn,
            request.CheckOut,
            request.Occupancy,
            nights,
            totalNet,
            totalVat,
            totalGross,
            RateCurrency,
            true,
            null,
            nightlyBreakdown);
    }

    private async Task EnsureCategoryExistsAsync(HotelDbContext dbContext, string roomType, CancellationToken cancellationToken)
    {
        if (!await dbContext.RoomCategories.AnyAsync(category => category.Name == roomType, cancellationToken))
        {
            throw new ReservationValidationException(localizer["Room category {0} does not exist.", roomType]);
        }
    }

    private async Task<RoomAvailabilityEvaluation> EvaluateAvailabilityInternalAsync(
        HotelDbContext dbContext,
        Room room,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken)
    {
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var rules = await dbContext.RoomAvailabilityRules
            .AsNoTracking()
            .Where(rule =>
                rule.StartDate < checkOut &&
                rule.EndDate > checkIn &&
                (rule.RoomId == room.Id || (rule.RoomId == null && rule.RoomType == room.Type)))
            .ToListAsync(cancellationToken);

        if (rules.Any(rule => rule.StopSell))
        {
            return new RoomAvailabilityEvaluation(false, localizer["This room is closed for sale in the selected date range."], null);
        }

        if (rules.Any(rule => rule.ClosedToArrival && checkIn >= rule.StartDate && checkIn < rule.EndDate))
        {
            return new RoomAvailabilityEvaluation(false, localizer["Arrival is closed for the selected check-in date."], null);
        }

        if (rules.Any(rule => rule.ClosedToDeparture && checkOut > rule.StartDate && checkOut <= rule.EndDate))
        {
            return new RoomAvailabilityEvaluation(false, localizer["Departure is closed for the selected check-out date."], null);
        }

        var minimumStay = rules
            .Where(rule => rule.MinStayNights.HasValue)
            .Select(rule => rule.MinStayNights!.Value)
            .DefaultIfEmpty(0)
            .Max();

        if (minimumStay > 0 && nights < minimumStay)
        {
            return new RoomAvailabilityEvaluation(
                false,
                localizer["Minimum stay is {0} nights for the selected period.", minimumStay],
                minimumStay);
        }

        return new RoomAvailabilityEvaluation(true, null, minimumStay > 0 ? minimumStay : null);
    }

    private static RoomRateRule? SelectApplicableRateRule(IEnumerable<RoomRateRule> rules, DateOnly date)
    {
        return rules.FirstOrDefault(rule => rule.StartDate <= date && rule.EndDate > date);
    }

    private void ValidateRateRuleRequest(RoomRateRuleUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RoomType))
        {
            throw new ReservationValidationException(localizer["Room type is required for rate rules."]);
        }

        if (request.RoomType.Trim().Length > 40)
        {
            throw new ReservationValidationException(localizer["Room type cannot exceed 40 characters."]);
        }

        if (request.EndDate <= request.StartDate)
        {
            throw new ReservationValidationException(localizer["Rate rule end date must be after start date."]);
        }

        if (request.OccupancyFrom <= 0)
        {
            throw new ReservationValidationException(localizer["Minimum occupancy must be greater than 0."]);
        }

        if (request.OccupancyTo is not null && request.OccupancyTo < request.OccupancyFrom)
        {
            throw new ReservationValidationException(localizer["Maximum occupancy must be greater than or equal to minimum occupancy."]);
        }

        if (request.NightlyNetAmount < 0)
        {
            throw new ReservationValidationException(localizer["Nightly net rate cannot be negative."]);
        }

        if (!AllowedVatRates.Contains(request.VatRatePercent))
        {
            throw new ReservationValidationException(localizer["VAT rate must be one of the supported Austrian rates (0, 10, 13, 20)."]);
        }
    }

    private void ValidateAvailabilityRuleRequest(RoomAvailabilityRuleUpsertRequest request)
    {
        if (request.EndDate <= request.StartDate)
        {
            throw new ReservationValidationException(localizer["Availability rule end date must be after start date."]);
        }

        if (request.RoomId is null && string.IsNullOrWhiteSpace(request.RoomType))
        {
            throw new ReservationValidationException(localizer["Select either a room or room type for an availability rule."]);
        }

        if (!string.IsNullOrWhiteSpace(request.RoomType) && request.RoomType.Trim().Length > 40)
        {
            throw new ReservationValidationException(localizer["Room type cannot exceed 40 characters."]);
        }

        if (request.MinStayNights is not null && request.MinStayNights <= 0)
        {
            throw new ReservationValidationException(localizer["Minimum stay must be greater than 0 nights when specified."]);
        }

        if (!request.StopSell && !request.ClosedToArrival && !request.ClosedToDeparture && request.MinStayNights is null)
        {
            throw new ReservationValidationException(localizer["Select at least one availability constraint."]);
        }
    }

    private void ValidateQuoteRequest(RoomRateQuoteRequest request)
    {
        ValidateStayRequest(request.CheckIn, request.CheckOut);
        if (request.RoomId == Guid.Empty)
        {
            throw new ReservationValidationException(localizer["Room is required."]);
        }

        if (request.Occupancy <= 0)
        {
            throw new ReservationValidationException(localizer["Occupancy must be greater than 0."]);
        }
    }

    private void ValidateStayRequest(DateOnly checkIn, DateOnly checkOut)
    {
        if (checkOut <= checkIn)
        {
            throw new ReservationValidationException(localizer["Check-out must be after check-in."]);
        }
    }

    private static RoomRateRuleDto MapRateRule(RoomRateRule rule)
    {
        return new RoomRateRuleDto(
            rule.Id,
            rule.RoomType,
            rule.StartDate,
            rule.EndDate,
            rule.OccupancyFrom,
            rule.OccupancyTo,
            rule.NightlyNetAmount,
            rule.VatRatePercent,
            rule.Currency,
            rule.Priority);
    }

    private static RoomAvailabilityRuleDto MapAvailabilityRule(RoomAvailabilityRule rule)
    {
        return new RoomAvailabilityRuleDto(
            rule.Id,
            rule.RoomId,
            rule.Room?.Number,
            rule.RoomType,
            rule.StartDate,
            rule.EndDate,
            rule.StopSell,
            rule.ClosedToArrival,
            rule.ClosedToDeparture,
            rule.MinStayNights);
    }

    private static RoomRateQuote CreateUnquoteableResult(RoomRateQuoteRequest request, Room room, string? message)
    {
        return new RoomRateQuote(
            room.Id,
            room.Number,
            room.Type,
            request.CheckIn,
            request.CheckOut,
            request.Occupancy,
            request.CheckOut.DayNumber - request.CheckIn.DayNumber,
            0m,
            0m,
            0m,
            RateCurrency,
            false,
            message,
            []);
    }

    private static decimal RoundCurrency(decimal amount)
    {
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
