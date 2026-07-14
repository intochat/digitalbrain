using System.Diagnostics;
using DigitalBrain.Kernel.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Features;

[GrainType("digitalbrain.v3.feature-installation")]
internal sealed class FeatureInstallationGrain([PersistentState("feature-installation")] IPersistentState<FeatureInstallationState> persistentState, TimeProvider timeProvider) : Grain, IFeatureInstallationGrain
{
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Features.Installation");

    public async Task InitializeAsync(ReleaseDigest release)
    {
        using var activity = Start("initialize");
        var (_, installationId) = ParseKey();
        if (persistentState.RecordExists)
        {
            if (RequiredState().ActiveRelease != release)
                throw new InvalidOperationException("The feature installation is already initialized with another release.");
            return;
        }
        await WriteAsync(FeatureInstallationState.Create(release, installationId));
    }

    public async Task<FeatureAppendStatus> AppendAsync(FeatureInput input)
    {
        using var activity = Start("append", input);
        var transition = Domain(() => FeatureInstallationTransitions.Append(RequiredState(), input, timeProvider.GetUtcNow()));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Status;
    }

    public async Task<FeatureRunClaim?> ClaimAsync(string hostId, TimeSpan leaseDuration)
    {
        using var activity = Start("claim");
        var transition = Domain(() => FeatureInstallationTransitions.Claim(RequiredState(), hostId, timeProvider.GetUtcNow(), leaseDuration));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Claim;
    }

    public async Task<FeatureFailureDisposition> FailAsync(FeatureLeaseFence fence, DateTimeOffset retryAt, string safeFailure)
    {
        using var activity = Start("fail");
        var next = Domain(() => FeatureInstallationTransitions.Fail(RequiredState(), fence, timeProvider.GetUtcNow(), retryAt, safeFailure));
        await WriteAsync(next);
        return next.Inbox.Single(entry => string.Equals(entry.Input.InputId, fence.InputId, StringComparison.Ordinal)).Parked
            ? FeatureFailureDisposition.Parked
            : FeatureFailureDisposition.RetryScheduled;
    }

    public async Task<FeatureAppendStatus> RecordScheduleOccurrenceAsync(FeatureScheduleOccurrence occurrence)
    {
        using var activity = Start("schedule", correlationId: occurrence.CorrelationId, traceId: occurrence.TraceId);
        var transition = Domain(() => FeatureInstallationTransitions.RecordScheduleOccurrence(RequiredState(), occurrence, timeProvider.GetUtcNow()));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Status;
    }

    public async Task<FeatureCompletionReceipt> CommitAsync(FeatureRunCommit commit)
    {
        using var activity = Start("commit");
        var transition = Domain(() => FeatureInstallationTransitions.Commit(RequiredState(), commit, timeProvider.GetUtcNow()));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return Receipt(transition.Completion);
    }

    public Task<FeatureIntentStatus[]> ListPendingIntentsAsync()
    {
        using var activity = Start("list-intents");
        return Task.FromResult(Domain(() => FeatureInstallationTransitions.ListPendingIntents(RequiredState()))
            .Select(IntentStatus)
            .ToArray());
    }

    public async Task ApplyIntentAsync(string operationKey)
    {
        using var activity = Start("apply-intent");
        var next = Domain(() => FeatureInstallationTransitions.ApplyIntent(RequiredState(), operationKey, timeProvider.GetUtcNow()));
        if (ReferenceEquals(next, persistentState.State))
            return;
        await WriteAsync(next);
    }

    public Task PauseAsync(string reason) => PersistAsync("pause", state => FeatureInstallationTransitions.Pause(state, reason));

    public Task ResumeAsync() => PersistAsync("resume", FeatureInstallationTransitions.Resume);

    public Task SwitchReleaseAsync(ReleaseDigest release) =>
        PersistAsync("switch-release", state => FeatureInstallationTransitions.SwitchRelease(state, release));

    public Task RollbackAsync() => PersistAsync("rollback", FeatureInstallationTransitions.Rollback);

    public Task<FeatureInstallationSnapshot> ReadAsync()
    {
        using var activity = Start("read");
        var state = RequiredState();
        return Task.FromResult(new FeatureInstallationSnapshot(
            state.InstallationId,
            state.ActiveRelease,
            state.PreviousRelease,
            state.StateJson,
            state.Paused,
            state.PauseReason,
            state.Inbox.Select(entry => entry.Input).ToArray(),
            state.Lease is { } lease ? new FeatureLeaseStatus(lease.HostId, lease.Fence, lease.ExpiresAt, lease.Attempt) : null,
            state.Completions.Select(Receipt).ToArray(),
            state.Intents.Select(IntentStatus).ToArray(),
            state.Schedules.Select(schedule => new FeatureScheduleStatus(schedule.ScheduleId, schedule.LastOccurrenceAt, schedule.NextOccurrenceAt)).ToArray(),
            state.Revision,
            state.Inbox.Where(entry => entry.Parked).Select(entry => new FeatureParkedInput(entry.Input, entry.Attempts, entry.LastFailure)).ToArray()));
    }

    private async Task PersistAsync(string operation, Func<FeatureInstallationState, FeatureInstallationState> transition)
    {
        using var activity = Start(operation);
        var next = Domain(() => transition(RequiredState()));
        if (ReferenceEquals(next, persistentState.State))
            return;
        await WriteAsync(next);
    }

    private async Task WriteAsync(FeatureInstallationState next)
    {
        await PersistedStateReconciliation.WriteWithRollbackAsync(persistentState, next, FeatureStateEquality.Same);
    }

    private FeatureInstallationState RequiredState() =>
        persistentState.RecordExists && persistentState.State is not null
            ? persistentState.State
            : throw new InvalidOperationException("The feature installation has not been initialized.");

    private (BrainOwnerId OwnerId, FeatureInstallationId InstallationId) ParseKey() =>
        FeatureGrainIds.ParseInstallation(this.GetPrimaryKeyString());

    private Activity? Start(string operation, FeatureInput? input = null, string? correlationId = null, string? traceId = null)
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("feature.grain_key", this.GetPrimaryKeyString());
        activity?.SetTag("feature.input_id", input?.InputId);
        activity?.SetTag("feature.correlation_id", correlationId ?? input?.CorrelationId);
        activity?.SetTag("feature.trace_id", traceId ?? input?.TraceId);
        return activity;
    }

    private static FeatureCompletionReceipt Receipt(FeatureCompletion completion) => new(completion.InputId, completion.Fence, completion.ResultJson, completion.CompletedAt, completion.CommitDigest, completion.InputDigest);

    private static FeatureIntentStatus IntentStatus(PersistedFeatureIntent intent) => new(intent.OperationKey, intent.Kind, intent.PayloadJson, intent.AppliedAt);

    private static T Domain<T>(Func<T> transition)
    {
        try
        {
            return transition();
        }
        catch (FeatureConcurrencyException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
        catch (FeatureLimitExceededException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
    }
}
