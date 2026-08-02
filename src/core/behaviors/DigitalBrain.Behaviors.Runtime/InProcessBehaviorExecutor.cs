
namespace DigitalBrain.Behaviors.Runtime;

internal sealed class InProcessBehaviorExecutor : IBehaviorExecutor
{
    public ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new BehaviorExecutionOutcome(
                false,
                BehaviorExecutionCodes.InProcessClosed));
    }

    public ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new BehaviorExecutionOutcome(
                false,
                BehaviorExecutionCodes.InProcessClosed));
    }
}
