using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Genesis.Contracts;
using Ino.Domains.Genesis.Neurons;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Phase 4 Slice E.2 gate (issue #25): the L1 loop closes when a missed-
/// intent cluster turns into a routable neuron without restarting any
/// silo. This drives the consumer half end-to-end:
/// <list type="number">
///   <item>Synthesise an <see cref="L1Proposal"/> the way <c>MissedIntentTracker</c>
///         would after three identical unrouted prompts (Slice E.1, already
///         covered by <c>MissedIntentTrackerTests</c>).</item>
///   <item>Hand it to <c>CreatorNeuron</c>'s reactive entry point — the
///         same surface a real <see cref="IFirePort.FireBroadcast"/> would
///         dispatch to.</item>
///   <item>Confirm <c>IDiscovery.DumpNeuronsAsync</c> now contains the
///         freshly drafted neuron (Cortex would pick this up on the next
///         routing pass).</item>
///   <item>Confirm the registry holds the script body.</item>
///   <item>Resolve the same shared <see cref="IRoslynPlan"/> grain the way
///         <c>CortexNeuron.TryExecutePlanAsync</c> does — keyed by user id
///         — and verify <see cref="INeuronPlan.ExecuteAsync"/> runs the
///         compiled body and returns a routed <see cref="NeuronResult"/>.</item>
/// </list>
/// The broadcast plumbing (tracker → FireBroadcast → reactive handler) is
/// intentionally bypassed; <c>MissedIntentTrackerTests</c> covers the
/// emission half. This test owns the consumer half.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class L1LoopAcceptanceTests(GenesisTestSiloFixture fixture)
{
    [Fact]
    public async Task Three_unrouted_prompts_then_proposal_yields_routable_neuron()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = $"user-{Guid.NewGuid():n}";
        var clusterKey = "translate this to french";

        // Stage 1 — emulate MissedIntentTracker emitting after the threshold.
        var proposal = new L1Proposal(
            ProposalId: Ulid.NewUlid().ToString(),
            UserId: userId,
            ClusterKey: clusterKey,
            ExamplePrompt: clusterKey,
            Occurrences: 3,
            ProposedAt: DateTimeOffset.UtcNow);

        // Stage 2 — drive CreatorNeuron the way FirePort.FireBroadcast would.
        // Reactive grains are keyed by the broadcaster's correlationId; we
        // pass a fresh one here (CreatorNeuron is per-broadcast scoped).
        var creator = fixture.Grains.GetGrain<IReactsTo<L1Proposal>>(Guid.NewGuid().ToString("n"));
        Assert.IsAssignableFrom<IReactsTo<L1Proposal>>(creator);
        var ctx = NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("kernel")),
            userId: userId);
        await creator.ReactAsync(proposal, ctx, ct);

        var neuronId = CreatorNeuron.DraftNeuronId(proposal);

        // Phase 4 epilogue Slice 3A: INeuronRegistry.GetApprovalRequiredAsync
        // defaults to true in tests (no Ino:Inspector:ApprovalRequired config key
        // is set). CreatorNeuron stashes the draft instead of registering directly.
        // Approve the proposal so stage 3 and beyond see the registered neuron.
        var registry = fixture.Grains.GetGrain<INeuronRegistry>(0);
        var approved = await registry.ApproveAsync(proposal.ProposalId, "test-user", ct);
        Assert.True(approved);

        // Stage 3 — Cortex's discovery surface now includes the new neuron.
        var discovery = fixture.Grains.GetGrain<IDiscovery>(0);
        var neurons = await discovery.DumpNeuronsAsync(ct);
        var dynamic = Assert.Single(neurons, e => e.Id.Value == neuronId);
        Assert.Equal(typeof(IRoslynPlan), dynamic.PlanType);
        Assert.Equal(typeof(DynamicNeuronTrigger), dynamic.CanonicalSynapseType);

        // Stage 4 — registry holds the compiled-and-validated script body.
        var body = await registry.GetScriptBodyAsync(neuronId, ct);
        Assert.NotNull(body);

        // Stage 5 — Cortex's plan dispatch surface produces a routed result.
        // Same key shape Cortex uses (user id, falling back to correlation id).
        var plan = fixture.Grains.GetGrain<IRoslynPlan>(userId);
        var input = new NeuronPlanContext(
            Prompt: "translate this to french please",
            Caller: ctx with { NeuronId = NeuronId.From(neuronId) },
            NeuronId: NeuronId.From(neuronId));
        var result = await plan.ExecuteAsync(input, ct);

        Assert.True(result.Success);
        Assert.Contains("translate this to french please", result.Message);
    }

    [Fact]
    public async Task Duplicate_proposal_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = $"user-{Guid.NewGuid():n}";
        var clusterKey = $"order pizza now-{Guid.NewGuid():n}";

        var proposal = new L1Proposal(
            ProposalId: Ulid.NewUlid().ToString(),
            UserId: userId,
            ClusterKey: clusterKey,
            ExamplePrompt: clusterKey,
            Occurrences: 3,
            ProposedAt: DateTimeOffset.UtcNow);

        // Same grain key for both calls so idempotence guard kicks in
        // (CreatorNeuron tracks seen ProposalIds in-memory per activation).
        var creatorKey = Guid.NewGuid().ToString("n");
        var creator = fixture.Grains.GetGrain<IReactsTo<L1Proposal>>(creatorKey);
        var ctx = NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("kernel")),
            userId: userId);

        await creator.ReactAsync(proposal, ctx, ct);
        await creator.ReactAsync(proposal, ctx, ct);

        // Phase 4 epilogue Slice 3A: ApprovalRequired=true by default.
        // Approve so the neuron appears in Discovery.
        var registry = fixture.Grains.GetGrain<INeuronRegistry>(0);
        var approved = await registry.ApproveAsync(proposal.ProposalId, "test-user", ct);
        Assert.True(approved);

        var neuronId = CreatorNeuron.DraftNeuronId(proposal);
        var discovery = fixture.Grains.GetGrain<IDiscovery>(0);
        var neurons = await discovery.DumpNeuronsAsync(ct);

        // Single matching entry — re-registration would still leave one,
        // but the duplicate-proposal guard short-circuits before touching
        // the registry on the second call.
        Assert.Single(neurons, e => e.Id.Value == neuronId);
    }
}
