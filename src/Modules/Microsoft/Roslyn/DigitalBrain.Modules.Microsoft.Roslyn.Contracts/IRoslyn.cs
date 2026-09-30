using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.Roslyn;

[Alias("microsoft.roslyn")]
public interface IRoslyn : INeuron
{
    Task<WorkspaceReceipt> Open(OpenWorkspace request);

    Task<WorkspaceReceipt> Reload();

    [ReadOnly]
    Task<WorkspaceSnapshot> Read();

    [ReadOnly]
    Task<SymbolSearchResult> FindSymbols(SymbolSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<ReferenceSearchResult> References(ReferenceSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<DiagnosticsResult> Diagnostics(DiagnosticsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<Skeleton> Skeleton(SkeletonQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<MemberSource> Member(MemberQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<CallersResult> Callers(CallersQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<SymbolSearchResult> Implementations(ImplementationsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<SymbolSearchResult> Derived(DerivedQuery query, CancellationToken cancellationToken = default);

    Task<EditCheck> CheckEdits(IReadOnlyList<EditRequest> edits, CancellationToken cancellationToken = default);

    Task<EditCommit> CommitEdits(IReadOnlyList<EditRequest> edits, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("microsoft.roslyn.edit-check")]
public sealed record EditCheck(
    [property: Id(0)] IReadOnlyList<DiagnosticHit> Diagnostics,
    [property: Id(1)] string Diff,
    [property: Id(2)] int? FailingEdit,
    [property: Id(3)] string? Detail,
    [property: Id(4)] bool HasErrors);

[GenerateSerializer, Alias("microsoft.roslyn.edit-commit")]
public sealed record EditCommit(
    [property: Id(0)] IReadOnlyList<DiagnosticHit> Diagnostics,
    [property: Id(1)] string Diff,
    [property: Id(2)] IReadOnlyList<string> WrittenPaths,
    [property: Id(3)] long SnapshotVersion,
    [property: Id(4)] string? Detail,
    [property: Id(5)] bool HasErrors);
