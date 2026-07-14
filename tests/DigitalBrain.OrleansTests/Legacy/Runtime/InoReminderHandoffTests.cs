using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.OrleansTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoReminderHandoffTests : NeuronTestBase
{
    private static int _workflowCalls;

    protected override void ConfigureSilo(ISiloBuilder builder)
    {
        var keyRing = new RuntimeStateKeyRing(
            1,
            new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)1, 32).ToArray() },
            Enumerable.Repeat((byte)2, 32).ToArray());

        builder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.Conversations)
            .AddMemoryGrainStorage(RuntimeStateStorageProviders.SurfaceFeeds)
            .Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
                services.AddSingleton(new EncryptedRuntimeStateProtector(keyRing));
                services.AddSingleton<IAgentWorkflowRunner, SucceedingWorkflowRunner>();
                services.AddSingleton<IInoEffectExecutor, DisabledInoEffectExecutor>();
            });
    }

    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Conversation_reminder_hands_off_to_worker_reminder_and_completes_the_operation()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(
            owner,
            actor,
            "conversation-reminder-handoff");
        var conversation = Grain<IConversationNeuron>(RuntimeStateKeys.Conversation(
            owner,
            actor,
            identity.ConversationId));
        var now = DateTimeOffset.UtcNow;
        var acceptedEventId = "accepted-reminder-handoff";
        var acceptedProjection = OperationOutboxRecord.Create(
            acceptedEventId,
            "operation-reminder-handoff",
            InoOperationPhase.Accepted,
            1,
            now,
            identity.ConversationId,
            2,
            "request-reminder-handoff",
            RuntimeStateKeys.Conversation(owner, actor, identity.ConversationId),
            new OperationFeedView(
                "command-reminder-handoff",
                string.Empty,
                false,
                null,
                null,
                null,
                [new OperationFeedTurn(
                    "command-reminder-handoff",
                    "user",
                    "summarize the status",
                    InoConversationStates.Queued)]));

        var initialized = await conversation.InitializeAsync(0, identity);
        await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-reminder-handoff",
            new string('a', 64),
            "operation-reminder-handoff",
            "summarize the status",
            "request-reminder-handoff",
            new ConversationOutboxEntry(acceptedEventId, "surface-feed", acceptedProjection.ToPayloadUtf8(), now, null),
            now);

        var completed = await WaitForOperationAsync(
            conversation,
            "operation-reminder-handoff",
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));

        Assert.Equal(ConversationOperationStatus.Succeeded, completed.Status);
        Assert.True(
            Volatile.Read(ref _workflowCalls) == 1,
            $"A reminder handoff must execute one claimed workflow; calls={Volatile.Read(ref _workflowCalls)}, attempt={completed.Attempt}, version={completed.Version}.");
    }

    [Fact]
    public async Task Conversation_operation_retains_capability_and_proposal_receipts_after_reload()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(
            owner,
            actor,
            "conversation-receipt-handoff");
        var conversationKey = RuntimeStateKeys.Conversation(owner, actor, identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var now = DateTimeOffset.UtcNow;
        var acceptedOutboxId = "accepted-receipt-handoff";
        var acceptedProjection = OperationOutboxRecord.Create(
            acceptedOutboxId,
            "operation-receipt-handoff",
            InoOperationPhase.Accepted,
            1,
            now,
            identity.ConversationId,
            2,
            "request-receipt-handoff",
            conversationKey,
            new OperationFeedView(
                "command-receipt-handoff",
                string.Empty,
                false,
                null,
                null,
                null,
                [new OperationFeedTurn(
                    "command-receipt-handoff",
                    "user",
                    "read my records",
                    InoConversationStates.Queued)]));

        var initialized = await conversation.InitializeAsync(0, identity);
        var begun = await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-receipt-handoff",
            new string('e', 64),
            "operation-receipt-handoff",
            "read my records",
            "request-receipt-handoff",
            new ConversationOutboxEntry(acceptedOutboxId, "surface-feed", acceptedProjection.ToPayloadUtf8(), now, null),
            now);
        var claim = await conversation.TryClaimOperationAsync(
            begun.Revision,
            "operation-receipt-handoff",
            "test-worker",
            now,
            TimeSpan.FromMinutes(1));
        Assert.True(claim.Acquired);

        var capability = new CapabilityResolutionReceipt(
            CapabilityResolutionKind.Match,
            "salesforce.record.read.v1",
            "Read Salesforce records",
            [],
            0.92);
        var proposal = new FeatureDraftReference(
            "proposal-0123456789abcdef0123456789abcdef",
            "Open Studio",
            "/features/proposals/proposal-0123456789abcdef0123456789abcdef");
        var terminalOutboxId = "terminal-receipt-handoff";
        var assistantText = "I can help with that using Read Salesforce records.";
        var terminalProjection = OperationOutboxRecord.Create(
            terminalOutboxId,
            "operation-receipt-handoff",
            InoOperationPhase.Succeeded,
            claim.Operation!.Version + 1,
            now,
            identity.ConversationId,
            claim.State.Revision + 1,
            "request-receipt-handoff",
            conversationKey,
            new OperationFeedView(
                "command-receipt-handoff",
                string.Empty,
                false,
                null,
                null,
                null,
                [
                    new OperationFeedTurn(
                        "command-receipt-handoff",
                        "user",
                        "read my records",
                        InoConversationStates.Succeeded),
                    new OperationFeedTurn(
                        "operation-receipt-handoff",
                        "assistant",
                        assistantText,
                        InoConversationStates.Succeeded)
                ],
                capability,
                proposal));

        await conversation.CompleteWithAssistantAsync(
            claim.State.Revision,
            "operation-receipt-handoff",
            ConversationOperationStatus.Succeeded,
            ConversationTerminalPolicy.NeverRetry,
            null,
            assistantText,
            new ConversationOutboxEntry(terminalOutboxId, "surface-feed", terminalProjection.ToPayloadUtf8(), now, null),
            now,
            leaseFence: new ConversationLeaseFence("test-worker", claim.Operation.Attempt),
            capability: capability,
            proposal: proposal);

        await Cluster.DeactivateAsync((IAddressable)conversation);

        var reloaded = Grain<IConversationNeuron>(conversationKey);
        var state = await reloaded.ReadAsync();
        var operation = state.Operations.Single(candidate =>
            string.Equals(candidate.OperationId, "operation-receipt-handoff", StringComparison.Ordinal));

        Assert.NotNull(operation.Capability);
        Assert.Equal(capability.Kind, operation.Capability!.Kind);
        Assert.Equal(capability.CapabilityId, operation.Capability.CapabilityId);
        Assert.Equal(capability.CapabilityName, operation.Capability.CapabilityName);
        Assert.Equal(capability.CandidateIds, operation.Capability.CandidateIds);
        Assert.Equal(capability.Confidence, operation.Capability.Confidence);
        Assert.Equal(proposal, operation.Proposal);
    }

    [Fact]
    public async Task Outbox_dispatcher_leaves_a_noncanonical_surface_feed_payload_pending_without_reordering_later_phases()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(
            owner,
            actor,
            "conversation-noncanonical-outbox");
        var conversationKey = RuntimeStateKeys.Conversation(
            owner,
            actor,
            identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var malformedOutboxId = "noncanonical-outbox";
        var now = DateTimeOffset.UtcNow;

        var initialized = await conversation.InitializeAsync(0, identity);
        await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-noncanonical-outbox",
            new string('b', 64),
            "operation-noncanonical-outbox",
            "summarize the status",
            "request-noncanonical-outbox",
            new ConversationOutboxEntry(
                malformedOutboxId,
                "surface-feed",
                Encoding.UTF8.GetBytes("{\"EventId\":\"noncanonical-outbox\"}"),
                now,
                null),
            now);
        await Grain<IInoConversationOutboxDispatcherGrain>(conversationKey).ScheduleAsync();

        await WaitForOperationAsync(
            conversation,
            "operation-noncanonical-outbox",
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));
        var state = await conversation.ReadAsync();

        Assert.Null(state.Outbox.Single(entry =>
            string.Equals(entry.OutboxId, malformedOutboxId, StringComparison.Ordinal)).DispatchedAt);
        Assert.All(state.Outbox, entry => Assert.Null(entry.DispatchedAt));
    }

    [Fact]
    public async Task Outbox_dispatcher_upgrades_the_exact_legacy_presentation_without_rebuilding_history()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(
            owner,
            actor,
            "ino-" + new string('a', 64));
        var conversationKey = RuntimeStateKeys.Conversation(
            owner,
            actor,
            identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var feed = Grain<ISurfaceFeedNeuron>(RuntimeStateKeys.SurfaceFeed(owner, actor));
        var now = DateTimeOffset.UtcNow;
        var legacyProjectionId = "legacy-five-field-presentation";
        await SeedLegacyPresentationAsync(
            feed,
            identity,
            legacyProjectionId,
            now,
            includeConversationRevision: false,
            includePresentationVersion: false);

        var acceptedOutboxId = "accepted-legacy-presentation";
        var initialized = await conversation.InitializeAsync(0, identity);
        var begun = await BeginOperationAsync(
            conversation,
            conversationKey,
            identity,
            initialized,
            acceptedOutboxId,
            now);
        var claim = await conversation.TryClaimOperationAsync(
            begun.Revision,
            "operation-legacy-presentation",
            "test-worker",
            now,
            TimeSpan.FromMinutes(1));
        Assert.True(claim.Acquired);
        var terminalOutboxId = "failed-legacy-presentation";
        var terminalOccurredAt = now.Subtract(UiProtocol.ActionTokenLifetime).AddSeconds(-1);
        var terminalProjection = OperationOutboxRecord.Create(
            terminalOutboxId,
            "operation-legacy-presentation",
            InoOperationPhase.Failed,
            claim.Operation!.Version + 1,
            terminalOccurredAt,
            identity.ConversationId,
            claim.State.Revision + 1,
            "request-legacy-presentation",
            conversationKey,
            new OperationFeedView(
                "command-legacy-presentation",
                string.Empty,
                false,
                "The workflow could not finish.",
                null,
                null,
                [
                    new OperationFeedTurn(
                        "command-legacy-presentation",
                        "user",
                        "summarize the status",
                        InoConversationStates.Failed),
                    new OperationFeedTurn(
                        "operation-legacy-presentation",
                        "assistant",
                        "I couldn't finish that response.",
                        InoConversationStates.Failed)
                ]));
        await conversation.CompleteWithAssistantAsync(
            claim.State.Revision,
            "operation-legacy-presentation",
            ConversationOperationStatus.Failed,
            ConversationTerminalPolicy.NeverRetry,
            "The workflow could not finish.",
            "I couldn't finish that response.",
            new ConversationOutboxEntry(
                terminalOutboxId,
                "surface-feed",
                terminalProjection.ToPayloadUtf8(),
                terminalOccurredAt,
                null),
            now,
            leaseFence: new ConversationLeaseFence("test-worker", claim.Operation.Attempt));
        await Grain<IInoConversationOutboxDispatcherGrain>(conversationKey).ScheduleAsync();

        var projected = await WaitForSurfaceStateAsync(
            feed,
            state => state.AppliedProjectionIds.Contains(terminalOutboxId, StringComparer.Ordinal),
            TimeSpan.FromSeconds(12));

        Assert.Equal(legacyProjectionId, projected.EventHistory[0].ProjectionId);
        Assert.Contains(projected.EventHistory, record =>
            string.Equals(record.ProjectionId, acceptedOutboxId, StringComparison.Ordinal));
        Assert.Contains(projected.EventHistory, record =>
            string.Equals(record.ProjectionId, terminalOutboxId, StringComparison.Ordinal));
        var current = Assert.Single(projected.CurrentSurfaces);
        var presentation = JsonSerializer.Deserialize<SurfaceFeedPresentation>(current.PayloadUtf8);
        Assert.NotNull(presentation);
        Assert.Equal(SurfaceFeedPresentation.CurrentVersion, presentation.PresentationVersion);
        Assert.Equal(identity.ConversationId, presentation.CauseId);
        var sendBinding = Assert.Single(projected.ActionBindings, binding =>
            string.Equals(binding.BindingId, ConversationSurfacePayload.SendBindingId, StringComparison.Ordinal));
        Assert.True(sendBinding.ExpiresAt > DateTimeOffset.UtcNow);
        var conversationState = await conversation.ReadAsync();
        Assert.NotNull(conversationState.Outbox.Single(entry =>
            string.Equals(entry.OutboxId, acceptedOutboxId, StringComparison.Ordinal)).DispatchedAt);
    }

    [Fact]
    public async Task Outbox_dispatcher_rejects_a_partial_legacy_presentation_upgrade()
    {
        Interlocked.Exchange(ref _workflowCalls, 0);
        var owner = new BrainOwnerId("owner");
        var actor = new ActorId("principal");
        var identity = new ConversationIdentity(
            owner,
            actor,
            "ino-" + new string('b', 64));
        var conversationKey = RuntimeStateKeys.Conversation(
            owner,
            actor,
            identity.ConversationId);
        var conversation = Grain<IConversationNeuron>(conversationKey);
        var feed = Grain<ISurfaceFeedNeuron>(RuntimeStateKeys.SurfaceFeed(owner, actor));
        var now = DateTimeOffset.UtcNow;
        var legacyProjectionId = "partial-legacy-presentation";
        await SeedLegacyPresentationAsync(
            feed,
            identity,
            legacyProjectionId,
            now,
            includeConversationRevision: true,
            includePresentationVersion: false);

        var acceptedOutboxId = "accepted-partial-legacy-presentation";
        var initialized = await conversation.InitializeAsync(0, identity);
        await BeginOperationAsync(
            conversation,
            conversationKey,
            identity,
            initialized,
            acceptedOutboxId,
            now);
        await Grain<IInoConversationOutboxDispatcherGrain>(conversationKey).ScheduleAsync();

        await WaitForOperationAsync(
            conversation,
            "operation-legacy-presentation",
            ConversationOperationStatus.Succeeded,
            TimeSpan.FromSeconds(12));
        var conversationState = await conversation.ReadAsync();
        var feedState = await feed.ReadAsync();

        Assert.Null(conversationState.Outbox.Single(entry =>
            string.Equals(entry.OutboxId, acceptedOutboxId, StringComparison.Ordinal)).DispatchedAt);
        Assert.Equal([legacyProjectionId], feedState.AppliedProjectionIds);
        Assert.Equal([legacyProjectionId], feedState.EventHistory.Select(record => record.ProjectionId));
    }

    private static async Task SeedLegacyPresentationAsync(
        ISurfaceFeedNeuron feed,
        ConversationIdentity identity,
        string projectionId,
        DateTimeOffset now,
        bool includeConversationRevision,
        bool includePresentationVersion)
    {
        var initialized = await feed.InitializeAsync(
            0,
            new SurfaceFeedIdentity(identity.OwnerId, identity.ActorId));
        var conversation = new InoConversationSnapshot(identity.ConversationId, 0, [], []);
        var payload = ConversationSurfacePayload.Build(conversation);
        var presentation = new Dictionary<string, object?>
        {
            [nameof(SurfaceFeedPresentation.CorrelationId)] = "request-legacy-presentation",
            [nameof(SurfaceFeedPresentation.CauseKind)] = "conversation",
            [nameof(SurfaceFeedPresentation.CauseId)] = identity.ConversationId,
            [nameof(SurfaceFeedPresentation.RequiredClientCapabilities)] = ConversationSurfacePayload.RequiredCapabilities,
            [nameof(SurfaceFeedPresentation.Payload)] = payload
        };
        if (includeConversationRevision)
            presentation[nameof(SurfaceFeedPresentation.ConversationRevision)] = 0;
        if (includePresentationVersion)
            presentation[nameof(SurfaceFeedPresentation.PresentationVersion)] = SurfaceFeedPresentation.CurrentVersion;
        StoredActionBinding[] descriptors =
        [
            new(
                "ino.new",
                "ino.conversation.new",
                "digitalbrain.ino.empty-input.v1",
                "ui.action",
                1,
                now.Add(UiProtocol.ActionTokenLifetime)),
            new(
                "ino.delete",
                "ino.conversation.delete",
                "digitalbrain.ino.empty-input.v1",
                "ui.action",
                1,
                now.Add(UiProtocol.ActionTokenLifetime))
        ];
        await feed.ApplyProjectionAsync(
            initialized.Revision,
            new SurfaceFeedProjection(
                projectionId,
                ConversationSurfacePayload.HomeSurfaceId,
                1,
                SurfaceContentHash.Compute(payload, descriptors),
                JsonSerializer.SerializeToUtf8Bytes(presentation),
                now,
                null,
                descriptors.Select(descriptor => new SurfaceActionBinding(
                    descriptor.BindingId,
                    ConversationSurfacePayload.HomeSurfaceId,
                    1,
                    descriptor.ActionType,
                    descriptor.InputSchemaRef,
                    descriptor.RequiredGrant,
                    descriptor.ActionSchemaVersion,
                    new string('c', 64),
                    descriptor.MaxUses,
                    0,
                    descriptor.ExpiresAt,
                    null,
                    null)).ToArray()),
            now);
    }

    private static async Task<ConversationState> BeginOperationAsync(
        IConversationNeuron conversation,
        string conversationKey,
        ConversationIdentity identity,
        ConversationState initialized,
        string acceptedOutboxId,
        DateTimeOffset now,
        DateTimeOffset? projectionOccurredAt = null)
    {
        var occurredAt = projectionOccurredAt ?? now;
        var acceptedProjection = OperationOutboxRecord.Create(
            acceptedOutboxId,
            "operation-legacy-presentation",
            InoOperationPhase.Accepted,
            1,
            occurredAt,
            identity.ConversationId,
            2,
            "request-legacy-presentation",
            conversationKey,
            new OperationFeedView(
                "command-legacy-presentation",
                string.Empty,
                false,
                null,
                null,
                null,
                [new OperationFeedTurn(
                    "command-legacy-presentation",
                    "user",
                    "summarize the status",
                    InoConversationStates.Queued)]));
        return await conversation.BeginOperationAsync(
            initialized.Revision,
            "command-legacy-presentation",
            new string('d', 64),
            "operation-legacy-presentation",
            "summarize the status",
            "request-legacy-presentation",
            new ConversationOutboxEntry(
                acceptedOutboxId,
                "surface-feed",
                acceptedProjection.ToPayloadUtf8(),
                now,
                null),
            now);
    }

    private static async Task<ConversationOperation> WaitForOperationAsync(
        IConversationNeuron conversation,
        string operationId,
        ConversationOperationStatus expectedStatus,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var operation = (await conversation.ReadAsync()).Operations.Single(candidate =>
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
            if (operation.Status == expectedStatus) return operation;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        var final = (await conversation.ReadAsync()).Operations.Single(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        throw new Xunit.Sdk.XunitException(
            $"Operation {operationId} did not reach {expectedStatus}; final state was {final.Status}.");
    }

    private static async Task<ConversationState> WaitForStateAsync(
        IConversationNeuron conversation,
        Func<ConversationState, bool> condition,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var state = await conversation.ReadAsync();
            if (condition(state)) return state;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException("The expected durable outbox state was not reached.");
    }

    private static async Task<SurfaceFeedState> WaitForSurfaceStateAsync(
        ISurfaceFeedNeuron feed,
        Func<SurfaceFeedState, bool> condition,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var state = await feed.ReadAsync();
            if (condition(state)) return state;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException("The expected durable surface-feed state was not reached.");
    }

    private sealed class SucceedingWorkflowRunner : IAgentWorkflowRunner
    {
        public Task<InoWorkflowResult> ExecuteAsync(
            InoWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _workflowCalls);
            return Task.FromResult(new InoWorkflowResult(
                "The status is ready.",
                new WorkflowReference("test", "workflow", "session")));
        }
    }
}
