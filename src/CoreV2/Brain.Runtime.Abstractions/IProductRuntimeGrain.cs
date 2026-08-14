namespace Brain.Runtime.Abstractions;

public interface IProductRuntimeGrain : IGrainWithStringKey
{
    Task<IReadOnlyList<RuntimeModuleDescriptor>> GetModulesAsync();

    Task<IReadOnlyList<RuntimeOperationDescriptor>> GetOperationsAsync();

    Task<RuntimeActivityReceipt> InvokeAsync(RuntimeInvocation invocation);
}
