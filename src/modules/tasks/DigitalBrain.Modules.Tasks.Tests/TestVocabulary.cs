using System.Collections.Concurrent;
using System.ComponentModel;
using DigitalBrain.Abstractions;

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
[Alias("tasks.tests.progress-goal")]
public sealed record ProgressGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.user-action-park-goal")]
public sealed record UserActionParkGoal(
    [property: Id(0)] string Label,
    [property: Id(1)] string ModuleId,
    [property: Id(2)] string DisplayText,
    [property: Id(3)] DateTimeOffset ExpiresAt,
    [property: Id(4)] string ModuleName) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.user-action-continue-success-goal")]
public sealed record UserActionContinueSuccessGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.complete-parked-user-action")]
[Description("Harness asks the completer worker to complete a parked user action")]
public sealed record CompleteParkedUserAction(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] ProtectedPayloadReference ActionReference,
    [property: Id(2)] Guid ActionEpoch,
    [property: Id(3)] long ExpectedParkRevision) : Synapse;

[GenerateSerializer]
[Alias("tasks.tests.deny-parked-user-action")]
[Description("Harness asks the completer worker to deny a parked user action")]
public sealed record DenyParkedUserAction(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] ProtectedPayloadReference ActionReference,
    [property: Id(2)] Guid ActionEpoch,
    [property: Id(3)] long ExpectedParkRevision) : Synapse;

[GenerateSerializer]
[Alias("tasks.tests.probe-user-action-completion-disposition")]
[Description("Harness completer delivers CompleteUserAction and records the exception disposition")]
public sealed record ProbeUserActionCompletionDisposition(
    [property: Id(0)] Guid ProbeId,
    [property: Id(1)] NeuronId Task,
    [property: Id(2)] ProtectedPayloadReference ActionReference,
    [property: Id(3)] Guid ActionEpoch,
    [property: Id(4)] long ExpectedParkRevision) : Synapse;

internal static class UserActionCompletionDispositionProbe
{
    private static readonly ConcurrentDictionary<Guid, string> Dispositions = new();

    internal static void Clear(Guid probeId) => Dispositions.TryRemove(probeId, out _);

    internal static void Record(Guid probeId, string disposition)
        => Dispositions[probeId] = disposition;

    internal static bool TryRead(Guid probeId, out string disposition)
        => Dispositions.TryGetValue(probeId, out disposition!);
}

internal sealed record ContinueCancellationObservation(
    bool HandlerTokenCanBeCanceled,
    bool TurnTokenCanBeCanceled,
    bool HandlerTokenIsDefault,
    bool TurnTokenIsDefault,
    bool RestagedContinue,
    bool HandlerTokenWasAlreadyCanceled,
    bool HandlerTokenCanceledDuringValidationGate,
    int ValidationGateEntries,
    bool DeliveryAcknowledgedByRestage);

// Test-only latch: arm so outbox-delivered DispatchWorkerContinue blocks in a cancellation-aware
// validation gate until the production Deliver attempt token cancels (DeliveryAttemptTimeout).
internal static class ContinueCancellationGate
{
    private static readonly ConcurrentDictionary<NeuronId, int> ArmedEntryCounts = new();

    internal static void Arm(NeuronId worker) => ArmedEntryCounts[worker] = 0;

    internal static void Disarm(NeuronId worker) => ArmedEntryCounts.TryRemove(worker, out _);

    internal static bool IsArmed(NeuronId worker) => ArmedEntryCounts.ContainsKey(worker);

    internal static int Enter(NeuronId worker)
        => ArmedEntryCounts.AddOrUpdate(worker, _ => 1, static (_, count) => count + 1);

    internal static int EntryCount(NeuronId worker)
        => ArmedEntryCounts.TryGetValue(worker, out var count) ? count : 0;
}

internal static class ContinueCancellationProbe
{
    private static readonly ConcurrentDictionary<NeuronId, ContinueCancellationObservation> Observations = new();

    internal static void Clear(NeuronId worker) => Observations.TryRemove(worker, out _);

    internal static void Record(NeuronId worker, ContinueCancellationObservation observation)
        => Observations[worker] = observation;

    internal static bool TryRead(NeuronId worker, out ContinueCancellationObservation? observation)
        => Observations.TryGetValue(worker, out observation);
}

[GenerateSerializer]
[Alias("tasks.tests.result")]
public sealed record TestResult(
    [property: Id(0)] string Label) : Result;

[GenerateSerializer]
[Alias("tasks.tests.failure")]
public sealed record TestFailure(
    [property: Id(0)] string Label) : Failure;

[GenerateSerializer]
[Alias("tasks.tests.prepare-operation-probe")]
[Description("Probe that asks a worker to prepare a task operation")]
public sealed record PrepareOperationProbe(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] AttemptId Attempt,
    [property: Id(2)] int Sequence,
    [property: Id(3)] TaskOperationEdge Edge,
    [property: Id(4)] ProtectedPayloadReference RequestPayload) : Synapse;

[GenerateSerializer]
[Alias("tasks.tests.transition-operation-probe")]
[Description("Probe that asks a worker to transition a task operation phase")]
public sealed record TransitionOperationProbe(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] AttemptId Attempt,
    [property: Id(2)] int Sequence,
    [property: Id(3)] TaskOperationPhase ExpectedPhase,
    [property: Id(4)] TaskOperationPhase Phase,
    [property: Id(5)] ProtectedPayloadReference? ResponsePayload) : Synapse;

[GenerateSerializer]
[Alias("tasks.tests.late-attempt-succeeded-probe")]
[Description("Probe that injects a late AttemptSucceeded from the worker after cancel")]
public sealed record LateAttemptSucceededProbe(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] AttemptId Attempt,
    [property: Id(2)] long Revision) : Synapse;

public static class TaskFixtures
{
    public static readonly TestResult Done = new("done");
    public static readonly TestResult StaleSuccess = new("stale-success");
    public static readonly TestFailure Retryable = new("retryable");

    public static readonly TaskPolicy SingleAttempt = new(MaximumAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1), Deadline: null);

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    public static readonly TaskPolicy TwoAttempts = new(MaximumAttempts: 2, RetryDelay: RetryDelay, Deadline: null);
}
