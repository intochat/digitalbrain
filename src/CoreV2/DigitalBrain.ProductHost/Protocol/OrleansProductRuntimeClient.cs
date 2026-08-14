using Brain.Runtime.Abstractions;

namespace DigitalBrain.ProductHost.Protocol;

public sealed class OrleansProductRuntimeClient(IClusterClient cluster) : IProductRuntimeClient
{
    private readonly IClusterClient _cluster = cluster;

    public Task<IReadOnlyList<RuntimeModuleDescriptor>> GetModulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.GetModulesAsync();
    }

    public Task<IReadOnlyList<RuntimeOperationDescriptor>> GetOperationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.GetOperationsAsync();
    }

    public Task<RuntimeActivityReceipt> InvokeAsync(
        RuntimeInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.InvokeAsync(invocation);
    }

    public Task<RuntimeActivitySnapshot?> GetActivityAsync(
        Guid activity,
        string workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _cluster.GetGrain<IProductActivityGrain>(activity).GetAsync(workspace);
    }

    private IProductRuntimeGrain Runtime => _cluster.GetGrain<IProductRuntimeGrain>("product");
}
