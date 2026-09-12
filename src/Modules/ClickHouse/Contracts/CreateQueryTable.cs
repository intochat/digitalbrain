using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.ClickHouse;

[GenerateSerializer]
[Alias("db.clickhouse.create-query-table")]
public sealed record CreateQueryTable(
    [property: Id(0)] string Title,
    [property: Id(1)] string Sql);

[GenerateSerializer]
[Alias("db.clickhouse.create-query-table-command")]
public sealed record CreateQueryTableCommand(CommandId Id, [property: Id(0)] CreateQueryTable Table) : Command(Id);
