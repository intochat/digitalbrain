using System.Text;
using Orleans;

namespace DigitalBrain.Kernel.Contracts;

public static class FeatureGrainIds
{
    public static string Hub(BrainOwnerId ownerId) => $"v3/{Segment(ownerId.Value)}/features";

    public static string Installation(BrainOwnerId ownerId, FeatureInstallationId installationId) =>
        $"{Hub(ownerId)}/{Segment(installationId.Value)}";

    public static BrainOwnerId ParseHub(string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);
        var segments = grainKey.Split('/', StringSplitOptions.None);
        if (segments is not ["v3", var owner, "features"])
            throw new ArgumentException("A canonical feature hub key is required.", nameof(grainKey));
        var ownerId = new BrainOwnerId(Unsegment(owner));
        if (!string.Equals(Hub(ownerId), grainKey, StringComparison.Ordinal))
            throw new ArgumentException("A canonical feature hub key is required.", nameof(grainKey));
        return ownerId;
    }

    public static (BrainOwnerId OwnerId, FeatureInstallationId InstallationId) ParseInstallation(string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);
        var segments = grainKey.Split('/', StringSplitOptions.None);
        if (segments is not ["v3", var owner, "features", var installation])
            throw new ArgumentException("A canonical feature installation key is required.", nameof(grainKey));
        var ownerId = new BrainOwnerId(Unsegment(owner));
        var installationId = new FeatureInstallationId(Unsegment(installation));
        if (!string.Equals(Installation(ownerId, installationId), grainKey, StringComparison.Ordinal))
            throw new ArgumentException("A canonical feature installation key is required.", nameof(grainKey));
        return (ownerId, installationId);
    }

    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Unsegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A canonical feature grain key is required.", nameof(value), exception);
        }
    }
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-registration")]
public sealed record FeatureInstallationRegistration(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] string[] Subscriptions);

[GenerateSerializer, Alias("digitalbrain.v3.feature-input")]
public sealed record FeatureInput(
    [property: Id(0)] string InputId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset OccurredAt,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] string TraceId);

[GenerateSerializer, Alias("digitalbrain.v3.feature-lease-fence")]
public sealed record FeatureLeaseFence(
    [property: Id(0)] string InputId,
    [property: Id(1)] long Fence);

[GenerateSerializer, Alias("digitalbrain.v3.feature-run-claim")]
public sealed record FeatureRunClaim(
    [property: Id(0)] FeatureInput Input,
    [property: Id(1)] FeatureLeaseFence Fence,
    [property: Id(2)] ReleaseDigest Release,
    [property: Id(3)] string StateJson,
    [property: Id(4)] DateTimeOffset LeaseExpiresAt,
    [property: Id(5)] int Attempt);

