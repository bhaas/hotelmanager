namespace HotelManager.Web.Features.Imports;

/// <summary>Columns of a Booking.com reservation export the importer understands.</summary>
public enum BookingComField
{
    BookingNumber,
    GuestName,
    BookedBy,
    CheckIn,
    CheckOut,
    Status,
    Rooms,
    Persons,
    Adults,
    Children,
    UnitType,
    Price,
    Commission,
    Remarks,
    Email,
    Phone,
    Country,
    Address
}

public enum BookingComImportAction
{
    Create,
    Update,
    Cancel,
    Unchanged,
    Skip,
    Error
}

/// <param name="Columns">Column index per field; unmapped fields are absent.</param>
/// <param name="UnitTypeCategories">Booking.com unit type (room name) → room category name of this hotel.</param>
public sealed record BookingComImportOptions(
    IReadOnlyDictionary<BookingComField, int> Columns,
    IReadOnlyDictionary<string, string?> UnitTypeCategories,
    bool CreateGuestProfiles = true,
    bool SkipPastStays = true);

public sealed record BookingComImportRow(
    int RowNumber,
    string ExternalReference,
    string GuestName,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    int Occupancy,
    string UnitType,
    bool IsCancelled,
    BookingComImportAction Action,
    string? RoomNumber,
    string? Message);

public sealed record BookingComImportPreview(
    IReadOnlyList<BookingComImportRow> Rows,
    IReadOnlyList<string> UnitTypes,
    IReadOnlyList<string> Categories)
{
    public int Count(BookingComImportAction action) => Rows.Count(row => row.Action == action);

    public int ChangeCount => Rows.Count(row => row.Action is BookingComImportAction.Create or BookingComImportAction.Update or BookingComImportAction.Cancel);
}

public sealed record BookingComImportResult(IReadOnlyList<BookingComImportRow> Rows)
{
    public int Count(BookingComImportAction action) => Rows.Count(row => row.Action == action);
}

public interface IBookingComImportService
{
    /// <summary>Detects columns from the header names (English and German extranet exports).</summary>
    IReadOnlyDictionary<BookingComField, int> DetectColumns(IReadOnlyList<string> headers);

    /// <summary>Suggests a room category for every unit type found in the file.</summary>
    Task<IReadOnlyDictionary<string, string?>> SuggestCategoriesAsync(ImportTable table, IReadOnlyDictionary<BookingComField, int> columns, CancellationToken cancellationToken = default);

    /// <summary>Shows what an import would do without changing data.</summary>
    Task<BookingComImportPreview> PreviewAsync(ImportTable table, BookingComImportOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the reservations. Re-importing is safe: bookings are matched by their Booking.com number, changed stays are
    /// updated and cancellations cancel the reservation.
    /// </summary>
    Task<BookingComImportResult> ImportAsync(ImportTable table, BookingComImportOptions options, CancellationToken cancellationToken = default);
}
