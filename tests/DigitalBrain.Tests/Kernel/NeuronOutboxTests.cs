using System.Collections.Concurrent;
using DigitalBrain;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public sealed class NeuronOutboxTests
{
    [Fact]
    public async Task Durable_state_and_outbox_entry_are_committed_before_publish()
    {
        await using var cluster = await OutboxCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-outbox-commit");
        var operationId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-outbox-commit");
        await neuron.ConfigurePublishFaultsAsync(failNextPublishCount: 1);

        var failure = await Assert.ThrowsAsync<BrainException>(
            () => neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.Succeed));
        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);

        var operation = await neuron.ReadOperationAsync(operationId);
        Assert.NotNull(operation);
        Assert.Equal(ExternalOperationStatus.Succeeded, operation.Status);

        var outbox = await neuron.ListOutboxAsync();
        Assert.Single(outbox);
        Assert.Equal(operationId, outbox[0].OperationId);
        Assert.Equal(NotificationDeliveryStatus.Pending, outbox[0].DeliveryStatus);
        Assert.True(outbox[0].AttemptCount >= 1);

        var publishedBeforeRetry = await neuron.GetPublishedCountAsync();
        Assert.Equal(0, publishedBeforeRetry);
    }

    [Fact]
    public async Task Stream_failure_keeps_outbox_pending_and_reminder_retry_publishes()
    {
        await using var cluster = await OutboxCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-outbox-retry");
        var operationId = Guid.Parse("66666666-7777-8888-9999-000000000000");

        var received = new ConcurrentBag<NeuronNotification>();
        var stream = cluster.Client
            .GetStreamProvider(nameof(NeuronNotification))
            .GetStream<NeuronNotification>(StreamId.Create(nameof(NeuronNotification), "owner-outbox-retry"));
        var handle = await stream.SubscribeAsync((notification, _) =>
        {
            received.Add(notification);
            return Task.CompletedTask;
        });

        try
        {
            var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-outbox-retry");
            await neuron.ConfigurePublishFaultsAsync(failNextPublishCount: 1);
            var failure = await Assert.ThrowsAsync<BrainException>(
                () => neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.Succeed));
            Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);

            var pending = await neuron.ListOutboxAsync();
            Assert.Single(pending);
            Assert.Equal(NotificationDeliveryStatus.Pending, pending[0].DeliveryStatus);
            Assert.True(pending[0].AttemptCount >= 1);
            Assert.True(await neuron.HasOutboxReminderAsync());

            await WaitUntilAsync(async () =>
            {
                _ = await neuron.ReadStatusAsync();
                if (received.Count >= 1)
                    return true;
                return await neuron.GetPublishedCountAsync() >= 1;
            }, TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => Task.FromResult(received.Count >= 1), TimeSpan.FromSeconds(10));
            Assert.Contains(received, n => n.OperationId == operationId);

            var afterPending = await neuron.ListOutboxAsync();
            Assert.Empty(afterPending);
            Assert.True(await neuron.GetPublishedCountAsync() >= 1);

            var completed = await neuron.ReadDeliveredNotificationAsync(pending[0].NotificationId);
            Assert.NotNull(completed);
            Assert.Equal(NotificationDeliveryStatus.Completed, completed.DeliveryStatus);
            Assert.True(completed.AttemptCount >= 1);

            var durableCompleted = await neuron.ReadNotificationAsync(pending[0].NotificationId);
            Assert.NotNull(durableCompleted);
            Assert.Equal(NotificationDeliveryStatus.Completed, durableCompleted.DeliveryStatus);

            await neuron.ForceDeactivateAsync();
            await Task.Delay(500);

            var recovered = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-outbox-retry");
            var afterReactivate = await recovered.ReadDeliveredNotificationAsync(pending[0].NotificationId);
            Assert.NotNull(afterReactivate);
            Assert.Equal(NotificationDeliveryStatus.Completed, afterReactivate.DeliveryStatus);
            Assert.Empty(await recovered.ListOutboxAsync());
        }
        finally
        {
            await handle.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task Completion_commit_failure_restores_pending_and_surfaces_StorageUnavailable()
    {
        await using var cluster = await OutboxCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-completion-fault");
        var operationId = Guid.Parse("99999999-aaaa-bbbb-cccc-dddddddddddd");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-completion-fault");
        await neuron.ConfigureFailCompletionCommitAfterPublishAsync(true);

        var failure = await Assert.ThrowsAsync<BrainException>(
            () => neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.Succeed));
        Assert.Equal(NeuronFailureKind.StorageUnavailable, failure.FailureKind);

        var pending = await neuron.ListOutboxAsync();
        Assert.Single(pending);
        Assert.Equal(operationId, pending[0].OperationId);
        Assert.Equal(NotificationDeliveryStatus.Pending, pending[0].DeliveryStatus);
        Assert.True(pending[0].AttemptCount >= 1);

        Assert.Null(await neuron.ReadDeliveredNotificationAsync(pending[0].NotificationId));
        Assert.True(await neuron.GetPublishedCountAsync() >= 1);
    }

    [Fact]
    public async Task At_least_once_consumer_deduplicates_by_operation_id()
    {
        await using var cluster = await OutboxCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-dedupe");
        var operationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var consumer = new OperationIdDedupingConsumer();
        var stream = cluster.Client
            .GetStreamProvider(nameof(NeuronNotification))
            .GetStream<NeuronNotification>(StreamId.Create(nameof(NeuronNotification), "owner-dedupe"));
        var handle = await stream.SubscribeAsync((notification, _) =>
        {
            consumer.Accept(notification);
            return Task.CompletedTask;
        });

        try
        {
            var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-dedupe");
            await neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.Succeed);
            await WaitUntilAsync(() => consumer.DeliveryCount >= 1, TimeSpan.FromSeconds(10));

            await neuron.RepublishLastNotificationAsync();
            await WaitUntilAsync(() => consumer.DeliveryCount >= 2, TimeSpan.FromSeconds(10));

            Assert.Equal(2, consumer.DeliveryCount);
            Assert.Equal(1, consumer.UniqueOperationCount);
            Assert.Contains(operationId, consumer.AcceptedOperationIds);
        }
        finally
        {
            await handle.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task Losing_stream_messages_cannot_delete_or_contradict_durable_neuron_state()
    {
        await using var cluster = await OutboxCluster.CreateAsync();
        using var owner = OwnerContext.Push("owner-stream-loss");
        var operationId = Guid.Parse("12345678-1234-1234-1234-1234567890ab");

        var neuron = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-stream-loss");
        await neuron.ConfigurePublishFaultsAsync(failNextPublishCount: 100);
        var failure = await Assert.ThrowsAsync<BrainException>(
            () => neuron.ExecuteTestExternalAsync(operationId, TestExternalMode.Succeed));
        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);

        var beforeOperation = await neuron.ReadOperationAsync(operationId);
        Assert.NotNull(beforeOperation);
        Assert.Equal(ExternalOperationStatus.Succeeded, beforeOperation.Status);

        var beforeOutbox = await neuron.ListOutboxAsync();
        Assert.Single(beforeOutbox);
        Assert.Equal(NotificationDeliveryStatus.Pending, beforeOutbox[0].DeliveryStatus);

        await neuron.ForceDeactivateAsync();
        await Task.Delay(300);

        var recovered = cluster.GrainFactory.GetGrain<ITestNeuron>("owner-stream-loss");
        var afterOperation = await recovered.ReadOperationAsync(operationId);
        Assert.NotNull(afterOperation);
        Assert.Equal(beforeOperation, afterOperation);

        var afterOutbox = await recovered.ListOutboxAsync();
        Assert.Single(afterOutbox);
        Assert.Equal(beforeOutbox[0].NotificationId, afterOutbox[0].NotificationId);
        Assert.Equal(beforeOutbox[0].OperationId, afterOutbox[0].OperationId);
        Assert.Equal(NotificationDeliveryStatus.Pending, afterOutbox[0].DeliveryStatus);
    }

    [Fact]
    public void Stream_provider_name_is_derived_with_nameof_NeuronNotification()
    {
        Assert.Equal(nameof(NeuronNotification), NeuronNotificationPublisher.StreamProviderName);
        Assert.Equal(nameof(NeuronNotification), NeuronNotificationPublisher.StreamNamespace);
    }

    private static Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout) =>
        WaitUntilAsync(() => Task.FromResult(condition()), timeout);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!await condition())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException("Condition not met before timeout.");
            await Task.Delay(50);
        }
    }

    private sealed class OperationIdDedupingConsumer
    {
        private readonly HashSet<Guid> _seen = [];
        public int DeliveryCount { get; private set; }
        public int UniqueOperationCount => _seen.Count;
        public IReadOnlyCollection<Guid> AcceptedOperationIds => _seen;

        public void Accept(NeuronNotification notification)
        {
            DeliveryCount++;
            _seen.Add(notification.OperationId);
        }
    }
}

file static class OutboxCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddBrainKernel();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(nameof(NeuronNotification), _ => { });
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams(nameof(NeuronNotification), _ => { });
        }
    }
}
