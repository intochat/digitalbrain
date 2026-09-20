using DigitalBrain.Contracts;

namespace DigitalBrain.ClickHouse.Tables.Signals;

// Fired by a table neuron whose rows should appear live; the name is the table id.
[GenerateSerializer, Alias("db.clickhouse.table-rendered")]
public sealed record TableRendered(
    [property: Id(0)] string Name,
    [property: Id(1)] string Title) : Signal;
