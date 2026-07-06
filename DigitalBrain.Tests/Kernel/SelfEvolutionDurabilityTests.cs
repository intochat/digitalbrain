using System.Collections.Concurrent;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.SelfEvolution;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Kernel;

#pragma warning disable ORLEANSEXP005

public sealed class SelfEvolutionDurabilityTests
{
    [Fact]
    public async Task Proposal_Replays_As_Pending_After_Grain_Reactivation()
    {
        using var cluster = StartCluster();
        var grain = cluster.GrainFactory.GetGrain<ISelfEvolutionNeuron>("self-evolution-durable-pending");
        var proposalId = "durable-pending-" + Guid.NewGuid().ToString("N");

        await grain.DeliverAsync(Proposal(proposalId));
        var activationCount = (await grain.GetOutgoingTimelineAsync()).Count(s => s is NeuronActivated);

        await ForceReactivationAsync(cluster, grain, activationCount);
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: false, DecidedBy: "user:owner", Reason: "deny after replay"));

        var timeline = await grain.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<SelfEvolutionProposalPending>(), pending => pending.ProposalId == proposalId);
        Assert.Contains(timeline.OfType<SelfEvolutionDecisionRecorded>(), decision =>
            decision.ProposalId == proposalId && !decision.Approved);
    }

    [Fact]
    public async Task Decision_Replays_As_Decided_After_Grain_Reactivation()
    {
        using var cluster = StartCluster();
        var grain = cluster.GrainFactory.GetGrain<ISelfEvolutionNeuron>("self-evolution-durable-decision");
        var proposalId = "durable-decision-" + Guid.NewGuid().ToString("N");

        await grain.DeliverAsync(Proposal(proposalId));
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: false, DecidedBy: "user:owner", Reason: "deny"));
        var activationCount = (await grain.GetOutgoingTimelineAsync()).Count(s => s is NeuronActivated);

        await ForceReactivationAsync(cluster, grain, activationCount);
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user:owner"));

        var timeline = await grain.GetOutgoingTimelineAsync();
        Assert.Single(timeline.OfType<SelfEvolutionDecisionRecorded>(), decision => decision.ProposalId == proposalId);
        Assert.Contains(timeline.OfType<SelfEvolutionDecisionRejected>(), rejected =>
            rejected.ProposalId == proposalId && rejected.Reason.Contains("already been decided", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Applied_Proposal_Is_Not_Reapplied_On_Replay()
    {
        using var cluster = StartCluster();
        var grain = cluster.GrainFactory.GetGrain<ISelfEvolutionNeuron>("self-evolution-durable-applied");
        var proposalId = "durable-applied-" + Guid.NewGuid().ToString("N");

        await grain.DeliverAsync(Proposal(proposalId));
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user:owner"));
        Assert.Equal(1, DurableRecordingApplyHandler.Count(proposalId));
        var activationCount = (await grain.GetOutgoingTimelineAsync()).Count(s => s is NeuronActivated);

        await ForceReactivationAsync(cluster, grain, activationCount);
        Assert.Equal(1, DurableRecordingApplyHandler.Count(proposalId));

        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user:owner"));
        Assert.Equal(1, DurableRecordingApplyHandler.Count(proposalId));
        Assert.Contains((await grain.GetOutgoingTimelineAsync()).OfType<SelfEvolutionDecisionRejected>(), rejected =>
            rejected.ProposalId == proposalId);
    }

    private static SelfEvolutionProposal Proposal(string proposalId) => new(
        ProposalId: proposalId,
        Scope: "kernel",
        Rationale: "durability test",
        ProposedChange: "replay self-evolution audit",
        ApplyVia: DurableRecordingApplyHandler.ApplyViaId,
        Risk: SelfEvolutionRisk.KernelRestart,
        RequiresHumanApproval: true,
        RollbackPlan: "restore durable-checkpoint",
        Origin: "durability-test");

    private static TestCluster StartCluster()
    {
        var cluster = new TestClusterBuilder()
            .AddSiloBuilderConfigurator<SelfEvolutionDurableSiloConfigurator>()
            .Build();
        cluster.Deploy();
        return cluster;
    }

    private static async Task ForceReactivationAsync(TestCluster cluster, ISelfEvolutionNeuron grain, int activationCount)
    {
        var management = cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            await Task.Delay(1_000);

            var timeline = await grain.GetOutgoingTimelineAsync();
            if (timeline.Count(s => s is NeuronActivated) > activationCount)
            {
                return;
            }
        }

        throw new TimeoutException("SelfEvolutionNeuron did not reactivate after activation collection.");
    }

    private sealed class SelfEvolutionDurableSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddJournalStorage()
                .ConfigureServices(services =>
                {
                    services.AddScoped<NeuronJournals>();
                    services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
                    services.AddSingleton<ISelfEvolutionApplyHandler, DurableRecordingApplyHandler>();
                    services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "orleans-binary");
                    services.Configure<GrainCollectionOptions>(options =>
                    {
                        options.CollectionQuantum = TimeSpan.FromMilliseconds(200);
                        options.CollectionAge = TimeSpan.FromMilliseconds(400);
                    });
                });
        }
    }

    private sealed class DurableRecordingApplyHandler : ISelfEvolutionApplyHandler
    {
        public const string ApplyViaId = "durable.apply";
        private static readonly ConcurrentDictionary<string, int> Applied = new(StringComparer.Ordinal);

        public string ApplyVia => ApplyViaId;
        public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.KernelRestart;

        public static int Count(string proposalId) => Applied.TryGetValue(proposalId, out var count) ? count : 0;

        public Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
        {
            Applied.AddOrUpdate(proposal.ProposalId, 1, (_, count) => count + 1);
            return Task.FromResult(new SelfEvolutionApplyResult(
                proposal.ProposalId,
                proposal.ApplyVia,
                Succeeded: true,
                Details: "applied",
                RollbackCheckpointId: "durable-checkpoint"));
        }
    }
}

#pragma warning restore ORLEANSEXP005

