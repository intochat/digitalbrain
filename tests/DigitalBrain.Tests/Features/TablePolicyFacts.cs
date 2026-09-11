using System.Text.Json;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TablePolicyFacts
{
    [Theory]
    [InlineData("0.1234567890123456789", false)]
    [InlineData("9007199254740993", false)]
    [InlineData("1e-29", false)]
    [InlineData("1.23456789012345e-28", false)]
    [InlineData("123.45", true)]
    [InlineData("123456789012345", true)]
    [InlineData("0.123456789012345", true)]
    [InlineData("1e-28", true)]
    [InlineData("9000000000000000", true)]
    public void Numbers_must_roundtrip_exactly_with_shared_client_precision(string json, bool accepted)
    {
        var input = new CreateTable("Test", [new("c", "Column", "number")], [new("r", [JsonSerializer.Deserialize<JsonElement>(json)])]);
        if (accepted)
        {
            Assert.Single(TablePolicy.Create("test", input).Rows);
        }
        else
        {
            Assert.Throws<TableValidationException>(() => TablePolicy.Create("test", input));
        }
    }

    [Theory]
    [InlineData("number", "eq", "10", "10", true)]
    [InlineData("number", "neq", "10", "2", true)]
    [InlineData("number", "lt", "2", "10", true)]
    [InlineData("number", "lte", "10", "10", true)]
    [InlineData("number", "gte", "10", "10", true)]
    [InlineData("number", "gt", "null", "10", false)]
    [InlineData("number", "eq", "null", "null", true)]
    [InlineData("text", "contains", "\"Ada Lovelace\"", "\"LOVE\"", true)]
    [InlineData("text", "eq", "\"ADA\"", "\"ada\"", true)]
    [InlineData("boolean", "eq", "true", "false", false)]
    [InlineData("boolean", "isNotNull", "false", "null", true)]
    [InlineData("date", "gt", "\"2026-09-11\"", "\"2025-12-31\"", true)]
    public void Filters_use_typed_values_and_explicit_null_semantics(string type, string op, string cell, string value, bool matches)
    {
        var source = TablePolicy.Create("test", new("Test", [new("c", "Column", type)], [new("r", [JsonSerializer.Deserialize<JsonElement>(cell)])]));
        var view = TablePolicy.ValidateView(source, new(1, [new("c", op, JsonSerializer.Deserialize<JsonElement>(value))], null, ["c"]));
        var result = TablePolicy.Query(source with { Filters = view.Filters }, 0, 50);
        Assert.Equal(matches ? 1 : 0, result.FilteredRows);
        Assert.Single(source.Rows);
    }

    [Theory]
    [InlineData("boolean", "\"true\"")]
    [InlineData("number", "\"2\"")]
    [InlineData("number", "1e100")]
    [InlineData("text", "{}")]
    [InlineData("text", "[]")]
    [InlineData("date", "\"2026-02-30\"")]
    [InlineData("date", "\"09/11/2026\"")]
    public void Invalid_scalar_types_are_rejected(string type, string value)
        => Assert.Throws<TableValidationException>(() => TablePolicy.Create("test", new("Test", [new("c", "Column", type)], [new("r", [JsonSerializer.Deserialize<JsonElement>(value)])])));

    [Fact]
    public void Limits_reject_excess_rows_columns_filters_and_text_without_truncation()
    {
        var input = new CreateTable("Test", [new("c", "Column", "text")], []);
        Assert.Throws<TableValidationException>(() => TablePolicy.Create("test", input with { Rows = Enumerable.Range(0, 1001).Select(index => new TableRow(index.ToString(), [JsonSerializer.SerializeToElement("value")])).ToArray() }));
        Assert.Throws<TableValidationException>(() => TablePolicy.Create("test", input with { Columns = Enumerable.Range(0, 33).Select(index => new TableColumn(index.ToString(), "Column", "text")).ToArray() }));
        Assert.Throws<TableValidationException>(() => TablePolicy.Create("test", input with { Rows = [new("r", [JsonSerializer.SerializeToElement(new string('x', 4001))])] }));
        var source = TablePolicy.Create("test", input);
        Assert.Throws<TableValidationException>(() => TablePolicy.ValidateView(source, new(1, Enumerable.Repeat(new TableFilter("c", "isNull"), 65).ToArray(), null, ["c"])));
        Assert.Throws<TableValidationException>(() => TablePolicy.ValidateView(source, new(1, [], null, ["c", "c"])));
        Assert.Throws<TableValidationException>(() => TablePolicy.ValidateView(source, new(1, [], new("missing"), ["c"])));
        Assert.Throws<TableValidationException>(() => TablePolicy.ValidateView(source, null!));
    }
}
