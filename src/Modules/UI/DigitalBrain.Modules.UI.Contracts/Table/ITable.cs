using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("ui.create-table-command")]
public sealed record CreateTableCommand(CommandId Id, [property: Id(0)] CreateTable Table) : Command(Id);

[GenerateSerializer, Alias("ui.update-table-command")]
public sealed record UpdateTableCommand(CommandId Id, [property: Id(0)] UpdateTableView View) : Command(Id);

[GenerateSerializer, Alias("ui.table-operation")]
public sealed record TableOperationResult([property: Id(0)] CommandId CommandId, [property: Id(1)] string Status,
    [property: Id(2)] string? Message = null);

[GenerateSerializer, Alias("ui.table-state")]
public sealed record TableState([property: Id(0)] TableSnapshot? Source, [property: Id(1)] IReadOnlyList<TableOperationResult> Operations);

[GenerateSerializer, Alias("ui.read-table")]
public sealed record ReadTable([property: Id(0)] int Offset = 0, [property: Id(1)] int Limit = 50);

[GenerateSerializer, Alias("ui.read-table-operation")]
public sealed record ReadTableOperation([property: Id(0)] CommandId CommandId);

[Alias("ui.table")]
public interface ITable : INeuron
{
    /// <summary>Schedules creation of a typed table.</summary>
    [Alias("create")]
    Task<Accepted<string>> Create(CreateTableCommand command, CancellationToken cancellationToken = default);

    /// <summary>Schedules replacement of the table view at an expected revision.</summary>
    [Alias("update")]
    Task<Accepted<string>> Update(UpdateTableCommand command, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("read")]
    Task<TableSnapshot?> Read(ReadTable query);

    [ReadOnly, Alias("operation")]
    Task<TableOperationResult?> ReadOperation(ReadTableOperation query);

    /// <summary>Reads id, title and revision from saved state without touching the table's rows or their source.</summary>
    [ReadOnly, Alias("summary")]
    Task<TableSummary?> ReadSummary();
}
