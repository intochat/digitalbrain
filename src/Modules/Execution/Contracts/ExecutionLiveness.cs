namespace DigitalBrain.Execution;

public static class ExecutionLiveness
{
    public static TimeSpan WorkerLeaseTimeout { get; } = TimeSpan.FromSeconds(15);

    public static TimeSpan WorkerLeaseRenewalInterval { get; } = TimeSpan.FromSeconds(5);
}
