using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

public sealed class ExecutionSession(
    ExecutionId id,
    OwnerId owner,
    IGrainFactory grains,
    EffectBroker broker,
    IReadOnlyList<CapabilityId> grants)
{
    public ExecutionId Id => id;

    public Task<ContextEntry?> QueryAsync(ContextPath path)
        => Context().Query(new ContextQuery(path));

    public Task ApplyDeltaAsync(ContextDelta delta)
        => Context().ApplyDelta(delta);

    public Task<ContextDelta> CallAsync(
        CapabilityId capability,
        string requestJson,
        CancellationToken cancellationToken)
        => broker.InvokeAsync(id, capability, requestJson, grants, cancellationToken);

    private IExecutionContext Context()
        => grains.GetGrain<IExecutionContext>(
            EntityId.For<IExecutionContext>(owner, id.ToString()).ToGrainId());
}
