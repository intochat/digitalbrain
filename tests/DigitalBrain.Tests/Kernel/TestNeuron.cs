using System.Collections.Concurrent;
using DigitalBrain;
using DigitalBrain.Kernel;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Kernel;

public interface ITestNeuron : INeuron
{
    Task WriteStatusAsync(NeuronStatus status);
    Task<NeuronStatus> ReadStatusAsync();
    Task WriteOperationAsync(Guid operationId, ExternalOperation operation);
    Task<ExternalOperation?> ReadOperationAsync(Guid operationId);
    Task WriteNotificationAsync(Guid notificationId, NeuronNotification notification);
    Task<NeuronNotification?> ReadNotificationAsync(Guid notificationId);
    Task ExecuteTestExternalAsync(Guid operationId, TestExternalMode mode);
    Task ReconcileFromProviderReceiptAsync(Guid operationId);
    Task ConfigurePublishFaultsAsync(int failNextPublishCount);
    Task ConfigureFailCompletionCommitAfterPublishAsync(bool enabled);
    Task ConfigurePersistFaultsAsync(int failNextPersistCount);
    Task DrainOutboxAsync();
    Task<IReadOnlyList<NeuronNotification>> ListOutboxAsync();
    Task<int> GetPublishedCountAsync();
    Task RepublishLastNotificationAsync();
    Task<NeuronNotification?> ReadDeliveredNotificationAsync(Guid notificationId);
    Task<TestExternalProbe?> ReadExternalProbeAsync(Guid operationId);
    Task ForceDeactivateAsync();
    Task ArmOutboxRecoveryAsync();
    Task<bool> HasOutboxReminderAsync();
}

[GenerateSerializer]
[Alias(nameof(TestExternalProbe))]
public sealed record TestExternalProbe(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] ExternalOperationStatus StatusAtInvoke,
    [property: Id(2)] bool PendingWasReadableAtInvoke,
    [property: Id(3)] bool EffectRecorded);

public sealed class TestNeuron([NeuronState] NeuronDurableState state) : Neuron(state), ITestNeuron
{
    private static readonly ConcurrentDictionary<string, int> PublishFaultsByKey = new();
    private static readonly ConcurrentDictionary<string, int> PublishedCountsByKey = new();
    private static readonly ConcurrentDictionary<string, NeuronNotification> LastPublishedByKey = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TestExternalProbe>> ProbesByKey = new();
    private static readonly ConcurrentDictionary<string, bool> FailCompletionCommitByKey = new();
    private static readonly ConcurrentDictionary<string, int> FailNextPersistByKey = new();

    public async Task WriteStatusAsync(NeuronStatus status)
    {
        DurableState.Status.Value = status;
        await CommitDurableStateAsync();
    }

    public Task<NeuronStatus> ReadStatusAsync() =>
        Task.FromResult(DurableState.Status.Value);

    public async Task WriteOperationAsync(Guid operationId, ExternalOperation operation)
    {
        DurableState.Operations[operationId] = operation;
        await CommitDurableStateAsync();
    }

    public Task<ExternalOperation?> ReadOperationAsync(Guid operationId) =>
        Task.FromResult(
            DurableState.Operations.TryGetValue(operationId, out var operation)
                ? operation
                : null);

    public async Task WriteNotificationAsync(Guid notificationId, NeuronNotification notification)
    {
        DurableState.Outbox[notificationId] = notification;
        await CommitDurableStateAsync();
    }

    public Task<NeuronNotification?> ReadNotificationAsync(Guid notificationId) =>
        Task.FromResult(
            DurableState.Outbox.TryGetValue(notificationId, out var notification)
                ? notification
                : null);

