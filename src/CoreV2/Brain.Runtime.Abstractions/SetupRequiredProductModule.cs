namespace Brain.Runtime.Abstractions;

public sealed class SetupRequiredProductModule : IRuntimeProductModule
{
    public SetupRequiredProductModule(string id, string displayName, string setupMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(setupMessage);
        Module = new RuntimeModuleDescriptor(
            id,
            displayName,
            RuntimeModuleStatus.NeedsSetup,
            setupMessage);
    }

    public RuntimeModuleDescriptor Module { get; }

    public IReadOnlyList<RuntimeOperationDescriptor> Operations { get; } = [];

    public Task<string> ExecuteAsync(
        string operationId,
        string inputJson,
        RuntimeModuleExecutionContext context,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException(
            $"Module '{Module.Id}' needs setup before operation '{operationId}' can run.");
}
