using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.Roslyn;

[GrainType("microsoft.roslyn")]
internal sealed class RoslynNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WorkspaceState> state,
    SolutionWorkspace workspace,
    ChangeSetEditor editor,
    TimeProvider clock)
    : Neuron, IRoslyn
{
    public async Task<WorkspaceReceipt> Open(OpenWorkspace request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SolutionPath))
        {
            throw new ArgumentException("solution path is blank", nameof(request));
        }

        var generation = state.State?.Generation ?? 0;
        if (request.ExpectedVersion is { } expected && expected != generation)
        {
            throw new InvalidOperationException(
                $"expected generation {expected} but the workspace is at {generation}. Read the workspace and retry with the generation it reports.");
        }

        // The load runs in the service; the neuron records the request so a restart reopens it, and reads
        // during the load answer from the durable snapshot and the last good map.
        await workspace.BeginOpenAsync(request.SolutionPath);
        var map = await CacheMapAsync();
        generation++;
        state.State = new WorkspaceState(request.SolutionPath, generation, clock.GetUtcNow(), map, workspace.Status.Detail);
        await state.WriteStateAsync();
        await PublishAsync(new WorkspaceChanged(this.GetPrimaryKeyString(), generation, workspace.Status.Phase));
        return new WorkspaceReceipt(this.GetPrimaryKeyString(), generation);
    }

    public async Task<WorkspaceReceipt> Reload()
    {
        var path = state.State?.SolutionPath ?? workspace.Status.SolutionPath;
        if (path is null)
        {
            throw new InvalidOperationException("No solution has been opened. Open a solution before reloading.");
        }

        if (workspace.Status.Phase == WorkspacePhase.NotOpened)
        {
            await workspace.BeginOpenAsync(path);
        }
        else
        {
            await workspace.BeginReloadAsync();
        }

        var map = await CacheMapAsync();
        var generation = (state.State?.Generation ?? 0) + 1;
        state.State = new WorkspaceState(path, generation, clock.GetUtcNow(), map, workspace.Status.Detail);
        await state.WriteStateAsync();
        await PublishAsync(new WorkspaceChanged(this.GetPrimaryKeyString(), generation, workspace.Status.Phase));
        return new WorkspaceReceipt(this.GetPrimaryKeyString(), generation);
    }

    [ReadOnly]
    public Task<WorkspaceSnapshot> Read()
    {
        var live = workspace.Status;
        return Task.FromResult(new WorkspaceSnapshot(
            state.State?.SolutionPath ?? live.SolutionPath,
            live.Phase,
            live.ProjectCount,
            live.DocumentCount,
            live.Detail ?? state.State?.Detail,
            state.State?.Generation ?? 0,
            live.ReloadNeeded));
    }

    [ReadOnly]
    public Task<SymbolSearchResult> FindSymbols(SymbolSearch query, CancellationToken cancellationToken = default)
        => workspace.FindSymbolsAsync(query, cancellationToken);

    [ReadOnly]
    public Task<ReferenceSearchResult> References(ReferenceSearch query, CancellationToken cancellationToken = default)
        => workspace.ReferencesAsync(query, cancellationToken);

    [ReadOnly]
    public Task<DiagnosticsResult> Diagnostics(DiagnosticsQuery query, CancellationToken cancellationToken = default)
        => workspace.DiagnosticsAsync(query, cancellationToken);

    [ReadOnly]
    public async Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default)
    {
        if (workspace.Status.Phase == WorkspacePhase.Ready)
        {
            return await workspace.MapAsync(query, cancellationToken);
        }

        return state.State?.LastMap ?? throw new WorkspaceNotReadyException(workspace.Status);
    }

    [ReadOnly]
    public Task<Skeleton> Skeleton(SkeletonQuery query, CancellationToken cancellationToken = default)
        => workspace.SkeletonAsync(query, cancellationToken);

    [ReadOnly]
    public Task<MemberSource> Member(MemberQuery query, CancellationToken cancellationToken = default)
        => workspace.MemberAsync(query, cancellationToken);

    [ReadOnly]
    public Task<CallersResult> Callers(CallersQuery query, CancellationToken cancellationToken = default)
        => workspace.CallersAsync(query, cancellationToken);

    [ReadOnly]
    public Task<SymbolSearchResult> Implementations(ImplementationsQuery query, CancellationToken cancellationToken = default)
        => workspace.ImplementationsAsync(query, cancellationToken);

    [ReadOnly]
    public Task<SymbolSearchResult> Derived(DerivedQuery query, CancellationToken cancellationToken = default)
        => workspace.DerivedAsync(query, cancellationToken);

    public async Task<EditCheck> CheckEdits(IReadOnlyList<EditRequest> edits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var outcome = await workspace.QueryAsync((solution, token) => editor.ApplyAsync(solution, edits, token), cancellationToken);
        return new EditCheck(outcome.Diagnostics, outcome.Diff, outcome.FailingEdit, outcome.Detail, outcome.HasErrors);
    }

    public async Task<EditCommit> CommitEdits(IReadOnlyList<EditRequest> edits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        EditOutcome? applied = null;
        try
        {
            var committed = await workspace.CommitAsync(async (solution, token) =>
            {
                applied = await editor.ApplyAsync(solution, edits, token);
                if (applied.HasErrors)
                {
                    throw new InvalidOperationException(applied.Detail ?? "the change set has errors");
                }

                return applied.Changed;
            }, cancellationToken);
            return new EditCommit(applied!.Diagnostics, applied.Diff, committed.WrittenPaths, committed.SnapshotVersion, null, false);
        }
        catch (InvalidOperationException error) when (applied is { HasErrors: true })
        {
            return new EditCommit(applied.Diagnostics, applied.Diff, [], 0, error.Message, true);
        }
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // A silo that restarted still knows which solution this workspace had open.
        if (state.State is { } current && workspace.Status.Phase == WorkspacePhase.NotOpened)
        {
            _ = workspace.BeginOpenAsync(current.SolutionPath);
        }

        return base.OnActivateAsync(cancellationToken);
    }

    private async Task<SolutionMap?> CacheMapAsync()
        => workspace.Status.Phase == WorkspacePhase.Ready
            ? await workspace.MapAsync(new MapQuery(), CancellationToken.None)
            : state.State?.LastMap;
}