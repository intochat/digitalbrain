namespace Brain.Abstractions.Runtime;

public interface IBrainRuntimeGrain : IGrainWithStringKey
{
    Task<IReadOnlyList<BrainModuleDescriptor>> GetModulesAsync();

    Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync();

    Task<BrainActivityReceipt> InvokeAsync(BrainOperationInvocation invocation);

    Task<BrainChildOperationResult> InvokeWithinActivityAsync(
        Guid activityId,
        BrainOperationInvocation invocation);

    Task<BrainActivitySnapshot?> GetActivityAsync(Guid activityId, string workspaceId);
}
