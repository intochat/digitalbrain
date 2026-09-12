using System.Text.Json;
using DigitalBrain.ClickHouse;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class QueryPlanCompilerFacts
{
    private const string BaseSql = "SELECT * FROM companies_current";

    private static readonly ClickHouseColumn[] Columns =
    [
        new("name", "String", "text"),
        new("employee_count", "Nullable(UInt32)", "number"),
        new("founded_on", "Nullable(Date)", "date"),
        new("is_active", "Bool", "boolean"),
        new("attributes", "JSON", "text"),
    ];

    private static JsonElement Value(object? value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Compiles_filters_sort_and_paging_with_bound_identifiers_and_typed_parameters()
    {
        var compiled = QueryPlanCompiler.Compile(new QueryPlan(BaseSql, Columns,
            [
                new("name", "contains", Value("roof")),
                new("employee_count", "gte", Value(50)),
                new("founded_on", "eq", Value("2000-01-01")),
                new("is_active", "eq", Value(true)),
                new("attributes", "isNull"),
            ],
            new("employee_count", Descending: true), Offset: 10, Limit: 5));

        Assert.Equal(
            "SELECT * FROM (SELECT * FROM companies_current) AS q"
            + " WHERE (positionCaseInsensitiveUTF8(toString({c0:Identifier}), {p0:String}) > 0)"
            + " AND ({c1:Identifier} >= {p1:Float64})"
            + " AND ({c2:Identifier} = {p2:Date})"
            + " AND ({c3:Identifier} = {p3:Bool})"
            + " AND ({c4:Identifier} IS NULL)"
            + " ORDER BY {sort:Identifier} DESC NULLS LAST, {t0:Identifier}, {t1:Identifier}, {t2:Identifier}"
            + " LIMIT {limit:UInt64} OFFSET {offset:UInt64}",
            compiled.PageSql);
        Assert.Equal(
            "SELECT count() FROM (SELECT * FROM companies_current) AS q"
            + " WHERE (positionCaseInsensitiveUTF8(toString({c0:Identifier}), {p0:String}) > 0)"
            + " AND ({c1:Identifier} >= {p1:Float64})"
            + " AND ({c2:Identifier} = {p2:Date})"
            + " AND ({c3:Identifier} = {p3:Bool})"
            + " AND ({c4:Identifier} IS NULL)",
            compiled.FilteredCountSql);
        Assert.Equal("SELECT count() FROM (SELECT * FROM companies_current) AS q", compiled.TotalCountSql);

        var expected = new Dictionary<string, object>
        {
            ["c0"] = "name",
            ["p0"] = "roof",
            ["c1"] = "employee_count",
            ["p1"] = 50d,
            ["c2"] = "founded_on",
            ["p2"] = "2000-01-01",
            ["c3"] = "is_active",
            ["p3"] = true,
            ["c4"] = "attributes",
            ["sort"] = "employee_count",
            ["t0"] = "name",
            ["t1"] = "founded_on",
            ["t2"] = "is_active",
            ["limit"] = 5UL,
            ["offset"] = 10UL,
        };
        Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), compiled.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        // Column names and filter text travel only as parameters; the SQL never carries them.
        Assert.DoesNotContain("roof", compiled.PageSql, StringComparison.Ordinal);
        Assert.DoesNotContain("employee_count", compiled.PageSql[BaseSql.Length..], StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsorted_plan_keeps_the_base_query_order_and_adds_no_order_by()
    {
        var compiled = QueryPlanCompiler.Compile(new QueryPlan("SELECT name, revenue_eur FROM companies_current ORDER BY revenue_eur DESC", Columns, [], null, 0, 50));
        Assert.Equal(
            "SELECT * FROM (SELECT name, revenue_eur FROM companies_current ORDER BY revenue_eur DESC) AS q"
            + " LIMIT {limit:UInt64} OFFSET {offset:UInt64}",
            compiled.PageSql);
        Assert.Equal(compiled.TotalCountSql, compiled.FilteredCountSql);
        Assert.Equal(["limit", "offset"], compiled.Parameters.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_sorted_plan_breaks_ties_with_every_other_orderable_column()
    {
        var compiled = QueryPlanCompiler.Compile(new QueryPlan(BaseSql, Columns, [], new("founded_on"), 0, 50));
        Assert.EndsWith(" ORDER BY {sort:Identifier} ASC NULLS FIRST, {t0:Identifier}, {t1:Identifier}, {t2:Identifier} LIMIT {limit:UInt64} OFFSET {offset:UInt64}", compiled.PageSql, StringComparison.Ordinal);
        Assert.Equal(["name", "employee_count", "is_active"], new[] { "t0", "t1", "t2" }.Select(name => compiled.Parameters[name]));
        Assert.DoesNotContain("attributes", compiled.Parameters.Values.OfType<string>());
    }

    [Fact]
    public void Text_equality_is_case_insensitive_and_neq_keeps_nulls_like_the_in_memory_table()
    {
        var compiled = QueryPlanCompiler.Compile(new QueryPlan(BaseSql, Columns,
            [new("name", "eq", Value("Thames Roofing Ltd")), new("employee_count", "neq", Value(64)), new("name", "neq", Value(null))],
            new("name", Descending: false), 0, 10));
        Assert.Contains("(lowerUTF8(toString({c0:Identifier})) = lowerUTF8({p0:String}))", compiled.PageSql, StringComparison.Ordinal);
        Assert.Contains("({c1:Identifier} IS NULL OR {c1:Identifier} != {p1:Float64})", compiled.PageSql, StringComparison.Ordinal);
        Assert.Contains("({c2:Identifier} IS NOT NULL)", compiled.PageSql, StringComparison.Ordinal);
        Assert.Contains(" ORDER BY {sort:Identifier} ASC NULLS FIRST, ", compiled.PageSql, StringComparison.Ordinal);
        Assert.False(compiled.Parameters.ContainsKey("p2"));
    }

    [Fact]
    public void Unknown_filter_and_sort_columns_are_refused()
    {
        Assert.Throws<TableValidationException>(() => QueryPlanCompiler.Compile(new QueryPlan(BaseSql, Columns, [new("missing", "eq", Value("x"))], null, 0, 10)));
        Assert.Throws<TableValidationException>(() => QueryPlanCompiler.Compile(new QueryPlan(BaseSql, Columns, [], new("missing"), 0, 10)));
        Assert.Throws<TableValidationException>(() => QueryPlanCompiler.Compile(new QueryPlan(BaseSql, Columns, [new("employee_count", "contains", Value(5))], null, 0, 10)));
    }
}
