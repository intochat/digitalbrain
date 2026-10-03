using DigitalBrain.Contracts.Data;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit.Data;

public sealed class StoredRowsFacts
{
    [Fact]
    public async Task ReplaceThenFilterAndGroupByCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var rows = brain.Get<IStoredRows>("people");
        await rows.Replace(
            new RowSchema([new("name", "text"), new("country", "text"), new("age", "number")]),
            [new(["ada", "us", "36"]), new(["grace", "us", "45"]), new(["alan", "uk", "41"])]);

        var filtered = await rows.Read(new RowQuery { Filters = [new("age", "gte", "40")], Sort = [new("name", false)] });
        Assert.Equal(["alan", "grace"], filtered.Rows.Select(row => row.Values[0]).ToArray());

        var grouped = await rows.Read(new RowQuery
        {
            GroupBy = ["country"],
            Aggregates = [new("count", null, "count")],
            Sort = [new("country", false)],
        });
        Assert.Equal(["country", "count"], grouped.Columns.Select(column => column.Name).ToArray());
        Assert.Equal(["uk", "1"], grouped.Rows[0].Values);
        Assert.Equal(["us", "2"], grouped.Rows[1].Values);
        Assert.False(grouped.Truncated);
    }

    [Fact]
    public async Task APageReportsTruncation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var rows = brain.Get<IStoredRows>("ids");
        await rows.Replace(new RowSchema([new("id", "number")]), [new(["1"]), new(["2"]), new(["3"])]);

        var page = await rows.Read(new RowQuery { Limit = 2 });

        Assert.Equal(2, page.Rows.Length);
        Assert.True(page.Truncated);
    }
}
