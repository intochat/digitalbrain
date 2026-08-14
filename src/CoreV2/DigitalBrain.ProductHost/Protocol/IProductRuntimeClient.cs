using Brain.Runtime.Abstractions;

namespace DigitalBrain.ProductHost.Protocol;

public interface IProductRuntimeClient
{
    Task<IReadOnlyList<RuntimeModuleDescriptor>> GetModulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RuntimeOperationDescriptor>> GetOperationsAsync(CancellationToken cancellationToken);

    Task<RuntimeActivityReceipt> InvokeAsync(
        RuntimeInvocation invocation,
        CancellationToken cancellationToken);

    Task<RuntimeActivitySnapshot?> GetActivityAsync(
        Guid activity,
        string workspace,
        CancellationToken cancellationToken);
}
