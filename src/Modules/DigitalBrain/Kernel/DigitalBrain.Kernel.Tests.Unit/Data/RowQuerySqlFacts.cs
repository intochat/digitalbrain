using DigitalBrain.Contracts.Data;
using DigitalBrain.Sdk.Data;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit.Data;

public sealed class RowQuerySqlFacts
{
    private static readonly RowSchema People = new([new("id", "number"), new("name", "text"), new("country", "text")]);

    [Fact]
    public void CompilesAFilteredSortedPage()
    {
        var sql = RowQuerySql.Compile("public.people", People, new RowQuery
        {
            Filters = [new("name", "eq", "O'Brien")],
            Sort = [new("id", true)],
            Limit = 10,
        });

        Assert.Equal(
            """SELECT "id", "name", "country" FROM "public"."people" WHERE "name" = 'O''Brien' ORDER BY "id" DESC LIMIT 10 OFFSET 0""",
            sql);
    }

    [Fact]
    public void CompilesGroupByCountAndANumericComparison()
    {
        var grouped = RowQuerySql.Compile("people", People, new RowQuery
        {
            GroupBy = ["country"],
            Aggregates = [new("count", null, "count")],
            Sort = [new("count", true)],
        });
        var aged = RowQuerySql.Compile("people", People, new RowQuery
        {
            Columns = ["name"],
            Filters = [new("id", "gte", "18"), new("name", "contains", "ann")],
        });

        Assert.Equal(
            """SELECT "country", COUNT(*) AS "count" FROM "people" GROUP BY "country" ORDER BY "count" DESC LIMIT 50 OFFSET 0""",
            grouped);
        Assert.Equal(
            """SELECT "name" FROM "people" WHERE "id"::double precision >= 18 AND strpos("name"::text, 'ann') > 0 LIMIT 50 OFFSET 0""",
            aged);
    }

    [Fact]
    public void ABackslashInAValueIsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => RowQuerySql.Compile("people", People, new RowQuery
        {
            Filters = [new("name", "eq", "a\\b")],
        }));

        Assert.Contains("backslash", error.Message, StringComparison.Ordinal);
    }
}
