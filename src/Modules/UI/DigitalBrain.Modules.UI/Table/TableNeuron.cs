using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.TableType)]
internal sealed class TableNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TableState>> state)
    : Neuron<TableState>(runtime, state), ITable
{
    public Task<Accepted<string>> Create(CreateTableCommand command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("create"), command, UIJson.Default.CreateTableCommand, UIJson.Default.AcceptedString, arguments =>
        {
            _ = TablePolicy.Create(Id.Name, arguments.Table);
            return new Accepted<string>(Id.Name, Schedule(Signal.FromJson(UIVocabulary.TableCreating, arguments, UIJson.Default.CreateTableCommand)));
        });

    public Task<Accepted<string>> Update(UpdateTableCommand command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("update"), command, UIJson.Default.UpdateTableCommand, UIJson.Default.AcceptedString, arguments =>
            new Accepted<string>(Id.Name, Schedule(Signal.FromJson(UIVocabulary.TableUpdating, arguments, UIJson.Default.UpdateTableCommand))));

    [ReadOnly]
    public Task<TableSnapshot?> Read(ReadTable query)
        => Task.FromResult(State?.Source is { } source ? TablePolicy.Query(source, query.Offset, query.Limit) : null);

    [ReadOnly]
    public Task<TableOperationResult?> ReadOperation(ReadTableOperation query)
        => Task.FromResult(State?.Operations.LastOrDefault(operation => operation.CommandId == query.CommandId));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = State ?? new TableState(null, []);
        var next = current.Source;
        TableOperationResult result;
        switch (delivery.Signal.Type)
        {
            case UIVocabulary.TableCreating:
                var create = Body(delivery, UIJson.Default.CreateTableCommand);
                if (create is null) { return; }
                if (current.Operations.Any(operation => operation.CommandId == create.Id)) { return; }
                if (next is not null) { result = new(create.Id, "conflict", "Table already exists."); }
                else
                {
                    try { next = TablePolicy.Create(Id.Name, create.Table); result = new(create.Id, "applied"); }
                    catch (TableValidationException error) { result = new(create.Id, "invalid", error.Message); }
                }
                break;
            case UIVocabulary.TableUpdating:
                var update = Body(delivery, UIJson.Default.UpdateTableCommand);
                if (update is null) { return; }
                if (current.Operations.Any(operation => operation.CommandId == update.Id)) { return; }
                if (next is null) { result = new(update.Id, "missing"); }
                else if (update.View is null) { result = new(update.Id, "invalid", "View is required."); }
                else if (next.Revision != update.View.ExpectedRevision)
                {
                    result = new(update.Id, "conflict", $"Expected revision {update.View.ExpectedRevision}; current revision is {next.Revision}.");
                }
                else
                {
                    try
                    {
                        var view = TablePolicy.ValidateView(next, update.View);
                        next = next with { Revision = next.Revision + 1, Filters = view.Filters, Sort = view.Sort, VisibleColumns = view.VisibleColumns };
                        result = new(update.Id, "applied");
                    }
                    catch (TableValidationException error) { result = new(update.Id, "invalid", error.Message); }
                }
                break;
            default: return;
        }
        // Persist the exact command outcome with its resulting state. A racing loser is terminal,
        // never retried indefinitely and never mistaken for another writer's successful revision.
        await SaveAsync(new(next, [.. current.Operations.TakeLast(255), result]), cancellationToken).ConfigureAwait(true);
    }
}
