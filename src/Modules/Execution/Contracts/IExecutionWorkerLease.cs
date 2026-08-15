namespace DigitalBrain.Execution;

// Internal worker protocol kept separate from the public Apply/Read client surface.
[Alias("db.execution.worker-lease")]
public interface IExecutionWorkerLease : IGrainWithStringKey
{
    [Alias(nameof(RenewLease))]
    Task RenewLease(AttemptCursor cursor);
}
