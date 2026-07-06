using DigitalBrain.Core;
using DigitalBrain.Ino;
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
        var staged = (await automation.GetOutgoingTimelineAsync())
            .OfType<AutomationDefinitionStaged>()
            .LastOrDefault(item => string.Equals(item.ProposalId, proposal.ProposalId, StringComparison.Ordinal));

        if (staged is null)
        {
            return Failed(proposal, $"No staged automation definition was found for proposal '{proposal.ProposalId}'.");
        }

        await automation.FireAsync(staged.Script);
        await automation.FireAsync(staged.Reaction);

        // Register capability for intent classifier / future vector index (part of modern intent arch)
        var cap = new InoIntentClassifier.Capability(
            staged.Reaction.Id,
            $"Automation: when {staged.Reaction.When} target {staged.Reaction.Target}",
            new[] { proposal.Rationale ?? "" },
            "automation");
        InoIntentClassifier.RegisterCapability(cap);

        await automation.FireAsync(new CapabilityRegistered(cap.Id, cap.Description, cap.Examples, cap.Tier, proposal.Origin));

        return new SelfEvolutionApplyResult(
            proposal.ProposalId,
            proposal.ApplyVia,
            Succeeded: true,
            $"Registered automation reaction {staged.Reaction.Id} with script {staged.Script.Id}.");
    }

    private static SelfEvolutionApplyResult Failed(SelfEvolutionProposal proposal, string details) =>
        new(proposal.ProposalId, proposal.ApplyVia, Succeeded: false, details);
}
