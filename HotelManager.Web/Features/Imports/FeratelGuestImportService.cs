using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HotelManager.Web.Features.Imports;

/// <summary>
/// Imports guest profiles from the detailed guest export of the feratel Deskline registration system (Meldewesen), which
/// tourism boards such as TVB Stubai Tirol use. The export has one row per registered guest and no stay dates.
/// </summary>
public sealed class FeratelGuestImportService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<FeratelGuestImportService> localizer) : IFeratelGuestImportService
{
    private enum Column
    {
        FirstName,
        LastName,
        Street,
        Street2,
        Nationality,
        PostalCode,
        City,
        DateOfBirth,
        TravelDocument,
        Country,
        Phone,
        Email,
        Company,
        Marketing,
        TravelPurpose,
        ReferralSource,
        ArrivalMode,
        LicensePlate,
        Interests,
        Language
    }

    /// <summary>German and English export headers; compared after <see cref="BookingComImportService.Normalize"/>.</summary>
    private static readonly (Column Column, string[] Aliases)[] ColumnAliases =
    [
        (Column.FirstName, ["Vorname", "First name"]),
        (Column.LastName, ["Nachname", "Last name", "Surname"]),
        (Column.Street, ["Straße", "Strasse", "Street"]),
        (Column.Street2, ["Straße 2", "Strasse 2", "Street 2"]),
        (Column.Nationality, ["Nationalität", "Nationality"]),
        (Column.PostalCode, ["PLZ", "Zip", "Postal code"]),
        (Column.City, ["Stadt/Ort", "Ort", "City"]),
        (Column.DateOfBirth, ["Geburtsdatum", "Date of birth", "Birthdate"]),
        (Column.Country, ["Land", "Country"]),
        (Column.Phone, ["Telefon", "Phone"]),
        (Column.Email, ["E-Mail", "Email"]),
        (Column.Company, ["Firma", "Company"]),
        (Column.Marketing, ["Nutzung Marketing", "Marketing"]),
        (Column.TravelPurpose, ["Reisemotiv", "Travel motive", "Travel purpose"]),
        (Column.ReferralSource, ["Empfehlungsart", "Referral"]),
        (Column.ArrivalMode, ["Anreisetyp", "Arrival type"]),
        (Column.LicensePlate, ["Kfz-Kennzeichen", "License plate", "Licence plate"]),
        (Column.Interests, ["Gast-Interessen", "Guest interests", "Interests"]),
        (Column.Language, ["Sprache", "Language"])
    ];

