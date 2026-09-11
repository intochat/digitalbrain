using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.UI;

/// <summary>Shared authoritative table API for humans and agent tools.</summary>
public sealed class TableService(IGrainFactory grains)
{
    private static readonly NeuronId CatalogId = NeuronId.Plain("ui-table-catalog");

    public async Task<TableSnapshot> CreateAsync(CreateTable input, CancellationToken cancellationToken = default)
    {
        var id = $"table-{Guid.NewGuid():N}";
        _ = TablePolicy.Create(id, input);
        var neuronId = new NeuronId(UIVocabulary.TableType, id);
        // Use the kernel's durable synapse index. Reserve before creation: process loss can leave
        // an empty reservation (ignored by List), but cannot orphan an applied table.
        await grains.GetGrain<INeuron>(CatalogId.ToGrainId()).Connect(neuronId, UIVocabulary.TableListed).WaitAsync(cancellationToken).ConfigureAwait(false);
        var table = grains.GetGrain<ITable>(neuronId.ToGrainId());
        var command = new CreateTableCommand(CommandId.New(), input);
        await table.Create(command, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        await WaitAppliedAsync(table, id, command.Id, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<TableSnapshot> ReadAsync(string id, int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
    {
        TablePolicy.ValidatePage(offset, limit);
        return await Table(id).Read(new(offset, limit)).WaitAsync(cancellationToken).ConfigureAwait(false) ?? throw new TableNotFoundException(id);
    }

    public async Task<TableSnapshot> UpdateAsync(string id, UpdateTableView input, CancellationToken cancellationToken = default)
    {
        var source = await ReadAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        var view = TablePolicy.ValidateView(source, input);
        var table = Table(id);
        var command = new UpdateTableCommand(CommandId.New(), view);
        await table.Update(command, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        await WaitAppliedAsync(table, id, command.Id, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TableSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var links = await grains.GetGrain<INeuron>(CatalogId.ToGrainId()).ReadSynapses().WaitAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<TableSummary>();
        foreach (var link in links.Where(link => link.SignalType == UIVocabulary.TableListed && link.Target.Type == UIVocabulary.TableType))
        {
            var saved = await grains.GetGrain<ITable>(link.Target.ToGrainId()).Read(new(0, 1)).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (saved is not null) { result.Add(new(saved.Id, saved.Title, saved.Revision)); }
        }
        return result.OrderBy(table => table.Title, StringComparer.OrdinalIgnoreCase).ThenBy(table => table.Id, StringComparer.Ordinal).ToArray();
    }

    private ITable Table(string id)
    {
        try { return grains.GetGrain<ITable>(new NeuronId(UIVocabulary.TableType, id).ToGrainId()); }
        catch (ArgumentException) { throw new TableValidationException("Table ID is invalid."); }
    }

    private static async Task WaitAppliedAsync(ITable table, string id, CommandId command, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                var result = await table.ReadOperation(new(command)).WaitAsync(deadline.Token).ConfigureAwait(false);
                switch (result?.Status)
                {
                    case "applied": return;
                    case "missing": throw new TableNotFoundException(id);
                    case "conflict": throw new TableRevisionConflictException(result.Message ?? "Table revision changed.");
                    case "invalid": throw new TableValidationException(result.Message ?? "Invalid table view.");
                }
                await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Table operation has not completed. Read the table before retrying; the operation may still apply.");
        }
    }
}
