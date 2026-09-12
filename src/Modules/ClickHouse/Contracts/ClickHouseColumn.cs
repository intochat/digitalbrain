namespace DigitalBrain.ClickHouse;

// TableType is the ui table column domain: text, number, date or boolean. SampleValues is filled
// by schema reads for low-cardinality and enum columns so the agent filters on real values.
[GenerateSerializer]
[Alias("db.clickhouse.column")]
public sealed record ClickHouseColumn(
    [property: Id(0)] string Name,
    [property: Id(1)] string ClickHouseType,
    [property: Id(2)] string TableType,
    [property: Id(3)] IReadOnlyList<string>? SampleValues = null);
