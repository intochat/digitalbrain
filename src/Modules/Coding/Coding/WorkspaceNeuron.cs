using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
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
        return Task.FromResult(new WorkspaceSnapshot(State?.SolutionPath ?? live.SolutionPath, live.Phase, live.ProjectCount, live.DocumentCount, live.Detail, State?.Generation ?? 0, ReloadNeeded: false));
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
    public Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default)
        => workspace.MapAsync(query, cancellationToken);

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
                    _ = workspace.BeginOpenAsync(body.SolutionPath);
                    await SaveAsync(new WorkspaceState(body.SolutionPath, (State?.Generation ?? 0) + 1, TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
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
                    await SaveAsync(new WorkspaceState(path, (State?.Generation ?? 0) + 1, TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
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
