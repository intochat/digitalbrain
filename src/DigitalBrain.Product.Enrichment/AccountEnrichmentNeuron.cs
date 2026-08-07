using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Memory;
using DigitalBrain.Product.Salesforce;

namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Coordinates provider-neutral evidence collection, freezes the exact Salesforce mutation,
/// and requests a whole-proposal approval only after that mutation is durable.
/// </summary>
public sealed class AccountEnrichmentNeuron(IAccountDescriptionComposer composer) : Neuron<AccountEnrichmentState>,
    INeuron<AccountEnrichmentStarted>,
    INeuron<EmailEvidenceCollected>,
    INeuron<WebEvidenceCollected>,
    INeuron<EmailEvidenceUnavailable>,
    INeuron<WebEvidenceUnavailable>,
    INeuron<SalesforceMutationPrepared>,
    INeuron<SalesforceChangeConfirmed>,
    INeuron<SalesforceChangeOutcomeUncertain>
{
    public const string Kind = "account-enrichment";

    private readonly IAccountDescriptionComposer composer = composer ?? throw new ArgumentNullException(nameof(composer));

    public Task HandleAsync(AccountEnrichmentStarted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesRun(synapse.Request.RunId) || !IsTrustedTrigger(Origin.Source))
        {
            return Task.CompletedTask;
        }

        var state = State;
        if (state.Request is not null)
        {
            if (string.Equals(Origin.Source.Kind, "gmail-webhook-trigger", StringComparison.Ordinal)
                && Equals(state.Request, synapse.Request))
            {
                Emit(
                    new AccountEnrichmentRunAccepted(synapse.Request.RunId),
                    Dispatch.Direct(Origin.Source));
            }

            return Task.CompletedTask;
        }

        state.Request = synapse.Request;
        state.ReviewContext = ReviewContextFor(Origin.Source, synapse.Request.ContextId);
        State = state;
        if (string.Equals(Origin.Source.Kind, "gmail-webhook-trigger", StringComparison.Ordinal))
        {
            Emit(
                new AccountEnrichmentRunAccepted(synapse.Request.RunId),
                Dispatch.Direct(Origin.Source));
        }

        Emit(
            new EmailEvidenceRequested(synapse.Request),
            Dispatch.Direct(new NeuronId(EmailEvidenceNeuron.Kind, synapse.Request.RunId)));
        Emit(
            new WebEvidenceRequested(synapse.Request),
            Dispatch.Direct(new NeuronId(WebEvidenceNeuron.Kind, synapse.Request.RunId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(EmailEvidenceCollected synapse, CancellationToken cancellationToken)
        => HandleEvidenceAsync(synapse.RunId, synapse.Evidence, EmailEvidenceNeuron.Kind, isEmail: true, cancellationToken);

    public Task HandleAsync(WebEvidenceCollected synapse, CancellationToken cancellationToken)
        => HandleEvidenceAsync(synapse.RunId, synapse.Evidence, WebEvidenceNeuron.Kind, isEmail: false, cancellationToken);

    public Task HandleAsync(EmailEvidenceUnavailable synapse, CancellationToken cancellationToken)
        => HandleUnavailableAsync(synapse.RunId, EmailEvidenceNeuron.Kind, isEmail: true, cancellationToken);

    public Task HandleAsync(WebEvidenceUnavailable synapse, CancellationToken cancellationToken)
        => HandleUnavailableAsync(synapse.RunId, WebEvidenceNeuron.Kind, isEmail: false, cancellationToken);

    public Task HandleAsync(SalesforceMutationPrepared synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (!MatchesRun(synapse.Mutation.MutationId)
            || state.Request is null
            || state.PreparedMutation is not { } mutation
            || state.ProposalProposed
            || !Equals(Origin.Source, new NeuronId(SalesforceMutationNeuron.Kind, synapse.Mutation.MutationId))
            || !SameMutation(mutation, synapse.Mutation))
        {
            return Task.CompletedTask;
        }

        var proposal = ProposalFor(
            state.Request,
            mutation,
            state.EmailEvidence,
            state.WebEvidence,
            Origin.OccurredAt,
            state.ReviewContext);
        state.ProposalProposed = true;
        State = state;
        Emit(
            new ApprovalProposed(proposal),
            Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, proposal.ProposalId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesforceChangeConfirmed synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return HandleSalesforceOutcomeAsync(synapse.Mutation, SalesforceGatewayOutcome.Confirmed);
    }

    public Task HandleAsync(SalesforceChangeOutcomeUncertain synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return HandleSalesforceOutcomeAsync(synapse.Mutation, SalesforceGatewayOutcome.OutcomeUncertain);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Composition failures become a redacted product outcome instead of leaking provider or model details.")]
    private async Task HandleEvidenceAsync(
        string runId,
        IReadOnlyList<EnrichmentEvidence> evidence,
        string expectedSourceKind,
        bool isEmail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (!MatchesRun(runId)
            || state.Request is null
            || state.OutcomeUncertain
            || state.PreparedMutation is not null
            || !Equals(Origin.Source, new NeuronId(expectedSourceKind, runId)))
        {
            return;
        }

        if (isEmail)
        {
            if (state.EmailEvidence.Count > 0 || state.EmailUnavailable)
            {
                return;
            }

            state.EmailEvidence = CopyEvidence(evidence);
        }
        else
        {
            if (state.WebEvidence.Count > 0 || state.WebUnavailable)
            {
                return;
            }

            state.WebEvidence = CopyEvidence(evidence);
        }

        State = state;
        if (state.EmailEvidence.Count == 0 || state.WebEvidence.Count == 0)
        {
            return;
        }

        try
        {
            var allEvidence = state.EmailEvidence.Concat(state.WebEvidence).ToArray();
            var draft = await composer.ComposeAsync(state.Request, allEvidence, cancellationToken);
            if (draft is null)
            {
                MarkOutcomeUncertain(state, "composition");
                return;
            }

            var mutation = new PreparedAccountDescriptionMutation(
                state.Request.RunId,
                state.Request.AccountId,
                draft.Description);
            state.PreparedMutation = mutation;
            State = state;
            Emit(
                new PreparedSalesforceMutation(mutation),
                Dispatch.Direct(new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId)));
            Emit(
                new MemoryStoreRequested(MemoryEntryFor(state.Request, allEvidence, draft)),
                Dispatch.Direct(new NeuronId(MemoryNeuron.Kind, state.Request.RunId)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            MarkOutcomeUncertain(state, "composition");
        }
    }

    private Task HandleUnavailableAsync(
        string runId,
        string expectedSourceKind,
        bool isEmail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (!MatchesRun(runId)
            || state.Request is null
            || state.OutcomeUncertain
            || state.PreparedMutation is not null
            || !Equals(Origin.Source, new NeuronId(expectedSourceKind, runId)))
        {
            return Task.CompletedTask;
        }

        if (isEmail)
        {
            if (state.EmailUnavailable || state.EmailEvidence.Count > 0)
            {
                return Task.CompletedTask;
            }

            state.EmailUnavailable = true;
        }
        else
        {
            if (state.WebUnavailable || state.WebEvidence.Count > 0)
            {
                return Task.CompletedTask;
            }

            state.WebUnavailable = true;
        }

        MarkOutcomeUncertain(state, "evidence");
        return Task.CompletedTask;
    }

    private Task HandleSalesforceOutcomeAsync(
        PreparedAccountDescriptionMutation mutation,
        SalesforceGatewayOutcome outcome)
    {
        var state = State;
        if (!MatchesRun(mutation.MutationId)
            || state.PreparedMutation is not { } prepared
            || state.Completed
            || state.OutcomeUncertain
            || !Equals(Origin.Source, new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId))
            || !SameMutation(prepared, mutation))
        {
            return Task.CompletedTask;
        }

        if (outcome == SalesforceGatewayOutcome.Confirmed)
        {
            state.Completed = true;
            State = state;
            Emit(new AccountEnrichmentCompleted(mutation.MutationId, mutation.MutationId));
        }
        else
        {
            MarkOutcomeUncertain(state, "salesforce");
        }

        return Task.CompletedTask;
    }

    private void MarkOutcomeUncertain(AccountEnrichmentState state, string stage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        state.OutcomeUncertain = true;
        State = state;
        Emit(new AccountEnrichmentOutcomeUncertain(Id.Name, stage));
    }

    private bool MatchesRun(string runId)
        => string.Equals(Id.Name, runId, StringComparison.Ordinal);

    private static bool IsTrustedTrigger(NeuronId source)
        => string.Equals(source.Kind, "conversation-ingress", StringComparison.Ordinal)
            || string.Equals(source.Kind, "gmail-webhook-trigger", StringComparison.Ordinal);

    private static ApprovalReviewContext? ReviewContextFor(NeuronId source, string contextId)
        => string.Equals(source.Kind, "conversation-ingress", StringComparison.Ordinal)
            ? new ApprovalReviewContext(ApprovalReviewContextKind.ChatConversation, contextId)
            : null;

    private static bool SameMutation(
        PreparedAccountDescriptionMutation expected,
        PreparedAccountDescriptionMutation actual)
        => string.Equals(expected.MutationId, actual.MutationId, StringComparison.Ordinal)
            && string.Equals(expected.Fingerprint, actual.Fingerprint, StringComparison.Ordinal);

    private static ReadOnlyCollection<EnrichmentEvidence> CopyEvidence(IReadOnlyList<EnrichmentEvidence> evidence)
    {
        var copy = evidence.ToArray();
        if (copy.Length == 0 || copy.Any(static item => item is null))
        {
            throw new ArgumentException("Evidence must contain one or more non-null entries.", nameof(evidence));
        }

        return Array.AsReadOnly(copy);
    }

    private static ApprovalProposal ProposalFor(
        AccountEnrichmentRequest request,
        PreparedAccountDescriptionMutation mutation,
        IReadOnlyList<EnrichmentEvidence> emailEvidence,
        IReadOnlyList<EnrichmentEvidence> webEvidence,
        DateTimeOffset observedAt,
        ApprovalReviewContext? reviewContext)
    {
        var evidence = emailEvidence
            .Concat(webEvidence)
            .Select(static item => new ApprovalEvidence(item.Source, item.Summary, item.ReferenceUri))
            .ToArray();
        return new ApprovalProposal(
            AccountEnrichmentIds.ProposalIdOf(request.RunId),
            $"{request.AccountName} enrichment proposal",
            $"Review the proposed Salesforce account description for {request.AccountName}.",
            evidence,
            [new ApprovalChange("Salesforce account description", before: null, mutation.Description)],
            new ApprovalActionBinding(
                PreparedAccountDescriptionMutation.ActionKind,
                mutation.MutationId,
                mutation.Fingerprint,
                new NeuronId(SalesforceMutationNeuron.Kind, mutation.MutationId)),
            observedAt.AddDays(1),
            reviewContext);
    }

    private static MemoryEntry MemoryEntryFor(
        AccountEnrichmentRequest request,
        IReadOnlyList<EnrichmentEvidence> evidence,
        AccountEnrichmentDraft draft)
    {
        var content = string.Join(
            Environment.NewLine,
            evidence.Select(static item => $"[{item.Source}] {item.Summary}")
                .Append($"Proposed description: {draft.Description}"));
        return new MemoryEntry(
            AccountEnrichmentIds.MemoryEntryIdOf(request.RunId),
            content,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "account-enrichment-evidence",
                ["run-id"] = request.RunId,
            });
    }
}
