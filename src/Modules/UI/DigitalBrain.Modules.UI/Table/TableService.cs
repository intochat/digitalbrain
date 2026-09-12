using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.UI;

/// <summary>Shared authoritative table API for humans and agent tools.</summary>
public sealed class TableService(IGrainFactory grains, IEnumerable<ITableSource>? sources = null)
{
    private static readonly NeuronId CatalogId = NeuronId.Plain("ui-table-catalog");
    private static readonly TableSource InMemorySource = new("table-", UIVocabulary.TableType);

    // Longest prefix wins so a module prefix such as "chtable-" can never be shadowed by "table-".
    private readonly ITableSource[] _sources = [.. (sources ?? []).Append(InMemorySource)
        .DistinctBy(source => source.IdPrefix, StringComparer.Ordinal)
        .OrderByDescending(source => source.IdPrefix.Length)];

    public async Task<TableSnapshot> CreateAsync(CreateTable input, CancellationToken cancellationToken = default)
    {
        var id = $"table-{Guid.NewGuid():N}";
        _ = TablePolicy.Create(id, input);
        var neuronId = new NeuronId(UIVocabulary.TableType, id);
        // Use the kernel's durable synapse index. Reserve before creation: process loss can leave
        // an empty reservation (ignored by List), but cannot orphan an applied table.
        await RegisterAsync(neuronId, cancellationToken).ConfigureAwait(false);
        var table = grains.GetGrain<ITable>(neuronId.ToGrainId());
        var command = new CreateTableCommand(CommandId.New(), input);
        await table.Create(command, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        await WaitAppliedAsync(table, id, command.Id, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // Lists a table another module serves. Listing keys on the grain type and reading on the id
    // prefix, so a registration has to satisfy both or the table would be listed but unreadable.
    public Task RegisterAsync(NeuronId table, CancellationToken cancellationToken = default)
    {
        var source = SourceOf(table.Name);
        if (!string.Equals(source.GrainType, table.Type, StringComparison.Ordinal))
        {
            throw new TableValidationException(
                $"Table '{table.Name}' cannot be listed as '{table.Type}': ids starting with '{source.IdPrefix}' are served by '{source.GrainType}'.");
        }

        return grains.GetGrain<INeuron>(CatalogId.ToGrainId()).Connect(table, UIVocabulary.TableListed).WaitAsync(cancellationToken);
    }

    public async Task<TableSnapshot> ReadAsync(string id, int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
    {
        TablePolicy.ValidatePage(offset, limit);
        return await Table(id).Read(new(offset, limit)).WaitAsync(cancellationToken).ConfigureAwait(false) ?? throw new TableNotFoundException(id);
    }

    // The neuron validates the view against its own columns and answers applied, invalid, conflict
    // or missing, so no live page is read before the command; only the resulting snapshot is.
    public async Task<TableSnapshot> UpdateAsync(string id, UpdateTableView input, CancellationToken cancellationToken = default)
    {
        var table = Table(id);
        var command = new UpdateTableCommand(CommandId.New(), TablePolicy.Normalize(input));
        await table.Update(command, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        await WaitAppliedAsync(table, id, command.Id, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // Summaries come from each neuron's saved state, never from its data source, so a table whose
    // source is down or refuses its query still lists and never hides the others.
    public async Task<IReadOnlyList<TableSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var links = await grains.GetGrain<INeuron>(CatalogId.ToGrainId()).ReadSynapses().WaitAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<TableSummary>();
        foreach (var link in links.Where(link => link.SignalType == UIVocabulary.TableListed && ServesTables(link.Target.Type)))
        {
            var summary = await grains.GetGrain<ITable>(link.Target.ToGrainId()).ReadSummary().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (summary is not null) { result.Add(summary); }
        }
        return result.OrderBy(table => table.Title, StringComparer.OrdinalIgnoreCase).ThenBy(table => table.Id, StringComparer.Ordinal).ToArray();
    }

    internal ITableSource SourceOf(string id)
        => _sources.FirstOrDefault(source => id.StartsWith(source.IdPrefix, StringComparison.Ordinal)) ?? InMemorySource;

    private bool ServesTables(string grainType)
        => _sources.Any(source => string.Equals(source.GrainType, grainType, StringComparison.Ordinal));

    private ITable Table(string id)
    {
        try { return grains.GetGrain<ITable>(new NeuronId(SourceOf(id).GrainType, id).ToGrainId()); }
        catch (ArgumentException) { throw new TableValidationException("Table ID is invalid."); }
    }

    internal static async Task WaitAppliedAsync(ITable table, string id, CommandId command, CancellationToken cancellationToken)
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
