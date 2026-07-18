using Brain.Client;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.AI;
using Orleans.Runtime;
using Xunit;

namespace Brain.Tests.Kernel;

public sealed class ReviewFixTests : IClassFixture<ReactiveNeuronClusterFixture>
{
    private readonly ReactiveNeuronClusterFixture _fixture;

    public ReviewFixTests(ReactiveNeuronClusterFixture fixture) => _fixture = fixture;

    private static SynapseMetadata Meta(
        Guid commandId,
        Guid eventId,
        Guid causationId,
        long sourceSequence = 1,
        int causalDepth = 0,
        string sourceInstance = "source-1") =>
        new(
            CommandId: commandId,
            EventId: eventId,
            CausationId: causationId,
            CorrelationId: commandId,
            OrganizationId: new OrganizationId("org-1"),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: new SpaceId("space-1"),
            Source: new NeuronAddress(new OrganizationId("org-1"), new SpaceId("space-1"), "probe.source.v1", sourceInstance),
            SourceSequence: sourceSequence,
            CausalDepth: causalDepth,
            OccurredAt: DateTimeOffset.UtcNow);

    private IProbeNeuron Probe(string instance) =>
        _fixture.Cluster.GrainFactory.GetGrain<IProbeNeuron>(
            new NeuronAddress(new OrganizationId("org-1"), new SpaceId("space-1"), "probe.neuron.v1", instance).ToGrainKey());

    private ITypedEventConsumer Consumer(Guid streamId) =>
        _fixture.Cluster.GrainFactory.GetGrain<ITypedEventConsumer>(streamId);

    private static async Task<ProbeDomainEvent> WaitForPayloadOnClientAsync(
        ITypedEventConsumer consumer,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var payload = await consumer.GetLastPayloadAsync();
            if (payload is not null)
                return payload;
            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for stream payload delivery on the consumer grain.");
    }

    [Fact]
    public async Task Typed_outbox_preserves_payload_type_and_publishes_EventSynapse_T()
    {
        var streamId = Guid.NewGuid();
        var consumer = Consumer(streamId);
        await consumer.ReadyAsync();
        Assert.True(await consumer.GetActiveSubscriptionCountAsync() >= 1);
        Assert.True(await consumer.HasActiveSubscriptionAsync());

        var probe = Probe("typed-outbox");
        var commandId = Guid.NewGuid();
        var payload = new ProbeDomainEvent("typed-hello", 42);
        await probe.SetAutoDrainAsync(false);
        await probe.ExecuteTypedEmitAsync(
            new CommandSynapse<ProbeDomainEvent>(Meta(commandId, commandId, commandId), payload),
            streamId);

        var pending = await probe.PeekOutboxEventAsync();
        Assert.NotNull(pending);
        Assert.Equal("typed-hello", pending!.Payload.Name);
        Assert.Equal(42, pending.Payload.Value);

        await probe.DrainOutboxStrictAsync();

        var received = await WaitForPayloadOnClientAsync(consumer, TimeSpan.FromSeconds(15));
        Assert.Equal("typed-hello", received.Name);
        Assert.Equal(42, received.Value);
    }

