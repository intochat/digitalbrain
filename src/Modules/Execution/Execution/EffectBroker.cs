using DigitalBrain.Abstractions.Execution;

namespace DigitalBrain.Execution;

public sealed class EffectBroker(IEnumerable<ICapabilityHandler> handlers)
{
    private readonly Dictionary<string, ICapabilityHandler> _handlers = handlers
        .ToDictionary(static handler => handler.Id.Value, StringComparer.Ordinal);

    public bool IsRegistered(CapabilityId capability)
        => _handlers.ContainsKey(capability.Value);

    public Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        CapabilityId capability,
        string requestJson,
        IReadOnlyList<CapabilityId> grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsGranted(grants, capability))
        {
            throw new InvalidOperationException(
                $"Capability '{capability}' is not granted for execution '{executionId}'.");
        }

        if (!_handlers.TryGetValue(capability.Value, out var handler))
        {
            throw new InvalidOperationException(
                $"No handler is registered for capability '{capability}'.");
        }

        return handler.InvokeAsync(executionId, requestJson, cancellationToken);
    }

    private static bool IsGranted(IReadOnlyList<CapabilityId> grants, CapabilityId capability)
    {
        for (var i = 0; i < grants.Count; i++)
        {
            if (string.Equals(grants[i].Value, capability.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
