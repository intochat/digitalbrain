using System.Text.Json;
using DigitalBrain.ClickHouse;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClickHouseTypeMapFacts
{
    [Theory]
    [InlineData("String", "text")]
    [InlineData("Nullable(LowCardinality(String))", "text")]
    [InlineData("LowCardinality(Nullable(String))", "text")]
    [InlineData("UInt32", "number")]
    [InlineData("Nullable(UInt32)", "number")]
    [InlineData("Int64", "number")]
    [InlineData("Float64", "number")]
    [InlineData("Decimal(18, 4)", "number")]
    [InlineData("Decimal64(4)", "number")]
    [InlineData("Int128", "text")]
    [InlineData("UInt256", "text")]
    [InlineData("Bool", "boolean")]
    [InlineData("Nullable(Bool)", "boolean")]
    [InlineData("Date", "date")]
    [InlineData("Nullable(Date32)", "date")]
    [InlineData("DateTime", "text")]
    [InlineData("DateTime64(3)", "text")]
    [InlineData("DateTime64(3, 'UTC')", "text")]
    [InlineData("Array(String)", "text")]
    [InlineData("Map(String, UInt8)", "text")]
    [InlineData("JSON", "text")]
    [InlineData("UUID", "text")]
    [InlineData("Enum8('a' = 1, 'b' = 2)", "text")]
    [InlineData("IPv4", "text")]
    public void Maps_clickhouse_types_onto_table_column_types(string clickHouseType, string tableType)
        => Assert.Equal(tableType, ClickHouseTypeMap.ToTableType(clickHouseType));

    [Theory]
    [InlineData("String", true)]
    [InlineData("Nullable(UInt32)", true)]
    [InlineData("Array(String)", true)]
    [InlineData("Map(String, UInt8)", true)]
    [InlineData("JSON", false)]
    [InlineData("Nullable(JSON)", false)]
    [InlineData("Object('json')", false)]
    [InlineData("AggregateFunction(uniq, UInt64)", false)]
    [InlineData("Dynamic", false)]
    public void Knows_which_types_can_order_a_page(string clickHouseType, bool orderable)
        => Assert.Equal(orderable, ClickHouseTypeMap.IsOrderable(clickHouseType));

    [Fact]
    public void Number_cells_follow_the_table_policy_rule()
    {
        Assert.Equal(JsonValueKind.Null, ClickHouseCells.ToCell(null, "number").ValueKind);
        Assert.Equal(JsonValueKind.Null, ClickHouseCells.ToCell(DBNull.Value, "number").ValueKind);
        Assert.Equal("8200000", ClickHouseCells.ToCell(8200000L, "number").GetRawText());
        Assert.Equal("64", ClickHouseCells.ToCell((uint)64, "number").GetRawText());
        Assert.Equal("4.6", ClickHouseCells.ToCell(4.6d, "number").GetRawText());
        Assert.Equal("0.333333333333333", ClickHouseCells.ToCell(1d / 3d, "number").GetRawText());
        Assert.Equal("12.50", ClickHouseCells.ToCell(12.50m, "number").GetRawText());
        Assert.Equal(JsonValueKind.String, ClickHouseCells.ToCell(ulong.MaxValue, "number").ValueKind);
        Assert.Equal(JsonValueKind.String, ClickHouseCells.ToCell(1234567890.123456789m, "number").ValueKind);
        Assert.Equal(JsonValueKind.String, ClickHouseCells.ToCell(double.NaN, "number").ValueKind);
        Assert.Equal("999999999999999", ClickHouseCells.ToCell(999999999999999L, "number").GetRawText());
        // 2^53 - 1 is within range but has 16 significant digits, so it is text like in TablePolicy.
        Assert.Equal(JsonValueKind.String, ClickHouseCells.ToCell(9007199254740991L, "number").ValueKind);
        Assert.Equal(JsonValueKind.String, ClickHouseCells.ToCell(9007199254740992L, "number").ValueKind);
    }

    [Fact]
    public void Date_boolean_and_text_cells_are_iso_strings_or_json()
    {
        Assert.Equal("1998-04-12", ClickHouseCells.ToCell(new DateTime(1998, 4, 12, 0, 0, 0, DateTimeKind.Utc), "date").GetString());
        Assert.Equal("1998-04-12", ClickHouseCells.ToCell(new DateOnly(1998, 4, 12), "date").GetString());
        Assert.True(ClickHouseCells.ToCell(true, "boolean").GetBoolean());
        Assert.Equal("2026-09-12T10:30:00Z", ClickHouseCells.ToCell(new DateTime(2026, 9, 12, 10, 30, 0, DateTimeKind.Utc), "text").GetString());
        Assert.Equal("[\"construction\",\"roofing\"]", ClickHouseCells.ToCell(new[] { "construction", "roofing" }, "text").GetString());
        Assert.Equal("{\"vat\":\"GB1\"}", ClickHouseCells.ToCell(new Dictionary<string, object> { ["vat"] = "GB1" }, "text").GetString());
        Assert.Equal("42", ClickHouseCells.ToCell(42, "text").GetString());
        Assert.Equal(ClickHouseCells.MaxTextLength, ClickHouseCells.ToCell(new string('x', ClickHouseCells.MaxTextLength + 10), "text").GetString()!.Length);
    }
}
