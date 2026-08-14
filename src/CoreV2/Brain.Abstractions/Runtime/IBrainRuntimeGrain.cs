namespace Brain.Abstractions.Runtime;

public interface IBrainRuntimeGrain : IGrainWithStringKey
{
    Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync();

    Task<BrainActivityReceipt> InvokeAsync(BrainOperationInvocation invocation);

    Task<BrainActivitySnapshot?> GetActivityAsync(Guid activityId, string workspaceId);
}
