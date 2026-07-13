using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Ui.Contracts.Ui;
using DigitalBrain.Ui.Runtime;
using Microsoft.Extensions.AI;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

using DigitalBrain.Ui.Contracts;

public static class KernelPack
{
    public const string Name = "kernel";
    public const string DefaultVersion = "0.3.0";
    public const string Description = "Core kernel substrate with rolling replica support.";
}

[GrainType("digitalbrain.kernel.aspire.v1")]
public class AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IAspireNeuron, IHandle<PerformKernelSelfUpdate>
{
    public async Task HandleAsync(StartDistributedApp cmd, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Aspire starting app: {App}", cmd.AppName);
        await FireAsync(new DistributedAppStarted(cmd.AppName, Success: true, "started via neuro"), cancellationToken);
        await FireAsync(new SystemStatusChanged("aspire", "started", cmd.AppName), cancellationToken);

        var dashboardProps = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "kernel-dashboard-" + cmd.AppName,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Kernel Dashboard"
        };
        await FireAsync(new UiSurface(KernelUiSurfaceKinds.Dashboard, dashboardProps), cancellationToken);
    }

    public async Task HandleAsync(RestartResource cmd, CancellationToken cancellationToken = default)
    {
        if (cmd.IsRollingUpdate)
        {
            Logger.LogInformation("Aspire rolling restart for {Res} target={Ver} strategy={Strategy}", cmd.ResourceName, cmd.TargetVersion, cmd.Strategy);
            await FireAsync(new SystemStatusChanged("aspire", "rolling-restart-started", $"{cmd.ResourceName}@{cmd.TargetVersion}"), cancellationToken);

            var rollingProps = new Dictionary<string, object?>
            {
                [UiSurfaceKeys.SurfaceId] = "rolling-" + cmd.ResourceName,
                [UiSurfaceKeys.Emitter] = Self.Value,
                [UiSurfaceKeys.Title] = "Rolling Kernel Update",
                [UiSurfaceKeys.Priority] = 50,
                [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                ["resource"] = cmd.ResourceName,
                ["version"] = cmd.TargetVersion ?? "next",
                ["strategy"] = cmd.Strategy,
                ["status"] = "draining-replica",
                ["haReplicas"] = 3
            };
            await FireAsync(new UiSurface(KernelUiSurfaceKinds.Rolling, rollingProps), cancellationToken);
        }
        else
        {
            Logger.LogInformation("Aspire restarting resource: {Res}", cmd.ResourceName);
        }

        await FireAsync(new DistributedAppStarted(cmd.ResourceName, Success: true, "restarted"), cancellationToken);
        await FireAsync(new SystemStatusChanged("aspire", "restarted", cmd.ResourceName), cancellationToken);
    }

    public async Task HandleAsync(PerformKernelSelfUpdate cmd, CancellationToken cancellationToken = default)
    {
        var version = string.IsNullOrWhiteSpace(cmd.Version) ? KernelPack.DefaultVersion : cmd.Version;

        var preUpdateCheckpoint = await CreateCheckpointAsync(cancellationToken);

        var lineageCount = 0;

        for (int replica = 1; replica <= 3; replica++)
        {
            await FireAsync(SystemRollingSurfaces.CreateDrain(replica, version, preUpdateCheckpoint.SynapseId, Self.Value), cancellationToken);

            await FireAsync(new RestartResource("kernel", IsRollingUpdate: true, TargetVersion: version, Strategy: $"replica-{replica}-of-3"), cancellationToken);

            var replicaLineage = await GetCausalLineageAsync(preUpdateCheckpoint.SynapseId, cancellationToken);
            lineageCount = replicaLineage.Count;

            var verifyFailed = cmd.FailAtReplica == replica;
            var verifyPhase = verifyFailed ? "verify-failed" : "verified";

            await FireAsync(SystemRollingSurfaces.CreateVerify(replica, version, verifyPhase, lineageCount, Self.Value), cancellationToken);

            if (verifyFailed)
            {
                await RestoreCheckpointAsync(preUpdateCheckpoint, cancellationToken);
                await FireAsync(SystemRollingSurfaces.CreateRollback(replica, version, preUpdateCheckpoint.SynapseId, Self.Value), cancellationToken);
                return; // Abort: do not process further replicas, do not emit RollingComplete.
            }
        }

        await FireAsync(SystemRollingSurfaces.CreateComplete(version, preUpdateCheckpoint.SynapseId, lineageCount, Self.Value), cancellationToken);
    }
}

[GrainType("digitalbrain.observability.v1")]
public class ObservabilityNeuron(ILogger<ObservabilityNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IObservabilityNeuron
{
    public Task HandleAsync(UiSurface surface, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Observability surface {Kind} correlation={CorrelationId}", surface.Kind, surface.CorrelationId);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(ClusterActivity activity, CancellationToken cancellationToken = default)
    {
        await PublishGraphFromJournalAsync(activity, cancellationToken);
    }

    public async Task HandleAsync(ThreeDGraphUpdate update, CancellationToken cancellationToken = default)
    {
        await PublishGraphFromJournalAsync(update, cancellationToken);
    }

    private async Task PublishGraphFromJournalAsync(Synapse cause, CancellationToken cancellationToken)
    {
        var graphTimeline = OutgoingJournal
            .Concat(IncomingJournal)
            .Where(s => s is ClusterActivity or ThreeDGraphUpdate)
            .DistinctBy(s => s.SynapseId)
            .OrderBy(s => s.Timestamp)
            .TakeLast(40)
            .ToList();

        var surface = UiSurfaceLiveData.ActivityGraphFromTimeline(graphTimeline) with
        {
            CorrelationId = cause.CorrelationId ?? cause.SynapseId
        };

        await FireAsync(surface, cancellationToken);
    }
}

[GrainType("digitalbrain.optimizer.v1")]
public class MetaOptimizerNeuron(ILogger<MetaOptimizerNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IMetaOptimizerNeuron
{
    public async Task HandleAsync(NeuronTelemetry telemetry, CancellationToken cancellationToken = default)
    {
        var count = IncomingJournal.Concat(OutgoingJournal).OfType<NeuronTelemetry>().Count();
        Logger.LogInformation("Optimizer received telemetry from {Neuron}: {Event} (total {Count})", telemetry.Neuron, telemetry.Event, count);

        if (count % 5 == 0)
        {
            string proposal;
            var chat = ServiceProvider.GetService<IChatClient>();
            if (chat != null)
            {
                var p = $"Telemetry count reached {count}. Propose ONE short, actionable wiring or scaling improvement for the DigitalBrain neuron system (Orleans grains + Aspire + compiler for code gen from English).";
                var response = await chat.GetResponseAsync(p, cancellationToken: cancellationToken);
                var acc = response.Text;
                proposal = acc.Length > 20 ? acc.Trim() : "Add parallel compiler neurons and route create requests through LlmNeuron";
            }
            else
            {
                proposal = "Add parallel compiler neurons routed via LlmNeuron for faster self-gen";
            }
            await FireAsync(new WiringOptimizationProposed(proposal, Self.Value), cancellationToken);
        }
    }

    public Task HandleAsync(WiringOptimizationProposed proposal, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Optimizer proposal received: {Proposal} from {From}", proposal.Proposal, proposal.FromNeuron);
        return Task.CompletedTask;
    }
}




