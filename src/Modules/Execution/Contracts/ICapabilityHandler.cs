using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

public interface ICapabilityHandler
{
    CapabilityId Id { get; }

    Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken);
}
