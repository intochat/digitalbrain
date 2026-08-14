namespace Brain.Runtime.Abstractions;

public interface IRuntimeProductModule
{
    RuntimeModuleDescriptor Module { get; }

    IReadOnlyList<RuntimeOperationDescriptor> Operations { get; }

    Task<string> ExecuteAsync(string operationId, string inputJson, CancellationToken cancellationToken);
}
