using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using DigitalBrain.Testing.Tests.Unit.Host;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;
using Xunit;

namespace DigitalBrain.Testing.Tests.Unit.Host;

public sealed class StagedUpdateSurvivesRestartFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    [Fact]
    public async Task StagedBuildSurvivesRestartAndPromotesOnApproval()
    {
        await using var os = OS.Local().Register<HsPayments>().With<ISqlRead>("analytics", new FakeSqlRead(1));
        var brain = Brain("staged-test");

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

        // 3. Restart TestOS
        await os.Restart();

        // 4. Verify staged state survived restart
        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"{brain}/synapse.deployment/{HsSource.Key}"));
        var proposedRef = await deployment.Ref(UnitView.Proposed, Ct);
        Assert.NotNull(proposedRef);
        Assert.Equal(outcome2.Hash, proposedRef.Value.Hash);

        var listed = Assert.Single(await os.Synapses.List(brain, Ct));
        Assert.Equal(DeployStatus.Staged, listed.Status);
        Assert.Equal(outcome1.Hash, listed.Hash);
        Assert.Equal(outcome2.Hash, listed.StagedHash);

        // 5. Approve confirmation plan
        await os.Confirmations.Approve(planId);
        await os.Idle();

        // 6. Verify promotion to live
        var liveRef = await deployment.Ref(UnitView.Live, Ct);
        Assert.NotNull(liveRef);
        Assert.Equal(outcome2.Hash, liveRef.Value.Hash);

        var proposedAfter = await deployment.Ref(UnitView.Proposed, Ct);
        Assert.Equal(outcome2.Hash, proposedAfter!.Value.Hash); // Staged is cleared, Ref(Proposed) falls back to Live

        var listedAfter = Assert.Single(await os.Synapses.List(brain, Ct));
        Assert.Equal(DeployStatus.Deployed, listedAfter.Status);
        Assert.Equal(outcome2.Hash, listedAfter.Hash);
        Assert.Null(listedAfter.StagedHash);
    }
}
