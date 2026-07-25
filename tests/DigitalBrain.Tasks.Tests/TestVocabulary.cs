namespace DigitalBrain.Tasks.Tests;

[GenerateSerializer]
[Alias("tasks.tests.goal")]
public sealed record TestGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.stale-probe-goal")]
public sealed record StaleProbeGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.retryable-failure-goal")]
public sealed record RetryableFailureGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.success-goal")]
public sealed record SuccessGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.result")]
public sealed record TestResult(
    [property: Id(0)] string Label) : Result;

[GenerateSerializer]
[Alias("tasks.tests.failure")]
public sealed record TestFailure(
    [property: Id(0)] string Label) : Failure;

public static class TaskFixtures
{
    public static readonly TestResult Done = new("done");
    public static readonly TestResult StaleSuccess = new("stale-success");
    public static readonly TestFailure Retryable = new("retryable");

    public static readonly TaskPolicy SingleAttempt = new(
        MaximumAttempts: 1,
        RetryDelay: TimeSpan.FromSeconds(1),
        Deadline: null);

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    public static readonly TaskPolicy TwoAttempts = new(
        MaximumAttempts: 2,
        RetryDelay: RetryDelay,
        Deadline: null);
}
