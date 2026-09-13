using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("workspace")]
public interface ICodeWorkspace : INeuron
{
    [Alias("open")]
    Task<Accepted<WorkspaceReceipt>> Open(OpenWorkspace command);

    [Alias("reload")]
    Task<Accepted<WorkspaceReceipt>> Reload(ReloadWorkspace command);

    [ReadOnly]
    [Alias("read")]
    Task<WorkspaceSnapshot> Read();

    [ReadOnly]
    [Alias("find-symbols")]
    Task<SymbolSearchResult> FindSymbols(SymbolSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("references")]
    Task<ReferenceSearchResult> References(ReferenceSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("diagnostics")]
    Task<DiagnosticsResult> Diagnostics(DiagnosticsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("map")]
    Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("skeleton")]
    Task<Skeleton> Skeleton(SkeletonQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("member")]
    Task<MemberSource> Member(MemberQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("callers")]
    Task<CallersResult> Callers(CallersQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("implementations")]
    Task<SymbolSearchResult> Implementations(ImplementationsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("derived")]
    Task<SymbolSearchResult> Derived(DerivedQuery query, CancellationToken cancellationToken = default);
}
