using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Product.Salesforce;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Presentation;

public sealed class ApprovalWorkspaceInboxTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private static readonly NeuronId Inbox = new(
        ApprovalWorkspaceInboxNeuron.Kind,
        ApprovalWorkspaceInboxNeuron.Name);

    private static readonly NeuronId Projection = new(
        ApprovalWorkspaceProjectionNeuron.Kind,
        ApprovalWorkspaceInboxNeuron.Name);

    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterVocabulary(typeof(ApprovalWorkspaceSurfaceRequested).Assembly)
            .RegisterVocabulary(typeof(PreparedSalesforceMutation).Assembly)
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ApprovalDecisionSubmitted>()
            .RegisterIngress<PreparedSalesforceMutation>()
            .RegisterWorkspaceService<ISalesforceGateway>(workspace => Gateways.For(workspace.Id))
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ApprovalDecisionIngress>(ApprovalDecisionIngress.Kind)
            .RegisterNeuron<ApprovalWorkspaceInboxNeuron>(ApprovalWorkspaceInboxNeuron.Kind)
            .RegisterNeuron<ApprovalWorkspaceProjectionNeuron>(ApprovalWorkspaceProjectionNeuron.Kind)
            .RegisterNeuron<SalesforceMutationNeuron>(SalesforceMutationNeuron.Kind)
            .RegisterNeuron<SalesforceEffectNeuron>(SalesforceEffectNeuron.Kind);

    [Fact]
    public async Task ChatProposalProducesFrozenWorkspaceSnapshotWithOpaqueActionsAndThreePlacements()
    {
        const string proposalId = "workspace-chat-proposal";
        const string mutationId = "private-salesforce-mutation-chat";
        var workspace = OpenApprovalWorkspace("workspace/chat-snapshot", proposalId);
        var proposal = Proposal(
            proposalId,
            mutationId,
            "Review Acme account",
            new ApprovalReviewContext(ApprovalReviewContextKind.ChatConversation, "conversation/acme"));

        await workspace.Publisher.PublishAsync(new ApprovalProposalSubmitted(proposal), Cancellation);

        var surface = await WaitForLatestSurfaceAsync(
            workspace,
            revision: 1,
            "the first chat approval workspace snapshot");
        var item = Assert.Single(surface.Serialization.GetProperty("items").EnumerateArray());

        Assert.Equal(proposalId, item.GetProperty("proposalId").GetString());
        Assert.Equal(proposal.Fingerprint, item.GetProperty("proposalFingerprint").GetString());
        Assert.Equal("Review Acme account", item.GetProperty("title").GetString());
        Assert.Equal("Apply the frozen account description update.", item.GetProperty("summary").GetString());
        Assert.Equal("gmail", item.GetProperty("evidence")[0].GetProperty("source").GetString());
        Assert.Equal(
            "https://evidence.example.test/records/acme",
            item.GetProperty("evidence")[0].GetProperty("referenceUri").GetString());
        Assert.Equal("Description", item.GetProperty("changes")[0].GetProperty("field").GetString());
        Assert.Equal("conversation/acme", item.GetProperty("context").GetProperty("opaqueContextRef").GetString());
        Assert.Equal((int)ApprovalWorkspaceItemStatus.Pending, item.GetProperty("status").GetInt32());
        Assert.Equal(
            [
                (int)ApprovalReviewPlacement.Chat,
                (int)ApprovalReviewPlacement.ContextDrawer,
                (int)ApprovalReviewPlacement.Inbox,
            ],
            item.GetProperty("placements").EnumerateArray().Select(static placement => placement.GetInt32()));

        var actions = item.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Equal(
            [(int)ApprovalReviewDecision.Approve, (int)ApprovalReviewDecision.Reject],
            actions.Select(static action => action.GetProperty("decision").GetInt32()));
        Assert.All(actions, static action => Assert.False(string.IsNullOrWhiteSpace(
            action.GetProperty("reference").GetString())));
        Assert.NotEqual(
            actions[0].GetProperty("reference").GetString(),
            actions[1].GetProperty("reference").GetString());

        var serialized = surface.Serialization.GetRawText();
        Assert.DoesNotContain(mutationId, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("executionTarget", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actionFingerprint", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebhookProposalProducesFrozenWorkspaceSnapshotForInboxOnly()
    {
        const string proposalId = "workspace-webhook-proposal";
        var workspace = OpenApprovalWorkspace("workspace/webhook-snapshot", proposalId);
        var proposal = Proposal(
            proposalId,
            "private-salesforce-mutation-webhook",
            "Review webhook account",
            context: null);

        await workspace.Publisher.PublishAsync(new ApprovalProposalSubmitted(proposal), Cancellation);

        var surface = await WaitForLatestSurfaceAsync(
            workspace,
            revision: 1,
            "the first webhook approval workspace snapshot");
        var item = Assert.Single(surface.Serialization.GetProperty("items").EnumerateArray());

        Assert.Equal(proposalId, item.GetProperty("proposalId").GetString());
        Assert.Equal(proposal.Fingerprint, item.GetProperty("proposalFingerprint").GetString());
        Assert.Equal("Review webhook account", item.GetProperty("title").GetString());
        Assert.Equal("Apply the frozen account description update.", item.GetProperty("summary").GetString());
        Assert.Equal("gmail", item.GetProperty("evidence")[0].GetProperty("source").GetString());
        Assert.Equal("Description", item.GetProperty("changes")[0].GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("context").ValueKind);
        Assert.Equal(
            [(int)ApprovalReviewPlacement.Inbox],
            item.GetProperty("placements").EnumerateArray().Select(static placement => placement.GetInt32()));
    }

    [Fact]
    public async Task DirectInboxDeliveryRecoversAfterActivationAndReproducesTheSnapshot()
    {
        const string proposalId = "workspace-recovery-proposal";
        var proposal = Proposal(
            proposalId,
            "private-salesforce-mutation-recovery",
            "Review recovered account",
            new ApprovalReviewContext(ApprovalReviewContextKind.ChatConversation, "conversation/recovery"));
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);
        var fault = FailNextJournalRecording(Inbox, stickyUntilDisarm: true);

        try
        {
            await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
            await fault.Consumed.WaitAsync(Cancellation);
            await DeactivateAsync([Inbox], Cancellation);
        }
        finally
        {
            await fault.DisposeAsync();
        }

        await DrainAsync(approval, Cancellation);
        var surface = await WaitForLatestSurfaceAsync(
            workspace: null,
            revision: 1,
            "the approval workspace snapshot after inbox activation recovery");
        var item = Assert.Single(surface.Serialization.GetProperty("items").EnumerateArray());

        Assert.Equal(proposalId, item.GetProperty("proposalId").GetString());
        Assert.Equal("Review recovered account", item.GetProperty("title").GetString());
        Assert.Equal("conversation/recovery", item.GetProperty("context").GetProperty("opaqueContextRef").GetString());
    }

    [Fact]
    public async Task ApprovedRejectedExpiredAndMutationUncertainRemainDistinct()
    {
        const string scope = "workspace/distinct-statuses";
        var workspace = OpenWorkspace(
            scope,
            "actor/workspace-statuses",
            typeof(ApprovalProposalSubmitted),
            typeof(ApprovalDecisionSubmitted),
            typeof(PreparedSalesforceMutation));
        var approved = Proposal("workspace-status-approved", "mutation-status-approved", "Approved item", context: null);
        var rejected = Proposal("workspace-status-rejected", "mutation-status-rejected", "Rejected item", context: null);
        var expired = Proposal(
            "workspace-status-expired",
            "mutation-status-expired",
            "Expired item",
            context: null,
            expiresAt: Clock.UtcNow.AddMinutes(-1));
        var uncertainMutation = Mutation("mutation-status-uncertain");
        var uncertain = Proposal(
            "workspace-status-uncertain",
            uncertainMutation,
            "Uncertain item",
            context: null);
        var mutationWorkspace = OpenWorkspace(
            scope,
            uncertainMutation.MutationId,
            typeof(PreparedSalesforceMutation));
        Gateways.For(scope).Configure(uncertainMutation, SalesforceGatewayOutcome.OutcomeUncertain);

        await workspace.Publisher.PublishAsync(new ApprovalProposalSubmitted(approved), Cancellation);
        await workspace.Publisher.PublishAsync(new ApprovalProposalSubmitted(rejected), Cancellation);
        await workspace.Publisher.PublishAsync(new ApprovalProposalSubmitted(expired), Cancellation);
        await workspace.Publisher.PublishAsync(new ApprovalProposalSubmitted(uncertain), Cancellation);
        var pendingSurface = await WaitForLatestSurfaceAsync(
            workspace,
            revision: 4,
            "the four pending approval workspace items");
        var pendingUncertain = pendingSurface.Serialization.GetProperty("items")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("proposalId").GetString(),
                uncertain.ProposalId,
                StringComparison.Ordinal));
        var pendingActionReferences = pendingUncertain.GetProperty("actions")
            .EnumerateArray()
            .Select(static action => action.GetProperty("reference").GetString())
            .ToArray();

        await mutationWorkspace.Publisher.PublishAsync(new PreparedSalesforceMutation(uncertainMutation), Cancellation);
        _ = await WaitForJournalAsync(
            mutationWorkspace,
            new NeuronId(SalesforceMutationNeuron.Kind, uncertainMutation.MutationId),
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceMutationPrepared).FullName),
            "the exact prepared mutation before approval",
            Cancellation);

        await DecideAsync(scope, approved, ApprovalDecision.Approve);
        await DecideAsync(scope, rejected, ApprovalDecision.Reject);
        await DecideAsync(scope, expired, ApprovalDecision.Approve);
        await DecideAsync(scope, uncertain, ApprovalDecision.Approve);

        var surface = await WaitForLatestSurfaceAsync(
            workspace,
            revision: 9,
            "the four distinct approval workspace terminal outcomes");
        var statuses = surface.Serialization.GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                static item => item.GetProperty("proposalId").GetString()!,
                static item => item.GetProperty("status").GetInt32(),
                StringComparer.Ordinal);

        Assert.Equal((int)ApprovalWorkspaceItemStatus.Approved, statuses[approved.ProposalId]);
        Assert.Equal((int)ApprovalWorkspaceItemStatus.Rejected, statuses[rejected.ProposalId]);
        Assert.Equal((int)ApprovalWorkspaceItemStatus.Expired, statuses[expired.ProposalId]);
        Assert.Equal((int)ApprovalWorkspaceItemStatus.MutationUncertain, statuses[uncertain.ProposalId]);
        var finalUncertain = surface.Serialization.GetProperty("items")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("proposalId").GetString(),
                uncertain.ProposalId,
                StringComparison.Ordinal));
        Assert.Equal(
            pendingActionReferences,
            finalUncertain.GetProperty("actions")
                .EnumerateArray()
                .Select(static action => action.GetProperty("reference").GetString()));

        var approvalPage = await ReadAsync(
            workspace,
            new NeuronId(ApprovalNeuron.Kind, uncertain.ProposalId),
            cancellationToken: Cancellation);
        var lifecycle = approvalPage.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalStatusChanged).FullName);
        Assert.Equal((int)ApprovalStatus.Approved, lifecycle.Serialization.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task FixedInboxIdentitiesRemainIsolatedAcrossWorkspaces()
    {
        const string proposalId = "shared-relative-proposal";
        var left = OpenApprovalWorkspace("workspace/inbox-left", proposalId);
        var right = OpenApprovalWorkspace("workspace/inbox-right", proposalId);
        var leftProposal = Proposal(proposalId, "shared-relative-mutation", "Left workspace item", context: null);
        var rightProposal = Proposal(proposalId, "shared-relative-mutation", "Right workspace item", context: null);

        await left.Publisher.PublishAsync(new ApprovalProposalSubmitted(leftProposal), Cancellation);
        await right.Publisher.PublishAsync(new ApprovalProposalSubmitted(rightProposal), Cancellation);

        var leftSurface = await WaitForLatestSurfaceAsync(
            left,
            revision: 1,
            "the isolated left workspace snapshot");
        var rightSurface = await WaitForLatestSurfaceAsync(
            right,
            revision: 1,
            "the isolated right workspace snapshot");
        var leftItem = Assert.Single(leftSurface.Serialization.GetProperty("items").EnumerateArray());
        var rightItem = Assert.Single(rightSurface.Serialization.GetProperty("items").EnumerateArray());

        Assert.Equal("Left workspace item", leftItem.GetProperty("title").GetString());
        Assert.Equal("Right workspace item", rightItem.GetProperty("title").GetString());
        Assert.Equal(proposalId, leftItem.GetProperty("proposalId").GetString());
        Assert.Equal(proposalId, rightItem.GetProperty("proposalId").GetString());
    }

    private async Task DecideAsync(
        string scope,
        ApprovalProposal proposal,
        ApprovalDecision decision)
        => await OpenWorkspace(
                scope,
                $"actor/{proposal.ProposalId}",
                typeof(ApprovalDecisionSubmitted))
            .Publisher
            .PublishAsync(new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                decision),
            Cancellation);

    private WorkspaceChannel OpenApprovalWorkspace(string scope, string source)
        => OpenWorkspace(scope, source, typeof(ApprovalProposalSubmitted));

    private async Task<JournalRecord> WaitForLatestSurfaceAsync(
        WorkspaceChannel? workspace,
        long revision,
        string expectation)
    {
        var page = workspace is null
            ? await WaitForJournalAsync(
                Projection,
                observed => HasRevision(observed, revision),
                expectation,
                Cancellation)
            : await WaitForJournalAsync(
                workspace,
                Projection,
                observed => HasRevision(observed, revision),
                expectation,
                Cancellation);
        return page.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalWorkspaceSurfaceRequested).FullName
            && record.Serialization.GetProperty("revision").GetInt64() >= revision);
    }

    private static bool HasRevision(JournalPage page, long revision)
        => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalWorkspaceSurfaceRequested).FullName
            && record.Serialization.GetProperty("revision").GetInt64() >= revision);

    private static PreparedAccountDescriptionMutation Mutation(string mutationId)
        => new(mutationId, "001-acme", "Updated account description");

    private static ApprovalProposal Proposal(
        string proposalId,
        string mutationId,
        string title,
        ApprovalReviewContext? context,
        DateTimeOffset? expiresAt = null)
        => Proposal(proposalId, Mutation(mutationId), title, context, expiresAt);

    private static ApprovalProposal Proposal(
        string proposalId,
        PreparedAccountDescriptionMutation mutation,
        string title,
        ApprovalReviewContext? context,
        DateTimeOffset? expiresAt = null)
        => new(
            proposalId,
            title,
            "Apply the frozen account description update.",
            [
                new ApprovalEvidence(
                    "gmail",
                    "The customer announced its funding round.",
                    new Uri("https://evidence.example.test/records/acme?access_token=secret#fragment")),
                new ApprovalEvidence(
                    "web",
                    "A hostile URI must not reach the workspace surface.",
                    new Uri("https://operator:credential@evidence.example.test/private")),
            ],
            [new ApprovalChange("Description", "", mutation.Description)],
            new ApprovalActionBinding(
                PreparedAccountDescriptionMutation.ActionKind,
                mutation.MutationId,
                mutation.Fingerprint,
                new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId)),
            expiresAt ?? new DateTimeOffset(2040, 1, 1, 1, 0, 0, TimeSpan.Zero),
            context);

    private sealed class ControlledSalesforceGateway : ISalesforceGateway
    {
        private readonly ConcurrentDictionary<string, SalesforceGatewayOutcome> outcomes = [];

        internal void Configure(PreparedAccountDescriptionMutation mutation, SalesforceGatewayOutcome outcome)
            => outcomes[mutation.MutationId] = outcome;

        public Task<SalesforceGatewayOutcome> ApplyOrReconcileAsync(
            PreparedAccountDescriptionMutation mutation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(outcomes.TryGetValue(mutation.MutationId, out var outcome)
                ? outcome
                : SalesforceGatewayOutcome.Confirmed);
        }
    }

    private static class Gateways
    {
        private static readonly ConcurrentDictionary<string, ControlledSalesforceGateway> ByWorkspace =
            new(StringComparer.Ordinal);

        internal static ControlledSalesforceGateway For(string workspace)
            => ByWorkspace.GetOrAdd(workspace, static _ => new ControlledSalesforceGateway());
    }
}
