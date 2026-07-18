using System.Reflection;
using System.Text.RegularExpressions;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Kernel;

public sealed class ReactiveNeuronTests : IClassFixture<ReactiveNeuronClusterFixture>
{
    private readonly ReactiveNeuronClusterFixture _fixture;

    public ReactiveNeuronTests(ReactiveNeuronClusterFixture fixture) => _fixture = fixture;

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

    [Fact]
    public async Task Duplicate_command_returns_the_durable_original_receipt()
    {
        var probe = Probe("dup-cmd");
        var commandId = Guid.NewGuid();
        var first = await probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "one"));
        var second = await probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "two"));

        Assert.Equal(first, second);
        Assert.Equal(CommandReceiptStatus.Accepted, first.Status);
        Assert.Equal(1, await probe.GetReactionCountAsync());
        Assert.Equal(1, await probe.GetRevisionAsync());
    }

    [Fact]
    public async Task Failed_journal_write_does_not_acknowledge()
    {
        var probe = Probe("fail-commit");
        await probe.SetFailNextCommitAsync(true);
        var commandId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "x")));
        Assert.Equal(BrainErrors.JournalCommitFailed, ex.Code);

        await probe.DeactivateAsync();
        var reloaded = Probe("fail-commit");
        Assert.Equal(0, await reloaded.GetReactionCountAsync());
        Assert.Equal(0, await reloaded.GetRevisionAsync());
        Assert.Null(await reloaded.TryGetReceiptAsync(commandId));
    }

    [Fact]
    public async Task Committed_pending_event_survives_reactivation()
    {
        var probe = Probe("outbox-survive");
        var commandId = Guid.NewGuid();
        var receipt = await probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "emit"));
        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.True(await probe.GetOutboxCountAsync() >= 1);
        Assert.Equal(1, await probe.GetRevisionAsync());

        var token = await probe.GetActivationTokenAsync();
        await probe.RequestDeactivationAsync();
        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var next = Probe("outbox-survive");
            if (await next.GetActivationTokenAsync() != token)
            {
                Assert.Equal(1, await next.GetRevisionAsync());
                Assert.True(await next.GetOutboxCountAsync() >= 1);
                Assert.NotNull(await next.PeekOutboxEventAsync());
                await next.SetAutoDrainAsync(true);
                await next.DrainOutboxAsync();
                Assert.Equal(0, await next.GetOutboxCountAsync());
                Assert.Equal(1, await next.GetPublishedCountAsync());
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("outbox-survive grain did not reactivate");
    }

    [Fact]
    public async Task Duplicate_event_is_not_reacted_to_twice()
    {
        var probe = Probe("dup-event");
        var eventId = Guid.NewGuid();
        var causation = Guid.NewGuid();
        var first = new EventSynapse<string>(Meta(causation, eventId, causation, sourceSequence: 1), "e");
        var second = new EventSynapse<string>(Meta(Guid.NewGuid(), eventId, causation, sourceSequence: 2), "e");

        await probe.HandleEventAsync(first);
        await probe.HandleEventAsync(second);

        Assert.Equal(1, await probe.GetReactionCountAsync());
    }

    [Fact]
    public async Task Out_of_order_source_event_is_rejected_explicitly()
    {
        var probe = Probe("ooo");
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        await probe.HandleEventAsync(new EventSynapse<string>(Meta(c1, Guid.NewGuid(), c1, sourceSequence: 1), "first"));

        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            probe.HandleEventAsync(new EventSynapse<string>(Meta(c2, Guid.NewGuid(), c2, sourceSequence: 3), "skipped")));

        Assert.Equal(BrainErrors.OutOfOrderSource, ex.Code);
        Assert.Equal(1, await probe.GetReactionCountAsync());
    }

    [Fact]
    public async Task Causal_loop_is_durably_rejected()
    {
        var probe = Probe("causal");
        var loopId = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            probe.HandleEventAsync(new EventSynapse<string>(
                Meta(loopId, loopId, loopId, sourceSequence: 1, causalDepth: 1),
                "loop")));

        Assert.Equal(BrainErrors.CausalLoop, ex.Code);
        await probe.DeactivateAsync();
        var again = await Assert.ThrowsAsync<BrainException>(() =>
            Probe("causal").HandleEventAsync(new EventSynapse<string>(
                Meta(loopId, loopId, loopId, sourceSequence: 1, causalDepth: 1),
                "loop")));
        Assert.Equal(BrainErrors.CausalLoop, again.Code);
    }

    [Fact]
    public async Task Ui_action_expected_revision_conflict_is_explicit()
    {
        var probe = Probe("ui");
        var commandId = Guid.NewGuid();
        await probe.ExecuteCommandAsync(new CommandSynapse<string>(Meta(commandId, commandId, commandId), "seed-ui"));

        var actionId = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            probe.ApplyUiActionAsync(new CommandSynapse<UiActionRequest>(
                Meta(actionId, actionId, actionId),
                new UiActionRequest("act", ExpectedRevision: 99))));

        Assert.Equal(BrainErrors.RevisionConflict, ex.Code);
    }

    [Fact]
    public void No_failure_path_catches_and_ignores_an_exception()
    {
        var kernelDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.Kernel"));
        Assert.True(Directory.Exists(kernelDir), kernelDir);

        var emptyCatch = new Regex(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled);
        foreach (var file in Directory.EnumerateFiles(kernelDir, "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.False(emptyCatch.IsMatch(source), $"Empty catch in {file}");
            foreach (Match match in Regex.Matches(source, @"catch\s*(\([^)]*\))?\s*\{(?<body>.*?)\}", RegexOptions.Singleline))
            {
                var body = match.Groups["body"].Value;
                var rethrows = body.Contains("throw", StringComparison.Ordinal)
                    || body.Contains("RecordFailure", StringComparison.Ordinal)
                    || body.Contains("UnknownFailureMessage", StringComparison.Ordinal);
                Assert.True(rethrows, $"Catch without failure handling in {file}: {body.Trim()}");
            }
        }

        Assert.Null(typeof(InMemoryReactiveStore<>).Assembly.GetType("Brain.Kernel.InMemoryReactiveStore`1"));
        Assert.DoesNotContain(
            typeof(ReactiveNeuron<>).Assembly.GetTypes().Select(t => t.Name),
            name => name.Contains("InMemory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Durable_fake_pipeline_deduplicates_without_orleans()
    {
        var store = new InMemoryReactiveStore<string>();
        var pipeline = new ReactiveNeuronPipeline<string>(store, maxCausalDepth: 8);
        var commandId = Guid.NewGuid();
        var meta = Meta(commandId, commandId, commandId);
        var reactions = 0;

        var first = await pipeline.ExecuteCommandAsync(
            new CommandSynapse<string>(meta, "a"),
            async (_, commit) =>
            {
                reactions++;
                await commit(new ReactiveCommit<string>("state", UiRevision: 1, Outbox: []));
                return CommandReceiptStatus.Accepted;
            });
        var second = await pipeline.ExecuteCommandAsync(
            new CommandSynapse<string>(meta, "b"),
            async (_, commit) =>
            {
                reactions++;
                await commit(new ReactiveCommit<string>("state", UiRevision: 1, Outbox: []));
                return CommandReceiptStatus.Accepted;
            });

        Assert.Equal(first.CommandId, second.CommandId);
        Assert.Equal(1, reactions);
        Assert.Equal(first.CommandId, store.Receipts[commandId.ToString("N")].CommandId);
        Assert.Equal(1, pipeline.ReactionCount);
    }
}

[Alias("brain.tests.IProbeNeuron")]
[NeuronContract("probe.neuron.v1")]
public interface IProbeNeuron : IGrainWithStringKey, IDeactivatableGrain
{
    [Alias("ExecuteCommandAsync")]
    Task<CommandReceipt> ExecuteCommandAsync(CommandSynapse<string> command);

    [Alias("ExecuteTypedEmitAsync")]
    Task<CommandReceipt> ExecuteTypedEmitAsync(CommandSynapse<ProbeDomainEvent> command, Guid streamId);

    [Alias("HandleEventAsync")]
    Task HandleEventAsync(EventSynapse<string> @event);

    [Alias("ApplyUiActionAsync")]
    Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command);

    [Alias("GetReactionCountAsync")]
    Task<int> GetReactionCountAsync();

    [Alias("GetRevisionAsync")]
    Task<long> GetRevisionAsync();

    [Alias("GetOutboxCountAsync")]
    Task<int> GetOutboxCountAsync();

    [Alias("GetOutboxAttemptCountAsync")]
    Task<int> GetOutboxAttemptCountAsync();

    [Alias("GetPublishedCountAsync")]
    Task<int> GetPublishedCountAsync();

    [Alias("TryGetReceiptAsync")]
    Task<CommandReceipt?> TryGetReceiptAsync(Guid commandId);

    [Alias("SetFailNextCommitAsync")]
    Task SetFailNextCommitAsync(bool fail);

    [Alias("SetPublishFailuresAsync")]
    Task SetPublishFailuresAsync(int failures);

    [Alias("SetAutoDrainAsync")]
    Task SetAutoDrainAsync(bool enabled);

    [Alias("SetThrowRawMessageAsync")]
    Task SetThrowRawMessageAsync(string message);

    [Alias("GetLastFailureAsync")]
    Task<SanitizedFailure?> GetLastFailureAsync();

    [Alias("HasOutboxReminderAsync")]
    Task<bool> HasOutboxReminderAsync();

    [Alias("ReceiveOutboxReminderAsync")]
    Task ReceiveOutboxReminderAsync();

    [Alias("DrainOutboxAsync")]
    Task DrainOutboxAsync();

    [Alias("DrainOutboxStrictAsync")]
    Task DrainOutboxStrictAsync();

    [Alias("PublishDirectAsync")]
    Task PublishDirectAsync(Guid streamId, ProbeDomainEvent payload);

    [Alias("PublishEventSynapseAsync")]
    Task PublishEventSynapseAsync(Guid streamId, EventSynapse<ProbeDomainEvent> @event);

    [Alias("GetLastPublishedIntentTypeWitnessAsync")]
    Task<object?> GetLastPublishedIntentTypeWitnessAsync();

    [Alias("PeekOutboxEventAsync")]
    Task<EventSynapse<ProbeDomainEvent>?> PeekOutboxEventAsync();

    [Alias("DeactivateAsync")]
    Task DeactivateAsync();
}

public sealed class ProbeNeuron(
    [FromKeyedServices("probe-receipts")] IDurableDictionary<string, CommandReceipt> receipts,
    [FromKeyedServices("probe-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("probe-sequences")] IDurableDictionary<string, long> sourceSequences,
    [FromKeyedServices("probe-outbox")] IDurableList<OutboxIntent<ProbeDomainEvent>> outbox,
    [FromKeyedServices("probe-domain")] IDurableDictionary<string, string> domain,
    [FromKeyedServices("probe-flags")] IDurableDictionary<string, string> flags,
    [FromKeyedServices("probe-failures")] IDurableList<SanitizedFailure> failures,
    [FromKeyedServices("probe-accepted-causation")] IDurableDictionary<string, byte> acceptedCausation,
    [FromKeyedServices("probe-causation")] IDurableDictionary<string, byte> rejectedCausation) : ReactiveNeuron<ProbeDomainEvent>(
        receipts,
        processedEvents,
        sourceSequences,
        outbox,
        domain,
        flags,
        failures,
        acceptedCausation,
        rejectedCausation), IProbeNeuron
{
    public const string EventStreamNamespace = "probe.events";
    private int _published;
    private object? _lastPublishedIntent;
    private string? _throwRawMessage;

    protected override bool AutoDrainAfterCommit =>
        Flags.TryGetValue("auto-drain", out var value) && value == "1";

    public Task<CommandReceipt> ExecuteCommandAsync(CommandSynapse<string> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            if (_throwRawMessage is not null)
                throw new InvalidOperationException(_throwRawMessage);

            var intents = payload == "emit"
                ? new[]
                {
                    OutboxIntent<ProbeDomainEvent>.Create(
                        EventStreamNamespace,
                        command.Metadata.CommandId,
                        new EventSynapse<ProbeDomainEvent>(
                            command.Metadata with
                            {
                                EventId = Guid.NewGuid(),
                                CausalDepth = command.Metadata.CausalDepth + 1,
                                Source = NeuronAddress.Parse(this.GetPrimaryKeyString()),
                            },
                            new ProbeDomainEvent(payload, 0))),
                }
                : Array.Empty<OutboxIntent<ProbeDomainEvent>>();
            var revision = CurrentRevision + 1;
            await commit(new ReactiveCommit<ProbeDomainEvent>(payload, UiRevision: revision, Outbox: intents));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> ExecuteTypedEmitAsync(CommandSynapse<ProbeDomainEvent> command, Guid streamId) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            var @event = new EventSynapse<ProbeDomainEvent>(
                command.Metadata with
                {
                    EventId = Guid.NewGuid(),
                    CausalDepth = command.Metadata.CausalDepth + 1,
                    Source = NeuronAddress.Parse(this.GetPrimaryKeyString()),
                },
                payload);
            var intent = OutboxIntent<ProbeDomainEvent>.Create(EventStreamNamespace, streamId, @event);
            await commit(new ReactiveCommit<ProbeDomainEvent>(
                DomainState,
                UiRevision: CurrentRevision + 1,
                Outbox: [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task HandleEventAsync(EventSynapse<string> @event) =>
        HandleEventCoreAsync(@event, async (_, commit) =>
        {
            await commit(new ReactiveCommit<ProbeDomainEvent>(DomainState, UiRevision: UiRevision, Outbox: []));
        });

    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            EnsureExpectedUiRevision(payload.ExpectedRevision);
            await commit(new ReactiveCommit<ProbeDomainEvent>(DomainState, UiRevision: UiRevision + 1, Outbox: []));
            return CommandReceiptStatus.Accepted;
        });

    public Task<int> GetReactionCountAsync() => Task.FromResult(ReactionCount);
    public Task<long> GetRevisionAsync() => Task.FromResult(CurrentRevision);
    public Task<int> GetOutboxCountAsync() => Task.FromResult(Outbox.Count);
    public Task<int> GetOutboxAttemptCountAsync() =>
        Task.FromResult(Outbox.Count == 0 ? 0 : Outbox[0].AttemptCount);
    public Task<int> GetPublishedCountAsync() => Task.FromResult(_published);

    public Task<CommandReceipt?> TryGetReceiptAsync(Guid commandId) =>
        Task.FromResult(Receipts.TryGetValue(commandId.ToString("N"), out var receipt) ? receipt : null);

    public Task SetFailNextCommitAsync(bool fail)
    {
        FailNextCommit = fail;
        return Task.CompletedTask;
    }

    public async Task SetPublishFailuresAsync(int failures)
    {
        Flags["publish-failures"] = failures.ToString();
        await WriteStateAsync(CancellationToken.None);
    }

    public async Task SetAutoDrainAsync(bool enabled)
    {
        Flags["auto-drain"] = enabled ? "1" : "0";
        await WriteStateAsync(CancellationToken.None);
    }

    public Task SetThrowRawMessageAsync(string message)
    {
        _throwRawMessage = message;
        return Task.CompletedTask;
    }

    public Task<SanitizedFailure?> GetLastFailureAsync() =>
        Task.FromResult(Failures.Count == 0 ? null : Failures[^1]);

    public async Task<bool> HasOutboxReminderAsync()
    {
        var reminder = await GetOutboxReminderAsync();
        return reminder is not null;
    }

    public Task ReceiveOutboxReminderAsync() =>
        ReceiveReminder(OutboxReminderName, new TickStatus(DateTime.UtcNow, TimeSpan.FromMinutes(1), DateTime.UtcNow));

    public Task DrainOutboxAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: false);

    public Task DrainOutboxStrictAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: true);

    public Task PublishDirectAsync(Guid streamId, ProbeDomainEvent payload)
    {
        var meta = new SynapseMetadata(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new OrganizationId("org-1"),
            new PrincipalId("principal-1"),
            new SpaceId("space-1"),
            NeuronAddress.Parse(this.GetPrimaryKeyString()),
            1,
            0,
            DateTimeOffset.UtcNow);
        return PublishEventSynapseAsync(streamId, new EventSynapse<ProbeDomainEvent>(meta, payload));
    }

    public Task PublishEventSynapseAsync(Guid streamId, EventSynapse<ProbeDomainEvent> @event) =>
        PublishEventAsync(@event, DefaultStreamProviderName, EventStreamNamespace, streamId);

    public Task<object?> GetLastPublishedIntentTypeWitnessAsync() => Task.FromResult(_lastPublishedIntent);

    public Task<EventSynapse<ProbeDomainEvent>?> PeekOutboxEventAsync() =>
        Task.FromResult(Outbox.Count == 0 ? null : Outbox[0].Event);

    public Task<Guid> GetActivationTokenAsync() => Task.FromResult(_activationToken);

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task DeactivateAsync() => RequestDeactivationAsync();

    private Guid _activationToken;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationToken = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<ProbeDomainEvent> intent)
    {
        if (Flags.TryGetValue("publish-failures", out var raw) && int.TryParse(raw, out var remaining) && remaining > 0)
        {
            Flags["publish-failures"] = (remaining - 1).ToString();
            throw new InvalidOperationException("publish failed");
        }

        await base.PublishOutboxIntentAsync(intent);
        _lastPublishedIntent = intent;
        _published++;
    }
}

public sealed class TypedEventConsumerNeuron(
    [FromKeyedServices("consumer-receipts")] IDurableDictionary<string, CommandReceipt> receipts,
    [FromKeyedServices("consumer-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("consumer-sequences")] IDurableDictionary<string, long> sourceSequences,
    [FromKeyedServices("consumer-outbox")] IDurableList<OutboxIntent<ProbeDomainEvent>> outbox,
    [FromKeyedServices("consumer-domain")] IDurableDictionary<string, string> domain,
    [FromKeyedServices("consumer-flags")] IDurableDictionary<string, string> flags,
    [FromKeyedServices("consumer-failures")] IDurableList<SanitizedFailure> failures,
    [FromKeyedServices("consumer-accepted-causation")] IDurableDictionary<string, byte> acceptedCausation,
    [FromKeyedServices("consumer-causation")] IDurableDictionary<string, byte> rejectedCausation) : ReactiveNeuron<ProbeDomainEvent>(
        receipts,
        processedEvents,
        sourceSequences,
        outbox,
        domain,
        flags,
        failures,
        acceptedCausation,
        rejectedCausation), ITypedEventConsumer
{
    public const string StreamSubscribedFlag = "event-stream-subscribed";

    private ProbeDomainEvent? _lastPayload;
    private Guid _activationToken;
    private string? _streamError;

    protected override bool AutoDrainAfterCommit => false;

    protected override async Task OnReactiveActivateAsync(CancellationToken cancellationToken)
    {
        _activationToken = Guid.NewGuid();
        if (Flags.TryGetValue(StreamSubscribedFlag, out var subscribed) && subscribed == "1")
            await RegisterStreamAsync();
    }

    public async Task ReadyAsync()
    {
        Flags[StreamSubscribedFlag] = "1";
        await WriteStateAsync(CancellationToken.None);
        await RegisterStreamAsync();
    }

    public Task<ProbeDomainEvent?> GetLastPayloadAsync()
    {
        if (_streamError is not null)
            throw new InvalidOperationException(_streamError);

        if (_lastPayload is not null)
            return Task.FromResult<ProbeDomainEvent?>(_lastPayload);

        if (Flags.TryGetValue("last-payload-name", out var name)
            && Flags.TryGetValue("last-payload-value", out var valueRaw)
            && int.TryParse(valueRaw, out var value))
        {
            return Task.FromResult<ProbeDomainEvent?>(new ProbeDomainEvent(name, value));
        }

        return Task.FromResult<ProbeDomainEvent?>(null);
    }

    public Task<bool> HasActiveSubscriptionAsync() => Task.FromResult(ActiveSubscriptionCount > 0);

    public Task<int> GetActiveSubscriptionCountAsync() => Task.FromResult(ActiveSubscriptionCount);

    public Task<Guid> GetActivationTokenAsync() => Task.FromResult(_activationToken);

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private Task RegisterStreamAsync() =>
        RegisterEventSubscriptionAsync(
            DefaultStreamProviderName,
            ProbeNeuron.EventStreamNamespace,
            this.GetPrimaryKey(),
            OnStreamEventAsync);

    private async Task OnStreamEventAsync(EventSynapse<ProbeDomainEvent> item, StreamSequenceToken? token)
    {
        try
        {
            _lastPayload = item.Payload;
            Flags["last-payload-name"] = item.Payload.Name;
            Flags["last-payload-value"] = item.Payload.Value.ToString();
            await WriteStateAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _streamError = ex.ToString();
            throw;
        }
    }
}

public sealed class ReactiveNeuronClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public ReactiveNeuronClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose()
    {
        Cluster.StopAllSilos();
        Cluster.Dispose();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.UseJsonJournalFormat(TestJournalJsonContext.Default);
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(ReactiveNeuron<ProbeDomainEvent>.DefaultStreamProviderName, configure =>
            {
                configure.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedOnly);
            });
        }
    }
}