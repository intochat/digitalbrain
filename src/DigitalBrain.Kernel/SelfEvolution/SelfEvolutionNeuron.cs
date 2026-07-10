using DigitalBrain.Core;

namespace DigitalBrain.Kernel.SelfEvolution;

[GrainType("self-evolution")]
public sealed class SelfEvolutionNeuron(
    ILogger<SelfEvolutionNeuron> logger,
    NeuronJournals journals,
    IEnumerable<ISelfEvolutionApplyHandler> applyHandlers)
    : Neuron(logger, journals), ISelfEvolutionNeuron
{
    private readonly SelfEvolutionApplyRegistry _applyRegistry = new(applyHandlers);
    private readonly Dictionary<string, SelfEvolutionProposal> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _decided = new(StringComparer.Ordinal);
    private readonly HashSet<string> _applied = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expired = new(StringComparer.Ordinal);

    protected override bool ShouldSubscribeToTimeline => false;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        RebuildProjection();
    }

    public async Task HandleAsync(SelfEvolutionProposal proposal, CancellationToken cancellationToken = default)
    {
        if (RejectIfInvalid(proposal) is { } invalidReason)
        {
            await FireAsync(new SelfEvolutionProposalRejected(proposal.ProposalId, invalidReason), cancellationToken);
            return;
        }

        if (_pending.ContainsKey(proposal.ProposalId)
            || _decided.Contains(proposal.ProposalId)
            || _applied.Contains(proposal.ProposalId)
            || _expired.Contains(proposal.ProposalId))
        {
            await FireAsync(new SelfEvolutionProposalRejected(proposal.ProposalId, "ProposalId has already been observed."), cancellationToken);
            return;
        }

        if (IsExpired(proposal))
        {
            _expired.Add(proposal.ProposalId);
            await FireAsync(new SelfEvolutionProposalExpired(proposal.ProposalId, proposal.ExpiresAt), cancellationToken);
            return;
        }

        _pending[proposal.ProposalId] = proposal;
        await FireAsync(new SelfEvolutionProposalPending(proposal.ProposalId, proposal.ApplyVia, proposal.Risk), cancellationToken);
    }

    public async Task HandleAsync(SelfEvolutionDecision decision, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decision.DecidedBy))
        {
            await FireAsync(new SelfEvolutionDecisionRejected(decision.ProposalId, "DecidedBy is required."), cancellationToken);
            return;
        }

        if (_decided.Contains(decision.ProposalId) || _applied.Contains(decision.ProposalId))
        {
            await FireAsync(new SelfEvolutionDecisionRejected(decision.ProposalId, "Proposal has already been decided."), cancellationToken);
            return;
        }

        if (!_pending.TryGetValue(decision.ProposalId, out var proposal))
        {
            await FireAsync(new SelfEvolutionDecisionRejected(decision.ProposalId, "No pending proposal exists for this decision."), cancellationToken);
            return;
        }

        if (IsExpired(proposal))
        {
            _pending.Remove(proposal.ProposalId);
            _expired.Add(proposal.ProposalId);
            await FireAsync(new SelfEvolutionProposalExpired(proposal.ProposalId, proposal.ExpiresAt), cancellationToken);
            await FireAsync(new SelfEvolutionDecisionRejected(decision.ProposalId, "Proposal has expired."), cancellationToken);
            return;
        }

        _pending.Remove(proposal.ProposalId);
        _decided.Add(proposal.ProposalId);
        await FireAsync(new SelfEvolutionDecisionRecorded(decision.ProposalId, decision.Approved, decision.DecidedBy, decision.Reason), cancellationToken);

        if (!decision.Approved)
        {
            return;
        }

        var result = await _applyRegistry.ApplyAsync(proposal, cancellationToken);
        await FireAsync(result, cancellationToken);
        if (result.Succeeded)
        {
            _applied.Add(proposal.ProposalId);
        }
        else if (!string.IsNullOrWhiteSpace(result.RollbackCheckpointId))
        {
            await FireAsync(new SelfEvolutionRollbackRequired(
                proposal.ProposalId,
                proposal.ApplyVia,
                result.RollbackCheckpointId,
                result.Details), cancellationToken);
        }
    }

    private void RebuildProjection()
    {
        _pending.Clear();
        _decided.Clear();
        _applied.Clear();
        _expired.Clear();

        var timeline = IncomingJournal.Concat(OutgoingJournal).ToArray();

        foreach (var proposal in timeline.OfType<SelfEvolutionProposal>())
        {
            if (RejectIfInvalid(proposal) is null && !IsExpired(proposal))
            {
                _pending[proposal.ProposalId] = proposal;
            }
            else if (IsExpired(proposal))
            {
                _expired.Add(proposal.ProposalId);
            }
        }

        foreach (var expired in timeline.OfType<SelfEvolutionProposalExpired>())
        {
            _pending.Remove(expired.ProposalId);
            _expired.Add(expired.ProposalId);
        }

        foreach (var rejected in timeline.OfType<SelfEvolutionProposalRejected>())
        {
            _pending.Remove(rejected.ProposalId);
        }

        foreach (var decision in timeline.OfType<SelfEvolutionDecisionRecorded>())
        {
            _pending.Remove(decision.ProposalId);
            _decided.Add(decision.ProposalId);
        }

        foreach (var result in timeline.OfType<SelfEvolutionApplyResult>().Where(result => result.Succeeded))
        {
            _pending.Remove(result.ProposalId);
            _applied.Add(result.ProposalId);
        }
    }

    private static string? RejectIfInvalid(SelfEvolutionProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.ProposalId))
        {
            return "ProposalId is required.";
        }

        if (string.IsNullOrWhiteSpace(proposal.ApplyVia))
        {
            return "ApplyVia is required.";
        }

        if (string.IsNullOrWhiteSpace(proposal.Origin))
        {
            return "Origin is required.";
        }

        if (string.IsNullOrWhiteSpace(proposal.RollbackPlan))
        {
            return "RollbackPlan is required.";
        }

        return null;
    }

    private static bool IsExpired(SelfEvolutionProposal proposal) =>
        proposal.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;
}


