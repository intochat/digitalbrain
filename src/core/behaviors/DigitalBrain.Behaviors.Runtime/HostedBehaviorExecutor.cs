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
                request.TriggerTypeName,
                request.TriggerJson,
                request.Capabilities,
                request.Time),
            cancellationToken);
    }
}
