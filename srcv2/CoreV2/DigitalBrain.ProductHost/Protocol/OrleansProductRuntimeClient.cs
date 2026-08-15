using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;

namespace DigitalBrain.ProductHost.Protocol;

public sealed class OrleansProductRuntimeClient(IClusterClient cluster) : IProductRuntimeClient
{
    private readonly IClusterClient _cluster = cluster;

    public Task<IReadOnlyList<BrainModuleDescriptor>> GetModulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.GetModulesAsync();
    }

    public Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.GetOperationsAsync();
    }

    public Task<BrainActivityReceipt> InvokeAsync(
        BrainOperationInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.InvokeAsync(invocation);
    }

    public Task<BrainActivitySnapshot?> GetActivityAsync(
        Guid activity,
        string workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.GetActivityAsync(activity, workspace);
    }

    public Task<BrainJournalPage> GetJournalAsync(
        Guid activity,
        string workspace,
        long afterSequence,
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _cluster
            .GetGrain<IBrainActivityGrain>($"{workspace}/{activity:n}")
            .ReadJournalAsync(workspace, afterSequence, take);
    }

    public Task<BrainSnapshot> GetBrainAsync(
        string workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _cluster.GetGrain<IBrainGraphGrain>(workspace).SnapshotAsync(workspace);
    }

    private IBrainRuntimeGrain Runtime => _cluster.GetGrain<IBrainRuntimeGrain>("brain");
}