    public async Task ExecuteTestExternalAsync(Guid operationId, TestExternalMode mode)
    {
        var pending = new ExternalOperation(
            operationId,
            ExternalOperationStatus.Pending,
            FailureKind: null);
        DurableState.Operations[operationId] = pending;
        await CommitDurableStateAsync();

        var durableAtCall = DurableState.Operations[operationId];
        var probes = ProbesByKey.GetOrAdd(this.GetPrimaryKeyString(), static _ => new ConcurrentDictionary<Guid, TestExternalProbe>());
        probes[operationId] = new TestExternalProbe(
            operationId,
            durableAtCall.Status,
            PendingWasReadableAtInvoke: durableAtCall.Status == ExternalOperationStatus.Pending,
            EffectRecorded: true);

        if (mode == TestExternalMode.CrashBeforeOutcome)
            throw new InvalidOperationException("crash after external effect before outcome persistence");

        ExternalOperation outcome = mode switch
        {
            TestExternalMode.Succeed => ExternalOperationTransitions.Apply(
                durableAtCall,
                new ExternalOperationTransition.Succeeded()),
            TestExternalMode.FailProvider => ExternalOperationTransitions.Apply(
                durableAtCall,
                new ExternalOperationTransition.Failed(NeuronFailureKind.ProviderUnavailable)),
            TestExternalMode.FailOperation => ExternalOperationTransitions.Apply(
                durableAtCall,
                new ExternalOperationTransition.Failed(NeuronFailureKind.OperationFailed)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        DurableState.Operations[operationId] = outcome;

        if (mode == TestExternalMode.Succeed)
        {
            var notification = new NeuronNotification(
                Guid.NewGuid(),
                operationId,
                NotificationDeliveryStatus.Pending,
                AttemptCount: 0);
            DurableState.Outbox[notification.NotificationId] = notification;
        }

        await CommitDurableStateAsync();

        if (mode == TestExternalMode.Succeed)
            await DrainOutboxCoreAsync(throwOnPublishFailure: true);
    }

    public async Task ReconcileFromProviderReceiptAsync(Guid operationId)
    {
        if (!DurableState.Operations.TryGetValue(operationId, out var current))
            throw new InvalidOperationException("operation missing");

        DurableState.Operations[operationId] = ExternalOperationTransitions.Apply(
            current,
            new ExternalOperationTransition.ReconcileSucceeded());
        await CommitDurableStateAsync();
    }

    public Task ConfigurePublishFaultsAsync(int failNextPublishCount)
    {
        PublishFaultsByKey[this.GetPrimaryKeyString()] = failNextPublishCount;
        return Task.CompletedTask;
    }

    public Task ConfigureFailCompletionCommitAfterPublishAsync(bool enabled)
    {
        FailCompletionCommitByKey[this.GetPrimaryKeyString()] = enabled;
        return Task.CompletedTask;
    }

    public Task ConfigurePersistFaultsAsync(int failNextPersistCount)
    {
        FailNextPersistByKey[this.GetPrimaryKeyString()] = failNextPersistCount;
        return Task.CompletedTask;
    }

    public Task DrainOutboxAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: true);

    public Task<IReadOnlyList<NeuronNotification>> ListOutboxAsync() =>
        Task.FromResult<IReadOnlyList<NeuronNotification>>(
            DurableState.Outbox.Values
                .Where(notification => notification.DeliveryStatus == NotificationDeliveryStatus.Pending)
                .ToArray());

    public Task<int> GetPublishedCountAsync() =>
        Task.FromResult(PublishedCountsByKey.GetValueOrDefault(this.GetPrimaryKeyString()));

    public async Task RepublishLastNotificationAsync()
    {
        if (!LastPublishedByKey.TryGetValue(this.GetPrimaryKeyString(), out var notification))
            throw new InvalidOperationException("no published notification");

        await PublishNotificationAsync(notification);
    }

    public Task<NeuronNotification?> ReadDeliveredNotificationAsync(Guid notificationId) =>
        Task.FromResult(
            DurableState.Outbox.TryGetValue(notificationId, out var notification)
            && notification.DeliveryStatus == NotificationDeliveryStatus.Completed
                ? notification
                : null);

    public Task<TestExternalProbe?> ReadExternalProbeAsync(Guid operationId)
    {
        if (ProbesByKey.TryGetValue(this.GetPrimaryKeyString(), out var probes) &&
            probes.TryGetValue(operationId, out var probe))
        {
            return Task.FromResult<TestExternalProbe?>(probe);
        }

        return Task.FromResult<TestExternalProbe?>(null);
    }

    public Task ForceDeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task ArmOutboxRecoveryAsync() =>
        NeuronReminder.RegisterOutboxRecoveryAsync(this);

    public async Task<bool> HasOutboxReminderAsync()
    {
        var reminder = await this.GetReminder(NeuronReminder.OutboxRecoveryName);
        return reminder is not null;
    }

    protected override Task PersistDurableStateAsync(CancellationToken cancellationToken)
    {
        var key = this.GetPrimaryKeyString();
        if (FailNextPersistByKey.TryGetValue(key, out var remaining) && remaining > 0)
        {
            FailNextPersistByKey[key] = remaining - 1;
            throw new InvalidOperationException("journal-backend-secret-marker");
        }

        return base.PersistDurableStateAsync(cancellationToken);
    }

    protected override async Task PublishNotificationAsync(NeuronNotification notification)
    {
        var key = this.GetPrimaryKeyString();
        if (PublishFaultsByKey.TryGetValue(key, out var remaining) && remaining > 0)
        {
            PublishFaultsByKey[key] = remaining - 1;
            throw new InvalidOperationException("stream publish failed");
        }

        await base.PublishNotificationAsync(notification);
        PublishedCountsByKey.AddOrUpdate(key, 1, static (_, count) => count + 1);
        LastPublishedByKey[key] = notification;

        if (FailCompletionCommitByKey.TryGetValue(key, out var failCompletion) && failCompletion)
        {
            FailCompletionCommitByKey[key] = false;
            FailNextPersistByKey[key] = 1;
        }
    }
}

public enum TestExternalMode
{
    Succeed,
    FailProvider,
    FailOperation,
    CrashBeforeOutcome
}