    /// <summary>The document header is long ("Reisedokument Nr. / Ausstell.Datum / Behörde"), so it is matched by prefix.</summary>
    private static readonly string[] TravelDocumentPrefixes = ["reisedokument", "traveldocument"];

    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.Ordinal)
    {
        ["deutsch"] = "de", ["german"] = "de",
        ["englisch"] = "en", ["english"] = "en",
        ["italienisch"] = "it", ["italian"] = "it",
        ["franzosisch"] = "fr", ["french"] = "fr",
        ["niederlandisch"] = "nl", ["hollandisch"] = "nl", ["dutch"] = "nl",
        ["tschechisch"] = "cs", ["czech"] = "cs",
        ["polnisch"] = "pl", ["polish"] = "pl",
        ["ungarisch"] = "hu", ["hungarian"] = "hu",
        ["spanisch"] = "es", ["spanish"] = "es",
        ["danisch"] = "da", ["danish"] = "da",
        ["schwedisch"] = "sv", ["swedish"] = "sv",
        ["slowakisch"] = "sk", ["slovak"] = "sk",
        ["slowenisch"] = "sl", ["slovenian"] = "sl",
        ["kroatisch"] = "hr", ["croatian"] = "hr",
        ["rumanisch"] = "ro", ["romanian"] = "ro",
        ["russisch"] = "ru", ["russian"] = "ru"
    };

    public Task<FeratelGuestImportResult> PreviewAsync(ImportTable table, CancellationToken cancellationToken = default) =>
        RunAsync(table, save: false, cancellationToken);

    public Task<FeratelGuestImportResult> ImportAsync(ImportTable table, CancellationToken cancellationToken = default) =>
        RunAsync(table, save: true, cancellationToken);

    private async Task<FeratelGuestImportResult> RunAsync(ImportTable table, bool save, CancellationToken cancellationToken)
    {
        var columns = DetectColumns(table.Headers);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.Guests
            .Where(guest => guest.AnonymizedAtUtc == null)
            .ToListAsync(cancellationToken);

        var byNameAndBirthDate = new Dictionary<string, Guest>(StringComparer.Ordinal);
        var byName = new Dictionary<string, List<Guest>>(StringComparer.Ordinal);
        foreach (var guest in existing)
        {
            Index(guest);
        }

        var originalValues = existing.ToDictionary(guest => guest.Id, Snapshot);
        var lastRowOfGuest = new Dictionary<Guid, int>();
        var parsedRows = new List<(int RowNumber, string Name, GuestData Data, Guest? Guest, bool IsNew, string? Message)>(table.Rows.Count);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var index = 0; index < table.Rows.Count; index++)
        {
            var rowNumber = index + 2;
            var data = Parse(table, table.Rows[index], columns, today);
            var displayName = string.Join(' ', new[] { data.FirstName, data.LastName }.Where(part => !string.IsNullOrEmpty(part)));
            if (data.LastName is null)
            {
                parsedRows.Add((rowNumber, displayName, data, null, false, localizer["The last name is missing."]));
                continue;
            }

            var guest = Find(data);
            var isNew = guest is null;
            if (guest is null)
            {
                guest = new Guest { Id = Guid.NewGuid(), LastName = data.LastName, CreatedAtUtc = now, UpdatedAtUtc = now };
                dbContext.Guests.Add(guest);
            }

            var hadBirthDate = guest.DateOfBirth is not null;
            Apply(guest, data);
            if (isNew || (!hadBirthDate && guest.DateOfBirth is not null))
            {
                Index(guest);
            }

            string? message = null;
            if (!isNew && lastRowOfGuest.TryGetValue(guest.Id, out var previousRow))
            {
                message = localizer["Same guest as row {0}.", previousRow];
            }
            else if (data.InvalidBirthDate)
            {
                message = localizer["The date of birth could not be read."];
            }

            lastRowOfGuest[guest.Id] = rowNumber;
            parsedRows.Add((rowNumber, displayName, data, guest, isNew, message));
        }

        // Several feratel rows can describe the same guest with different values, so changes are judged per guest
        // against the state before the import; otherwise every re-import would report the same rows again.
        var updatedGuests = new HashSet<Guid>();
        foreach (var guest in existing)
        {
            if (lastRowOfGuest.ContainsKey(guest.Id) && Snapshot(guest) != originalValues[guest.Id])
            {
                guest.UpdatedAtUtc = now;
                updatedGuests.Add(guest.Id);
            }
        }

        var rows = parsedRows.Select(row =>
        {
            var action = row.Guest is null ? FeratelGuestImportAction.Error
                : row.IsNew ? FeratelGuestImportAction.Create
                : updatedGuests.Contains(row.Guest.Id) && lastRowOfGuest[row.Guest.Id] == row.RowNumber ? FeratelGuestImportAction.Update
                : FeratelGuestImportAction.Unchanged;
            return new FeratelGuestImportRow(row.RowNumber, row.Name, row.Data.DateOfBirth, row.Data.City, action, row.Message);
        }).ToList();

        if (save)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new FeratelGuestImportResult(rows);

        void Index(Guest guest)
        {
            var nameKey = NameKey(guest.FirstName, guest.LastName);
            if (!byName.TryGetValue(nameKey, out var list))
            {
                byName[nameKey] = list = [];
            }

            if (!list.Contains(guest))
            {
                list.Add(guest);
            }

            if (guest.DateOfBirth is { } birthDate)
            {
                byNameAndBirthDate.TryAdd($"{nameKey}|{birthDate:yyyy-MM-dd}", guest);
            }
        }

        Guest? Find(GuestData data)
        {
            var nameKey = NameKey(data.FirstName, data.LastName!);
            if (data.DateOfBirth is { } birthDate)
            {
                if (byNameAndBirthDate.TryGetValue($"{nameKey}|{birthDate:yyyy-MM-dd}", out var exact))
                {
                    return exact;
                }

                // Profiles created by channel imports have no date of birth; take one over when the name is unambiguous.
                var withoutBirthDate = byName.GetValueOrDefault(nameKey)?.Where(guest => guest.DateOfBirth is null).ToList();
                return withoutBirthDate is { Count: 1 } ? withoutBirthDate[0] : null;
            }

            var sameName = byName.GetValueOrDefault(nameKey);
            return sameName is { Count: 1 } ? sameName[0] : null;
        }
    }

    private Dictionary<Column, int> DetectColumns(IReadOnlyList<string> headers)
    {
        var normalized = headers.Select(BookingComImportService.Normalize).ToList();
        var columns = new Dictionary<Column, int>();
        foreach (var (column, aliases) in ColumnAliases)
        {
            var index = aliases
                .Select(alias => normalized.IndexOf(BookingComImportService.Normalize(alias)))
                .FirstOrDefault(found => found >= 0, -1);
            if (index >= 0)
            {
                columns[column] = index;
            }
        }

        var document = normalized.FindIndex(header => TravelDocumentPrefixes.Any(header.StartsWith));
        if (document >= 0)
        {
            columns[Column.TravelDocument] = document;
        }

        if (!columns.ContainsKey(Column.LastName) || !columns.ContainsKey(Column.FirstName) || !columns.ContainsKey(Column.DateOfBirth))
        {
            throw new ImportFileException(localizer["This is not a feratel guest export. The columns \"Vorname\", \"Nachname\" and \"Geburtsdatum\" are required."]);
        }

        return columns;
    }

    private static GuestData Parse(ImportTable table, IReadOnlyList<string> row, Dictionary<Column, int> columns, DateOnly today)
    {
        string? Text(Column column, int maxLength)
        {
            var value = table.Cell(row, columns.TryGetValue(column, out var index) ? index : null);
            value = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return value.Length == 0 ? null : value.Length <= maxLength ? value : value[..maxLength];
        }

        var birthText = Text(Column.DateOfBirth, 40);
        var birthDate = BookingComImportService.ParseDate(birthText);
        if (birthDate is { } parsed && (parsed > today || parsed.Year < 1900))
        {
            birthDate = null;
        }

        var email = Text(Column.Email, 256);
        if (email is not null && (!email.Contains('@') || email.Contains(' ')))
        {
            email = null;
        }

        var street = string.Join(", ", new[] { Text(Column.Street, 200), Text(Column.Street2, 200) }.Where(part => part is not null));
        var language = Text(Column.Language, 60);
        var nationality = Text(Column.Nationality, 100);

        return new GuestData(
            Text(Column.FirstName, 80),
            Text(Column.LastName, 120),
            Truncate(street, 200),
            nationality is { Length: 2 } ? nationality.ToUpperInvariant() : nationality,
            Text(Column.PostalCode, 16),
            Text(Column.City, 100),
            birthDate,
            birthText is not null && birthDate is null,
            Text(Column.TravelDocument, 200),
            Text(Column.Country, 100),
            Text(Column.Phone, 40),
            email,
            Text(Column.Company, 200),
            ParseYesNo(Text(Column.Marketing, 20)),
            Text(Column.TravelPurpose, 100),
            Text(Column.ReferralSource, 100),
            Text(Column.ArrivalMode, 50),
            Text(Column.LicensePlate, 20),
            Text(Column.Interests, 300),
            language is null ? null : LanguageCodes.GetValueOrDefault(BookingComImportService.Normalize(language)));
    }

    private static string Snapshot(Guest guest) => string.Join('\u001f',
        guest.FirstName, guest.LastName, guest.Street, guest.PostalCode, guest.City, guest.Country, guest.Nationality,
        guest.Phone, guest.Email, guest.Company, guest.Language, guest.TravelDocument, guest.LicensePlate, guest.TravelPurpose,
        guest.ReferralSource, guest.ArrivalMode, guest.Interests, guest.DateOfBirth?.ToString("yyyy-MM-dd"), guest.MarketingConsent);

    /// <summary>Copies every value feratel has onto the profile (feratel is the master); empty cells keep the current value.</summary>
    private static void Apply(Guest guest, GuestData data)
    {
        Set(data.FirstName, guest.FirstName, value => guest.FirstName = value);
        Set(data.LastName, guest.LastName, value => guest.LastName = value);
        Set(data.Street, guest.Street, value => guest.Street = value);
        Set(data.PostalCode, guest.PostalCode, value => guest.PostalCode = value);
        Set(data.City, guest.City, value => guest.City = value);
        Set(data.Country, guest.Country, value => guest.Country = value);
        Set(data.Nationality, guest.Nationality, value => guest.Nationality = value);
        Set(data.Phone, guest.Phone, value => guest.Phone = value);
        Set(data.Email, guest.Email, value => guest.Email = value);
        Set(data.Company, guest.Company, value => guest.Company = value);
        Set(data.Language, guest.Language, value => guest.Language = value);
        Set(data.TravelDocument, guest.TravelDocument, value => guest.TravelDocument = value);
        Set(data.LicensePlate, guest.LicensePlate, value => guest.LicensePlate = value);
        Set(data.TravelPurpose, guest.TravelPurpose, value => guest.TravelPurpose = value);
        Set(data.ReferralSource, guest.ReferralSource, value => guest.ReferralSource = value);
        Set(data.ArrivalMode, guest.ArrivalMode, value => guest.ArrivalMode = value);
        Set(data.Interests, guest.Interests, value => guest.Interests = value);
        if (data.DateOfBirth is { } birthDate)
        {
            guest.DateOfBirth = birthDate;
        }

        if (data.MarketingConsent is { } consent)
        {
            guest.MarketingConsent = consent;
        }

        static void Set(string? value, string? current, Action<string> assign)
        {
            if (value is not null && !string.Equals(value, current, StringComparison.Ordinal))
            {
                assign(value);
            }
        }
    }

    private static bool? ParseYesNo(string? value) => value?.ToLowerInvariant() switch
    {
        "ja" or "yes" or "true" or "1" or "x" => true,
        "nein" or "no" or "false" or "0" => false,
        _ => null
    };

    private static string NameKey(string? firstName, string lastName) =>
        $"{BookingComImportService.Normalize(firstName ?? string.Empty)}|{BookingComImportService.Normalize(lastName)}";

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];

    private sealed record GuestData(
        string? FirstName,
        string? LastName,
        string? Street,
        string? Nationality,
        string? PostalCode,
        string? City,
        DateOnly? DateOfBirth,
        bool InvalidBirthDate,
        string? TravelDocument,
        string? Country,
        string? Phone,
        string? Email,
        string? Company,
        bool? MarketingConsent,
        string? TravelPurpose,
        string? ReferralSource,
        string? ArrivalMode,
        string? LicensePlate,
        string? Interests,
        string? Language);
}
