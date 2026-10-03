using DigitalBrain.Contracts.Data;
using DigitalBrain.Kernel.Data;
using DigitalBrain.Sdk.Data;
using Xunit;

namespace DigitalBrain.Sdk.Tests.Unit.Data;

public sealed class RowQuerySqlFacts
{
    private static readonly RowSchema People = new([new("id", "number"), new("name", "text"), new("country", "text")]);

    [Theory]
    [InlineData("eq", "2", "=", "2")]
    [InlineData("gt", "2", ">", "")]
    [InlineData("lt", "2", "<", "10")]
    public void TextFiltersUseTextSemanticsInBothBackends(string op, string value, string sqlOperator, string expected)
    {
        var schema = new RowSchema([new("value", "text")]);
        var query = new RowQuery { Filters = [new("value", op, value)] };
        var page = RowQueryEvaluator.Evaluate(schema, [new(["10"]), new(["2"])], query);
        Assert.Equal(expected.Length == 0 ? Array.Empty<string>() : [expected], page.Rows.Select(r => r.Values[0]).ToArray());
        Assert.Contains($"\"value\" {sqlOperator} '{value}'", RowQuerySql.Compile("values", schema, query), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "eq", "1")]
    [InlineData("id", "unknown", "1")]
    [InlineData("id", "eq", "NaN")]
    public void BothBackendsRejectInvalidFiltersEvenWithoutRows(string column, string op, string value)
    {
        var query = new RowQuery { Filters = [new(column, op, value)] };
        Assert.Throws<ArgumentException>(() => RowQueryEvaluator.Evaluate(People, [], query));
        Assert.Throws<ArgumentException>(() => RowQuerySql.Compile("people", People, query));
    }

    [Fact]
    public void EmptyCountReturnsOneZeroRow()
    {
        var query = new RowQuery { Aggregates = [new("count", null, "count")] };
        Assert.Equal("0", Assert.Single(RowQueryEvaluator.Evaluate(People, [], query).Rows).Values[0]);
        Assert.Contains("COUNT(*)", RowQuerySql.Compile("people", People, query), StringComparison.Ordinal);
    }

    [Fact]
    public void GroupKeysCannotCollideOnEmbeddedSeparators()
    {
        var schema = new RowSchema([new("a", "text"), new("b", "text")]);
        var result = RowQueryEvaluator.Evaluate(schema, [new(["a\u001fb", "c"]), new(["a", "b\u001fc"])],
            new RowQuery { GroupBy = ["a", "b"], Aggregates = [new("count", null, "count")] });
        Assert.Equal(2, result.Rows.Length);
    }

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
