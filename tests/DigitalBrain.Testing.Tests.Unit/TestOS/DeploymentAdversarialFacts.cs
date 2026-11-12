using System.Text.Json;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using DigitalBrain.Testing.Tests.Unit.Host;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;
using Xunit;

namespace DigitalBrain.Testing.Tests.Unit.Host;

public sealed class DeploymentAdversarialFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static StoredBuild MakeBuild(string sourceKey = "Me.Test", string hash = "h1", IReadOnlyList<string>? aliases = null, IReadOnlyDictionary<string, string>? needs = null)
    {
        aliases ??= ["test.alias"];
        needs ??= aliases.ToDictionary(a => a, _ => "needs1", StringComparer.Ordinal);
        var image = new BuildImage(new BuildRef(sourceKey, hash), "TestAssembly", [1, 2, 3], [4, 5, 6]);
        return new StoredBuild(image, new Dictionary<string, string> { ["Test.cs"] = "// code" }, null, aliases, needs);
    }

    [Fact]
    public void PureTransitions_NullArguments_ThrowsArgumentNullException()
    {
        var state = new DeploymentState();
        var build = MakeBuild();

        Assert.Throws<ArgumentNullException>(() => state.Propose(null!, []));
        Assert.Throws<ArgumentNullException>(() => state.Propose(build, null!));
        Assert.Throws<ArgumentNullException>(() => state.Asked(null!));
        Assert.Throws<ArgumentNullException>(() => state.Approve(null!, "hash"));
        Assert.Throws<ArgumentNullException>(() => state.Approve("alias", null!));
        Assert.Throws<ArgumentNullException>(() => state.Deny(null!, "plan"));
        Assert.Throws<ArgumentNullException>(() => state.Deny("alias", null!));
        Assert.Throws<ArgumentNullException>(() => state.Refuse(null!, "reason"));
    }

    [Fact]
    public void PureTransitions_EdgeCasesAndImmutability()
    {
        var state = new DeploymentState();
        var b1 = MakeBuild(hash: "h1", aliases: ["a1", "a2"]);

        // Staged is null: Approve, Deny, Asked, Supersede return this
        Assert.Same(state, state.Approve("a1", "needs1"));
        Assert.Same(state, state.Deny("a1", "p1"));
        Assert.Same(state, state.Asked(new Dictionary<string, string>()));
        Assert.Same(state, state.Supersede());
        Assert.Same(state, state.Retire());

        // Propose with duplicate diff aliases
        var proposed = state.Propose(b1, ["a1", "a1", "a2"]);
        Assert.Equal(2, proposed.Staged!.Waiting.Count);

        // Deny with empty planId drops stage when waiting has empty planId (pre-asked stage)
        var deniedEarly = proposed.Deny("a1", "");
        Assert.Null(deniedEarly.Staged);

        // Refuse hash matching neither Live nor Staged returns this
        var asked = proposed.Asked(new Dictionary<string, string> { ["a1"] = "p1", ["a2"] = "p2" });
        var refusedUnmatched = asked.Refuse("non-existent-hash", "bad");
        Assert.Same(asked, refusedUnmatched);

        // Refuse hash matching BOTH Live and Staged
        var promoted = asked.Approve("a1", "needs1").Approve("a2", "needs1").Promote(DateTimeOffset.UtcNow);
        var sameHashProposed = promoted.Propose(MakeBuild(hash: promoted.Live!.Ref.Hash), ["a1"]);
        var refusedBoth = sameHashProposed.Refuse(promoted.Live.Ref.Hash, "broken");
        Assert.NotNull(refusedBoth.Live);
        Assert.Equal("broken", refusedBoth.Live.RebuildReason);
        Assert.Null(refusedBoth.Staged); // Staged dropped!
    }

    [Fact]
    public void LiveBindings_SetAndSupersede_TracksHashCorrectly()
    {
        var live = new LiveBindings();
        var brain = Brain("lb-test");
        var build1 = new BuildRef("Source1", "hash-1");
        var build2 = new BuildRef("Source1", "hash-2");

        // Initially untracked
        Assert.True(live.IsCurrent(brain, build1));

        // Set build1
        live.Set(brain, build1);
        Assert.True(live.IsCurrent(brain, build1));
        Assert.False(live.IsCurrent(brain, build2));

        // Set build2 (superseding build1)
        live.Set(brain, build2);
        Assert.False(live.IsCurrent(brain, build1));
        Assert.True(live.IsCurrent(brain, build2));
    }

    [Fact]
    public void LiveBindings_Retire_EmpiricalBehavior()
    {
        var live = new LiveBindings();
        var brain = Brain("lb-retire-test");
        var build = new BuildRef("Source1", "hash-1");

        live.Set(brain, build);
        Assert.True(live.IsCurrent(brain, build));

        // When retired
        live.Retire(brain, "Source1");

        // Check TryGet: source key is no longer recorded
        Assert.False(live.TryGet(brain, "Source1", out _));

        // After retirement, IsCurrent must return false so host grains deactivate and unload ALCs
        var isCurrentAfterRetire = live.IsCurrent(brain, build);
        Assert.False(isCurrentAfterRetire);
    }

    [Fact]
    public async Task SaveBeforeAsk_SurvivesMultipleRestartsAndDenial()
    {
        await using var os = OS.Local().Register<HsPayments>().With<ISqlRead>("analytics", new FakeSqlRead(1));
        var brain = Brain("multi-restart-deny");

        // 1. Initial version deployed and approved
        var outcome1 = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcome1.Status);
        await os.Confirmations.Approve(outcome1.Installs["hs.week"].Reason!);
        await os.Idle();

        // 2. Propose update requiring slot consent (WithDb)
        var bindings = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["hs.week"] = new Dictionary<string, string> { ["Db"] = "analytics" }
        };
        var outcome2 = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.WithDb), bindings, Ct);
        Assert.Equal(DeployStatus.Staged, outcome2.Status);
        var planId = outcome2.Installs["hs.week"].Reason!;
        Assert.NotNull(planId);

        // 3. Silo Restart 1
        await os.Restart();

        // 4. Silo Restart 2 (consecutive restarts)
        await os.Restart();

        // 5. Verify staged proposal still survived two restarts
        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"{brain}/synapse.deployment/{HsSource.Key}"));
        var proposedRef = await deployment.Ref(UnitView.Proposed, Ct);
        Assert.NotNull(proposedRef);
        Assert.Equal(outcome2.Hash, proposedRef.Value.Hash);

        var liveRef = await deployment.Ref(UnitView.Live, Ct);
        Assert.NotNull(liveRef);
        Assert.Equal(outcome1.Hash, liveRef.Value.Hash);

        // 6. Deny the confirmation plan
        await os.Confirmations.Deny(planId);
        await os.Idle();

        // 7. Verify staged build was dropped and live build remains active
        var proposedAfterDeny = await deployment.Ref(UnitView.Proposed, Ct);
        Assert.Equal(outcome1.Hash, proposedAfterDeny!.Value.Hash); // Falls back to live!

        var liveAfterDeny = await deployment.Ref(UnitView.Live, Ct);
        Assert.Equal(outcome1.Hash, liveAfterDeny!.Value.Hash);

        // 8. Silo Restart 3
        await os.Restart();

        // 9. Verify denial survived restart: staged build remains dropped, live build unaffected
        var deploymentAfterRestart = os.Get<IUnitDeployment>(NeuronId.Parse($"{brain}/synapse.deployment/{HsSource.Key}"));
        var proposedFinal = await deploymentAfterRestart.Ref(UnitView.Proposed, Ct);
        Assert.Equal(outcome1.Hash, proposedFinal!.Value.Hash);

        var liveFinal = await deploymentAfterRestart.Ref(UnitView.Live, Ct);
        Assert.Equal(outcome1.Hash, liveFinal!.Value.Hash);

        var list = await os.Synapses.List(brain, Ct);
        var listed = Assert.Single(list);
        Assert.Equal(DeployStatus.Deployed, listed.Status);
        Assert.Equal(outcome1.Hash, listed.Hash);
        Assert.Null(listed.StagedHash);
    }

    [Fact]
    public async Task AliasConflict_DroppedAliasInNewVersion_ReleasesForRivalAcrossRestart()
    {
        await using var os = OS.Local();
        var brain = Brain("alias-drop-test");

        // 1. Deploy source A claiming "hs.week"
        var outcomeA = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeA.Status);
        await os.Idle();

        // 2. Rival B cannot claim "hs.week"
        var rival = HsSource.Of(HsSource.Rival, "Me.Rival");
        var outcomeB1 = await os.Synapses.Deploy(brain, rival, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Refused, outcomeB1.Status);

        // 3. Deploy source A version 2 that does NOT declare "hs.week" (renames alias to "hs.budget")
        var budgetNoWeek = HsSource.Budget.Replace("\"hs.week\"", "\"hs.budget\"", StringComparison.Ordinal);
        var outcomeA2 = await os.Synapses.Deploy(brain, HsSource.Of(budgetNoWeek), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeA2.Status);
        await os.Idle();

        // 4. Silo Restart
        await os.Restart();

        // 5. Verify source A now owns "hs.budget" and does NOT own "hs.week"
        var sources = os.Get<ISources>(NeuronId.Parse($"{brain}/synapse.sources/default"));
        Assert.Equal(HsSource.Key, await sources.OwnerOf("hs.budget", Ct));
        Assert.Null(await sources.OwnerOf("hs.week", Ct));

        // 6. Rival B now deploys claiming "hs.week" -> must succeed!
        var outcomeB2 = await os.Synapses.Deploy(brain, rival, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeB2.Status);
        await os.Idle();

        // 7. Silo Restart again
        await os.Restart();

        // 8. Verify both co-exist across restart with their respective aliases
        var sourcesAfterRestart = os.Get<ISources>(NeuronId.Parse($"{brain}/synapse.sources/default"));
        Assert.Equal(HsSource.Key, await sourcesAfterRestart.OwnerOf("hs.budget", Ct));
        Assert.Equal("Me.Rival", await sourcesAfterRestart.OwnerOf("hs.week", Ct));
    }

    [Fact]
    public async Task Retirement_ReleasesAliases_AllowsRivalClaimAcrossRestart()
    {
        await using var os = OS.Local();
        var brain = Brain("retire-claim-test");

        // 1. Deploy source A
        var outcomeA = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeA.Status);
        await os.Idle();

        // 2. Rival B cannot claim
        var rival = HsSource.Of(HsSource.Rival, "Me.Rival");
        var outcomeB1 = await os.Synapses.Deploy(brain, rival, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Refused, outcomeB1.Status);

        // 3. Retire source A
        var deploymentA = os.Get<IUnitDeployment>(NeuronId.Parse($"{brain}/synapse.deployment/{HsSource.Key}"));
        await deploymentA.Retire(Ct);
        await os.Idle();

        // 4. Silo Restart
        await os.Restart();

        // 5. Verify alias "hs.week" is unowned
        var sources = os.Get<ISources>(NeuronId.Parse($"{brain}/synapse.sources/default"));
        Assert.Null(await sources.OwnerOf("hs.week", Ct));

        // 6. Rival B can now deploy claiming "hs.week"
        var outcomeB2 = await os.Synapses.Deploy(brain, rival, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeB2.Status);
        await os.Idle();

        // 7. Silo Restart
        await os.Restart();

        var sourcesFinal = os.Get<ISources>(NeuronId.Parse($"{brain}/synapse.sources/default"));
        Assert.Equal("Me.Rival", await sourcesFinal.OwnerOf("hs.week", Ct));
    }

    [Fact]
    public async Task Refuse_PersistsAcrossRestart_BlocksUnitResolver()
    {
        await using var os = OS.Local();
        var brain = Brain("refuse-restart-test");

        // Deploy source
        var outcome = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcome.Status);
        await os.Idle();

        // Verify resolver can acquire handle
        using (var handleBefore = await os.Resolver.Resolve(brain, "hs.week", UnitView.Live, Ct))
        {
            Assert.NotNull(handleBefore);
        }

        // Now refuse the build
        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"{brain}/synapse.deployment/{HsSource.Key}"));
        Assert.NotNull(outcome.Hash);
        await deployment.Refuse(outcome.Hash, "security policy violation", Ct);
        await os.Idle();

        // Verify resolver immediately refuses to resolve it
        using (var handleRefused = await os.Resolver.Resolve(brain, "hs.week", UnitView.Live, Ct))
        {
            Assert.Null(handleRefused);
        }

        // Silo Restart
        await os.Restart();

        // Verify that after restart, RebuildReason persisted durably and resolver STILL refuses to resolve it!
        using (var handleAfterRestart = await os.Resolver.Resolve(brain, "hs.week", UnitView.Live, Ct))
        {
            Assert.Null(handleAfterRestart);
        }

        var sourceView = await deployment.Source(Ct);
        Assert.NotNull(sourceView);
        Assert.Equal("security policy violation", sourceView.RebuildReason);
    }

    [Fact]
    public async Task Retire_WhenStagedWithPendingConfirmation_CancelsPendingPlans()
    {
        await using var os = OS.Local().Register<HsPayments>().With<ISqlRead>("analytics", new FakeSqlRead(1));
        var brain = Brain("retire-staged-cancel-pending");

        // 1. Initial version deployed and approved
        var outcome1 = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcome1.Status);
        await os.Confirmations.Approve(outcome1.Installs["hs.week"].Reason!);
        await os.Idle();

        // 2. Propose update requiring confirmation
        var bindings = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["hs.week"] = new Dictionary<string, string> { ["Db"] = "analytics" }
        };
        var outcome2 = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.WithDb), bindings, Ct);
        Assert.Equal(DeployStatus.Staged, outcome2.Status);

        var installs = os.Get<IInstalls>(NeuronId.Parse($"{brain}/synapse.installs/default"));
        var pendingBefore = await installs.Pending(Ct);
        Assert.Contains(pendingBefore, p => p.Alias == "hs.week");

        // 3. Retire the deployment
        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"{brain}/synapse.deployment/{HsSource.Key}"));
        await deployment.Retire(Ct);
        await os.Idle();

        // 4. Verify pending plans for staged build were cancelled
        var pendingAfter = await installs.Pending(Ct);
        Assert.DoesNotContain(pendingAfter, p => p.Alias == "hs.week");
    }
}
