using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace HotelManager.Web.Features.Imports;

/// <summary>A spreadsheet as text: the header row and the data rows (empty rows removed).</summary>
public sealed record ImportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public string Cell(IReadOnlyList<string> row, int? column) =>
        column is { } index && index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;
}

public sealed class ImportFileException(string message) : Exception(message);

/// <summary>Reads CSV (comma, semicolon or tab separated) and Excel (.xlsx/.xls) exports into an <see cref="ImportTable"/>.</summary>
public static class ImportFileReader
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    static ImportFileReader()
    {
        // Legacy .xls files and ANSI CSV exports use Windows code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static ImportTable Read(byte[] content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = extension is ".xlsx" or ".xls"
            ? ReadExcel(content)
            : ReadCsv(DecodeText(content));

        var nonEmpty = rows.Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToList();
        if (nonEmpty.Count == 0)
        {
            throw new ImportFileException("The file is empty.");
        }

        var headers = nonEmpty[0].Select(header => header.Trim()).ToList();
        var data = nonEmpty.Skip(1)
            .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, headers.Count)
                .Select(index => index < row.Count ? row[index] : string.Empty)
                .ToList())
            .ToList();
        return new ImportTable(headers, data);
    }

    private static List<List<string>> ReadExcel(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var rows = new List<List<string>>();
            while (reader.Read())
            {
                var row = new List<string>(reader.FieldCount);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    row.Add(reader.GetValue(index) switch
                    {
                        null => string.Empty,
                        DateTime date when date.TimeOfDay == TimeSpan.Zero => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        double number => number.ToString(CultureInfo.InvariantCulture),
                        var value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    });
                }

                rows.Add(row);
            }

            return rows;
        }
        catch (Exception exception) when (exception is not ImportFileException)
        {
            throw new ImportFileException("The Excel file could not be read. Save it as .xlsx or export it as CSV and try again.");
        }
    }

    private static string DecodeText(byte[] content)
    {
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
        }

        var offset = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content, offset, content.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(content);
        }
    }

    /// <summary>RFC 4180 parser; the delimiter is detected from the header line.</summary>
    internal static List<List<string>> ReadCsv(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.MaxBy(candidate => firstLine.Count(character => character == candidate));

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                cell.Append(character);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
