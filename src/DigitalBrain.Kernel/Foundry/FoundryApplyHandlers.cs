using DigitalBrain.Core;
using DigitalBrain.Kernel.SelfEvolution;

namespace DigitalBrain.Kernel.Foundry;

public sealed class FoundryRunApplyHandler(IGrainFactory grains) : ISelfEvolutionApplyHandler
{
    public string ApplyVia => SelfEvolutionApplyVia.FoundryRun;
    public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.InProcessCode;

    public async Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var staged = await FindStagedAsync(proposal);
        if (staged is null)
        {
            return Failed(proposal, $"No staged foundry run was found for proposal '{proposal.ProposalId}'.");
        }

        var runner = grains.GetGrain<ICodeRunNeuron>("foundry-coderun");
        await runner.FireAsync(new RunGeneratedCode(staged.Source, Refs: staged.RequiredRefs));
        var runResult = (await runner.GetOutgoingTimelineAsync()).OfType<CodeRunResult>().LastOrDefault();

        var foundry = grains.GetGrain<ICodeFoundryLoopNeuron>(staged.FoundryNeuronId);
        if (runResult is { Success: true })
        {
            await foundry.FireAsync(new FoundryCompleted(staged.Spec, staged.Tier, runResult.Output, Applied: true));
            return new SelfEvolutionApplyResult(proposal.ProposalId, proposal.ApplyVia, Succeeded: true, runResult.Output, staged.CheckpointId);
        }

        var reason = runResult?.Error ?? "run-failed";
        await foundry.FireAsync(new FoundryRolledBack(staged.Spec, reason, staged.CheckpointId));
        return Failed(proposal, reason, staged.CheckpointId);
    }

    private async Task<FoundryApplyStaged?> FindStagedAsync(SelfEvolutionProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.Origin)) return null;
        var foundry = grains.GetGrain<ICodeFoundryLoopNeuron>(proposal.Origin);
        return (await foundry.GetOutgoingTimelineAsync())
            .OfType<FoundryApplyStaged>()
            .LastOrDefault(staged => string.Equals(staged.ProposalId, proposal.ProposalId, StringComparison.Ordinal));
    }

    private static SelfEvolutionApplyResult Failed(SelfEvolutionProposal proposal, string details, string? checkpointId = null) =>
        new(proposal.ProposalId, proposal.ApplyVia, Succeeded: false, details, checkpointId);
}

public sealed class FoundryDeployApplyHandler(IGrainFactory grains) : ISelfEvolutionApplyHandler
{
    public string ApplyVia => SelfEvolutionApplyVia.FoundryDeploy;
    public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.KernelRestart;

    public async Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var staged = await FindStagedAsync(proposal);
        if (staged is null)
        {
            return Failed(proposal, $"No staged foundry deploy was found for proposal '{proposal.ProposalId}'.");
        }

        var deployer = grains.GetGrain<ICodeDeployNeuron>("foundry-codedeploy");
        await deployer.FireAsync(new DeployGeneratedCode(staged.Source, staged.ModuleName, staged.RequiredRefs, staged.CheckpointId));
        var built = (await deployer.GetOutgoingTimelineAsync()).OfType<CodeBuilt>().LastOrDefault(b => b.ModuleName == staged.ModuleName);

        var foundry = grains.GetGrain<ICodeFoundryLoopNeuron>(staged.FoundryNeuronId);
        if (built is { Success: true })
        {
            var outcome = "restart-requested:" + staged.ModuleName;
            await foundry.FireAsync(new FoundryCompleted(staged.Spec, staged.Tier, outcome, Applied: true));
            return new SelfEvolutionApplyResult(proposal.ProposalId, proposal.ApplyVia, Succeeded: true, outcome, staged.CheckpointId);
        }

        await foundry.FireAsync(new FoundryRolledBack(staged.Spec, "build", staged.CheckpointId));
        return Failed(proposal, built?.BuildLog ?? "build", staged.CheckpointId);
    }

    private async Task<FoundryApplyStaged?> FindStagedAsync(SelfEvolutionProposal proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.Origin)) return null;
        var foundry = grains.GetGrain<ICodeFoundryLoopNeuron>(proposal.Origin);
        return (await foundry.GetOutgoingTimelineAsync())
            .OfType<FoundryApplyStaged>()
            .LastOrDefault(staged => string.Equals(staged.ProposalId, proposal.ProposalId, StringComparison.Ordinal));
    }

    private static SelfEvolutionApplyResult Failed(SelfEvolutionProposal proposal, string details, string? checkpointId = null) =>
        new(proposal.ProposalId, proposal.ApplyVia, Succeeded: false, details, checkpointId);
}
