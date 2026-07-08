using DigitalBrain.Core;
using DigitalBrain.Ino.Context;
using Microsoft.Extensions.AI;
using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Kernel.Company;

[GrainType("company.skill.orchestrator.v1")]
public sealed class CompanySkillOrchestratorNeuron(ILogger<CompanySkillOrchestratorNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICompanySkillOrchestratorNeuron
{
    public async Task HandleAsync(CreateCompanySkill cmd)
    {
        await HandleAsync(cmd, CancellationToken.None);
    }

    public async Task HandleAsync(CreateCompanySkill cmd, CancellationToken cancellationToken)
    {
        var processName = cmd.ProcessName;
        cancellationToken.ThrowIfCancellationRequested();

        Logger.LogInformation("Orchestrator starting skill creation for {Process}", processName);

        string baseDir = AppContext.BaseDirectory;
        string samplesRoot = Path.Combine(baseDir, "..", "..", "..", "samples", "CompanyBrain");
        if (!Directory.Exists(samplesRoot))
            samplesRoot = Path.Combine("samples", "CompanyBrain");

        string policyText = File.Exists(Path.Combine(samplesRoot, "refund-policy.md"))
            ? await File.ReadAllTextAsync(Path.Combine(samplesRoot, "refund-policy.md"), cancellationToken)
            : "Eligibility: within 30 days + receipt or loyalty. Defective <14d auto-approve. High value >500 manual.";

        string transcriptText = File.Exists(Path.Combine(samplesRoot, "refund-transcript.txt"))
            ? await File.ReadAllTextAsync(Path.Combine(samplesRoot, "refund-transcript.txt"), cancellationToken)
            : "Check date first. 30 day window. Defective auto early. Flag high value.";

        var knowledge = GrainFactory.GetGrain<ICompanyKnowledgeNeuron>("company-main");
        await knowledge.FireAsync(new IngestCompanySource("company-skills", $"{processName}-policy", policyText), cancellationToken);
        await knowledge.FireAsync(new IngestCompanySource("company-skills", $"{processName}-transcript", transcriptText), cancellationToken);

        var context = GrainFactory.GetGrain<IContextNeuron>("context-main");
        var fragments = await context.RecallAsync($"how to handle {processName} process decisions", top: 6);

        var crystallizer = ServiceProvider.GetService<ProcessCrystallizer>()
            ?? new ProcessCrystallizer(ServiceProvider.GetService<IChatClient>());
        var crystallized = await crystallizer.CrystallizeAsync(processName, fragments, cancellationToken);

        var synthesizer = ServiceProvider.GetService<SkillPackSynthesizer>() ?? new SkillPackSynthesizer();
        string code = synthesizer.SynthesizePackSource(crystallized.Spec, "1.0");

        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(
            processName, "1.0", code, "system", false, 0.0, $"Auto-generated executable skill for {processName}"), cancellationToken);

        // Trusted internal/system bootstrap path (not user-created mutation). Bypasses human SelfEvolutionProposal
        // approval per design (see architecture-trash-action-plan § bootstrap exceptions). "system" buyer + internal
        // synthesis makes this safe. Public user paths (MCP/UI/Ino) must continue to stage proposals.
        await market.FireAsync(new InstallFromMarketplace(processName, "1.0", "system"), cancellationToken);

        var generated = GrainFactory.GetGrain<IGeneratedNeuron>($"generated-{processName.ToLowerInvariant()}");
        var testTrigger = new RefundRequested("verify-001", 75m, "defective", "cust-test", 5);
        await generated.FireAsync(testTrigger, cancellationToken);

        await Task.Delay(50, cancellationToken);

        var timeline = await generated.GetOutgoingTimelineAsync(cancellationToken);
        var lastEmission = timeline.OfType<PackEmission>().LastOrDefault(e => e.Pack.Equals(processName, StringComparison.OrdinalIgnoreCase));
        bool verified = lastEmission != null && lastEmission.Output.Contains("approved", StringComparison.OrdinalIgnoreCase);

        string details = verified
            ? $"Embodied and executed successfully. Last emission: {lastEmission!.Output}"
            : "Installed but verification emission not found or not successful.";

        await FireAsync(new CompanySkillCreationResult(processName, "1.0", verified, details), cancellationToken);

        Logger.LogInformation("Company skill creation completed for {Process}: {Success} - {Details}", processName, verified, details);
    }
}
