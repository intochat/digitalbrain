namespace DigitalBrain.Behaviors;

internal sealed class HostedBehaviorExecutor(IBehaviorHostGateway host) : IBehaviorExecutor
{
    public ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return host.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                request.Metadata,
                request.ArtifactHash,
                request.Task,
                request.Attempt,
                request.TriggerTypeName,
                request.TriggerPayload,
                request.Capabilities,
                request.UtcNow),
            cancellationToken);
    }

    public ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return host.ExecuteLegacyAsync(request, cancellationToken);
    }
}
