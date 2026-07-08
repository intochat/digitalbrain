using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.codedeploy.v1")]
public class CodeDeployNeuron(ILogger<CodeDeployNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICodeDeployNeuron
{
    public async Task HandleAsync(DeployGeneratedCode cmd, CancellationToken cancellationToken = default)
    {
        if (RestartPending())
        {
            await FireAsync(new FoundryRolledBack(cmd.ModuleName, "deploy-in-progress", cmd.CheckpointId), cancellationToken);
            return;
        }

        var buildRunner = ServiceProvider.GetRequiredService<IBuildRunner>();
        var outcome = await buildRunner.VerifyBuildAsync(cmd.ModuleName, cmd.Source, cancellationToken);
        await FireAsync(new CodeBuilt(cmd.ModuleName, outcome.Success, outcome.Log), cancellationToken);

        if (!outcome.Success)
        {
            await FireAsync(new FoundryRolledBack(cmd.ModuleName, "build", cmd.CheckpointId), cancellationToken);
            return;
        }

        CommitSource(cmd.ModuleName, cmd.Source);

        var resourceController = ServiceProvider.GetRequiredService<IResourceController>();
        await resourceController.RestartKernelAsync("apply-" + cmd.ModuleName, cancellationToken);
        await FireAsync(new KernelRestartRequested("apply-" + cmd.ModuleName, cmd.ModuleName), cancellationToken);
    }

    private bool RestartPending()
    {
        var lastRestart = OutgoingJournal.OfType<KernelRestartRequested>().LastOrDefault();
        return lastRestart is not null && lastRestart.Timestamp >= ActivatedAt;
    }

    private static void CommitSource(string moduleName, string source)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Generated");
        // Resolve to the source tree Generated folder when running from the kernel project.
        var projectGenerated = Path.Combine(Directory.GetCurrentDirectory(), "Generated");
        var target = Directory.Exists(projectGenerated) ? projectGenerated : dir;
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, moduleName + ".cs"), source);
    }
}
