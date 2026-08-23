using DigitalBrain.Abstractions.Execution;

namespace DigitalBrain.Execution;

public interface ICapabilityHandler
{
    CapabilityId Id { get; }

    Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        string requestJson,
        CancellationToken cancellationToken);
}
