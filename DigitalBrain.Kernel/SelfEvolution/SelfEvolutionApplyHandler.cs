using DigitalBrain.Core;

namespace DigitalBrain.Kernel.SelfEvolution;

public interface ISelfEvolutionApplyHandler
{
    string ApplyVia { get; }
    SelfEvolutionRisk MaxRisk { get; }
    Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct);
}

internal sealed class SelfEvolutionApplyRegistry(IEnumerable<ISelfEvolutionApplyHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, ISelfEvolutionApplyHandler> _handlers = handlers
        .Where(handler => !string.IsNullOrWhiteSpace(handler.ApplyVia))
        .GroupBy(handler => handler.ApplyVia, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    private readonly HashSet<string> _duplicateApplyVia = handlers
        .Where(handler => !string.IsNullOrWhiteSpace(handler.ApplyVia))
        .GroupBy(handler => handler.ApplyVia, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet(StringComparer.Ordinal);

    public async Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        if (_duplicateApplyVia.Contains(proposal.ApplyVia))
        {
            return Failed(proposal, $"Multiple self-evolution apply handlers are registered for '{proposal.ApplyVia}'.");
        }

        if (!_handlers.TryGetValue(proposal.ApplyVia, out var handler))
        {
            return Failed(proposal, $"No self-evolution apply handler is registered for '{proposal.ApplyVia}'.");
        }

        if (proposal.Risk > handler.MaxRisk)
        {
            return Failed(proposal, $"Handler '{proposal.ApplyVia}' allows {handler.MaxRisk} but proposal risk is {proposal.Risk}.");
        }

        try
        {
            var result = await handler.ApplyAsync(proposal, ct);
            return NormalizeResult(proposal, result);
        }
        catch (Exception ex)
        {
            return Failed(proposal, ex.GetBaseException().Message);
        }
    }

    private static SelfEvolutionApplyResult Failed(SelfEvolutionProposal proposal, string details) =>
        new(proposal.ProposalId, proposal.ApplyVia, Succeeded: false, details);

    private static SelfEvolutionApplyResult NormalizeResult(SelfEvolutionProposal proposal, SelfEvolutionApplyResult result)
    {
        if (string.Equals(result.ProposalId, proposal.ProposalId, StringComparison.Ordinal)
            && string.Equals(result.ApplyVia, proposal.ApplyVia, StringComparison.Ordinal))
        {
            return result;
        }

        return new SelfEvolutionApplyResult(
            proposal.ProposalId,
            proposal.ApplyVia,
            result.Succeeded,
            result.Details,
            result.RollbackCheckpointId);
    }
}