[Alias("digitalbrain.v3.feature-intent-kind")]
public enum FeatureIntentKind
{
    TextSurface,
    Event,
    ExternalEffect,
    InternalWrite
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-intent")]
public sealed record FeatureIntent(
    [property: Id(0)] string LogicalOperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson);

[GenerateSerializer, Alias("digitalbrain.v3.feature-resource-usage")]
public sealed record FeatureResourceUsage(
    [property: Id(0)] int Reads,
    [property: Id(1)] int ModelCalls);

[GenerateSerializer, Alias("digitalbrain.v3.feature-run-commit")]
public sealed record FeatureRunCommit(
    [property: Id(0)] FeatureLeaseFence Fence,
    [property: Id(1)] string NewStateJson,
    [property: Id(2)] IReadOnlyList<FeatureIntent> Intents,
    [property: Id(3)] FeatureResourceUsage Usage,
    [property: Id(4)] string ResultJson);

[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-occurrence")]
public sealed record FeatureScheduleOccurrence(
    [property: Id(0)] string ScheduleId,
    [property: Id(1)] DateTimeOffset ScheduledFor,
    [property: Id(2)] DateTimeOffset NextOccurrenceAt,
    [property: Id(3)] string PayloadJson,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] string TraceId);

[Alias("digitalbrain.v3.feature-append-status")]
public enum FeatureAppendStatus
{
    Accepted,
    Duplicate,
    Full,
    Paused
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-completion-receipt")]
public sealed record FeatureCompletionReceipt(
    [property: Id(0)] string InputId,
    [property: Id(1)] long Fence,
    [property: Id(2)] string ResultJson,
    [property: Id(3)] DateTimeOffset CompletedAt,
    [property: Id(4)] string CommitDigest,
    [property: Id(5)] string InputDigest);

[GenerateSerializer, Alias("digitalbrain.v3.feature-intent-status")]
public sealed record FeatureIntentStatus(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset? AppliedAt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-status")]
public sealed record FeatureScheduleStatus(
    [property: Id(0)] string ScheduleId,
    [property: Id(1)] DateTimeOffset LastOccurrenceAt,
    [property: Id(2)] DateTimeOffset NextOccurrenceAt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-lease-status")]
public sealed record FeatureLeaseStatus(
    [property: Id(0)] string HostId,
    [property: Id(1)] FeatureLeaseFence Fence,
    [property: Id(2)] DateTimeOffset ExpiresAt,
    [property: Id(3)] int Attempt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-snapshot")]
public sealed record FeatureInstallationSnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ActiveRelease,
    [property: Id(2)] ReleaseDigest? PreviousRelease,
    [property: Id(3)] string StateJson,
    [property: Id(4)] bool Paused,
    [property: Id(5)] string? PauseReason,
    [property: Id(6)] FeatureInput[] Inbox,
    [property: Id(7)] FeatureLeaseStatus? Lease,
    [property: Id(8)] FeatureCompletionReceipt[] Completions,
    [property: Id(9)] FeatureIntentStatus[] Intents,
    [property: Id(10)] FeatureScheduleStatus[] Schedules,
    [property: Id(11)] long Revision);

[Alias("digitalbrain.v3.feature-failure-disposition")]
public enum FeatureFailureDisposition
{
    RetryScheduled,
    Parked
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-result")]
public sealed record FeatureFanOutResult(
    [property: Id(0)] string InputId,
    [property: Id(1)] int Delivered,
    [property: Id(2)] int Pending);

[GenerateSerializer, Alias("digitalbrain.v3.feature-hub-snapshot")]
public sealed record FeatureHubSnapshot(
    [property: Id(0)] FeatureInstallationRegistration[] Installations,
    [property: Id(1)] FeatureFanOutResult[] FanOuts,
    [property: Id(2)] long Revision);

[Alias("digitalbrain.v3.feature-hub-grain")]
public interface IFeatureHubGrain : IGrainWithStringKey
{
    [Alias("register")]
    Task RegisterAsync(FeatureInstallationRegistration registration);
    [Alias("publish")]
    Task<FeatureFanOutResult> PublishAsync(FeatureInput input);
    [Alias("read")]
    Task<FeatureHubSnapshot> ReadAsync();
}

[Alias("digitalbrain.v3.feature-installation-grain")]
public interface IFeatureInstallationGrain : IGrainWithStringKey
{
    [Alias("initialize")]
    Task InitializeAsync(ReleaseDigest release);
    [Alias("append")]
    Task<FeatureAppendStatus> AppendAsync(FeatureInput input);
    [Alias("claim")]
    Task<FeatureRunClaim?> ClaimAsync(string hostId, TimeSpan leaseDuration);
    [Alias("fail")]
    Task<FeatureFailureDisposition> FailAsync(FeatureLeaseFence fence, DateTimeOffset retryAt, string safeFailure);
    [Alias("record-schedule-occurrence")]
    Task<FeatureAppendStatus> RecordScheduleOccurrenceAsync(FeatureScheduleOccurrence occurrence);
    [Alias("commit")]
    Task<FeatureCompletionReceipt> CommitAsync(FeatureRunCommit commit);
    [Alias("list-pending-intents")]
    Task<FeatureIntentStatus[]> ListPendingIntentsAsync();
    [Alias("apply-intent")]
    Task ApplyIntentAsync(string operationKey);
    [Alias("pause")]
    Task PauseAsync(string reason);
    [Alias("resume")]
    Task ResumeAsync();
    [Alias("switch-release")]
    Task SwitchReleaseAsync(ReleaseDigest release);
    [Alias("rollback")]
    Task RollbackAsync();
    [Alias("read")]
    Task<FeatureInstallationSnapshot> ReadAsync();
}
