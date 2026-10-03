using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using Orleans.Runtime;

namespace DigitalBrain.Postgres;

[GenerateSerializer]
internal sealed record PostgresTablesState
{
    [Id(0)] public string[] Tables { get; init; } = [];
    [Id(1)] public bool Retired { get; init; }
}

[GrainType("postgres.tables")]
internal sealed class PostgresTablesNeuron(
    [PersistentState("tables", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PostgresTablesState> state)
    : Neuron, IPostgresTables, IPostgresTablesImport, IPostgresAppStorage, IPostgresLegacyOwner
{
    private readonly SemaphoreSlim writes = new(1, 1);

    public Task<string[]> ReadTables() => Task.FromResult(state.State.Tables.ToArray());

    public async Task Migrate(string[] legacyFiles)
    {
        var owner = this.GetPrimaryKeyString();
        var scope = JsonSerializer.Deserialize<string[]>(owner)!;
        if (state.State.Retired) { await Persist(new()); }
        foreach (var file in legacyFiles.Distinct(StringComparer.Ordinal))
        {
            var legacy = JsonSerializer.Serialize(new[] { scope[0], file });
            if (legacy == owner) { continue; }
            foreach (var table in await GrainFactory.GetGrain<IPostgresLegacyOwner>(legacy).ReadLegacyTables())
            {
                // Index first, then transfer. A retry covers a lost transfer response; no table
                // can become owned by an app whose lifetime index does not include it.
                await Import(table);
                await GrainFactory.GetGrain<IPostgresTableOwnership>(table).Transfer(legacy, owner);
            }
        }
    }

    public async Task<string[]> ReadLegacyTables()
    {
        await RequireOpen();
        return state.State.Tables.ToArray();
    }

    public Task RequireOpen()
    {
        if (state.State.Retired) { throw new InvalidOperationException("This app's storage has been retired."); }
        return Task.CompletedTask;
    }

    public async Task Register(string tableId)
    {
        await writes.WaitAsync();
        try
        {
            await RequireOpen();
            if (state.State.Tables.Contains(tableId, StringComparer.Ordinal)) { return; }
            await Persist(state.State with { Tables = [.. state.State.Tables, tableId] });
        }
        finally { writes.Release(); }
    }

    public async Task Import(string tableId)
    {
        await writes.WaitAsync();
        try
        {
            if (!state.State.Tables.Contains(tableId, StringComparer.Ordinal))
            { await Persist(state.State with { Tables = [.. state.State.Tables, tableId] }); }
        }
        finally { writes.Release(); }
    }

    public async Task Retire()
    {
        await writes.WaitAsync();
        try { if (!state.State.Retired) { await Persist(state.State with { Retired = true }); } }
        finally { writes.Release(); }
        // Registration must still answer while a table is finishing its in-flight Define.
        // The write gate is released before calls back to tables, so closed scopes refuse promptly.
        foreach (var table in state.State.Tables)
        { await GrainFactory.GetGrain<IPostgresTableLifetime>(table).Retire(this.GetPrimaryKeyString()); }
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        writes.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    private async Task Persist(PostgresTablesState next)
    {
        var previous = state.State;
        state.State = next;
        try { await state.WriteStateAsync(); }
        catch { state.State = previous; throw; }
    }
}

internal sealed class PostgresLifecycleGuard : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var contract = context.InterfaceMethod.DeclaringType;
        if (contract == typeof(IPostgresAppStorage))
        {
            var scope = JsonSerializer.Deserialize<string?[]>(context.TargetId.Key.ToString())!;
            if (scope.Length != 2 || scope[0] != BrainScope.CurrentId())
            { throw new UnauthorizedAccessException("This storage belongs to another brain."); }
            var storageCaller = CallerContextStamper.Require();
            if (!CallerContextStamper.IsTrusted(storageCaller) || (context.InterfaceMethod.Name == nameof(IPostgresAppStorage.ReadTables) && storageCaller.Kind == DigitalBrain.Contracts.Enforcement.CallerKind.App))
            { throw new UnauthorizedAccessException("Only trusted host code may access app storage."); }
            if (context.InterfaceMethod.Name == nameof(IPostgresAppStorage.Migrate)
                && (context.SourceId?.Type.ToString() != "apps.app" || context.SourceId?.Key.ToString() != scope[1]))
            { throw new UnauthorizedAccessException("Only the owning Apps grain may migrate storage."); }
        }
        if (contract == typeof(IPostgresLegacyOwner) || contract == typeof(IPostgresTableOwnership))
        {
            var source = context.SourceId;
            if (source?.Type.ToString() != "postgres.tables")
            { throw new UnauthorizedAccessException("Only Postgres owner indexes may transfer storage."); }
            var owner = contract == typeof(IPostgresLegacyOwner) ? context.TargetId.Key.ToString()
                : (string)context.Request.GetArgument(1)!;
            var scope = JsonSerializer.Deserialize<string?[]>(owner)!;
            var sourceScope = JsonSerializer.Deserialize<string?[]>(source.Value.Key.ToString())!;
            if (scope.Length != 2 || scope[0] != BrainScope.CurrentId() || sourceScope[0] != scope[0]
                || (contract == typeof(IPostgresTableOwnership) && source.Value.Key.ToString() != owner))
            { throw new UnauthorizedAccessException("Ownership transfer cannot cross brains or app indexes."); }
        }
        if (contract == typeof(IPostgresTablesImport))
        {
            if (context.SourceId?.Type.ToString() != "postgres.migration")
            { throw new UnauthorizedAccessException("Only Postgres may migrate table ownership."); }
        }
        if (contract == typeof(IPostgresStorageMigration)
            && context.SourceId?.Type.ToString() != "apps.app"
            && (!CallerContextStamper.TryGet(out var caller) || caller.Kind != DigitalBrain.Contracts.Enforcement.CallerKind.Platform))
        { throw new UnauthorizedAccessException("Only platform startup or Apps may prepare storage migration."); }
        if (contract == typeof(IPostgresTables) || contract == typeof(IPostgresTableLifetime))
        {
            var source = context.SourceId;
            var expected = contract == typeof(IPostgresTableLifetime) ? "postgres.tables"
                : context.InterfaceMethod.Name == nameof(IPostgresTables.Retire) ? "apps.app" : "postgres.table";
            if (source is null || source.Value.Type.ToString() != expected)
            { throw new UnauthorizedAccessException("Only the owning module may change table lifetime."); }
            var owner = contract == typeof(IPostgresTables) ? context.TargetId.Key.ToString()
                : (string)context.Request.GetArgument(0)!;
            var scope = JsonSerializer.Deserialize<string?[]>(owner)!;
            if (scope.Length != 2 || scope[0] != BrainScope.CurrentId())
            { throw new UnauthorizedAccessException("This storage belongs to another brain."); }
            if (contract == typeof(IPostgresTableLifetime) && source.Value.Key.ToString() != owner)
            { throw new UnauthorizedAccessException("This table belongs to another owner."); }
            if (contract == typeof(IPostgresTables) && context.InterfaceMethod.Name == nameof(IPostgresTables.Register)
                && source.Value.Key.ToString() != (string)context.Request.GetArgument(0)!)
            { throw new UnauthorizedAccessException("A table can only register itself."); }
        }
        await context.Invoke();
    }
}
