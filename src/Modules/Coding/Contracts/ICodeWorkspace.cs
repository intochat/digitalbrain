using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("workspace")]
public interface ICodeWorkspace : INeuron
{
    [Alias("open")]
    [NeuronTool]
    Task<Accepted<WorkspaceReceipt>> Open(OpenWorkspace command);

    [Alias("reload")]
    [NeuronTool]
    Task<Accepted<WorkspaceReceipt>> Reload(ReloadWorkspace command);

    [ReadOnly]
    [Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<WorkspaceSnapshot> Read();

    [ReadOnly]
    [Alias("find-symbols")]
    [NeuronTool(IsReadOnly = true)]
    Task<SymbolSearchResult> FindSymbols(SymbolSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("references")]
    [NeuronTool(IsReadOnly = true)]
    Task<ReferenceSearchResult> References(ReferenceSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("diagnostics")]
    [NeuronTool(IsReadOnly = true)]
    Task<DiagnosticsResult> Diagnostics(DiagnosticsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("map")]
    [NeuronTool(IsReadOnly = true)]
    Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("skeleton")]
    [NeuronTool(IsReadOnly = true)]
    Task<Skeleton> Skeleton(SkeletonQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("member")]
    [NeuronTool(IsReadOnly = true)]
    Task<MemberSource> Member(MemberQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("callers")]
    [NeuronTool(IsReadOnly = true)]
    Task<CallersResult> Callers(CallersQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("implementations")]
    [NeuronTool(IsReadOnly = true)]
    Task<SymbolSearchResult> Implementations(ImplementationsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("derived")]
    [NeuronTool(IsReadOnly = true)]
    Task<SymbolSearchResult> Derived(DerivedQuery query, CancellationToken cancellationToken = default);
}
