using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("workspace")]
public interface ICodeWorkspace : INeuron
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
}