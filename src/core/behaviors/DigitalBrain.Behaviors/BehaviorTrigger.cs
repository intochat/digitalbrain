namespace DigitalBrain.Behaviors;

public sealed class BehaviorTrigger<TTrigger>
{
    public BehaviorTrigger(TTrigger value, CancellationToken attemptCancellation)
    {
        if (!attemptCancellation.CanBeCanceled)
        {
            throw new ArgumentException(
                "Worker attempt cancellation is required for every behavior operation.",
                nameof(attemptCancellation));
        }

        Value = value;
        AttemptCancellation = attemptCancellation;
    }

    public TTrigger Value { get; }

    public CancellationToken AttemptCancellation { get; }
}
