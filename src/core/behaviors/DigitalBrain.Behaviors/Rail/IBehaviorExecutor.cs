namespace DigitalBrain.Behaviors;

public interface IBehaviorExecutor
{
    ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken);
}
