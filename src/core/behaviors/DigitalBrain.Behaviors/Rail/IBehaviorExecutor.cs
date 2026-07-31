namespace DigitalBrain.Behaviors;

public interface IBehaviorExecutor
{
    ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken);

    ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken);
}
