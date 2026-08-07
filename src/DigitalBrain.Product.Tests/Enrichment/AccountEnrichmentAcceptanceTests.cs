using System.Collections.Concurrent;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Conversation;
using DigitalBrain.Product.Enrichment;
using DigitalBrain.Product.Google;
using DigitalBrain.Product.Memory;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Product.Salesforce;
using DigitalBrain.Product.Time;
using DigitalBrain.Product.Webhooks;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Enrichment;

public sealed class AccountEnrichmentAcceptanceTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterVocabulary(typeof(PreparedSalesforceMutation).Assembly)
            .RegisterVocabulary(typeof(MemoryStoreRequested).Assembly)
            .RegisterVocabulary(typeof(AccountEnrichmentStarted).Assembly)
            .RegisterVocabulary(typeof(ChatEnrichmentRequested).Assembly)
            .RegisterVocabulary(typeof(VerifiedWebhookDeliveryReceived).Assembly)
            .RegisterVocabulary(typeof(GmailWebhookTriggerNeuron).Assembly)
            .RegisterVocabulary(typeof(ApprovalReviewSurfaceRequested).Assembly)
            .RegisterVocabulary(typeof(ProposalDeadlineArmed).Assembly)
            .RegisterIngress<ChatEnrichmentRequested>()
            .RegisterIngress<VerifiedWebhookDeliveryReceived>()
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ApprovalDecisionSubmitted>()
            .RegisterWorkspaceService<IEmailEvidenceReader>(workspace => Scenarios.For(workspace.Id))
            .RegisterWorkspaceService<IWebEvidenceResearcher>(workspace => Scenarios.For(workspace.Id))
            .RegisterWorkspaceService<IAccountDescriptionComposer>(workspace => Scenarios.For(workspace.Id))
            .RegisterWorkspaceService<IMemoryStore>(workspace => Scenarios.For(workspace.Id))
            .RegisterWorkspaceService<IProposalDeadlineScheduler>(workspace => Scenarios.For(workspace.Id))
            .RegisterWorkspaceService<ISalesforceGateway>(workspace => Scenarios.For(workspace.Id))
            .RegisterWorkspaceService<IGmailWebhookDeliveryReader>(workspace => Scenarios.For(workspace.Id))
            .RegisterNeuron<ConversationIngressNeuron>(ConversationIngressNeuron.Kind)
            .RegisterNeuron<WebhookIngressNeuron>(WebhookIngressNeuron.Kind)
            .RegisterNeuron<GmailWebhookTriggerNeuron>(GmailWebhookTriggerNeuron.Kind)
            .RegisterNeuron<GmailWebhookDeliveryFailureObserver>(GmailWebhookDeliveryFailureObserver.Kind)
            .RegisterNeuron<AccountEnrichmentNeuron>(AccountEnrichmentNeuron.Kind)
            .RegisterNeuron<EmailEvidenceNeuron>(EmailEvidenceNeuron.Kind)
            .RegisterNeuron<WebEvidenceNeuron>(WebEvidenceNeuron.Kind)
            .RegisterNeuron<MemoryNeuron>(MemoryNeuron.Kind)
            .RegisterNeuron<SalesforceMutationNeuron>(SalesforceMutationNeuron.Kind)
            .RegisterNeuron<SalesforceEffectNeuron>(SalesforceEffectNeuron.Kind)
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ApprovalDecisionIngress>(ApprovalDecisionIngress.Kind)
            .RegisterNeuron<ProposalDeadlineNeuron>(ProposalDeadlineNeuron.Kind)
            .RegisterNeuron<ApprovalReviewProjectionNeuron>(ApprovalReviewProjectionNeuron.Kind);

    [Fact]
    public async Task ChatFlowFreezesEvidenceRendersReviewAndCompletesOnlyAfterApproval()
    {
        const string scope = "workspace/enrichment-chat";
        const string runId = "run-chat-acme";
        const string conversationId = "conversation/acme";
        Scenarios.Reset(scope);
        var scenario = Scenarios.For(scope);
        var request = Request(runId, conversationId);
        var chat = OpenChannel(scope, conversationId, typeof(ChatEnrichmentRequested));

        await chat.Publisher.PublishAsync(new ChatEnrichmentRequested(request), Cancellation);

        var pending = await WaitForPendingAsync(chat, runId, "the frozen enrichment approval", Cancellation);
        Assert.Equal("Acme enrichment proposal", pending.Serialization.GetProperty("title").GetString());
        Assert.Equal(2, pending.Serialization.GetProperty("evidence").GetArrayLength());
        Assert.Equal("Acme closed a Series B funding round.", pending.Serialization.GetProperty("changes")[0].GetProperty("proposedValue").GetString());
        Assert.DoesNotContain("action", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executionTarget", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(scenario.AppliedMutations);

        var proposalId = AccountEnrichmentIds.ProposalIdOf(runId);
        var projection = new NeuronId(ApprovalReviewProjectionNeuron.Kind, proposalId);
        var surfacePage = await WaitForJournalAsync(
            chat,
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName),
            "a semantic approval review surface",
            Cancellation);
        var surface = surfacePage.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName);
        Assert.Equal(3, surface.Serialization.GetProperty("placements").GetArrayLength());
        Assert.Equal(
            conversationId,
            surface.Serialization.GetProperty("context").GetProperty("opaqueContextRef").GetString());
        Assert.DoesNotContain("executionTarget", surface.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("001-acme", surface.Serialization.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("workspace", surface.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var fingerprint = pending.Serialization.GetProperty("proposalFingerprint").GetString()
            ?? throw new InvalidOperationException("The frozen proposal is missing its fingerprint.");
        var actor = OpenChannel(scope, "actor/ada", typeof(ApprovalDecisionSubmitted));
        await actor.Publisher.PublishAsync(
            new ApprovalDecisionSubmitted(proposalId, fingerprint, Guid.NewGuid(), ApprovalDecision.Approve),
            Cancellation);

        var enrichment = new NeuronId(AccountEnrichmentNeuron.Kind, runId);
        _ = await WaitForJournalAsync(
            chat,
            enrichment,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(AccountEnrichmentCompleted).FullName),
            "a confirmed enrichment outcome",
            Cancellation);
        var applied = Assert.Single(scenario.AppliedMutations);
        Assert.Equal(runId, applied.MutationId);
        Assert.Equal("001-acme", applied.AccountId);
        Assert.Equal("Acme closed a Series B funding round.", applied.Description);

        var inbox = await WaitForJournalAsync(
            chat,
            projection,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName) >= 2,
            "a resolved approval inbox item",
            Cancellation);
        var resolved = inbox.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName);
        Assert.Equal((int)ApprovalInboxStatus.Resolved, resolved.Serialization.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task VerifiedDuplicateGmailWebhookStartsOneRunAndOnePendingInboxItem()
    {
        const string scope = "workspace/enrichment-webhook";
        const string subscriptionId = "gmail/subscription-acme";
        const string deliveryId = "gmail-delivery-001";
        const string runId = "run-webhook-acme";
        Scenarios.Reset(scope);
        var scenario = Scenarios.For(scope);
        scenario.WebhookRequests[deliveryId] = Request(runId, "gmail-message/acme");
        var webhook = OpenChannel(scope, subscriptionId, typeof(VerifiedWebhookDeliveryReceived));
        var delivery = new VerifiedWebhookDeliveryReceived(
            "gmail",
            subscriptionId,
            deliveryId,
            new string('a', 64));

        await webhook.Publisher.PublishAsync(delivery, Cancellation);
        _ = await WaitForPendingAsync(webhook, runId, "the webhook-created approval", Cancellation);
        await webhook.Publisher.PublishAsync(delivery, Cancellation);

        var receiver = new NeuronId(WebhookIngressNeuron.Kind, subscriptionId);
        _ = await WaitForJournalAsync(
            webhook,
            receiver,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WebhookDeliveryDuplicate).FullName),
            "a durable duplicate receipt",
            Cancellation);
        var enrichment = new NeuronId(AccountEnrichmentNeuron.Kind, runId);
        var runPage = await ReadAsync(webhook, enrichment, cancellationToken: Cancellation);
        Assert.Equal(
            1,
            runPage.Records.Count(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(AccountEnrichmentStarted).FullName));
        Assert.Equal(1, scenario.WebhookReadCount);

        var projection = new NeuronId(ApprovalReviewProjectionNeuron.Kind, AccountEnrichmentIds.ProposalIdOf(runId));
        var projectionPage = await ReadAsync(webhook, projection, cancellationToken: Cancellation);
        Assert.Equal(
            1,
            projectionPage.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName));
        Assert.Empty(scenario.AppliedMutations);

        var surface = projectionPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName);
        Assert.Equal(
            [(int)ApprovalReviewPlacement.Inbox],
            surface.Serialization.GetProperty("placements").EnumerateArray().Select(static placement => placement.GetInt32()));
    }

    [Fact]
    public async Task DistinctGmailDeliveryForAnAlreadyMappedRunIsTerminallyIgnored()
    {
        const string scope = "workspace/enrichment-webhook-same-run";
        const string subscriptionId = "gmail/subscription-same-run";
        const string firstDeliveryId = "gmail-delivery-first";
        const string secondDeliveryId = "gmail-delivery-second";
        const string runId = "run-webhook-same-run";
        Scenarios.Reset(scope);
        var scenario = Scenarios.For(scope);
        var request = Request(runId, "gmail-message/same-run");
        scenario.WebhookRequests[firstDeliveryId] = request;
        scenario.WebhookRequests[secondDeliveryId] = request;
        var webhook = OpenChannel(scope, subscriptionId, typeof(VerifiedWebhookDeliveryReceived));
        var trigger = new NeuronId(GmailWebhookTriggerNeuron.Kind, subscriptionId);
        var first = new VerifiedWebhookDeliveryReceived(
            "gmail",
            subscriptionId,
            firstDeliveryId,
            new string('c', 64));
        var second = new VerifiedWebhookDeliveryReceived(
            "gmail",
            subscriptionId,
            secondDeliveryId,
            new string('d', 64));

        await webhook.Publisher.PublishAsync(first, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            trigger,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(WebhookDeliveryAccepted).FullName),
            "the first Gmail delivery reconciliation",
            Cancellation);
        await webhook.Publisher.PublishAsync(second, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            trigger,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(WebhookDeliveryAccepted).FullName) >= 2,
            "the second Gmail delivery reconciliation",
            Cancellation);
        await webhook.Publisher.PublishAsync(second, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            trigger,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(WebhookDeliveryDuplicate).FullName),
            "the duplicate Gmail delivery receipt",
            Cancellation);

        Assert.Equal(2, scenario.WebhookReadCount);
    }

    [Fact]
    public async Task FailedGmailStartIsReconciledFromTheFrozenDeliveryMappingAfterProviderRedelivery()
    {
        // Journal fault injection is scoped to the fixture's default workspace.
        const string scope = "testing/default";
        const string subscriptionId = "gmail/subscription-recovery";
        const string deliveryId = "gmail-delivery-recovery";
        const string runId = "run-webhook-recovery";
        Scenarios.Reset(scope);
        var scenario = Scenarios.For(scope);
        scenario.WebhookRequests[deliveryId] = Request(runId, "gmail-message/recovery");
        var webhook = OpenChannel(scope, subscriptionId, typeof(VerifiedWebhookDeliveryReceived));
        var delivery = new VerifiedWebhookDeliveryReceived(
            "gmail",
            subscriptionId,
            deliveryId,
            new string('b', 64));
        var trigger = new NeuronId(GmailWebhookTriggerNeuron.Kind, subscriptionId);
        var failureObserver = new NeuronId(GmailWebhookDeliveryFailureObserver.Kind, subscriptionId);
        var enrichment = new NeuronId(AccountEnrichmentNeuron.Kind, runId);
        var fault = FailNextJournalRecording(enrichment, stickyUntilDisarm: true);

        await webhook.Publisher.PublishAsync(delivery, Cancellation);
        await fault.Consumed.WaitAsync(Cancellation);
        await Clock.AdvanceAsync(TimeSpan.FromMinutes(31), Cancellation);
        await DrainAsync(trigger, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            trigger,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(DeliveryFailed).FullName),
            "the staged terminal Gmail-to-enrichment delivery failure",
            Cancellation);
        await DrainAsync(trigger, Cancellation);
        await DrainAsync(failureObserver, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            trigger,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(GmailWebhookStartDeliveryFailed).FullName),
            "the Gmail-internal terminal-start failure signal",
            Cancellation);

        await fault.DisposeAsync();
        await DeactivateAsync([trigger, enrichment], Cancellation);
        scenario.WebhookRequests[deliveryId] = Request("run-that-must-not-replace-the-frozen-request", "gmail-message/replaced");
        await webhook.Publisher.PublishAsync(delivery, Cancellation);

        _ = await WaitForPendingAsync(webhook, runId, "the recovered frozen webhook run", Cancellation);
        var starts = await ReadAsync(webhook, enrichment, cancellationToken: Cancellation);
        var start = Assert.Single(starts.Records, record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(AccountEnrichmentStarted).FullName);
        Assert.Equal(runId, start.Serialization.GetProperty("request").GetProperty("runId").GetString());
        Assert.Equal(1, scenario.WebhookReadCount);
    }

    [Fact]
    public async Task MemoryFailureDoesNotChangeThePreparedMutationOrBlockTheApproval()
    {
        const string scope = "workspace/enrichment-memory-unavailable";
        const string runId = "run-memory-acme";
        const string conversationId = "conversation/memory-acme";
        Scenarios.Reset(scope);
        var scenario = Scenarios.For(scope);
        scenario.FailMemoryStores = true;
        var chat = OpenChannel(scope, conversationId, typeof(ChatEnrichmentRequested));

        await chat.Publisher.PublishAsync(new ChatEnrichmentRequested(Request(runId, conversationId)), Cancellation);

        var pending = await WaitForPendingAsync(chat, runId, "an approval despite optional memory failure", Cancellation);
        Assert.Equal(
            "Acme closed a Series B funding round.",
            pending.Serialization.GetProperty("changes")[0].GetProperty("proposedValue").GetString());
        var memory = new NeuronId(MemoryNeuron.Kind, runId);
        _ = await WaitForJournalAsync(
            chat,
            memory,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MemoryUnavailable).FullName),
            "a redacted optional-memory failure",
            Cancellation);
        Assert.Empty(scenario.AppliedMutations);
    }

    [Fact]
    public async Task PreparedRunSurvivesReactivationAndStillReportsTheConfirmedOutcome()
    {
        const string scope = "workspace/enrichment-reload";
        const string runId = "run-reload-acme";
        const string conversationId = "conversation/reload-acme";
        Scenarios.Reset(scope);
        var scenario = Scenarios.For(scope);
        var chat = OpenChannel(scope, conversationId, typeof(ChatEnrichmentRequested));

        await chat.Publisher.PublishAsync(
            new ChatEnrichmentRequested(Request(runId, conversationId)),
            Cancellation);

        var pending = await WaitForPendingAsync(chat, runId, "the durable enrichment approval", Cancellation);
        await DeactivateAsync([new NeuronId(AccountEnrichmentNeuron.Kind, runId)], Cancellation);

        var fingerprint = pending.Serialization.GetProperty("proposalFingerprint").GetString()
            ?? throw new InvalidOperationException("The frozen proposal is missing its fingerprint.");
        var actor = OpenChannel(scope, "actor/reload-ada", typeof(ApprovalDecisionSubmitted));
        await actor.Publisher.PublishAsync(
            new ApprovalDecisionSubmitted(
                AccountEnrichmentIds.ProposalIdOf(runId),
                fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var outcome = await WaitForJournalAsync(
            chat,
            new NeuronId(AccountEnrichmentNeuron.Kind, runId),
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(AccountEnrichmentCompleted).FullName),
            "the confirmed outcome after enrichment reactivation",
            Cancellation);
        Assert.Contains(outcome.Records, record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName);
        Assert.Single(scenario.AppliedMutations);
    }

    private static AccountEnrichmentRequest Request(string runId, string contextId)
        => new(
            runId,
            "001-acme",
            "Acme",
            contextId,
            "gmail-message-acme",
            "Acme Series B funding");

    private WorkspaceChannel OpenChannel(string scope, string source, params Type[] ingress)
        => OpenWorkspace(scope, source, ingress);

    private static async Task<JournalRecord> WaitForPendingAsync(
        WorkspaceChannel workspace,
        string runId,
        string expectation,
        CancellationToken cancellationToken)
    {
        var page = await WaitForJournalAsync(
            workspace,
            new NeuronId(ApprovalNeuron.Kind, AccountEnrichmentIds.ProposalIdOf(runId)),
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalPending).FullName),
            expectation,
            cancellationToken);
        return page.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalPending).FullName);
    }

    private static class Scenarios
    {
        private static readonly ConcurrentDictionary<string, Scenario> ByWorkspace = new(StringComparer.Ordinal);

        internal static Scenario For(string workspace)
            => ByWorkspace.GetOrAdd(workspace, static _ => new Scenario());

        internal static void Reset(string workspace) => For(workspace).Reset();
    }

    private sealed class Scenario :
        IEmailEvidenceReader,
        IWebEvidenceResearcher,
        IAccountDescriptionComposer,
        IMemoryStore,
        IProposalDeadlineScheduler,
        ISalesforceGateway,
        IGmailWebhookDeliveryReader
    {
        private readonly ConcurrentDictionary<string, MemoryEntry> memory = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<PreparedAccountDescriptionMutation> appliedMutations = [];
        private int webhookReadCount;

        internal bool FailMemoryStores { get; set; }

        internal ConcurrentDictionary<string, AccountEnrichmentRequest> WebhookRequests { get; } = new(StringComparer.Ordinal);

        internal IReadOnlyCollection<PreparedAccountDescriptionMutation> AppliedMutations => [.. appliedMutations];

        internal int WebhookReadCount => Volatile.Read(ref webhookReadCount);

        internal void Reset()
        {
            memory.Clear();
            while (appliedMutations.TryDequeue(out _))
            {
            }

            WebhookRequests.Clear();
            FailMemoryStores = false;
            Interlocked.Exchange(ref webhookReadCount, 0);
        }

        public Task<IReadOnlyList<EnrichmentEvidence>> ReadAsync(
            EmailEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<EnrichmentEvidence>>(
            [
                new EnrichmentEvidence(
                    "gmail",
                    "Acme announced its Series B funding round in email.",
                    new Uri("https://mail.google.test/messages/gmail-message-acme")),
            ]);
        }

        public Task<IReadOnlyList<EnrichmentEvidence>> ResearchAsync(
            WebEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<EnrichmentEvidence>>(
            [
                new EnrichmentEvidence(
                    "web",
                    "Independent coverage confirms Acme's Series B funding.",
                    new Uri("https://news.example.test/acme-series-b")),
            ]);
        }

        public Task<AccountEnrichmentDraft> ComposeAsync(
            AccountEnrichmentRequest request,
            IReadOnlyList<EnrichmentEvidence> evidence,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AccountEnrichmentDraft("Acme closed a Series B funding round."));
        }

        public Task<MemoryStoreResult> StoreAsync(MemoryEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailMemoryStores)
            {
                throw new InvalidOperationException("memory-provider-secret");
            }

            memory[entry.Id] = entry;
            return Task.FromResult(new MemoryStoreResult(entry.Id));
        }

        public Task<IReadOnlyList<MemoryHit>> SearchAsync(MemoryQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<MemoryHit>>([]);
        }

        public Task RemoveAsync(string entryId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = memory.TryRemove(entryId, out _);
            return Task.CompletedTask;
        }

        public Task ScheduleAsync(ProposalDeadline deadline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<SalesforceGatewayOutcome> ApplyOrReconcileAsync(
            PreparedAccountDescriptionMutation mutation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            appliedMutations.Enqueue(mutation);
            return Task.FromResult(SalesforceGatewayOutcome.Confirmed);
        }

        public Task<AccountEnrichmentRequest?> ReadOrReconcileAsync(
            WebhookDeliveryAccepted delivery,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref webhookReadCount);
            return Task.FromResult(
                WebhookRequests.TryGetValue(delivery.DeliveryId, out var request)
                    ? request
                    : null);
        }
    }
}
