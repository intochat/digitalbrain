using DigitalBrain.Core;
using DigitalBrain.Kernel.SelfEvolution;

namespace DigitalBrain.Kernel;

public sealed class AutomationDefinitionApplyHandler(IGrainFactory grains) : ISelfEvolutionApplyHandler
{
    public string ApplyVia => SelfEvolutionApplyVia.AutomationDefineReaction;
    public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.InProcessCode;

    public async Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(proposal.Origin))
        {
            return Failed(proposal, "Automation proposal origin must be the automation neuron id.");
        }

        var automation = grains.GetGrain<IAutomationNeuron>(proposal.Origin);
        var staged = (await automation.GetOutgoingTimelineAsync(ct))
            .OfType<AutomationDefinitionStaged>()
            .LastOrDefault(item => string.Equals(item.ProposalId, proposal.ProposalId, StringComparison.Ordinal));

        if (staged is null)
        {
            return Failed(proposal, $"No staged automation definition was found for proposal '{proposal.ProposalId}'.");
        }

        await automation.FireAsync(staged.Script, ct);
        await automation.FireAsync(staged.Reaction, ct);

        // Capability registration is journal-only (CapabilityRegistered). Static classifier projection removed.
        await automation.FireAsync(new CapabilityRegistered(
            staged.Reaction.Id,
            $"Automation: when {staged.Reaction.When} target {staged.Reaction.Target}",
            [proposal.Rationale ?? ""],
            "automation",
            proposal.Origin), ct);

        return new SelfEvolutionApplyResult(
            proposal.ProposalId,
            proposal.ApplyVia,
            Succeeded: true,
            $"Registered automation reaction {staged.Reaction.Id} with script {staged.Script.Id}.");
    }

    private static SelfEvolutionApplyResult Failed(SelfEvolutionProposal proposal, string details) =>
        new(proposal.ProposalId, proposal.ApplyVia, Succeeded: false, details);
}

public sealed class AutomationRemovalApplyHandler(IGrainFactory grains) : ISelfEvolutionApplyHandler
{
    public string ApplyVia => SelfEvolutionApplyVia.AutomationRemoveReaction;
    public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.InProcessCode;

    public async Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(proposal.Origin))
        {
            return Failed(proposal, "Automation removal proposal origin must be the automation neuron id.");
        }

        var automation = grains.GetGrain<IAutomationNeuron>(proposal.Origin);
        var staged = (await automation.GetOutgoingTimelineAsync(ct))
            .OfType<AutomationRemovalStaged>()
            .LastOrDefault(item => string.Equals(item.ProposalId, proposal.ProposalId, StringComparison.Ordinal));

        if (staged is null)
        {
            return Failed(proposal, $"No staged automation removal was found for proposal '{proposal.ProposalId}'.");
        }

        await automation.RemoveReactionAsync(staged.ReactionId);
        await automation.FireAsync(new CapabilityRegistered(
            "automation_removed:" + staged.ReactionId,
            $"Removed automation reaction {staged.ReactionId}",
            [staged.ReactionId],
            "automation",
            proposal.Origin), ct);

        return new SelfEvolutionApplyResult(
            proposal.ProposalId,
            proposal.ApplyVia,
            Succeeded: true,
            $"Removed automation reaction {staged.ReactionId}.");
    }

    private static SelfEvolutionApplyResult Failed(SelfEvolutionProposal proposal, string details) =>
        new(proposal.ProposalId, proposal.ApplyVia, Succeeded: false, details);
}
