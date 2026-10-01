using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Orleans.Runtime;

namespace DigitalBrain.Postgres;

[GenerateSerializer]
internal sealed record PostgresTableState
{
    [Id(0)] public string? Owner { get; init; }
    [Id(1)] public PostgresTableDefinition? Accepted { get; init; }
}

[GrainType("postgres.table")]
internal sealed class PostgresTableNeuron(IPostgresTableProvider provider,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PostgresTableState> state) : Neuron, IPostgresTable
{
    private string Scope()
    {
        var caller = CallerContextStamper.Require();
        if (!CallerContextStamper.IsTrusted(caller) || (caller.Kind == DigitalBrain.Contracts.Enforcement.CallerKind.App && string.IsNullOrWhiteSpace(caller.AppId)))
        { throw new UnauthorizedAccessException("A trusted brain and app caller is required."); }
        var scope = JsonSerializer.Serialize(new[] { BrainScope.CurrentId(), caller.AppId });
        if (state.State.Owner is { } owner && owner != scope)
        { throw new UnauthorizedAccessException("This table belongs to another brain or app."); }
        return scope;
    }

    private PostgresTableDefinition Current()
    {
        _ = Scope();
        return state.State.Accepted ?? throw new PostgresQueryException("Define the table first.");
    }

    public async Task<PostgresTableDefinition> Define(TableDefinition definition)
    {
        var scope = Scope();
        var normalized = PostgresTablePolicy.Validate(definition);
        if (state.State.Accepted is { } accepted)
        {
            if (!PostgresTablePolicy.Compatible(accepted.Definition, normalized))
            { throw new PostgresQueryException("Incompatible table redefinition; migrations are not supported."); }
            return accepted;
        }
        var table = PostgresTablePolicy.PhysicalName(scope, this.GetPrimaryKeyString());
        await provider.DefineAsync(table, normalized, CancellationToken.None);
        var previous = state.State;
        var result = new PostgresTableDefinition(table, normalized, 1);
        state.State = new() { Owner = scope, Accepted = result };
        try { await state.WriteStateAsync(); }
        catch { state.State = previous; throw; }
        await PublishAsync(new TableDefined(table, result.Revision));
        return result;
    }

    public async Task<bool> Upsert(TableValue[] key, TableValue[] values)
    {
        var current = Current();
        key = PostgresTablePolicy.Values(current.Definition, key, true);
        values = PostgresTablePolicy.Values(current.Definition, values, false);
        var changed = await provider.UpsertAsync(current.Table, current.Definition, key, values, CancellationToken.None);
        if (changed) { await PublishAsync(new RowUpserted(current.Table, key)); }
        return changed;
    }

    public async Task<bool> Delete(TableValue[] key)
    {
        var current = Current();
        key = PostgresTablePolicy.Values(current.Definition, key, true);
        var changed = await provider.DeleteAsync(current.Table, current.Definition, key, CancellationToken.None);
        if (changed) { await PublishAsync(new RowDeleted(current.Table, key)); }
        return changed;
    }

    public Task<TableValue[]?> Read(TableValue[] key)
    {
        var current = Current();
        key = PostgresTablePolicy.Values(current.Definition, key, true);
        return provider.ReadAsync(current.Table, current.Definition, key, CancellationToken.None);
    }

    public Task<TableValue[][]> Page(int offset = 0, int limit = 200)
    {
        var current = Current();
        if (offset < 0 || limit is < 1 or > PostgresQuery.MaxRowsLimit)
        { throw new PostgresQueryException($"Offset must be nonnegative and limit must be 1–{PostgresQuery.MaxRowsLimit}."); }
        return provider.PageAsync(current.Table, current.Definition, offset, limit, CancellationToken.None);
    }
}
