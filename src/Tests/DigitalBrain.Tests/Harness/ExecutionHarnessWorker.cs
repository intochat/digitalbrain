using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Execution;

namespace DigitalBrain.Tests.Harness;

[GenerateSerializer]
[Alias("probe.execution-goal")]
public sealed record ProbeGoal([property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("probe.execution-result")]
public sealed record ProbeResult([property: Id(0)] string Label) : Result;

[GenerateSerializer]
[Alias("probe.execution-failure")]
public sealed record ProbeFailure([property: Id(0)] string Reason) : Failure;

[GenerateSerializer]
[Alias("probe.worker-resume")]
public sealed record ResumeWorkerBlocker([property: Id(0)] string Token) : Synapse;

[GenerateSerializer]
[Alias("probe.worker-mark-uncertain")]
public sealed record ForceUncertainWrite([property: Id(0)] string OperationKey) : Synapse;

public enum HarnessWorkerScript
{
    SucceedOnAccept,
    WaitForOauth,
    UncertainExternalWrite,
    CancelAware,
    // Accept and stay Running; Cancel is a no-op (no AttemptCancelled) — stuck Cancelling proof.
    IgnoreCancel,
    CompleteThenRetryableFail,
    // Dispatch external effect, then retryable AttemptFailed without Transition→Uncertain (messy fail).
    DispatchThenRetryableFail,
}

public static class HarnessWorkerControl
{
    private static readonly ConcurrentDictionary<string, HarnessWorkerScript> Scripts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> AcceptCounts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> PrepareCounts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> ExternalEffectCounts = new(StringComparer.Ordinal);

    public static void Configure(string workerName, HarnessWorkerScript script)
    {
        Scripts[workerName] = script;
        AcceptCounts[workerName] = 0;
        PrepareCounts[workerName] = 0;
        ExternalEffectCounts[workerName] = 0;
    }

    public static HarnessWorkerScript ScriptFor(string workerName)
        => Scripts.TryGetValue(workerName, out var script) ? script : HarnessWorkerScript.SucceedOnAccept;

    public static int AcceptCount(string workerName)
        => AcceptCounts.TryGetValue(workerName, out var count) ? count : 0;

    public static int PrepareCount(string workerName)
        => PrepareCounts.TryGetValue(workerName, out var count) ? count : 0;

    public static int ExternalEffectCount(string workerName)
        => ExternalEffectCounts.TryGetValue(workerName, out var count) ? count : 0;

    public static void IncrementAccept(string workerName)
        => AcceptCounts.AddOrUpdate(workerName, 1, static (_, current) => current + 1);

    public static void IncrementPrepare(string workerName)
        => PrepareCounts.AddOrUpdate(workerName, 1, static (_, current) => current + 1);

    public static void IncrementExternalEffect(string workerName)
        => ExternalEffectCounts.AddOrUpdate(workerName, 1, static (_, current) => current + 1);
}

// Grain type "worker" matches NeuronId.GrainTypeNameOf(typeof(IWorker)).
[GrainType("worker")]
internal sealed class HarnessExecutionWorker :
    WorkerNeuron,
    IHandle<ResumeWorkerBlocker>,
    IHandle<ForceUncertainWrite>
{
    private AttemptRequest? _active;
    private string? _pendingOauthToken;

    public override async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _active = request;
        HarnessWorkerControl.IncrementAccept(Id.Name);

        await SendAsync(
            request.Execution,
            new AttemptAccepted(request.Execution, request.Worker, request.Attempt, request.Revision))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        switch (HarnessWorkerControl.ScriptFor(Id.Name))
        {
            case HarnessWorkerScript.WaitForOauth:
                _pendingOauthToken = "oauth";
                await SendAsync(
                    request.Execution,
                    new AttemptWaiting(
                        request.Execution,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new InputRequired(new BlockerId(Guid.NewGuid()))))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                break;

            case HarnessWorkerScript.UncertainExternalWrite:
                await RunUncertainWriteAsync(request, "external-write")
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                break;

            case HarnessWorkerScript.CompleteThenRetryableFail:
                // First accept: complete stable-write then fail retryably.
                // Later accepts: adversarial re-Prepare of the same key (must short-circuit Completed)
                // and must not re-drive the external effect.
                if (HarnessWorkerControl.AcceptCount(Id.Name) == 1)
                {
                    await PrepareDispatchCompleteAsync(request, "stable-write", executeExternalEffect: true)
                        .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                    await SendAsync(
                        request.Execution,
                        new AttemptFailed(
                            request.Execution,
                            request.Worker,
                            request.Attempt,
                            request.Revision,
                            new ProbeFailure("retry-me"),
                            Retryable: true))
                        .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                }
                else
                {
                    await PrepareDispatchCompleteAsync(request, "stable-write", executeExternalEffect: false)
                        .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                    await SendAsync(
                        request.Execution,
                        new AttemptSucceeded(
                            request.Execution,
                            request.Worker,
                            request.Attempt,
                            request.Revision,
                            new ProbeResult("retried"),
                            Evidence: []))
                        .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                }

                break;

            case HarnessWorkerScript.DispatchThenRetryableFail:
                // Messy fail: Dispatched (external started) then retryable AttemptFailed
                // without cooperative Transition→Uncertain.
                await PrepareAndDispatchAsync(request, "messy-write")
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                await SendAsync(
                    request.Execution,
                    new AttemptFailed(
                        request.Execution,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new ProbeFailure("crashed-after-dispatch"),
                        Retryable: true))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                break;

            case HarnessWorkerScript.CancelAware:
            case HarnessWorkerScript.IgnoreCancel:
                // Stay running until Cancel arrives (IgnoreCancel never acks Cancel).
                break;

            default:
                await SendAsync(
                    request.Execution,
                    new AttemptSucceeded(
                        request.Execution,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new ProbeResult(((ProbeGoal)request.Goal).Label),
                        Evidence: []))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                break;
        }
    }

    public override async Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        _active = new AttemptRequest(cursor.Execution, cursor.Worker, cursor.Attempt, cursor.Revision, _active?.Goal ?? new ProbeGoal("continued"));

        if (HarnessWorkerControl.ScriptFor(Id.Name) is HarnessWorkerScript.UncertainExternalWrite)
        {
            // After ResolveOperation PermitRetry/Completed, finish cleanly.
            await SendAsync(
                cursor.Execution,
                new AttemptSucceeded(
                    cursor.Execution,
                    cursor.Worker,
                    cursor.Attempt,
                    cursor.Revision,
                    new ProbeResult("reconciled"),
                    Evidence: []))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        await SendAsync(
            cursor.Execution,
            new AttemptSucceeded(
                cursor.Execution,
                cursor.Worker,
                cursor.Attempt,
                cursor.Revision,
                new ProbeResult("resumed"),
                Evidence: []))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public override async Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        if (HarnessWorkerControl.ScriptFor(Id.Name) is HarnessWorkerScript.IgnoreCancel)
        {
            return;
        }

        await SendAsync(
            cursor.Execution,
            new AttemptCancelled(cursor.Execution, cursor.Worker, cursor.Attempt, cursor.Revision))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ResumeWorkerBlocker synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (_active is null || _pendingOauthToken is null)
        {
            return;
        }

        if (!string.Equals(synapse.Token, _pendingOauthToken, StringComparison.Ordinal))
        {
            return;
        }

        _pendingOauthToken = null;
        var active = _active;
        await SendAsync(
            active.Execution,
            new AttemptProgressed(active.Execution, active.Worker, active.Attempt, active.Revision))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ForceUncertainWrite synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        if (_active is null)
        {
            return;
        }

        await RunUncertainWriteAsync(_active, synapse.OperationKey)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task RunUncertainWriteAsync(AttemptRequest request, string operationKey)
    {
        HarnessWorkerControl.IncrementPrepare(Id.Name);
        var edge = new OperationEdge(
            Target: request.Execution,
            RequestSynapseId: "probe.external-write",
            RequestSchemaVersion: 1,
            ResponseSynapseId: "probe.external-write-result",
            ResponseSchemaVersion: 1);
        var payload = new ProtectedPayloadReference(Guid.NewGuid());

        await SendAsync(
            request.Execution,
            new PrepareOperation(request.Attempt, operationKey, edge, payload))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await SendAsync(
            request.Execution,
            new TransitionOperation(
                request.Attempt,
                operationKey,
                OperationPhase.Prepared,
                OperationPhase.Dispatched,
                ResponsePayload: null,
                RedactedSummary: null))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await SendAsync(
            request.Execution,
            new TransitionOperation(
                request.Attempt,
                operationKey,
                OperationPhase.Dispatched,
                OperationPhase.Uncertain,
                ResponsePayload: null,
                RedactedSummary: "unknown outcome"))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task PrepareAndDispatchAsync(AttemptRequest request, string operationKey)
    {
        HarnessWorkerControl.IncrementPrepare(Id.Name);
        var edge = ExternalWriteEdge(request.Execution);
        var requestPayload = new ProtectedPayloadReference(Guid.NewGuid());

        await SendAsync(
            request.Execution,
            new PrepareOperation(request.Attempt, operationKey, edge, requestPayload))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        HarnessWorkerControl.IncrementExternalEffect(Id.Name);
        await SendAsync(
            request.Execution,
            new TransitionOperation(
                request.Attempt,
                operationKey,
                OperationPhase.Prepared,
                OperationPhase.Dispatched,
                ResponsePayload: null,
                RedactedSummary: null))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task PrepareDispatchCompleteAsync(
        AttemptRequest request,
        string operationKey,
        bool executeExternalEffect)
    {
        HarnessWorkerControl.IncrementPrepare(Id.Name);
        var edge = ExternalWriteEdge(request.Execution);
        var requestPayload = new ProtectedPayloadReference(Guid.NewGuid());
        var responsePayload = new ProtectedPayloadReference(Guid.NewGuid());

        // Always re-Prepare: Completed keys must short-circuit to the recorded snapshot.
        await SendAsync(
            request.Execution,
            new PrepareOperation(request.Attempt, operationKey, edge, requestPayload))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!executeExternalEffect)
        {
            // Second attempt: kernel returned Completed; do not re-drive the external write.
            return;
        }

        HarnessWorkerControl.IncrementExternalEffect(Id.Name);
        await SendAsync(
            request.Execution,
            new TransitionOperation(
                request.Attempt,
                operationKey,
                OperationPhase.Prepared,
                OperationPhase.Dispatched,
                ResponsePayload: null,
                RedactedSummary: null))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await SendAsync(
            request.Execution,
            new TransitionOperation(
                request.Attempt,
                operationKey,
                OperationPhase.Dispatched,
                OperationPhase.Completed,
                responsePayload,
                RedactedSummary: "done"))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static OperationEdge ExternalWriteEdge(NeuronId execution) => new(
        Target: execution,
        RequestSynapseId: "probe.external-write",
        RequestSchemaVersion: 1,
        ResponseSynapseId: "probe.external-write-result",
        ResponseSchemaVersion: 1);
}
