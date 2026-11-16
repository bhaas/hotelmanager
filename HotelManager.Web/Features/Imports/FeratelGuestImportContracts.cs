namespace HotelManager.Web.Features.Imports;

public enum FeratelGuestImportAction
{
    Create,
    Update,
    Unchanged,
    Error
}

public sealed record FeratelGuestImportRow(
    int RowNumber,
    string Name,
    DateOnly? DateOfBirth,
    string? City,
    FeratelGuestImportAction Action,
    string? Message);

public sealed record FeratelGuestImportResult(IReadOnlyList<FeratelGuestImportRow> Rows)
{
    public int Count(FeratelGuestImportAction action) => Rows.Count(row => row.Action == action);

    public int ChangeCount => Rows.Count(row => row.Action is FeratelGuestImportAction.Create or FeratelGuestImportAction.Update);
}

public interface IFeratelGuestImportService
{
    /// <summary>Shows what an import would do without changing data. Throws <see cref="ImportFileException"/> for other files.</summary>
    Task<FeratelGuestImportResult> PreviewAsync(ImportTable table, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the guests of a feratel Deskline guest export (Meldewesen → Export detailed). Guests are matched by name and
    /// date of birth, so the export can be imported repeatedly; feratel values overwrite the profile, empty cells keep it.
    /// </summary>
    Task<FeratelGuestImportResult> ImportAsync(ImportTable table, CancellationToken cancellationToken = default);
}
