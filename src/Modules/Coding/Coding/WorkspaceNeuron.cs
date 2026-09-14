using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType(CodingVocabulary.WorkspaceType)]
internal sealed class WorkspaceNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<WorkspaceState>> state,
    SolutionWorkspace workspace)
    : Neuron<WorkspaceState>(runtime, state), ICodeWorkspace
{
    // 600 attempts at the reaction's 1s wait is ten minutes; long enough for any real load, bounded so a
    // solution that never becomes ready cannot keep re-scheduling this reaction forever.
    private const int MaxMappingAttempts = 600;

    public Task<Accepted<WorkspaceReceipt>> Open(OpenWorkspace command) => ExecuteCommandAsync(
        Descriptor("open"), command, CodingJson.Default.OpenWorkspace, CodingJson.Default.AcceptedWorkspaceReceipt, arguments =>
        {
            if (string.IsNullOrWhiteSpace(arguments.SolutionPath))
            {
                throw new CommandRejectedException(arguments.Id, "solution path is blank", "Provide the full solution path of a .slnx or .sln file.");
            }

            var generation = State?.Generation ?? 0;
            if (arguments.ExpectedVersion is { } expected && expected != generation)
            {
                throw new CommandRejectedException(arguments.Id, $"expected generation {expected} but the workspace is at {generation}",
                    "Read the workspace and retry with the generation it reports.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.WorkspaceOpening, new OpeningBody(arguments.SolutionPath), CodingJson.Default.OpeningBody));
            return new Accepted<WorkspaceReceipt>(new WorkspaceReceipt(Id.Name, generation), work);
        });

    public Task<Accepted<WorkspaceReceipt>> Reload(ReloadWorkspace command) => ExecuteCommandAsync(
        Descriptor("reload"), command, CodingJson.Default.ReloadWorkspace, CodingJson.Default.AcceptedWorkspaceReceipt, arguments =>
        {
            if (State is null && workspace.Status.Phase == WorkspacePhase.NotOpened)
            {
                throw new CommandRejectedException(arguments.Id, "no solution has been opened", "Open a solution before reloading.");
            }

            var work = Schedule(Signal.Create(CodingVocabulary.WorkspaceReloading, "{}"));
            return new Accepted<WorkspaceReceipt>(new WorkspaceReceipt(Id.Name, State?.Generation ?? 0), work);
        });

    [ReadOnly]
    public Task<WorkspaceSnapshot> Read()
    {
        var live = workspace.Status;
        return Task.FromResult(new WorkspaceSnapshot(State?.SolutionPath ?? live.SolutionPath, live.Phase, live.ProjectCount, live.DocumentCount, live.Detail, State?.Generation ?? 0, live.ReloadNeeded));
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
            return await workspace.MapAsync(query, cancellationToken).ConfigureAwait(true);
        }

        return State?.LastMap ?? throw new WorkspaceNotReadyException(workspace.Status);
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

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case CodingVocabulary.WorkspaceOpening:
                {
                    if (Body(delivery, CodingJson.Default.OpeningBody) is not { } body)
                    {
                        return;
                    }

                    // The load runs in the service; the reaction only records the request so a restart re-opens.
                    // The previous LastMap carries forward: a reload in flight still answers Map() from the last good snapshot.
                    _ = workspace.BeginOpenAsync(body.SolutionPath);
                    Schedule(Signal.FromJson(CodingVocabulary.WorkspaceMapping, new MappingBody(0), CodingJson.Default.MappingBody));
                    await SaveAsync(new WorkspaceState(body.SolutionPath, (State?.Generation ?? 0) + 1, TimeProvider.GetUtcNow(), State?.LastMap), cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.WorkspaceReloading:
                {
                    var path = State?.SolutionPath ?? workspace.Status.SolutionPath;
                    if (path is null)
                    {
                        return;
                    }

                    _ = workspace.Status.Phase == WorkspacePhase.NotOpened ? workspace.BeginOpenAsync(path) : workspace.BeginReloadAsync();
                    Schedule(Signal.FromJson(CodingVocabulary.WorkspaceMapping, new MappingBody(0), CodingJson.Default.MappingBody));
                    await SaveAsync(new WorkspaceState(path, (State?.Generation ?? 0) + 1, TimeProvider.GetUtcNow(), State?.LastMap), cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.WorkspaceMapping:
                {
                    if (State is not { } current)
                    {
                        return;
                    }

                    var attempt = Body(delivery, CodingJson.Default.MappingBody)?.Attempt ?? 0;
                    switch (workspace.Status.Phase)
                    {
                        case WorkspacePhase.Ready:
                            var map = await workspace.MapAsync(new MapQuery(), cancellationToken).ConfigureAwait(true);
                            await SaveAsync(current with { LastMap = map }, cancellationToken).ConfigureAwait(true);
                            break;
                        case WorkspacePhase.Opening when attempt < MaxMappingAttempts:
                            // One bounded wait per reaction keeps reads flowing; the next reaction looks again.
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                            Schedule(Signal.FromJson(CodingVocabulary.WorkspaceMapping, new MappingBody(attempt + 1), CodingJson.Default.MappingBody));
                            break;
                        case WorkspacePhase.Opening:
                            ServiceProvider.GetService<ILogger<WorkspaceNeuron>>()?.LogDebug(
                                "Gave up caching the map for {SolutionPath} after {Attempts} attempts; the solution is still opening.", current.SolutionPath, attempt);
                            break;
                        default:
                            break;
                    }

                    break;
                }
            default:
                return;
        }
    }

    protected override Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        // A silo that restarted still knows which solution this workspace had open.
        if (State is { } current && workspace.Status.Phase == WorkspacePhase.NotOpened)
        {
            _ = workspace.BeginOpenAsync(current.SolutionPath);
        }

        return Task.CompletedTask;
    }
}
