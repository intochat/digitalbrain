using ClosedXML.Excel;
using DigitalBrain.Kernel.TabularData;

namespace DigitalBrain.Tests.TabularData;

public class TabularDataParserTests
{
    private static byte[] BuildWorkbook(string[] headers, object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");
        for (var col = 0; col < headers.Length; col++)
        {
            ws.Cell(1, col + 1).Value = headers[col];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var value = rows[r][c];
                ws.Cell(r + 2, c + 1).Value = value is double d ? (XLCellValue)d : (XLCellValue)value.ToString();
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Parses_headers_and_rows_from_a_simple_sheet()
    {
        var bytes = BuildWorkbook(
            ["Month", "Revenue", "Units"],
            [
                ["Jan", 12000.0, 45.0],
                ["Feb", 14500.0, 52.0],
                ["Mar", 13800.0, 48.0],
            ]);

        var dataset = TabularDataParser.Parse(bytes);

        Assert.Equal(["Month", "Revenue", "Units"], dataset.Headers);
        Assert.Equal(3, dataset.Rows.Count);
        Assert.Equal("Jan", dataset.Rows[0][0]);
        Assert.Equal("Feb", dataset.Rows[1][0]);
    }

    [Fact]
    public void Computes_min_max_sum_average_for_numeric_columns_only()
    {
        var bytes = BuildWorkbook(
            ["Month", "Revenue"],
            [
                ["Jan", 100.0],
                ["Feb", 300.0],
            ]);

        var dataset = TabularDataParser.Parse(bytes);

        var monthStats = dataset.ColumnStats.Single(s => s.Header == "Month");
        Assert.Equal(0, monthStats.NumericCount);
        Assert.Null(monthStats.Sum);

        var revenueStats = dataset.ColumnStats.Single(s => s.Header == "Revenue");
        Assert.Equal(2, revenueStats.NumericCount);
        Assert.Equal(100.0, revenueStats.Min);
        Assert.Equal(300.0, revenueStats.Max);
        Assert.Equal(400.0, revenueStats.Sum);
        Assert.Equal(200.0, revenueStats.Average);
    }

    [Fact]
    public void Caps_returned_rows_at_MaxUiRows_but_stats_still_cover_the_full_sheet()
    {
        var rowCount = TabularDataParser.MaxUiRows + 10;
        var rows = Enumerable.Range(1, rowCount).Select(i => new object[] { $"r{i}", (double)i }).ToArray();
        var bytes = BuildWorkbook(["Label", "Value"], rows);

        var dataset = TabularDataParser.Parse(bytes);

        Assert.Equal(TabularDataParser.MaxUiRows, dataset.Rows.Count);
        var valueStats = dataset.ColumnStats.Single(s => s.Header == "Value");
        Assert.Equal(rowCount, valueStats.NumericCount);
        Assert.Equal(Enumerable.Range(1, rowCount).Sum(), valueStats.Sum);
    }
}