    [Fact]
    public async Task Publish_failure_persists_attempt_and_registers_reminder()
    {
        var probe = Probe("pub-fail");
        await probe.SetAutoDrainAsync(false);
        var commandId = Guid.NewGuid();
        await probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "emit"));
        await probe.SetPublishFailuresAsync(1);

        await probe.DrainOutboxAsync();

        Assert.Equal(1, await probe.GetOutboxCountAsync());
        Assert.Equal(1, await probe.GetOutboxAttemptCountAsync());
        Assert.True(await probe.HasOutboxReminderAsync());
    }

    [Fact]
    public async Task Reminder_retry_drains_committed_outbox()
    {
        var probe = Probe("reminder-retry");
        await probe.SetAutoDrainAsync(false);
        var commandId = Guid.NewGuid();
        await probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "emit"));
        await probe.SetPublishFailuresAsync(1);
        await probe.DrainOutboxAsync();
        Assert.True(await probe.HasOutboxReminderAsync());
        Assert.Equal(1, await probe.GetOutboxCountAsync());

        await probe.SetPublishFailuresAsync(0);
        await probe.ReceiveOutboxReminderAsync();

        Assert.Equal(0, await probe.GetOutboxCountAsync());
        Assert.Equal(1, await probe.GetPublishedCountAsync());
        Assert.False(await probe.HasOutboxReminderAsync());
    }

    [Fact]
    public async Task Initial_out_of_order_source_event_is_rejected_explicitly()
    {
        var probe = Probe("initial-ooo");
        var c1 = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            probe.HandleEventAsync(new EventSynapse<string>(Meta(c1, Guid.NewGuid(), c1, sourceSequence: 7), "first")));

        Assert.Equal(BrainErrors.OutOfOrderSource, ex.Code);
        Assert.Equal(0, await probe.GetReactionCountAsync());
    }

    [Fact]
    public async Task Duplicate_causation_is_durably_rejected_after_reactivation()
    {
        var probe = Probe("dup-causation");
        var causation = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await probe.HandleEventAsync(new EventSynapse<string>(Meta(firstId, firstId, causation, sourceSequence: 1), "a"));
        await WaitForGrainDeactivationAsync(probe);

        var reloaded = Probe("dup-causation");
        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            reloaded.HandleEventAsync(new EventSynapse<string>(Meta(secondId, secondId, causation, sourceSequence: 2), "b")));

        Assert.Equal(BrainErrors.CausalLoop, ex.Code);
        Assert.Equal(1, await reloaded.GetReactionCountAsync());
    }

    [Fact]
    public async Task Unknown_failure_is_durable_sanitized_and_omits_raw_message()
    {
        var probe = Probe("sanitize");
        var commandId = Guid.NewGuid();
        const string secret = "sk-live-super-secret-token-should-not-persist";
        await probe.SetThrowRawMessageAsync(secret);

        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "boom")));

        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);
        Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ReactiveNeuronPipeline<ProbeDomainEvent>.UnknownFailureMessage, ex.Message, StringComparison.Ordinal);

        await WaitForGrainDeactivationAsync(probe);
        var failure = await Probe("sanitize").GetLastFailureAsync();
        Assert.NotNull(failure);
        Assert.Equal(BrainErrors.FailureSanitized, failure!.Code);
        Assert.Equal(ReactiveNeuronPipeline<ProbeDomainEvent>.UnknownFailureMessage, failure.Message);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typed_subscription_resumes_after_reactivation()
    {
        var streamId = Guid.NewGuid();
        var consumer = Consumer(streamId);
        await consumer.ReadyAsync();
        Assert.True(await consumer.GetActiveSubscriptionCountAsync() >= 1);
        Assert.True(await consumer.HasActiveSubscriptionAsync());
        var activationBefore = await consumer.GetActivationTokenAsync();

        await WaitForGrainDeactivationAsync(consumer);
        var reactivated = Consumer(streamId);
        var activationAfter = await reactivated.GetActivationTokenAsync();
        Assert.NotEqual(activationBefore, activationAfter);
        Assert.True(await reactivated.GetActiveSubscriptionCountAsync() >= 1);
        Assert.True(await reactivated.HasActiveSubscriptionAsync());

        var publisher = Probe("resume-publisher");
        await publisher.PublishDirectAsync(streamId, new ProbeDomainEvent("after-resume", 9));

        var received = await WaitForPayloadOnClientAsync(reactivated, TimeSpan.FromSeconds(15));
        Assert.Equal("after-resume", received.Name);
        Assert.Equal(9, received.Value);
        Assert.True(await reactivated.HasActiveSubscriptionAsync());
    }

    private async Task WaitForGrainDeactivationAsync(IDeactivatableGrain grain)
    {
        var tokenBefore = await grain.GetActivationTokenAsync();
        await grain.RequestDeactivationAsync();

        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var token = await grain.GetActivationTokenAsync();
            if (token != tokenBefore)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Grain did not deactivate and reactivate within the allotted time.");
    }
}

public sealed class ClientReviewFixTests : IClassFixture<Brain.Tests.Client.BrainClientClusterFixture>
{
    private readonly Brain.Tests.Client.BrainClientClusterFixture _fixture;

    public ClientReviewFixTests(Brain.Tests.Client.BrainClientClusterFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Three_argument_StartDiscussion_creates_typed_command()
    {
        var brain = new Brain.Client.Brain(_fixture.Cluster.Client);
        Assert.IsAssignableFrom<IClusterClient>(_fixture.Cluster.Client);

        var gpt = brain.Get<IGpt56>(new OrganizationId("org-1"), new SpaceId("space-1"), "gpt-1");
        var grok = brain.Get<IGrok45>(new OrganizationId("org-1"), new SpaceId("space-1"), "grok-1");
        var chat = brain.Get<IGroupChat>(new OrganizationId("org-1"), new SpaceId("space-1"), "chat-3arg");

        var receipt = await chat.StartDiscussion("three-arg-topic", gpt, grok);

        var probe = _fixture.Cluster.GrainFactory.GetGrain<Brain.Tests.Client.IGroupChatTestProbe>(
            ((IAddressable)chat).GetGrainId().Key.ToString());

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal("three-arg-topic", await probe.GetLastTopicAsync());
        Assert.Equal(((IAddressable)gpt).GetGrainId().Key.ToString(), await probe.GetLastGptKeyAsync());
        Assert.Equal(((IAddressable)grok).GetGrainId().Key.ToString(), await probe.GetLastGrokKeyAsync());
        Assert.Equal("org-1", await probe.GetLastOrganizationIdAsync());
        Assert.Equal("space-1", await probe.GetLastSpaceIdAsync());
        Assert.Equal(Brain.Client.GroupChatExtensions.DevelopmentPrincipalId.Value, await probe.GetLastPrincipalIdAsync());
    }
}

[GenerateSerializer, Alias("brain.tests.ProbeDomainEvent")]
public sealed record ProbeDomainEvent(
    [property: Id(0)] string Name,
    [property: Id(1)] int Value);

[Alias("brain.tests.IDeactivatableGrain")]
public interface IDeactivatableGrain : IAddressable
{
    [Alias("GetActivationTokenAsync")]
    Task<Guid> GetActivationTokenAsync();

    [Alias("RequestDeactivationAsync")]
    Task RequestDeactivationAsync();
}

[Alias("brain.tests.ITypedEventConsumer")]
public interface ITypedEventConsumer : IGrainWithGuidKey, IDeactivatableGrain
{
    [Alias("ReadyAsync")]
    Task ReadyAsync();

    [Alias("GetLastPayloadAsync")]
    Task<ProbeDomainEvent?> GetLastPayloadAsync();

    [Alias("HasActiveSubscriptionAsync")]
    Task<bool> HasActiveSubscriptionAsync();

    [Alias("GetActiveSubscriptionCountAsync")]
    Task<int> GetActiveSubscriptionCountAsync();
}
