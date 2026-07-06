using System.Globalization;
using ClosedXML.Excel;

namespace DigitalBrain.Kernel.TabularData;

public sealed record TabularColumnStats(
    string Header,
    int NumericCount,
    double? Min,
    double? Max,
    double? Sum,
    double? Average);

public sealed record TabularDataset(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<TabularColumnStats> ColumnStats);

// Pure, Orleans-independent: xlsx bytes in, (headers, rows, per-column stats) out. Stats cover every data
// row in the sheet; Rows is capped at MaxUiRows so a large upload can't blow the client render or LLM prompt.
public static class TabularDataParser
{
    public const int MaxUiRows = 50;

    public static TabularDataset Parse(byte[] xlsxBytes)
    {
        using var stream = new MemoryStream(xlsxBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var used = worksheet.RangeUsed();
        if (used is null)
            return new TabularDataset([], [], []);

        var usedRows = used.RowsUsed().ToList();
        if (usedRows.Count == 0)
            return new TabularDataset([], [], []);

        var headers = usedRows[0].CellsUsed().Select(c => c.GetString()).ToList();
        var columnCount = headers.Count;

        var allDataRows = usedRows
            .Skip(1)
            .Select(row => (IReadOnlyList<string>)Enumerable.Range(1, columnCount)
                .Select(col => row.Cell(col).GetFormattedString())
                .ToList())
            .ToList();

        var stats = BuildColumnStats(headers, allDataRows);
        var uiRows = allDataRows.Take(MaxUiRows).ToList();

        return new TabularDataset(headers, uiRows, stats);
    }

    private static List<TabularColumnStats> BuildColumnStats(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var stats = new List<TabularColumnStats>();
        for (var col = 0; col < headers.Count; col++)
        {
            var numericValues = rows
                .Select(row => double.TryParse(row[col], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (double?)d : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            stats.Add(new TabularColumnStats(
                headers[col],
                numericValues.Count,
                numericValues.Count > 0 ? numericValues.Min() : null,
                numericValues.Count > 0 ? numericValues.Max() : null,
                numericValues.Count > 0 ? numericValues.Sum() : null,
                numericValues.Count > 0 ? numericValues.Average() : null));
        }

        return stats;
    }
}
