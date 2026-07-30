namespace DigitalBrain.Behaviors;

internal static class BehaviorOperationCancellation
{
    public static CancellationTokenSource Link(
        CancellationToken attemptCancellation,
        CancellationToken callerToken)
    {
        if (!attemptCancellation.CanBeCanceled)
        {
            throw new ArgumentException(
                "Worker attempt cancellation is required for every behavior operation.",
                nameof(attemptCancellation));
        }

        if (!callerToken.CanBeCanceled)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(attemptCancellation);
        }

        return CancellationTokenSource.CreateLinkedTokenSource(attemptCancellation, callerToken);
    }
}
