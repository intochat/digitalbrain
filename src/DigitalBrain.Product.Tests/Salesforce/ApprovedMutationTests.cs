using System.Collections.Concurrent;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Salesforce;
using DigitalBrain.Product.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Salesforce;

public sealed class ApprovedMutationTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    private const string DefaultScope = "testing/default";

    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterVocabulary(typeof(PreparedSalesforceMutation).Assembly)
            .RegisterVocabulary(typeof(ForgeApprovalGranted).Assembly)
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ApprovalDecisionSubmitted>()
            .RegisterIngress<PreparedSalesforceMutation>()
            .RegisterIngress<ForgeApprovalGranted>()
            .RegisterIngress<ForgeSalesforceInvocation>()
            .RegisterIngress<ForgeSalesforceOutcome>()
            .RegisterWorkspaceService<ISalesforceGateway>(workspace => Gateways.For(workspace.Id))
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalWorkspaceInboxNeuron>(ApprovalWorkspaceInboxNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ApprovalDecisionIngress>(ApprovalDecisionIngress.Kind)
            .RegisterNeuron<SalesforceMutationNeuron>(SalesforceMutationNeuron.Kind)
            .RegisterNeuron<SalesforceEffectNeuron>(SalesforceEffectNeuron.Kind)
            .RegisterNeuron<ForgedApprovalGrantEmitter>(ForgedApprovalGrantEmitter.Kind)
            .RegisterNeuron<ForgedSalesforceInvocationEmitter>(ForgedSalesforceInvocationEmitter.Kind)
            .RegisterNeuron<ForgedSalesforceOutcomeEmitter>(ForgedSalesforceOutcomeEmitter.Kind);

    [Fact]
    public async Task PreparedMutationDoesNotCallSalesforceBeforeItsApproval()
    {
        var mutation = Mutation("mutation-before-approval");
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);

        _ = await WaitForJournalAsync(
            new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId),
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(PreparedSalesforceMutation).FullName),
            "the stored prepared Salesforce mutation",
            Cancellation);

        Assert.Empty(DefaultGateway.AppliedFor(mutation.MutationId));
    }

    [Fact]
    public async Task MatchingApprovalInvokesTheExactFrozenMutationOnce()
    {
        var mutation = Mutation("mutation-confirmed");
        var proposal = Proposal(mutation);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(
            mutation.MutationId,
            new PreparedSalesforceMutation(new PreparedAccountDescriptionMutation(
                mutation.MutationId,
                "001-replaced",
                "This later mutation must not replace the prepared one.")),
            Cancellation);
        await PublishAsync(proposal.ProposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/mutation-confirmed",
            new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var effect = new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId);
        _ = await WaitForJournalAsync(
            effect,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(SalesforceInvocationRequested).FullName),
            "the approval-bound Salesforce invocation",
            Cancellation);

        var applied = Assert.Single(DefaultGateway.AppliedFor(mutation.MutationId));
        Assert.Equal("001-acme", applied.AccountId);
        Assert.Equal("Acme raised a Series B.", applied.Description);
        Assert.Equal(mutation.Fingerprint, applied.Fingerprint);
        Assert.Equal(mutation.MutationId, applied.MutationId);

        var mutationPage = await WaitForJournalAsync(
            new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId),
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName),
            "a confirmed Salesforce outcome",
            Cancellation);
        Assert.DoesNotContain(mutationPage.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesforceChangeOutcomeUncertain).FullName);
    }

    [Theory]
    [InlineData("different-action-id", null)]
    [InlineData(null, "different-fingerprint")]
    public async Task MismatchedApprovalBindingNeverReachesTheEffectOrGateway(
        string? actionId,
        string? actionFingerprint)
    {
        var mutation = Mutation("mutation-mismatched-binding");
        var proposal = Proposal(mutation, actionId, actionFingerprint);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(proposal.ProposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/mutation-mismatched-binding",
            new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var mutationNeuron = new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId);
        var page = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalGranted).FullName),
            "the mismatched approval grant",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesforceInvocationRequested).FullName);
        Assert.Empty(DefaultGateway.AppliedFor(mutation.MutationId));
        Assert.Empty((await ReadAsync(
            new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId),
            cancellationToken: Cancellation)).Records);
    }

    [Fact]
    public async Task OutcomeUncertainIsNotReportedAsAConfirmationOrRetried()
    {
        var mutation = Mutation("mutation-uncertain");
        var proposal = Proposal(mutation);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.OutcomeUncertain);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(proposal.ProposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/mutation-uncertain",
            new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var mutationNeuron = new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId);
        var page = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeOutcomeUncertain).FullName),
            "an uncertain Salesforce outcome",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName);
        Assert.Single(DefaultGateway.AppliedFor(mutation.MutationId));
    }

    [Fact]
    public async Task ForgedInternalApprovalGrantDoesNotInvokeTheGateway()
    {
        var mutation = Mutation("mutation-forged-grant");
        var proposal = Proposal(mutation);
        var mutationNeuron = new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(
            "forged-salesforce-signal/mutation-forged-grant",
            new ForgeApprovalGranted(new ApprovalGranted(
                proposal,
                Guid.NewGuid(),
                "actor/mallory",
                Clock.UtcNow)),
            Cancellation);

        var page = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalGranted).FullName),
            "the forged internal approval grant",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesforceInvocationRequested).FullName);
        Assert.Empty(DefaultGateway.AppliedFor(mutation.MutationId));
    }

    [Fact]
    public async Task ForgedInternalInvocationDoesNotCallTheGateway()
    {
        var mutation = Mutation("mutation-forged-invocation");
        var effect = new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(
            "forged-salesforce-signal/mutation-forged-invocation",
            new ForgeSalesforceInvocation(mutation),
            Cancellation);

        _ = await WaitForJournalAsync(
            effect,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(SalesforceInvocationRequested).FullName),
            "the forged internal invocation",
            Cancellation);
        Assert.Empty(DefaultGateway.AppliedFor(mutation.MutationId));
    }

    [Fact]
    public async Task ForgedInternalOutcomeCannotConfirmAnInvocation()
    {
        var mutation = Mutation("mutation-forged-outcome");
        var proposal = Proposal(mutation);
        var mutationNeuron = new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId);
        var effect = new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId);
        var fault = FailNextJournalRecording(effect);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(proposal.ProposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/mutation-forged-outcome",
            new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);
        _ = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceInvocationRequested).FullName),
            "the real approval-bound invocation",
            Cancellation);
        await fault.Consumed.WaitAsync(Cancellation);
        await DeactivateAsync([effect], Cancellation);

        await PublishAsync(
            "forged-salesforce-signal/mutation-forged-outcome",
            new ForgeSalesforceOutcome(mutation, SalesforceGatewayOutcome.Confirmed),
            Cancellation);

        var page = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName),
            "the forged internal confirmation",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName);

        await DrainAsync(mutationNeuron, Cancellation);
        _ = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName),
            "the real provider confirmation",
            Cancellation);
    }

    [Fact]
    public async Task DefaultGatewayOutcomeFailsClosedAsOutcomeUncertain()
    {
        var mutation = Mutation("mutation-default-outcome");
        var proposal = Proposal(mutation);
        DefaultGateway.Configure(mutation, default);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(proposal.ProposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/mutation-default-outcome",
            new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var mutationNeuron = new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId);
        var page = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeOutcomeUncertain).FullName),
            "the default gateway uncertain outcome",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName);
    }

    [Fact]
    public async Task EffectReplayConvergesOnOneLogicalMutationAndAConfirmedOutcome()
    {
        var mutation = Mutation("mutation-effect-replay");
        var proposal = Proposal(mutation);
        var mutationNeuron = new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId);
        var effect = new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId);
        var fault = FailNextJournalRecording(effect);
        DefaultGateway.Configure(mutation, SalesforceGatewayOutcome.Confirmed);

        await PublishAsync(mutation.MutationId, new PreparedSalesforceMutation(mutation), Cancellation);
        await PublishAsync(proposal.ProposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/mutation-effect-replay",
            new ApprovalDecisionSubmitted(
                proposal.ProposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);
        await fault.Consumed.WaitAsync(Cancellation);
        await DeactivateAsync([effect], Cancellation);
        await DrainAsync(mutationNeuron, Cancellation);

        _ = await WaitForJournalAsync(
            mutationNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName),
            "the replayed confirmed Salesforce outcome",
            Cancellation);
        Assert.Single(DefaultGateway.AppliedFor(mutation.MutationId));
    }

    [Fact]
    public async Task WorkspaceBoundGatewayKeepsIdenticalMutationIdsIndependent()
    {
        const string leftScope = "workspace/salesforce-left";
        const string rightScope = "workspace/salesforce-right";
        const string sharedMutationId = "mutation-shared-across-workspaces";
        Gateways.Reset(leftScope);
        Gateways.Reset(rightScope);

        var leftMutation = Mutation(sharedMutationId);
        var rightMutation = Mutation(sharedMutationId);
        var leftProposal = Proposal(leftMutation);
        var rightProposal = Proposal(rightMutation);
        var leftGateway = Gateways.For(leftScope);
        var rightGateway = Gateways.For(rightScope);
        leftGateway.Configure(leftMutation, SalesforceGatewayOutcome.Confirmed);
        rightGateway.Configure(rightMutation, SalesforceGatewayOutcome.OutcomeUncertain);

        var leftMutationChannel = OpenSalesforceWorkspace(leftScope, leftMutation.MutationId);
        var leftProposalChannel = OpenSalesforceWorkspace(leftScope, leftProposal.ProposalId);
        var leftDecisionChannel = OpenSalesforceWorkspace(leftScope, "actor/salesforce-left");
        await leftMutationChannel.Publisher.PublishAsync(new PreparedSalesforceMutation(leftMutation), Cancellation);
        await leftProposalChannel.Publisher.PublishAsync(new ApprovalProposalSubmitted(leftProposal), Cancellation);
        await leftDecisionChannel.Publisher.PublishAsync(
            new ApprovalDecisionSubmitted(
                leftProposal.ProposalId,
                leftProposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var leftNeuron = new NeuronId(SalesforceMutationNeuron.Kind, sharedMutationId);
        _ = await WaitForJournalAsync(
            leftMutationChannel,
            leftNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeConfirmed).FullName),
            "the left workspace confirmation",
            Cancellation);
        Assert.Single(leftGateway.AppliedFor(sharedMutationId));
        Assert.Empty(rightGateway.AppliedFor(sharedMutationId));

        var rightMutationChannel = OpenSalesforceWorkspace(rightScope, rightMutation.MutationId);
        var rightProposalChannel = OpenSalesforceWorkspace(rightScope, rightProposal.ProposalId);
        var rightDecisionChannel = OpenSalesforceWorkspace(rightScope, "actor/salesforce-right");
        await rightMutationChannel.Publisher.PublishAsync(new PreparedSalesforceMutation(rightMutation), Cancellation);
        await rightProposalChannel.Publisher.PublishAsync(new ApprovalProposalSubmitted(rightProposal), Cancellation);
        await rightDecisionChannel.Publisher.PublishAsync(
            new ApprovalDecisionSubmitted(
                rightProposal.ProposalId,
                rightProposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var rightNeuron = new NeuronId(SalesforceMutationNeuron.Kind, sharedMutationId);
        _ = await WaitForJournalAsync(
            rightMutationChannel,
            rightNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(SalesforceChangeOutcomeUncertain).FullName),
            "the independent right workspace uncertain outcome",
            Cancellation);
        Assert.Single(leftGateway.AppliedFor(sharedMutationId));
        Assert.Single(rightGateway.AppliedFor(sharedMutationId));
    }

    private static ControlledSalesforceGateway DefaultGateway => Gateways.For(DefaultScope);

    private WorkspaceChannel OpenSalesforceWorkspace(string scope, string source)
        => OpenWorkspace(
            scope,
            source,
            typeof(ApprovalProposalSubmitted),
            typeof(ApprovalDecisionSubmitted),
            typeof(PreparedSalesforceMutation));

    private static PreparedAccountDescriptionMutation Mutation(string mutationId)
        => new(
            mutationId,
            "001-acme",
            "Acme raised a Series B.");

    private static ApprovalProposal Proposal(
        PreparedAccountDescriptionMutation mutation,
        string? actionId = null,
        string? actionFingerprint = null)
        => new(
            $"proposal-{mutation.MutationId}",
            "Enrich Acme account",
            "Update Acme's Salesforce description from Gmail and web evidence.",
            [new ApprovalEvidence("gmail", "Acme announced its Series B.")],
            [new ApprovalChange("Description", "", mutation.Description)],
            new ApprovalActionBinding(
                PreparedAccountDescriptionMutation.ActionKind,
                actionId ?? mutation.MutationId,
                actionFingerprint ?? mutation.Fingerprint,
                new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId)),
            new DateTimeOffset(2040, 1, 1, 1, 0, 0, TimeSpan.Zero));

    private sealed class ControlledSalesforceGateway : ISalesforceGateway
    {
        private readonly ConcurrentDictionary<string, PreparedAccountDescriptionMutation> applied = [];
        private readonly ConcurrentDictionary<string, SalesforceGatewayOutcome> outcomes = [];

        internal IReadOnlyCollection<PreparedAccountDescriptionMutation> AppliedFor(string mutationId)
            => applied.TryGetValue(mutationId, out var mutation) ? [mutation] : [];

        internal void Configure(
            PreparedAccountDescriptionMutation mutation,
            SalesforceGatewayOutcome outcome)
        {
            applied.TryRemove(mutation.MutationId, out _);
            outcomes[mutation.MutationId] = outcome;
        }

        public Task<SalesforceGatewayOutcome> ApplyOrReconcileAsync(
            PreparedAccountDescriptionMutation mutation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            applied[mutation.MutationId] = mutation;
            return Task.FromResult(outcomes.TryGetValue(mutation.MutationId, out var outcome)
                ? outcome
                : SalesforceGatewayOutcome.OutcomeUncertain);
        }

        internal void Reset()
        {
            applied.Clear();
            outcomes.Clear();
        }
    }

    private static class Gateways
    {
        private static readonly ConcurrentDictionary<string, ControlledSalesforceGateway> ByWorkspace = new(StringComparer.Ordinal);

        internal static ControlledSalesforceGateway For(string workspace)
            => ByWorkspace.GetOrAdd(workspace, static _ => new ControlledSalesforceGateway());

        internal static void Reset(string workspace) => For(workspace).Reset();
    }
}
