using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

public sealed class ExecutionSession(
    ExecutionId id,
    OwnerId owner,
    IGrainFactory grains)
{
    public ExecutionId Id => id;

    public Task<ContextEntry?> QueryAsync(ContextPath path)
        => Context().Query(new ContextQuery(path));

    public Task ApplyDeltaAsync(ContextDelta delta)
        => Context().ApplyDelta(delta);

    private IExecutionContext Context()
        => grains.GetGrain<IExecutionContext>(
            EntityId.For<IExecutionContext>(owner, id.ToString()).ToGrainId());
}
