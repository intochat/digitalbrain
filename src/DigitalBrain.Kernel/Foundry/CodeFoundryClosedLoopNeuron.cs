using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.foundry.loop.v1")]
public class CodeFoundryClosedLoopNeuron(ILogger<CodeFoundryClosedLoopNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICodeFoundryLoopNeuron
{
    public async Task HandleAsync(FoundryRequest request)
    {
        var checkpoint = await CreateCheckpointAsync();
        var checkpointId = checkpoint.Timestamp.ToUnixTimeMilliseconds().ToString();
        await FireAsync(new FoundryCheckpointed(request.Spec, checkpointId));

        var codeGen = GrainFactory.GetGrain<ICodeGenNeuron>("foundry-codegen");
        await codeGen.FireAsync(new GenerateCode(request.Spec, request.Tier));
        var generated = (await codeGen.GetOutgoingTimelineAsync())
            .OfType<CodeGenerated>()
            .LastOrDefault(g => g.Spec == request.Spec);

        if (generated is null)
        {
            await FireAsync(new FoundryRolledBack(request.Spec, "no-source", checkpointId));
            return;
        }

        if (request.AutoApply)
        {
            if (!TrustedAutoApply)
            {
                await FireAsync(new FoundryRolledBack(request.Spec, "auto-apply-requires-trusted-config", checkpointId));
                return;
            }

            await ApplyImmediatelyAsync(request, generated, checkpointId);
            return;
        }

        await StageApplyAsync(request, generated, checkpointId);
    }

    private async Task StageApplyAsync(FoundryRequest request, CodeGenerated generated, string checkpointId)
    {
        var moduleName = request.Tier == TargetTier.Deploy ? StableModuleName(request.Spec) : string.Empty;
        var proposalId = "foundry-" + Guid.NewGuid().ToString("N");
        var staged = new FoundryApplyStaged(
            proposalId,
            Self.Value,
            request.Spec,
            request.Tier,
            generated.Source,
            generated.RequiredRefs,
            checkpointId,
            moduleName);
        await FireAsync(staged);

        var risk = request.Tier == TargetTier.Run ? SelfEvolutionRisk.InProcessCode : SelfEvolutionRisk.KernelRestart;
        var applyVia = request.Tier == TargetTier.Run ? SelfEvolutionApplyVia.FoundryRun : SelfEvolutionApplyVia.FoundryDeploy;
        var approval = GrainFactory.GetGrain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionProposal(
            ProposalId: proposalId,
            Scope: "foundry",
            Rationale: $"Code foundry generated {request.Tier} code for: {request.Spec}",
            ProposedChange: request.Tier == TargetTier.Run
                ? "Run generated code in the in-process code runner."
                : $"Build generated module {moduleName} and restart kernel resources.",
            ApplyVia: applyVia,
            Risk: risk,
            RequiresHumanApproval: true,
            RollbackPlan: $"Restore checkpoint {checkpointId} if foundry apply fails verification.",
            Origin: Self.Value)
        {
            Sender = Self,
            Receiver = new NeuronId(SelfEvolutionNeuronIds.Main),
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = CurrentCause?.CorrelationId ?? CurrentCause?.SynapseId,
            CausationId = CurrentCause?.SynapseId
        });
    }

    private async Task ApplyImmediatelyAsync(FoundryRequest request, CodeGenerated generated, string checkpointId)
    {
        if (request.Tier == TargetTier.Run)
        {
            var runner = GrainFactory.GetGrain<ICodeRunNeuron>("foundry-coderun");
            await runner.FireAsync(new RunGeneratedCode(generated.Source, Refs: generated.RequiredRefs));
            var runResult = (await runner.GetOutgoingTimelineAsync()).OfType<CodeRunResult>().LastOrDefault();

            if (runResult is { Success: true })
                await FireAsync(new FoundryCompleted(request.Spec, request.Tier, runResult.Output, Applied: true));
            else
                await FireAsync(new FoundryRolledBack(request.Spec, runResult?.Error ?? "run-failed", checkpointId));
            return;
        }

        var moduleName = StableModuleName(request.Spec);
        var deployer = GrainFactory.GetGrain<ICodeDeployNeuron>("foundry-codedeploy");
        await deployer.FireAsync(new DeployGeneratedCode(generated.Source, moduleName, generated.RequiredRefs, checkpointId));
        var built = (await deployer.GetOutgoingTimelineAsync()).OfType<CodeBuilt>().LastOrDefault(b => b.ModuleName == moduleName);

        if (built is { Success: true })
            await FireAsync(new FoundryCompleted(request.Spec, request.Tier, "restart-requested:" + moduleName, Applied: true));
        else
            await FireAsync(new FoundryRolledBack(request.Spec, "build", checkpointId));
    }

    private bool TrustedAutoApply =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Foundry:TrustedAutoApply", false) ?? false;

    internal static string StableModuleName(string spec)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(spec));
        return "Gen_" + Convert.ToHexString(bytes)[..12];
    }

    // Note on resume-after-restart: in production a Tier-2 restart interrupts this handler after
    // KernelRestartRequested. On reactivation the orchestrator re-reads its journal; because
    // FoundryCompleted is fired immediately after a successful CodeBuilt (before the physical restart
    // completes), the cycle's terminal synapse is already journaled and the loop does not re-run.
    // The Tier-2 scenario here asserts the CodeBuilt/restart path via the deploy neuron (Task 7);
    // end-to-end restart survival is covered by the manual validation in Task 10.
}
